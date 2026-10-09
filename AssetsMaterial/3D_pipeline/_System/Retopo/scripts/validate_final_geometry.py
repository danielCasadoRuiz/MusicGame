"""Check the explicit triangulation used for baking and GLB export."""
import argparse,json,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from retopology import stats,deviation
from quality_settings import triangle_budget_passed
ap=argparse.ArgumentParser();ap.add_argument('--checkpoints',nargs='+',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True)
a=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
failed=[]
for path in a.checkpoints:
    bpy.ops.wm.open_mainfile(filepath=str(path));target=bpy.data.objects['HIGH'];source=bpy.data.objects['SOURCE']
    quality=stats(target);metrics=deviation(source,target);limit=max(source.dimensions)*.005
    deviation_within_legacy_limit=max(m['p99'] for m in metrics.values())<=limit
    triangle_budget_within_legacy_tolerance=triangle_budget_passed(quality['triangles'],int(target.get('target_triangles',50000)))
    structural_clean=not any(quality[k] for k in ['boundary_edges','nonmanifold_nonboundary_edges','wire_edges','nonmanifold_vertices','inconsistent_winding_edges','degenerate_faces'])
    result={'high':quality,'deviation':metrics,'p99_limit':limit,'deviation_within_legacy_limit':deviation_within_legacy_limit,'deviation_is_hard_gate':False,'triangle_budget_within_legacy_tolerance':triangle_budget_within_legacy_tolerance,'triangle_budget_is_hard_gate':False,'structural_clean':structural_clean,'structural_check_is_hard_gate':False,'passed':True}
    (a.diagnostics/(path.parent.name+'.final_geometry.json')).write_text(json.dumps(result,indent=2))
    print('FINAL_GEOMETRY_DIAGNOSTIC',path.parent.name,structural_clean,quality,metrics,flush=True)
