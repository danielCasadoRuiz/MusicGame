"""Repair explicit triangulation before baking; preserve untouched UV charts."""
import argparse,json,sys
from pathlib import Path
import bpy,bmesh
sys.path.insert(0,str(Path(__file__).resolve().parent))
from retopology import stats,deviation
from blender_inspect import activate
from quality_settings import triangle_budget_passed
ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True);ap.add_argument('--diagnostics',type=Path,required=True)
a=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));target=bpy.data.objects['HIGH'];source=bpy.data.objects['SOURCE'];activate(target)
modifier=target.modifiers.new('Explicit_validated_triangulation','TRIANGULATE');modifier.quad_method='BEAUTY';modifier.ngon_method='BEAUTY';bpy.ops.object.modifier_apply(modifier=modifier.name)
target.data.validate(verbose=True,clean_customdata=False);history=[]
for attempt in range(4):
    before=stats(target)
    if not any(before[k] for k in ['boundary_edges','nonmanifold_nonboundary_edges','wire_edges','nonmanifold_vertices','inconsistent_winding_edges','degenerate_faces']):break
    bm=bmesh.new();bm.from_mesh(target.data);bad={v for v in bm.verts if not v.is_manifold}
    for e in bm.edges:
        if not e.is_manifold or not e.is_contiguous:bad.update(e.verts)
    for f in bm.faces:
        if f.calc_area()<1e-14:bad.update(f.verts)
    patch={f for v in bad for f in v.link_faces}
    bmesh.ops.delete(bm,geom=list(patch),context='FACES')
    wires=[e for e in bm.edges if e.is_wire]
    if wires:bmesh.ops.delete(bm,geom=wires,context='EDGES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bounds=[e for e in bm.edges if e.is_boundary];filled=bmesh.ops.holes_fill(bm,edges=bounds,sides=0)['faces']
    result=bmesh.ops.triangulate(bm,faces=[f for f in filled if len(f.verts)>3]);new_faces=set(result['faces'])|{f for f in filled if f.is_valid and len(f.verts)==3}
    layer=bm.loops.layers.uv.active
    for f in new_faces:
        f.smooth=True
        for l in f.loops:l[layer].uv=(0,0)
        for e in f.edges:e.seam=True
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(target.data);bm.free();target.data.update()
    row={'iteration':attempt,'removed_faces':len(patch),'new_faces':len(new_faces),'before':before,'after':stats(target)};history.append(row);print('TRIANGLE_REPAIR',row,flush=True)
final=stats(target);metrics=deviation(source,target);limit=max(source.dimensions)*.005
deviation_within_legacy_limit=max(v['p99'] for v in metrics.values())<=limit
passed=not any(final[k] for k in ['boundary_edges','nonmanifold_nonboundary_edges','wire_edges','nonmanifold_vertices','inconsistent_winding_edges','degenerate_faces']) and triangle_budget_passed(final['triangles'],int(target.get('target_triangles',50000)))
report={'history':history,'high':final,'deviation':metrics,'p99_limit':limit,'deviation_within_legacy_limit':deviation_within_legacy_limit,'deviation_is_hard_gate':False,'passed':passed}
(a.diagnostics/(a.checkpoint.parent.name+'.triangle_preparation.json')).write_text(json.dumps(report,indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
uvpath=a.diagnostics/(a.checkpoint.parent.name+'.uv.json');uv=json.loads(uvpath.read_text());uv['passed']=False;uv['triangle_preparation']=report;uvpath.write_text(json.dumps(uv,indent=2))
if not passed:raise RuntimeError('Explicit triangle repair failed geometry gates')
