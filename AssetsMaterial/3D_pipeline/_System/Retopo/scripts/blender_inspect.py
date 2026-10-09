"""Background Blender inspection. Never saves or modifies source GLB files."""
import argparse, json, sys, math
from pathlib import Path
import bpy, bmesh
from mathutils import Vector


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
    bpy.context.view_layer.objects.active=obj


def inspect(path, dest, render=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    objects=[]
    for o in bpy.context.scene.objects:
        r={'name':o.name,'type':o.type,'matrix_world':[list(row) for row in o.matrix_world],
           'dimensions':list(o.dimensions),
           'bbox_world':[list(o.matrix_world@Vector(c)) for c in o.bound_box]}
        if o.type=='MESH':
            m=o.data;m.calc_loop_triangles()
            r.update(vertices=len(m.vertices),edges=len(m.edges),polygons=len(m.polygons),
                     triangles=len(m.loop_triangles),uv_sets=[u.name for u in m.uv_layers],
                     normals={'has_custom_normals':m.has_custom_normals,'domain':'corner/split normals'},
                     shape_keys=[k.name for k in m.shape_keys.key_blocks] if m.shape_keys else [],
                     vertex_groups=[v.name for v in o.vertex_groups],
                     modifiers=[{'type':mod.type,'name':mod.name} for mod in o.modifiers],
                     material_slots=[s.material.name if s.material else None for s in o.material_slots])
            if m.uv_layers:
                try:
                    m.calc_tangents(uvmap=m.uv_layers[0].name)
                    r['tangents']={'computable':True,'note':'Computed by Blender; source encoding inspected in raw JSON.'}
                except Exception as e:r['tangents']={'computable':False,'error':str(e)}
            # Weld a separate geometry-only copy, not the imported source.
            bm=bmesh.new();bm.from_mesh(m)
            before=len(bm.verts)
            bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=max(o.dimensions)*1e-7)
            r['welded_topology']={'vertices':len(bm.verts),'merged_vertices':before-len(bm.verts),
                  'boundary_edges':sum(e.is_boundary for e in bm.edges),
                  'wire_edges':sum(e.is_wire for e in bm.edges),
                  'nonmanifold_nonboundary_edges':sum(not e.is_manifold and not e.is_boundary for e in bm.edges)}
            unseen=set(bm.verts);components=[]
            while unseen:
                stack=[unseen.pop()];size=0
                while stack:
                    v=stack.pop();size+=1
                    for e in v.link_edges:
                        n=e.other_vert(v)
                        if n in unseen:unseen.remove(n);stack.append(n)
                components.append(size)
            r['welded_topology']['connected_components']=sorted(components,reverse=True)
            bm.free()
        if o.type=='ARMATURE':r['bones']=[b.name for b in o.data.bones]
        objects.append(r)
    mats=[]
    for m in bpy.data.materials:
        mats.append({'name':m.name,'use_nodes':m.use_nodes,
          'nodes':[{'name':n.name,'type':n.bl_idname,
                    'image':n.image.name if n.type=='TEX_IMAGE' and n.image else None,
                    'inputs':{s.name:str(s.default_value) for s in n.inputs if hasattr(s,'default_value') and not s.is_linked}}
                    for n in m.node_tree.nodes] if m.use_nodes else [],
          'links':[{'from':l.from_node.name+':'+l.from_socket.name,'to':l.to_node.name+':'+l.to_socket.name}
                   for l in m.node_tree.links] if m.use_nodes else []})
    result={'source':path.name,'blender_version':bpy.app.version_string,'binary':bpy.app.binary_path,
            'objects':objects,'materials':mats,
            'images':[{'name':i.name,'size':list(i.size),'colorspace':i.colorspace_settings.name,
                       'source':i.source,'packed':bool(i.packed_file)} for i in bpy.data.images],
            'axes':'Blender +Z up after GLTF import. Anatomical front requires visual inspection.'}
    dest.mkdir(parents=True,exist_ok=True)
    (dest/(path.stem+'.blender.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('INSPECTED',path.name,[(o['name'],o.get('triangles'),o.get('welded_topology')) for o in objects],flush=True)
    if render:
        render_views(dest/path.stem)


def render_views(prefix, objects=None, material=True, resolution=512):
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=False
    scene.render.threads_mode='FIXED';scene.render.threads=8
    scene.render.resolution_x=resolution;scene.render.resolution_y=resolution;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Standard'
    scene.world=bpy.data.worlds.new('InspectionWorld');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(0.13,0.13,0.13,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.7
    objs=objects or [o for o in scene.objects if o.type=='MESH' and not o.hide_render]
    bounds=[o.matrix_world@Vector(v) for o in objs for v in o.bound_box]
    lo=Vector(tuple(min(v[a] for v in bounds) for a in range(3)))
    hi=Vector(tuple(max(v[a] for v in bounds) for a in range(3)))
    center=(hi+lo)/2;size=max(hi-lo)
    if not material:
        mat=bpy.data.materials.new('InspectionClay');mat.diffuse_color=(0.6,0.65,0.7,1)
        for obj in objs:obj.data.materials.clear();obj.data.materials.append(mat)
    light=bpy.data.lights.new('InspectionLight','AREA');light.energy=450;light.size=size*2
    lamp=bpy.data.objects.new(light.name,light);scene.collection.objects.link(lamp)
    lamp.location=center+Vector((size,-size,size*1.5))
    lamp.rotation_euler=(center-lamp.location).to_track_quat('-Z','Y').to_euler()
    camera=bpy.data.cameras.new('InspectionCamera');cam=bpy.data.objects.new(camera.name,camera)
    scene.collection.objects.link(cam);scene.camera=cam;camera.type='ORTHO';camera.ortho_scale=size*1.17
    for name, direction in [('front',(0,-1,0)),('back',(0,1,0)),('side',(1,0,0))]:
        cam.location=center+Vector(direction)*size*3
        cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(prefix)+'.'+name+'.png'
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam,do_unlink=True);bpy.data.objects.remove(lamp,do_unlink=True)


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--input',type=Path,default=Path('input'))
    ap.add_argument('--diagnostics',type=Path,default=Path('diagnostics'));ap.add_argument('--render',action='store_true')
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    paths=[args.input] if args.input.is_file() else sorted(args.input.glob('*.glb'))
    for path in paths:inspect(path.resolve(),args.diagnostics.resolve(),args.render)
