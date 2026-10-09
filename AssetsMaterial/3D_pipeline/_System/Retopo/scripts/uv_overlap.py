"""Bounded-memory positive-area UV overlap detection, independent of Blender.

No global set of candidate pairs. Degenerate triangles are excluded here and
reported by the caller. Oversized boxes never expand into unbounded grid cells.
"""
import numpy as np


def cross2(a,b): return a[0]*b[1]-a[1]*b[0]


def clip_area(a,b):
    poly=[p for p in a];sign=1 if cross2(b[1]-b[0],b[2]-b[0])>=0 else -1
    for k in range(3):
        if not poly:return 0.0
        start=b[k];edge=b[(k+1)%3]-start;new=[];last=poly[-1];last_d=sign*cross2(edge,last-start)
        for current in poly:
            current_d=sign*cross2(edge,current-start)
            if (current_d>=0)!=(last_d>=0):
                denom=last_d-current_d
                if abs(denom)>1e-20:new.append(last+(current-last)*(last_d/denom))
            if current_d>=0:new.append(current)
            last=current;last_d=current_d
        poly=new
    if len(poly)<3:return 0.0
    p=np.asarray(poly);return abs(float(np.sum(p[:,0]*np.roll(p[:,1],-1)-p[:,1]*np.roll(p[:,0],-1))))*.5


def find_overlaps(tri,area,max_candidates=5000000,batch_size=4096,bins=128):
    tri=np.asarray(tri);minima=tri.min(axis=1);maxima=tri.max(axis=1)
    grid={};large=[];processed=[];affected=set();count=0;total_area=0.;tested=0;raw_candidates=0;complete=True
    raw_limit=max(50000000,len(tri)*1000)
    for i in range(len(tri)):
        if area[i]<1e-12 or not np.isfinite(tri[i]).all():continue
        lower=np.floor(minima[i]*bins).astype(np.int64);upper=np.floor(maxima[i]*bins).astype(np.int64)
        spans=upper-lower+1;oversized=int(spans[0])*int(spans[1])>64
        if oversized:candidates=set(processed)
        else:
            candidates=set(large)
            for x in range(int(lower[0]),int(upper[0])+1):
                for y in range(int(lower[1]),int(upper[1])+1):candidates.update(grid.get((x,y),()))
        raw_candidates+=len(candidates)
        if raw_candidates>raw_limit:complete=False;break
        ids=np.fromiter(candidates,dtype=np.int64);del candidates
        for begin in range(0,len(ids),batch_size):
            js=ids[begin:begin+batch_size]
            keep=np.all(np.minimum(maxima[js],maxima[i])-np.maximum(minima[js],minima[i])>1e-10,axis=1);js=js[keep]
            tested+=len(js)
            if tested>max_candidates:complete=False;break
            if not len(js):continue
            a=tri[js];b=np.broadcast_to(tri[i],a.shape)
            edges=np.concatenate((a-np.roll(a,1,axis=1),b-np.roll(b,1,axis=1)),axis=1)
            axes=edges[:,:,[1,0]]*np.array([-1,1]);pa=np.einsum('nvd,nad->nva',a,axes);pb=np.einsum('nvd,nad->nva',b,axes)
            keep=np.all(np.minimum(pa.max(1),pb.max(1))-np.maximum(pa.min(1),pb.min(1))>1e-12,axis=1)
            for j in js[keep]:
                intersection=clip_area(tri[j],tri[i])
                if intersection>1e-11:count+=1;total_area+=intersection;affected.update((int(j),i))
        if not complete:break
        if oversized:large.append(i)
        else:
            for x in range(int(lower[0]),int(upper[0])+1):
                for y in range(int(lower[1]),int(upper[1])+1):grid.setdefault((x,y),[]).append(i)
        processed.append(i)
    return {'positive_area_overlap_pairs':count,'overlap_area_sum':total_area,
            'affected_triangle_ids':sorted(affected),'overlap_audit_complete':complete,
            'candidate_pairs_tested':tested,'raw_grid_candidates':raw_candidates,'raw_grid_candidates_limit':raw_limit,'candidate_pairs_limit':max_candidates,
            'algorithm':'streamed spatial-grid queries, bounded per-triangle candidates and 4096-pair batches'}
