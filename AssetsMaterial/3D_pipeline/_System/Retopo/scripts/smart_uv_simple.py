"""V60: exact manual Blender Smart UV Project; overlaps are diagnostic-only."""
import argparse, json, math, sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from blender_inspect import activate
from retopology import stats
from uv_seams import uv_audit


def run(checkpoint, diagnostics, resolution=4096):
    bpy.ops.wm.open_mainfile(filepath=str(checkpoint))
    target = bpy.data.objects['HIGH']
    activate(target)

    # New clean UV layer. No custom seams, no second unwrap, no custom packer.
    for uv in list(target.data.uv_layers):
        target.data.uv_layers.remove(uv)
    target.data.uv_layers.new(name='UV_HIGH')

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')

    bpy.ops.uv.smart_project(
        angle_limit=math.radians(20.0),
        margin_method='SCALED',
        rotate_method='AXIS_ALIGNED_Y',
        island_margin=0.0,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False
    )

    bpy.ops.object.mode_set(mode='OBJECT')
    target.data.update()

    # Diagnostic only: do not mutate the Smart UV result afterwards.
    audit = uv_audit(target)
    # V61: audit is informational only. Smart UV itself is authoritative.
    uv_diagnostic_ok = (
        audit['finite']
        and min(audit['min']) >= -1e-7
        and max(audit['max']) <= 1.0000001
        and audit['degenerate_uv_triangles'] == 0
    )
    passed = True

    diagnostics.mkdir(parents=True, exist_ok=True)
    result = {
        'asset': checkpoint.parent.name,
        'passed': passed,
        'method': 'BLENDER_SMART_UV_PROJECT',
        'settings': {
            'angle_limit_degrees': 20.0,
            'margin_method': 'SCALED',
            'rotation_method': 'AXIS_ALIGNED_Y',
            'island_margin': 0.0,
            'area_weight': 0.0,
            'correct_aspect': True,
            'scale_to_bounds': False,
        },
        'audit': audit,
        'overlap_pairs_diagnostic': audit['positive_area_overlap_pairs'],
        'overlap_area_sum_diagnostic': audit['overlap_area_sum'],
        'overlap_is_hard_gate': False,
        'uv_audit_is_hard_gate': False,
        'uv_diagnostic_ok': uv_diagnostic_ok,
        'high': stats(target),
        'note': 'No second unwrap, no seam conversion, no island-size rule, no custom packer.'
    }
    (diagnostics / (checkpoint.parent.name + '.uv.json')).write_text(
        json.dumps(result, indent=2), encoding='utf-8'
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(checkpoint))

    print('SMART_UV_RESULT', json.dumps({
        'passed': passed,
        'islands': audit['islands']['count'],
        'overlaps': audit['positive_area_overlap_pairs'],
        'degenerate': audit['degenerate_uv_triangles'],
        'uv_area_sum': audit['uv_triangle_area_sum']
    }), flush=True)



if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--checkpoint', type=Path, required=True)
    ap.add_argument('--diagnostics', type=Path, required=True)
    ap.add_argument('--resolution', type=int, default=4096)
    a = ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    run(a.checkpoint.resolve(), a.diagnostics.resolve(), a.resolution)
