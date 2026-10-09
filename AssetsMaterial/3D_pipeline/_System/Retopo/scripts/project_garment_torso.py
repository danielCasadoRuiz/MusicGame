"""Resolve inner-wall color hits using an outer front/back cage on the torso.

The original local bake is retained on collar/sleeves and where the directed
projection has no source hit. Color and scalar PBR channels use identical blend.
"""
import argparse,json,sys,time,hashlib
from pathlib import Path
import bpy,numpy as np
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from bake_export import switch_emission,restore
from blender_inspect import activate
ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True);ap.add_argument('--normal-only',action='store_true');a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);started=time.time();bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));source=bpy.data.objects['SOURCE'];target=bpy.data.objects['HIGH'];scene=bpy.context.scene;mat=target.active_material;nt=mat.node_tree
keys=['Normal'] if a.normal_only else ['BaseColor','Roughness','Metallic']
images={key:next(n.image for n in nt.nodes if n.type=='TEX_IMAGE' and n.image and n.image.name==key) for key in keys};w,h=next(iter(images.values())).size
bounds=[source.matrix_world@Vector(c) for c in source.bound_box]
center=np.array([(min(v[i] for v in bounds)+max(v[i] for v in bounds))/2 for i in range(3)])
width=max(v.x for v in bounds)-min(v.x for v in bounds)
reference_center=np.array([-0.00016251206398010254, -0.0009760037064552307, -0.0046530067920684814]);reference_width=1.8998330235481262
working_scale=190.0/max(source.dimensions)
m=target.data;m.calc_loop_triangles();uv=m.uv_layers.active;alpha=np.zeros((h,w),np.float32);occupied=np.zeros((h,w),bool)
for tri in m.loop_triangles:
 coords=np.array([uv.data[i].uv[:] for i in tri.loops])*[w,h]-.5;lo=np.maximum(np.ceil(coords.min(0)).astype(int),0);hi=np.minimum(np.floor(coords.max(0)).astype(int),[w-1,h-1])
 if np.any(hi<lo):continue
 yy,xx=np.mgrid[lo[1]:hi[1]+1,lo[0]:hi[0]+1];xx=xx.ravel();yy=yy.ravel();e0=coords[1]-coords[0];e1=coords[2]-coords[0];det=e0[0]*e1[1]-e0[1]*e1[0]
 if abs(det)<1e-12:continue
 p=np.column_stack((xx,yy))-coords[0];b=(p[:,0]*e1[1]-p[:,1]*e1[0])/det;c=(e0[0]*p[:,1]-e0[1]*p[:,0])/det;weights=np.column_stack((1-b-c,b,c));keep=np.all(weights>=-1e-6,axis=1);xx=xx[keep];yy=yy[keep];weights=weights[keep];co=np.array([target.matrix_world@m.vertices[i].co for i in tri.vertices]);world=(weights@co-center)*(reference_width/width)+reference_center;blend=np.minimum(np.clip((.35-world[:,2])/.05,0,1),np.clip((.42-np.abs(world[:,0]))/.06,0,1));alpha[yy,xx]=blend;occupied[yy,xx]=True
originals={obj:(obj.data,obj.matrix_world.copy()) for obj in [source,target]}
for obj in [source,target]:obj.data=obj.data.copy();obj.data.transform(Matrix.Scale(working_scale,4));obj.matrix_world.translation*=working_scale
bpy.context.view_layer.update();cage=bpy.data.objects.new('TorsoFrontBackCage',target.data.copy());scene.collection.objects.link(cage);cage.matrix_world=target.matrix_world.copy();cage.data.materials.clear();cage.hide_render=True;cage.hide_set(True)
for v in cage.data.vertices:v.co.y+=(1 if (cage.matrix_world@v.co).y>=center[1]*working_scale else -1)*max(source.dimensions)*.1
cage.data.update()
# Neutral receiver avoids evaluating the previous target PBR maps during bake.
neutral=bpy.data.materials.new('ProjectionReceiver');neutral.use_nodes=True;target.data.materials.clear();target.data.materials.append(neutral);temp=bpy.data.images.new('TorsoProjection',width=w,height=h,alpha=False);receiver=neutral.node_tree.nodes.new('ShaderNodeTexImage');receiver.image=temp;neutral.node_tree.nodes.active=receiver
for obj in scene.objects:obj.select_set(False)
source.hide_set(False);source.hide_render=False;source.select_set(True);target.hide_set(False);target.select_set(True);bpy.context.view_layer.objects.active=target
scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.device='CPU';scene.render.threads_mode='FIXED';scene.render.threads=8;scene.render.bake.use_selected_to_active=True;scene.render.bake.use_cage=True;scene.render.bake.cage_object=cage;scene.render.bake.max_ray_distance=max(source.dimensions)*.25;scene.render.bake.margin=0
rec=switch_emission(source.active_material,'__coverage__');bpy.ops.object.bake(type='EMIT');restore(source.active_material,rec);data=np.empty(w*h*4,np.float32);temp.pixels.foreach_get(data);hits=data.reshape(h,w,4)[:,:,0]>.5;alpha*=hits
# Apply the same domain to gutters; native margin provides source samples.
scene.render.bake.margin=12;grown=occupied.copy()
for _ in range(12):
 frontier=~grown
 for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]:
  shifted=np.roll(grown,(dy,dx),(0,1));take=frontier&shifted;alpha[take]=np.roll(alpha,(dy,dx),(0,1))[take];grown[take]=True;frontier[take]=False
blend=alpha[:,:,None]
normal_flips=0
for key,socket in ([('Normal',None)] if a.normal_only else [('BaseColor','Base Color'),('Roughness','Roughness'),('Metallic','Metallic')]):
 print('TORSO_PROJECT',key,flush=True);temp.colorspace_settings.name='sRGB' if key=='BaseColor' else 'Non-Color'
 if key=='Normal':bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',normal_r='POS_X',normal_g='POS_Y',normal_b='POS_Z')
 else:
  rec=switch_emission(source.active_material,socket);bpy.ops.object.bake(type='EMIT');restore(source.active_material,rec)
 temp.pixels.foreach_get(data);projected=data.reshape(h,w,4);img=images[key];old=np.empty(w*h*4,np.float32);img.pixels.foreach_get(old);old=old.reshape(h,w,4)
 if key=='Normal':
  flipped=(projected[:,:,2]<.5)&(alpha>0);normal_flips=int(np.count_nonzero(flipped&occupied));projected[flipped,:3]=1-projected[flipped,:3]
  normal=(old[:,:,:3]*2-1)*(1-blend)+(projected[:,:,:3]*2-1)*blend;normal/=np.maximum(np.linalg.norm(normal,axis=2)[:,:,None],1e-12);old[:,:,:3]=normal*.5+.5;del normal
 else:old[:,:,:3]=old[:,:,:3]*(1-blend)+projected[:,:,:3]*blend
 img.pixels.foreach_set(old.ravel());img.update();img.filepath_raw=str(a.checkpoint.parent/'textures'/(key+'.png'));img.file_format='PNG';img.save();img.pack();del old
scene.render.bake.cage_object=None;bpy.data.objects.remove(cage,do_unlink=True)
for obj,(data_mesh,matrix) in originals.items():temporary=obj.data;obj.data=data_mesh;obj.matrix_world=matrix;bpy.data.meshes.remove(temporary)
bpy.context.view_layer.update();source.hide_render=True;source.hide_set(True);activate(target)
bpy.ops.export_scene.gltf(filepath=str(a.checkpoint.parent/'HIGH.glb'),export_format='GLB',use_selection=True,export_texcoords=True,export_normals=True,export_tangents=True,export_materials='EXPORT',export_animations=False,export_skins=False,export_morph=False);bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
report={'method':('Outer front/back Y cage tangent normals on torso; negative tangent vectors reversed for the existing double-sided mesh and blended with local normals' if a.normal_only else 'Outer front/back Y cage, composed on torso with matched BaseColor/Roughness/Metallic domain; collar and sleeves retain local bake'),'working_scale':working_scale,'normalized_source_center':center.tolist(),'source_width':width,'torso_full_region':{'abs_x_max':.36,'z_max':.30},'blend_end':{'abs_x':.42,'z':.35},'projected_occupied_texels':int(np.count_nonzero((alpha>0)&occupied)),'missing_source_hits_retaining_local_bake':int(np.count_nonzero(occupied&~hits)),'seconds':time.time()-started,'channels':keys,'normal_backface_tangent_flips':normal_flips}
a.diagnostics.mkdir(exist_ok=True,parents=True);(a.diagnostics/(a.checkpoint.parent.name+('.torso_normal_projection.json' if a.normal_only else '.torso_projection.json'))).write_text(json.dumps(report,indent=2));print('TORSO_PROJECTION_DONE',report,flush=True)
