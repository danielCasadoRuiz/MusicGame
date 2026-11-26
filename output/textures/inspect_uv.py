import bpy, json
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r'C:\Users\VISYON\Documents\AvatarLab\AvatarLab\Assets\AvatarLab\Canonical\Reference\MusicGame_MakeHuman_Canonical_Reference.fbx')
for o in bpy.data.objects:
 if o.type=='MESH':
  print('MESH',o.name,len(o.data.vertices),len(o.data.polygons),'UV',len(o.data.uv_layers))
  print('BOUNDS', [list(v) for v in (o.matrix_world@Vector(o.bound_box[0]),o.matrix_world@Vector(o.bound_box[6]))])
  print('MATERIALS',[m.name for m in o.data.materials])
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\uv_inspection.blend')
