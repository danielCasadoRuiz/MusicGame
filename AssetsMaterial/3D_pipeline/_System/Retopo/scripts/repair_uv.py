"""Repair remaining UV intersections without unwrapping healthy charts again."""
import argparse,json,sys
from pathlib import Path
import bpy,bmesh,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from uv_seams import uv_audit
from blender_inspect import activate
from retopology import stats

ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True)
ap.add_argument('--diagnostics',type=Path,required=True);ap.add_argument('--smart',action='store_true');a=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint));target=bpy.data.objects['HIGH'];activate(target)
path=a.diagnostics/(a.checkpoint.parent.name+'.uv.json');report=json.loads(path.read_text())
history=[]
for attempt in range(4):
    audit=uv_audit(target);history.append(audit)
    print('LOCAL_UV_AUDIT',attempt,audit['positive_area_overlap_pairs'],audit['degenerate_uv_triangles'],flush=True)
    if not audit['positive_area_overlap_pairs'] and not audit['degenerate_uv_triangles']:break
    bad=set(audit['overlap_face_ids']);m=target.data;m.calc_loop_triangles();uv=m.uv_layers.active
    for t in m.loop_triangles:
        coords=np.array([uv.data[l].uv[:] for l in t.loops])
        if abs(np.cross(coords[1]-coords[0],coords[2]-coords[0]))<2e-12:bad.add(t.polygon_index)
    bm=bmesh.new();bm.from_mesh(m);bm.faces.ensure_lookup_table()
    layer=bm.loops.layers.uv.active
    for v in bm.verts:v.select=False
    for e in bm.edges:e.select=False
    for f in bm.faces:f.select=False
    for i in bad:bm.faces[i].select_set(True)
    originals=[f for f in bm.faces if f.select]
    result=bmesh.ops.triangulate(bm,faces=[f for f in originals if len(f.verts)>3])
    triangles=set(result['faces'])|{f for f in originals if f.is_valid and len(f.verts)==3}
    for f in triangles:f.select=True
    for i,f in enumerate(triangles):
        p=[l.vert.co for l in f.loops];u=(p[1]-p[0]).normalized();normal=(p[1]-p[0]).cross(p[2]-p[0]).normalized();v=normal.cross(u)
        if not a.smart:
            extent=max((p[j]-p[k]).length for j in range(3) for k in range(j))
            local=[((q-p[0]).dot(u)*.01/max(extent,1e-20),(q-p[0]).dot(v)*.01/max(extent,1e-20)) for q in p]
            # Tiny sliver faces still need finite positive UV area after packing.
            local[0]=(0,0);local[1]=(max(local[1][0],.003),0);local[2]=(local[2][0],max(local[2][1],.003))
            for l,(x,y) in zip(f.loops,local):l[layer].uv=(2+i*.05+x,2+y)
        for e in f.edges:
            if not a.smart or not all(g in triangles for g in e.link_faces):e.seam=True
    bm.to_mesh(m);bm.free();m.update()
    bpy.context.tool_settings.mesh_select_mode=(False,False,True)
    bpy.ops.object.mode_set(mode='EDIT')
    if a.smart:
        bpy.context.tool_settings.mesh_select_mode=(False,False,True)
        bpy.ops.uv.smart_project(angle_limit=1.151917, island_margin=0.01,correct_aspect=True)
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='ACTIVE_UDIM',rotate=True,margin_method='FRACTION',margin=32/report['resolution'],shape_method='CONCAVE',scale=True)
    bpy.ops.object.mode_set(mode='OBJECT')
audit=uv_audit(target)
report['local_repair_audits']=history;report['uv_audits'].append(audit);report['high']=stats(target)
report['passed']=audit['finite'] and min(audit['min'])>=0 and max(audit['max'])<=1 and not audit['positive_area_overlap_pairs'] and not audit['degenerate_uv_triangles']
path.write_text(json.dumps(report,indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
m=target.data;m.calc_loop_triangles();layer=m.uv_layers.active
np.savez_compressed(str(a.diagnostics/(a.checkpoint.parent.name+'.uv_geometry.npz')),triangles=np.array([[list(layer.data[i].uv) for i in t.loops] for t in m.loop_triangles]))
print('LOCAL_UV_RESULT',report['passed'],audit['islands'],flush=True)
if not report['passed']:raise RuntimeError('UV repair failed unchanged gate')
