"""Correct back-facing normal bake hits by nearest compatible source surface.

Reprojects only inverted texels, transferring geometric and source normal-map
detail into the existing target tangent frame. No geometry or UV changes.
"""
import argparse,json,sys,time,hashlib,struct
from pathlib import Path
import bpy,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import activate,render_views

def arrays(obj):
    m=obj.data;m.calc_loop_triangles();m.calc_tangents(uvmap=m.uv_layers.active.name)
    def get(collection,attr,width,dtype=np.float64):
        a=np.empty(len(collection)*width,dtype=dtype);collection.foreach_get(attr,a);return a.reshape(-1,width)
    return {'co':get(m.vertices,'co',3),'tri':get(m.loop_triangles,'vertices',3,np.int32),
            'loops':get(m.loop_triangles,'loops',3,np.int32),'uv':get(m.uv_layers.active.data,'uv',2),
            'normal':get(m.corner_normals,'vector',3),'tangent':get(m.loops,'tangent',3),
            'sign':get(m.loops,'bitangent_sign',1).ravel()}

def unit(x):return x/max(np.linalg.norm(x),1e-20)
def barycentric(q,verts):
    a,b,c=verts;e0=b-a;e1=c-a;v=q-a
    aa=np.dot(e0,e0);bb=np.dot(e0,e1);cc=np.dot(e1,e1);dd=np.dot(v,e0);ee=np.dot(v,e1)
    denom=aa*cc-bb*bb
    if abs(denom)<1e-24:return np.array([1,0,0])
    s=(cc*dd-bb*ee)/denom;t=(aa*ee-bb*dd)/denom
    weights=np.maximum([1-s-t,s,t],0);return weights/max(weights.sum(),1e-20)

ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True);ap.add_argument('--correct-channels',action='store_true')
a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);started=time.time();bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint))
source=bpy.data.objects['SOURCE'];target=bpy.data.objects['HIGH'];src=arrays(source);dst=arrays(target)
tree=BVHTree.FromObject(source,bpy.context.evaluated_depsgraph_get())
normalnode=next(n for n in source.active_material.node_tree.nodes if n.type=='NORMAL_MAP')
texture=normalnode.inputs['Color'].links[0].from_node.image;sw,sh=texture.size
sp=np.empty(sw*sh*4,dtype=np.float32);texture.pixels.foreach_get(sp);sp=sp.reshape(sh,sw,4)
strength=float(normalnode.inputs['Strength'].default_value)
image=next(n.image for n in target.active_material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image and n.image.name=='Normal')
w,h=image.size;pixels=np.empty(w*h*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
bad=pixels[:,:,2]<.5;original_bad=int(bad.sum());processed=np.zeros((h,w),dtype=bool)
channel_pixels={};channel_images={};source_channels={}
if a.correct_channels:
    source_path=Path(__file__).resolve().parent.parent/'input'/(a.checkpoint.parent.name+'.glb')
    if not source_path.exists():raise RuntimeError('Channel correction requires the original input GLB for material profile validation')
    raw_glb=source_path.read_bytes();json_length=struct.unpack_from('<I',raw_glb,12)[0];document=json.loads(raw_glb[20:20+json_length]);pbr=document['materials'][0]['pbrMetallicRoughness']
    if len(document['materials'])!=1 or pbr.get('baseColorFactor',[1,1,1,1])!=[1,1,1,1] or pbr.get('metallicFactor',1)!=1 or pbr.get('roughnessFactor',1)!=1:
        raise RuntimeError('Nearest-hit PBR correction currently supports the audited one-material/unit-factor profile only')
    # Probe the original small-cage correspondence even if normals have already
    # been repaired. Its inverted texels identify inconsistent source hits.
    mat=target.active_material;nt=mat.node_tree;probe=bpy.data.images.new('NormalHitProbe',width=w,height=h,alpha=False);probe.colorspace_settings.name='Non-Color'
    node=nt.nodes.new('ShaderNodeTexImage');node.image=probe;nt.nodes.active=node
    for obj in bpy.context.scene.objects:obj.select_set(False)
    source.hide_set(False);source.hide_render=False;source.select_set(True);target.select_set(True);bpy.context.view_layer.objects.active=target
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.render.bake.use_selected_to_active=True;scene.render.bake.use_cage=True
    bake_report_path=a.diagnostics/(a.checkpoint.parent.name+'.bake.json')
    probe_scale=json.loads(bake_report_path.read_text()).get('working_scale',1.0) if bake_report_path.exists() else 1.0
    probe_originals={}
    if probe_scale!=1:
        from mathutils import Matrix
        for obj in [source,target]:
            probe_originals[obj]=(obj.data,obj.matrix_world.copy())
            obj.data=obj.data.copy();obj.data.transform(Matrix.Scale(probe_scale,4));obj.matrix_world.translation*=probe_scale
        bpy.context.view_layer.update()
    scene.render.bake.cage_extrusion=max(source.dimensions)*.001;scene.render.bake.max_ray_distance=max(source.dimensions)*.04;scene.render.bake.margin=12
    low_shader=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED');normal_links=[(l.from_socket,l.to_socket) for l in low_shader.inputs['Normal'].links]
    for link in list(low_shader.inputs['Normal'].links):nt.links.remove(link)
    bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',normal_r='POS_X',normal_g='POS_Y',normal_b='POS_Z')
    for obj,(original_mesh,original_matrix) in probe_originals.items():
        temporary=obj.data;obj.data=original_mesh;obj.matrix_world=original_matrix;bpy.data.meshes.remove(temporary)
    bpy.context.view_layer.update()
    for x,y in normal_links:nt.links.new(x,y)
    raw=np.empty(w*h*4,dtype=np.float32);probe.pixels.foreach_get(raw);bad=raw.reshape(h,w,4)[:,:,2]<.5;original_bad=int(bad.sum());del raw
    nt.nodes.remove(node);bpy.data.images.remove(probe)
    source_shader=next(n for n in source.active_material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    base=source_shader.inputs['Base Color'].links[0].from_node.image
    mr=next(n.image for n in source.active_material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image not in [base,texture])
    for key,img in [('BaseColor',base),('MR',mr)]:
        if tuple(img.size)!=(sw,sh):raise RuntimeError('Nearest-hit correction currently expects matching source texture dimensions')
        arr=np.empty(img.size[0]*img.size[1]*4,dtype=np.float32);img.pixels.foreach_get(arr);source_channels[key]=arr.reshape(img.size[1],img.size[0],4)
    for key in ['BaseColor','Roughness','Metallic']:
        target_image=next(n.image for n in nt.nodes if n.type=='TEX_IMAGE' and n.image and n.image.name==key)
        arr=np.empty(w*h*4,dtype=np.float32);target_image.pixels.foreach_get(arr);channel_pixels[key]=arr.reshape(h,w,4);channel_images[key]=target_image
source_inverse=source.matrix_world.inverted();source_normal_matrix=np.array(source.matrix_world.to_3x3().inverted().transposed())
target_normal_matrix=np.array(target.matrix_world.to_3x3().inverted().transposed())
source_tangent_matrix=np.array(source.matrix_world.to_3x3());target_tangent_matrix=np.array(target.matrix_world.to_3x3())
face_normals=src['normal'][src['loops']].mean(1)@source_normal_matrix.T
face_normals/=np.maximum(np.linalg.norm(face_normals,axis=1)[:,None],1e-20)
print('REPROJECTION_START','inverted_texels',original_bad,'source_triangles',len(src['tri']),flush=True)
count=0;fallback=0;incompatible=0
for tid,ids in enumerate(dst['tri']):
    loops=dst['loops'][tid];uv=dst['uv'][loops];xy=uv*np.array([w,h])-.5
    lo=np.maximum(np.ceil(xy.min(0)).astype(int),0);hi=np.minimum(np.floor(xy.max(0)).astype(int),[w-1,h-1])
    if np.any(hi<lo):continue
    yy,xx=np.nonzero(bad[lo[1]:hi[1]+1,lo[0]:hi[0]+1] & ~processed[lo[1]:hi[1]+1,lo[0]:hi[0]+1])
    if not len(xx):continue
    xx+=lo[0];yy+=lo[1]
    e0=xy[1]-xy[0];e1=xy[2]-xy[0];det=e0[0]*e1[1]-e0[1]*e1[0]
    if abs(det)<1e-12:continue
    p=np.column_stack((xx,yy))-xy[0]
    b=(p[:,0]*e1[1]-p[:,1]*e1[0])/det;c=(e0[0]*p[:,1]-e0[1]*p[:,0])/det
    weights=np.column_stack((1-b-c,b,c));inside=np.all(weights>=-1e-6,axis=1)
    for x,y,weight in zip(xx[inside],yy[inside],weights[inside]):
        point=target.matrix_world@Vector(weight@dst['co'][ids]);local=source_inverse@point
        tn=unit(target_normal_matrix@(weight@dst['normal'][loops]));tt=unit(target_tangent_matrix@(weight@dst['tangent'][loops]));tt=unit(tt-tn*np.dot(tt,tn))
        tb=unit(np.cross(tn,tt))*(1 if weight@dst['sign'][loops]>=0 else -1)
        q,n,index,d=tree.find_nearest(local)
        def compatible(index):
            return np.dot(face_normals[index],tn)>.1
        if not compatible(index):
            candidates=tree.find_nearest_range(local,d+max(source.dimensions)*.003)
            candidate_ids=np.fromiter((v[2] for v in candidates),dtype=np.int32)
            alignment=face_normals[candidate_ids]@tn
            valid=np.flatnonzero(alignment>.1)
            if len(valid):
                distances=np.fromiter((v[3] for v in candidates),dtype=np.float64)
                q,n,index,d=candidates[valid[np.argmin(distances[valid])]]
            else:incompatible+=1
        sl=src['loops'][index];swgt=barycentric(np.array(q),src['co'][src['tri'][index]])
        sn=unit(source_normal_matrix@(swgt@src['normal'][sl]));st=unit(source_tangent_matrix@(swgt@src['tangent'][sl]));st=unit(st-sn*np.dot(st,sn))
        sb=unit(np.cross(sn,st))*(1 if swgt@src['sign'][sl]>=0 else -1)
        suv=swgt@src['uv'][sl];tx=(suv[0]%1)*sw-.5;ty=(suv[1]%1)*sh-.5
        ix=int(np.floor(tx));iy=int(np.floor(ty));fx=tx-ix;fy=ty-iy
        value=(sp[iy%sh,ix%sw,:3]*(1-fx)*(1-fy)+sp[iy%sh,(ix+1)%sw,:3]*fx*(1-fy)+sp[(iy+1)%sh,ix%sw,:3]*(1-fx)*fy+sp[(iy+1)%sh,(ix+1)%sw,:3]*fx*fy)*2-1
        if a.correct_channels:
            sampled={}
            for key,arr in source_channels.items():
                sampled[key]=(arr[iy%sh,ix%sw,:3]*(1-fx)*(1-fy)+arr[iy%sh,(ix+1)%sw,:3]*fx*(1-fy)+arr[(iy+1)%sh,ix%sw,:3]*(1-fx)*fy+arr[(iy+1)%sh,(ix+1)%sw,:3]*fx*fy)
            channel_pixels['BaseColor'][y,x,:3]=sampled['BaseColor']
            channel_pixels['Roughness'][y,x,:3]=sampled['MR'][1]
            channel_pixels['Metallic'][y,x,:3]=sampled['MR'][2]
        value[:2]*=strength;value=unit(value)
        world=unit(st*value[0]+sb*value[1]+sn*value[2]);normal=np.array([np.dot(world,tt),np.dot(world,tb),np.dot(world,tn)])
        if normal[2]<=0:
            normal=np.array([np.dot(sn,tt),np.dot(sn,tb),np.dot(sn,tn)]);fallback+=1
        if normal[2]<=0:normal=np.array([0,0,1])
        pixels[y,x,:3]=unit(normal)*.5+.5;processed[y,x]=True;count+=1
        if count%10000==0:print('TEXELS_REPROJECTED',count,'seconds',round(time.time()-started,1),flush=True)
    if tid%5000==0:print('REPROJECT',tid,count,flush=True)
# Rebuild padding around inverted texels outside occupied UV triangles. Use
# nearest occupied UV normal; this does not alter the valid source projection.
pad=bad & ~processed
remaining=pad.copy();grown=processed.copy()
for _ in range(16):
    for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]:
        py,px=np.nonzero(remaining)
        sy=np.clip(py+dy,0,h-1);sx=np.clip(px+dx,0,w-1);take=grown[sy,sx]
        pixels[py[take],px[take],:3]=pixels[sy[take],sx[take],:3]
        for arr in channel_pixels.values():arr[py[take],px[take],:3]=arr[sy[take],sx[take],:3]
        remaining[py[take],px[take]]=False;grown[py[take],px[take]]=True
    if not remaining.any():break
pixels[remaining,:3]=(.5,.5,1)
image.pixels.foreach_set(pixels.ravel());image.update();image.filepath_raw=str(a.checkpoint.parent/'textures'/'Normal.png');image.file_format='PNG';image.save();image.pack()
for key,arr in channel_pixels.items():
    img=channel_images[key];img.pixels.foreach_set(arr.ravel());img.update();img.filepath_raw=str(a.checkpoint.parent/'textures'/(key+'.png'));img.file_format='PNG';img.save();img.pack()
source.hide_set(True);source.hide_render=True
activate(target);bpy.ops.export_scene.gltf(filepath=str(a.checkpoint.parent/'HIGH.glb'),export_format='GLB',use_selection=True,export_texcoords=True,export_normals=True,export_tangents=True,export_materials='EXPORT',export_animations=False,export_skins=False,export_morph=False)
bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
report={'inverted_texels_before':original_bad,'occupied_texels_reprojected':count,'compatible_surface_missing':incompatible,'source_geometric_normal_fallback':fallback,'padding_texels_repaired':int(pad.sum()),'seconds':time.time()-started,'method':'Nearest source surface with compatible normal, source tangent normal texture transferred to target tangent frame; geometric-normal fallback when shader normal faces away.'}
report['pbr_channels_corrected']=list(channel_pixels)
(a.diagnostics/(a.checkpoint.parent.name+'.normal_reprojection.json')).write_text(json.dumps(report,indent=2));print('REPROJECTION_DONE',report,flush=True)
bp=a.diagnostics/(a.checkpoint.parent.name+'.bake.json')
if bp.exists():
    bake=json.loads(bp.read_text());glb=a.checkpoint.parent/'HIGH.glb'
    bake.update(glb_bytes=glb.stat().st_size,glb_sha256=hashlib.sha256(glb.read_bytes()).hexdigest(),normal_reprojection=report)
    bake['textures']['Normal'].update(min_rgb=pixels[:,:,:3].min((0,1)).tolist(),max_rgb=pixels[:,:,:3].max((0,1)).tolist(),std_rgb=pixels[:,:,:3].std((0,1)).tolist())
    for key,arr in channel_pixels.items():
        bake['textures'][key].update(min_rgb=arr[:,:,:3].min((0,1)).tolist(),max_rgb=arr[:,:,:3].max((0,1)).tolist(),std_rgb=arr[:,:,:3].std((0,1)).tolist())
    bp.write_text(json.dumps(bake,indent=2))
render_views(a.diagnostics/(a.checkpoint.parent.name+'.high'),[target])
