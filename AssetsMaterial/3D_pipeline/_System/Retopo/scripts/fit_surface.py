"""Bidirectional surface fitting of NEW topology to source, bounded per iteration."""
import argparse,json,sys
from pathlib import Path
import numpy as np
import bpy,bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from retopology import samples,stats,deviation
from quality_settings import triangle_budget_passed

def fit(source,target,points):
    m=target.data;m.calc_loop_triangles()
    before=np.array([tuple(v.co) for v in m.vertices]);tri=np.array([tuple(t.vertices) for t in m.loop_triangles])
    tree=BVHTree.FromPolygons([Vector(p) for p in before],tri.tolist(),all_triangles=True)
    accum=np.zeros_like(before);weights=np.zeros(len(before))
    for p in points:
        q,n,index,d=tree.find_nearest(p)
        if q is None:continue
        ids=tri[index];a,b,c=before[ids];q=np.array(q)
        v0=b-a;v1=c-a;v2=q-a
        d00=np.dot(v0,v0);d01=np.dot(v0,v1);d11=np.dot(v1,v1);d20=np.dot(v2,v0);d21=np.dot(v2,v1)
        denom=d00*d11-d01*d01
        if denom<=1e-20:continue
        v=(d11*d20-d01*d21)/denom;w=(d00*d21-d01*d20)/denom
        bary=np.maximum([1-v-w,v,w],0);bary/=bary.sum()
        delta=np.array(p)-q
        for idx,weight in zip(ids,bary):accum[idx]+=weight*delta;weights[idx]+=weight
    delta=accum/np.maximum(weights[:,None],1e-12)
    shortest=np.full(len(before),np.inf)
    for e in m.edges:
        a,b=e.vertices;l=np.linalg.norm(before[a]-before[b]);shortest[a]=min(shortest[a],l);shortest[b]=min(shortest[b],l)
    lengths=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,shortest*.3/np.maximum(lengths,1e-20))[:,None]
    proposed=before+delta*.8
    orig=np.cross(before[tri[:,1]]-before[tri[:,0]],before[tri[:,2]]-before[tri[:,0]])
    for _ in range(12):
        norm=np.cross(proposed[tri[:,1]]-proposed[tri[:,0]],proposed[tri[:,2]]-proposed[tri[:,0]])
        bad=np.einsum('ij,ij->i',orig,norm)<=0
        if not bad.any():break
        ids=np.unique(tri[bad]);proposed[ids]=before[ids]
    for i,v in enumerate(m.vertices):v.co=proposed[i]
    m.update()

ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True)
ap.add_argument('--diagnostics',type=Path,required=True);a=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
bpy.ops.wm.open_mainfile(filepath=str(a.checkpoint))
source=bpy.data.objects['SOURCE'];target=bpy.data.objects['HIGH'];name=a.checkpoint.parent.name
path=a.diagnostics/(name+'.retopology.json');r=json.loads(path.read_text())
points=samples(source,60000,seed=71)
metrics=deviation(source,target);bestscore=max(d['p99'] for d in metrics.values());best=target.data.copy();bestmetrics=metrics
history=[]
for i in range(30):
    fit(source,target,points)
    if i%3==2:
        metrics=deviation(source,target);score=max(d['p99'] for d in metrics.values())
        print('FIT',i,score,'limit',r['p99_limit'],flush=True);history.append({'iteration':i,'deviation':metrics})
        if score<bestscore:best=target.data.copy();bestscore=score;bestmetrics=metrics
        if score<=r['p99_limit']:break
target.data=best
# Remove isolated duplicate triangles; fill only true boundary cycles.
bm=bmesh.new();bm.from_mesh(target.data)
isolated=[f for f in bm.faces if all(e.is_boundary for e in f.edges)]
if isolated:bmesh.ops.delete(bm,geom=isolated,context='FACES')
bounds=[e for e in bm.edges if e.is_boundary]
if bounds:bmesh.ops.holes_fill(bm,edges=bounds,sides=0)
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(target.data);bm.free();target.data.update()
r['deviation']=deviation(source,target);r['surface_fit']=history;r['high']=stats(target)
r['deviation_within_legacy_limit']=max(d['p99'] for d in r['deviation'].values())<=r['p99_limit'];r['deviation_is_hard_gate']=False
r['triangle_budget_within_legacy_tolerance']=triangle_budget_passed(r['high']['triangles'],int(r.get('requested_triangles',50000)));r['triangle_budget_is_hard_gate']=False
r['quality_gate_passed']=not any(r['high'][k] for k in ['boundary_edges','nonmanifold_nonboundary_edges','wire_edges','nonmanifold_vertices','inconsistent_winding_edges','degenerate_faces'])
path.write_text(json.dumps(r,indent=2));bpy.ops.wm.save_as_mainfile(filepath=str(a.checkpoint))
print('FIT_RESULT',r['quality_gate_passed'],r['high'],flush=True)
if not r['quality_gate_passed']:raise RuntimeError('Fitting still has structurally invalid topology')
