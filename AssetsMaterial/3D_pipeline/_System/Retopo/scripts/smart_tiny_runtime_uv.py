import argparse, json, math, sys
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from blender_inspect import activate
from uv_seams import uv_audit

ap = argparse.ArgumentParser()
ap.add_argument('--checkpoint', type=Path, required=True)
ap.add_argument('--diagnostics', type=Path, required=True)
ap.add_argument('--resolution', type=int, default=4096)
ap.add_argument('--gap', type=int, default=8)
ap.add_argument('--angle', type=float, default=5.0)
a = ap.parse_args(sys.argv[sys.argv.index('--')+1:])

report_path = a.diagnostics/(a.checkpoint.parent.name+'.uv.json')

bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint))
obj = bpy.data.objects['HIGH']
activate(obj)

while obj.data.uv_layers:
    obj.data.uv_layers.remove(obj.data.uv_layers[0])
obj.data.uv_layers.new(name='UV_HIGH')

mesh = obj.data
mesh.calc_loop_triangles()
tri_count = len(mesh.loop_triangles)

bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(
    angle_limit=math.radians(a.angle),
    island_margin=a.gap/a.resolution,
    area_weight=0.0,
    correct_aspect=True,
    scale_to_bounds=True
)
bpy.ops.object.mode_set(mode='OBJECT')

# Diagnostic only. Never reject/retry/repack based on this.
audit = uv_audit(obj)
row = {
    'angle_degrees': a.angle,
    'triangles': tri_count,
    'islands': int(audit['islands']['count']),
    'occupancy': float(audit['uv_triangle_area_sum']),
    'degenerate_uv_triangles': int(audit['degenerate_uv_triangles']),
    'overlaps': int(audit['positive_area_overlap_pairs'])
}

result = {
    'passed': True,
    'mode': 'SMART_UV_5_DEG_CLEAN_BASELINE',
    'resolution': a.resolution,
    'packing_margin_pixels': a.gap,
    'uv_audits': [audit],
    'one_shot': row,
    'audit_is_diagnostic_only': True
}

bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
report_path.write_text(json.dumps(result, indent=2), encoding='utf-8')
(a.diagnostics/'SMART_UV_SUMMARY.json').write_text(
    json.dumps(result, indent=2),
    encoding='utf-8'
)
print('SMART_UV_DONE',json.dumps(row),flush=True)
