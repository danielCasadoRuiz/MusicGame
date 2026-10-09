"""Open UV charts to disks using a boundary-contracted tree/cotree cut graph.

Input/output refer to existing edges: geometry and triangle counts are unchanged.
Existing panel boundaries remain. Only non-disk charts receive extra edge cuts.
"""
import heapq
import numpy as np


class DSU:
    def __init__(self,n):self.p=list(range(n));self.rank=[0]*n
    def root(self,i):
        p=self.p
        while p[i]!=i:p[i]=p[p[i]];i=p[i]
        return i
    def union(self,a,b):
        a=self.root(a);b=self.root(b)
        if a==b:return False
        if self.rank[a]<self.rank[b]:a,b=b,a
        self.p[b]=a
        if self.rank[a]==self.rank[b]:self.rank[a]+=1
        return True


def cut_mesh(tri,loop_edges,seams):
    n=len(tri);flat=tri.ravel();order=np.argsort(loop_edges,kind='stable');edge_sorted=loop_edges[order]
    starts=np.r_[0,np.flatnonzero(np.diff(edge_sorted))+1];ends=np.r_[starts[1:],len(order)]
    corners=DSU(3*n);faces=DSU(n);edge_sides=[]
    nxt=lambda i:i//3*3+(i%3+1)%3
    for start,end in zip(starts,ends):
        ids=order[start:end];e=int(edge_sorted[start])
        if len(ids)>2:raise ValueError('Nonmanifold edge in UV topology.')
        if len(ids)==2 and not seams[e]:
            a,b=map(int,ids);faces.union(a//3,b//3)
            if flat[a]==flat[b]:corners.union(a,b);corners.union(nxt(a),nxt(b))
            else:corners.union(a,nxt(b));corners.union(nxt(a),b)
            edge_sides.append((a,b,e))
        else:
            for a in ids:edge_sides.append((int(a),-1,e))
    roots=np.fromiter((corners.root(i) for i in range(3*n)),dtype=np.int32,count=3*n)
    unique,renumber=np.unique(roots,return_inverse=True);cut_faces=renumber.reshape(n,3)
    face_roots=np.fromiter((faces.root(i) for i in range(n)),dtype=np.int32,count=n)
    _,chart=np.unique(face_roots,return_inverse=True)
    edges=np.array([(renumber[a],renumber[nxt(a)],a//3,b//3 if b>=0 else -1,e) for a,b,e in edge_sides],dtype=np.int32)
    return cut_faces,edges,chart,flat[unique]


def chart_summary(cut_faces,edges,chart):
    reports=[];boundary_dsu=DSU(int(cut_faces.max())+1)
    for u,v,f,g,e in edges:
        if g<0:boundary_dsu.union(int(u),int(v))
    edge_charts=chart[edges[:,2]]
    for cid in range(int(chart.max())+1):
        fs=np.flatnonzero(chart==cid);es=np.flatnonzero(edge_charts==cid);verts=np.unique(cut_faces[fs]);be=edges[es][edges[es,3]<0]
        bv=np.unique(be[:,:2]) if len(be) else np.array([],np.int32)
        loops=len({boundary_dsu.root(int(v)) for v in bv});chi=len(verts)-len(es)+len(fs)
        reports.append({'chart':cid,'faces':len(fs),'vertices':len(verts),'edges':len(es),'boundary_loops':loops,'chi':chi,
                        'genus':(2-loops-chi)/2,'disk':chi==1 and loops==1})
    return reports


def disk_cuts(co,tri,loop_edges,seams):
    cut_faces,edges,chart,original_verts=cut_mesh(tri,loop_edges,seams)
    reports=chart_summary(cut_faces,edges,chart);new=set();rows=[]
    edge_charts=chart[edges[:,2]];extent=np.ptp(co,axis=0);scale=max(float(extent.max()),1e-12)
    for info in reports:
        if info['disk']:continue
        cid=info['chart'];es=np.flatnonzero(edge_charts==cid);fs=np.flatnonzero(chart==cid);verts=np.unique(cut_faces[fs]);local={int(v):i for i,v in enumerate(verts)}
        be=[int(e) for e in es if edges[e,3]<0];boundary={int(v) for e in be for v in edges[e,:2]}
        graph={int(v):[] for v in verts};costs={}
        center=co[original_verts[verts]].mean(axis=0)
        for e in es:
            u,v,f,g,orig=map(int,edges[e])
            if g<0:continue
            pu,pv=co[original_verts[[u,v]]];length=float(np.linalg.norm(pu-pv))
            # Bias cuts toward the back (-Y is the front) while preserving a
            # positive geodesic metric. Panel boundaries are roots, not new cuts.
            mid=(pu+pv)*.5;hidden=(mid[1]-center[1])/scale;cost=length*(2-np.clip(hidden*4,-.8,.8))+scale*1e-9
            graph[u].append((v,int(e),cost));graph[v].append((u,int(e),cost));costs[int(e)]=cost
        roots=boundary or {int(verts[np.argmax(co[original_verts[verts],1])])}
        dist={v:0. for v in roots};parents={};heap=[(0.,v) for v in roots];heapq.heapify(heap)
        while heap:
            distance,u=heapq.heappop(heap)
            if distance!=dist[u]:continue
            for v,e,cost in graph[u]:
                value=distance+cost
                if value<dist.get(v,float('inf')):dist[v]=value;parents[v]=(u,e);heapq.heappush(heap,(value,v))
        if len(dist)!=len(verts):raise ValueError('Disconnected primal chart graph.')
        tree={e for u,e in parents.values()};dual=DSU(len(fs));face_local={int(f):i for i,f in enumerate(fs)};cotree=set()
        for e in sorted(costs,key=lambda e:costs[e],reverse=True):
            if e in tree:continue
            f,g=map(int,edges[e,2:4])
            if dual.union(face_local[f],face_local[g]):cotree.add(e)
        if len(cotree)!=len(fs)-1:raise ValueError('Chart dual did not form a cotree; inspect topology.')
        leftover=set(costs)-tree-cotree;cut=set(leftover)
        for e in leftover:
            for start in map(int,edges[e,:2]):
                v=start
                while v in parents:
                    v,parent_edge=parents[v];cut.add(parent_edge)
        if not boundary and not leftover:
            # Closed genus-zero chart: a long slit opens it without isolating
            # a triangle. Usually panel front/back boundaries already avoid it.
            v=max(dist,key=dist.get)
            while v in parents:v,e=parents[v];cut.add(e)
        origs={int(edges[e,4]) for e in cut};new.update(origs)
        rows.append(dict(info,handle_links=len(leftover),extra_seam_edges=len(origs)))
    return sorted(new),reports,rows


def ensure_disks(co,tri,loop_edges,seams,max_passes=3):
    seams=seams.copy();history=[]
    for iteration in range(max_passes):
        new,reports,rows=disk_cuts(co,tri,loop_edges,seams)
        history.append({'iteration':iteration,'charts':len(reports),'non_disk_charts':sum(not r['disk'] for r in reports),
                        'added_edges':sum(not seams[e] for e in new),'repairs':rows})
        if all(r['disk'] for r in reports):return seams,history,reports
        if not new:break
        seams[new]=True
    cut_faces,edges,chart,_=cut_mesh(tri,loop_edges,seams);reports=chart_summary(cut_faces,edges,chart)
    if any(not r['disk'] for r in reports):raise ValueError('Topology cut graph did not produce disk charts.')
    return seams,history,reports
