import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\uv_inspection.blend')
img=bpy.data.images.load(r'C:\Users\VISYON\Documents\AvatarLab\AvatarLab\Assets\AvatarLab\Textures\Avatar\skinTest_variation_v5.png')
m=bpy.data.materials.new('Original');m.use_nodes=True
n=m.node_tree.nodes;n.clear();t=n.new('ShaderNodeTexImage');t.image=img;e=n.new('ShaderNodeEmission');out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(t.outputs['Color'],e.inputs[0]);m.node_tree.links.new(e.outputs[0],out.inputs[0])
for o in bpy.data.objects:
 if o.type=='MESH':o.data.materials.clear();o.data.materials.append(m)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE_NEXT';scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('World');scene.world.color=(0.15,0.15,0.15)
bpy.ops.object.camera_add(location=(0,-1.2,1.65));cam=bpy.context.object;cam.rotation_euler=(Vector((0,-.01,1.63))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=.36;scene.camera=cam
scene.view_settings.view_transform='Standard';scene.render.filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\original_head.png';bpy.ops.render.render(write_still=True)
body=bpy.data.objects['Body'];uv=body.data.uv_layers.active.data
for label,p in [('browL',(-.034,-.125,1.669)),('browR',(.034,-.125,1.669)),('forehead',(0,-.1,1.70)),('crown',(0,0,1.75)),('back',(0,.07,1.69))]:
 target=Vector(p);v=min(body.data.vertices,key=lambda v:(body.matrix_world@v.co-target).length)
 coords=[tuple(uv[l.index].uv) for poly in body.data.polygons for l in [body.data.loops[i] for i in poly.loop_indices] if l.vertex_index==v.index]
 print(label,tuple(v.co),coords[:6])

