import bpy
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\VISYON\Documents\MusicGame\output\textures\uv_inspection.blend')
o=bpy.data.objects['Body'];uv=o.data.uv_layers.active.data
for z in [1.48,1.50,1.52,1.54,1.56,1.60]:
 vs=sorted(o.data.vertices,key=lambda v:(o.matrix_world@v.co-Vector((0,.05,z))).length)[:1]
 for v in vs:print(z,tuple(o.matrix_world@v.co),[tuple(uv[l.index].uv) for l in o.data.loops if l.vertex_index==v.index])
