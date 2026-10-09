"""Phase 4: bake source PBR/shading detail into UV_HIGH, export GLB.

Scalar channels use EMIT baking from the imported Principled inputs, preserving
GLTF factors and texture channel selection. Normal bake keeps the original
normal-map shader connected. Export uses the SAME explicit triangulation as
the bake, and combines Roughness(G)/Metallic(B) with glTF's exporter.
"""
import argparse, hashlib, json, sys, time
from pathlib import Path
import bpy, numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import activate,render_views
from retopology import stats
from mathutils.bvhtree import BVHTree


def switch_emission(mat, channel):
    nt=mat.node_tree;out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
    original=[(l.from_socket,l.to_socket) for l in list(nt.links) if l.to_node==out and l.to_socket.name=='Surface']
    for link in list(out.inputs['Surface'].links):nt.links.remove(link)
    shader=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    emission=nt.nodes.new('ShaderNodeEmission')
    socket=shader.inputs.get(channel)
    if channel=='__coverage__':emission.inputs['Color'].default_value=(1,1,1,1)
    elif socket.is_linked:nt.links.new(socket.links[0].from_socket,emission.inputs['Color'])
    else:
        v=socket.default_value
        emission.inputs['Color'].default_value=(float(v),float(v),float(v),1) if isinstance(v,(float,int)) else v
    nt.links.new(emission.outputs['Emission'],out.inputs['Surface'])
    return original,emission


def restore(mat,record):
    links,node=record;nt=mat.node_tree;nt.nodes.remove(node)
    for a,b in links:nt.links.new(a,b)


def target_material(obj):
    mat=bpy.data.materials.new('HIGH_PBR');mat.use_nodes=True
    obj.data.materials.clear();obj.data.materials.append(mat)
    return mat


def connect_material(mat,images):
    nt=mat.node_tree;bsdf=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    positions={'BaseColor':(-600,300),'Normal':(-600,-300),'Roughness':(-600,60),'Metallic':(-600,-100)}
    nodes={}
    for name,img in images.items():
        node=nt.nodes.new('ShaderNodeTexImage');node.image=img;node.label=name;node.location=positions[name];nodes[name]=node
    nt.links.new(nodes['BaseColor'].outputs['Color'],bsdf.inputs['Base Color'])
    nt.links.new(nodes['Roughness'].outputs['Color'],bsdf.inputs['Roughness'])
    nt.links.new(nodes['Metallic'].outputs['Color'],bsdf.inputs['Metallic'])
    normal=nt.nodes.new('ShaderNodeNormalMap');normal.space='TANGENT';normal.uv_map='UV_HIGH'
    nt.links.new(nodes['Normal'].outputs['Color'],normal.inputs['Color']);nt.links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])



def build_adaptive_cage(source, target, scale):
    """Build a same-topology cage around target using nearest SOURCE distance.

    The cage is not a second remesh. It is a temporary duplicate of the final
    triangulated target. Each cage vertex is moved along the target vertex normal
    by a local distance derived from the nearest SOURCE surface point. This gives
    flatter/close regions a tight cage and allows more room only where retopo has
    moved farther from SOURCE.
    """
    source.data.calc_loop_triangles()
    source_to_target = target.matrix_world.inverted() @ source.matrix_world
    src_verts = [source_to_target @ v.co for v in source.data.vertices]
    src_faces = [tuple(p.vertices) for p in source.data.polygons]
    bvh = BVHTree.FromPolygons(src_verts, src_faces, all_triangles=False)

    # Blender 4.5 no longer exposes Mesh.calc_normals(); vertex normals are
    # maintained by the mesh runtime. Force an update and cache them BEFORE
    # deforming the cage so every offset uses the original target normal.
    target.data.update()
    target_normals = [v.normal.copy() for v in target.data.vertices]

    base_pad = scale * 0.0005       # 0.05% minimum breathing room
    hard_cap = scale * 0.02         # never let one bad nearest-match explode the cage
    offsets = []
    for v in target.data.vertices:
        hit = bvh.find_nearest(v.co)
        dist = float(hit[3]) if hit and hit[3] is not None else base_pad
        offsets.append(min(hard_cap, max(base_pad, dist * 1.35 + base_pad)))

    # One conservative neighbour dilation pass prevents a single low offset at a
    # coarse vertex from pinching the cage between nearby vertices that need room.
    neighbours=[set() for _ in target.data.vertices]
    for e in target.data.edges:
        a,b=e.vertices; neighbours[a].add(b); neighbours[b].add(a)
    dilated=offsets[:]
    for i,ns in enumerate(neighbours):
        if ns:
            dilated[i]=max(offsets[i], max(offsets[j] for j in ns)*0.75)

    cage=target.copy(); cage.data=target.data.copy(); bpy.context.collection.objects.link(cage)
    cage.name='BAKE_CAGE'; cage.hide_render=True; cage.hide_set(True)
    for v,n,off in zip(cage.data.vertices,target_normals,dilated):
        if n.length_squared > 1e-20:
            v.co += n.normalized() * off
    cage.data.update()

    report={
        'mode':'adaptive_nearest_source_custom_cage',
        'base_padding':base_pad,
        'hard_cap':hard_cap,
        'min_offset':min(dilated) if dilated else 0.0,
        'max_offset':max(dilated) if dilated else 0.0,
        'mean_offset':float(sum(dilated)/len(dilated)) if dilated else 0.0,
        'vertices':len(dilated)
    }
    print('BAKE_CAGE_DIAGNOSTIC',json.dumps(report),flush=True)
    return cage,report

def bake_phase(checkpoint,diagnostics,resolution=4096,render=True,multi_cage=False,working_scale=1.0):
    started=time.time();outdir=checkpoint.parent;bpy.ops.wm.open_mainfile(filepath=str(checkpoint))
    source=bpy.data.objects['SOURCE'];quads=bpy.data.objects['HIGH']
    if working_scale<=0:raise ValueError('working_scale must be positive')
    from mathutils import Matrix
    original_matrices={o:o.matrix_world.copy() for o in [source,quads]}
    original_meshes={o:o.data for o in [source,quads]}
    for obj in [source,quads]:
        obj.data=obj.data.copy();obj.data.transform(Matrix.Scale(working_scale,4))
        obj.matrix_world.translation*=working_scale
    bpy.context.view_layer.update()
    quads.name='HIGH_QUADS';quads.hide_render=True;quads.hide_set(True)
    target=quads.copy();target.data=quads.data.copy();bpy.context.collection.objects.link(target)
    target.name='HIGH';target.hide_render=False;target.hide_set(False);activate(target)
    modifier=target.modifiers.new('Bake_export_same_triangulation','TRIANGULATE')
    modifier.quad_method='BEAUTY';modifier.ngon_method='BEAUTY';modifier.keep_custom_normals=True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    triangulated=stats(target)
    print('BAKE_TRIANGULATION_DIAGNOSTIC',triangulated,flush=True)
    mat=target_material(target);nt=mat.node_tree
    receiver=nt.nodes.new('ShaderNodeTexImage');receiver.name='BAKE_RECEIVER'
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8
    scene.cycles.device='CPU';scene.render.threads_mode='FIXED';scene.render.threads=8
    scene.render.bake.use_selected_to_active=True;scene.render.bake.margin=4
    scene.render.bake.margin_type='EXTEND';scene.render.bake.use_clear=True
    scale=max(source.dimensions)
    # SIMPLE ORIGINAL BAKE:
    # one selected-to-active bake per map, using Blender's ordinary cage extrusion.
    # This intentionally removes adaptive custom cages, coverage pre-passes,
    # distance ladders and multi-pass compositing.
    scene.render.bake.use_cage=True
    scene.render.bake.cage_object=None
    scene.render.bake.cage_extrusion=scale*.004
    scene.render.bake.max_ray_distance=scale*.012
    print('BAKE_SIMPLE_SETTINGS',{
        'use_cage':scene.render.bake.use_cage,
        'cage_extrusion':scene.render.bake.cage_extrusion,
        'max_ray_distance':scene.render.bake.max_ray_distance,
        'margin':scene.render.bake.margin
    },flush=True)

    source.hide_set(False);source.hide_render=False
    bpy.ops.object.select_all(action='DESELECT');source.select_set(True);target.select_set(True)
    bpy.context.view_layer.objects.active=target
    images={};reports={};texdir=outdir/'textures';texdir.mkdir(parents=True,exist_ok=True)
    materials={slot.material for slot in source.material_slots if slot.material}

    for channel in ['BaseColor','Roughness','Metallic','Normal']:
        img=bpy.data.images.new(channel,width=resolution,height=resolution,alpha=False,float_buffer=False)
        img.colorspace_settings.name='sRGB' if channel=='BaseColor' else 'Non-Color'
        receiver.image=img
        for n in nt.nodes:n.select=False
        receiver.select=True;nt.nodes.active=receiver
        records={}
        if channel!='Normal':
            socket={'BaseColor':'Base Color','Roughness':'Roughness','Metallic':'Metallic'}[channel]
            records={m:switch_emission(m,socket) for m in materials}
        print('BAKE_START',channel,resolution,flush=True)
        try:
            # Keep the exact same simple selected-to-active projection for every channel.
            scene.render.bake.use_selected_to_active=True
            scene.render.bake.use_cage=True
            scene.render.bake.cage_object=None
            scene.render.bake.cage_extrusion=scale*.004
            scene.render.bake.max_ray_distance=scale*.012
            bpy.ops.object.bake(type='NORMAL' if channel=='Normal' else 'EMIT',
                  normal_space='TANGENT',normal_r='POS_X',normal_g='POS_Y',normal_b='POS_Z')
        finally:
            for m,record in records.items():restore(m,record)
        pixels=np.empty(resolution*resolution*4,dtype=np.float32);img.pixels.foreach_get(pixels)
        pixels=pixels.reshape(-1,4)
        if not np.isfinite(pixels).all():raise RuntimeError('Non-finite bake pixels')
        reports[channel]={'resolution':[resolution,resolution],'colorspace':img.colorspace_settings.name,
          'min_rgb':pixels[:,:3].min(0).tolist(),'max_rgb':pixels[:,:3].max(0).tolist(),
          'std_rgb':pixels[:,:3].std(0).tolist()}
        img.filepath_raw=str(texdir/(channel+'.png'));img.file_format='PNG';img.save()
        images[channel]=img
        print('BAKE_DONE',channel,reports[channel],flush=True)

    nt.nodes.remove(receiver);connect_material(mat,images)
    target.data.transform(Matrix.Scale(1/working_scale,4));target.data.update()
    target.matrix_world=original_matrices[quads].copy()
    for obj,matrix in original_matrices.items():
        temporary=obj.data;obj.data=original_meshes[obj];obj.matrix_world=matrix
        bpy.data.meshes.remove(temporary)
    bpy.context.view_layer.update()
    # HIGH_QUADS was only a non-triangulated working backup for the bake.
    # It must not survive into the export scene: a hidden object can retain selection
    # state in Blender, which can make selection-based export ambiguous.
    if quads.name in bpy.data.objects:
        quads_mesh=quads.data
        bpy.data.objects.remove(quads,do_unlink=True)
        if quads_mesh.users==0:
            bpy.data.meshes.remove(quads_mesh)
    source.hide_render=True;source.hide_set(True)
    # Force selection explicitly at datablock level, including hidden objects.
    for obj in list(bpy.context.scene.objects):
        try: obj.select_set(False)
        except Exception: pass
    target.hide_render=False;target.hide_set(False);target.select_set(True)
    bpy.context.view_layer.objects.active=target
    for img in images.values():img.pack()
    print('PRE_EXPORT_MESH_OBJECTS',[(o.name,o.hide_get(),o.hide_render,o.select_get()) for o in bpy.context.scene.objects if o.type=='MESH'],flush=True)
    print('PRE_EXPORT_SELECTED',[o.name for o in bpy.context.selected_objects],flush=True)
    # SOURCE remains in the checkpoint for optional later source-based corrections,
    # but the GLB export selection is exactly HIGH and nothing else.
    glb=outdir/'HIGH.glb'
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,
       export_texcoords=True,export_normals=True,export_tangents=True,export_materials='EXPORT',
       export_image_format='AUTO',export_animations=False,export_skins=False,export_morph=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(outdir/'04_baked.blend'))
    result={'asset':outdir.name,'blender':bpy.app.version_string,'high':stats(target),
            'textures':reports,'glb_bytes':glb.stat().st_size,
            'glb_sha256':hashlib.sha256(glb.read_bytes()).hexdigest(),
            'working_scale':working_scale,'cage_extrusion':scale*.004/working_scale,'max_ray_distance':scale*.012/working_scale,'bake_margin_pixels':4,
             'coverage_missing_fraction':None,'multi_cage':False,
             'projection_mode':'SIMPLE_ORIGINAL_CAGE_EXTRUSION',
             'ray_selection':'Single selected-to-active bake per map; Blender cage extrusion only',
            'normal_space':'Tangent +X +Y +Z (OpenGL/glTF); source normal texture included in source shader',
            'specular':'No KHR_materials_specular in original inputs; no fabricated map.',
            'seconds':time.time()-started,'status':'Baked/exported; requires roundtrip and appearance verification.'}
    diagnostics.mkdir(parents=True,exist_ok=True)
    (diagnostics/(outdir.name+'.bake.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    if render:render_views(diagnostics/(outdir.name+'.high'),[target])
    print('BAKE_EXPORT_COMPLETE',outdir.name,flush=True)


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True)
    ap.add_argument('--diagnostics',type=Path,default=Path('diagnostics'));ap.add_argument('--resolution',type=int,default=4096)
    ap.add_argument('--no-render',action='store_true')
    ap.add_argument('--multi-cage',action='store_true')
    ap.add_argument('--working-scale',type=float,default=1.0)
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    bake_phase(args.checkpoint.resolve(),args.diagnostics.resolve(),args.resolution,not args.no_render,args.multi_cage,args.working_scale)
