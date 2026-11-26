import bpy, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\uv_inspection.blend')
img=bpy.data.images.load(r'C:\Users\VISYON\Documents\MusicGame\output\textures\skinTest_variation_v7_rapat.png')
m=bpy.data.materials.new('Rapat v7');m.use_nodes=True
n=m.node_tree.nodes;n.clear();t=n.new('ShaderNodeTexImage');t.image=img;e=n.new('ShaderNodeEmission');out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(t.outputs['Color'],e.inputs[0]);m.node_tree.links.new(e.outputs[0],out.inputs[0])
for o in bpy.data.objects:
 if o.type=='MESH':o.data.materials.clear();o.data.materials.append(m)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE_NEXT';scene.render.resolution_x=600;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('World');scene.world.color=(.15,.15,.15)
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=.35;scene.camera=cam;scene.view_settings.view_transform='Standard'
for name,location in [('front',(0,-1.2,1.65)),('side',(.95,-.65,1.65)),('back',(0,1.2,1.65))]:
 cam.location=location;cam.rotation_euler=(Vector((0,-.005,1.63))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath='C:/Users/VISYON/Documents/MusicGame/output/textures/v7_'+name+'.png';bpy.ops.render.render(write_still=True)
img.pack();bpy.ops.wm.save_as_mainfile(filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\avatar_rapat_v7.blend')
