"""Positive-weight harmonic UVs for disk charts, with a convex boundary.

NumPy-only sparse conjugate gradients; no external pip dependencies in Blender.
Final UVs are still audited after float32 storage and packing.
"""
import numpy as np
from topology_seams import cut_mesh,chart_summary


def cg_solve(diagonal,rows,cols,b,tolerance=1e-10,max_iterations=4000):
    n=len(diagonal)
    def matvec(x):return diagonal*x-np.bincount(rows,weights=x[cols],minlength=n)
    x=np.zeros(n);r=b.copy();z=r/diagonal;p=z.copy();rz=float(np.dot(r,z));limit=tolerance*max(float(np.linalg.norm(b)),1.)
    for iteration in range(max_iterations):
        if np.linalg.norm(r)<=limit:return x,iteration
        q=matvec(p);denom=float(np.dot(p,q))
        if denom<=0:raise ValueError('Harmonic chart matrix is not positive definite.')
        alpha=rz/denom;x+=alpha*p;r-=alpha*q;z=r/diagonal;next_rz=float(np.dot(r,z));p=z+(next_rz/rz)*p;rz=next_rz
    raise ValueError('Harmonic chart solver did not converge within budget.')


def harmonic_uv(co,tri,loop_edges,seams):
    faces,edges,charts,original_verts=cut_mesh(tri,loop_edges,seams);summaries=chart_summary(faces,edges,charts)
    if any(not r['disk'] for r in summaries):raise ValueError('Harmonic UVs require verified disk charts.')
    uv=np.zeros((int(faces.max())+1,2));rows_report=[];edge_charts=charts[edges[:,2]]
    for info in summaries:
        cid=info['chart'];fs=np.flatnonzero(charts==cid);es=np.flatnonzero(edge_charts==cid);verts=np.unique(faces[fs]);lookup={int(v):i for i,v in enumerate(verts)}
        e=edges[es];boundary=e[e[:,3]<0];following={int(u):int(v) for u,v in boundary[:,:2]}
        start=int(boundary[0,0]);ordered=[start];v=following[start]
        while v!=start:
            if len(ordered)>len(boundary):raise ValueError('Chart boundary is not a simple loop.')
            ordered.append(v);v=following[v]
        if len(ordered)!=len(boundary) or len(set(ordered))!=len(ordered):raise ValueError('Chart has a pinched boundary.')
        boundary=np.array(ordered,np.int32);points=co[original_verts[boundary]];lengths=np.linalg.norm(np.roll(points,-1,axis=0)-points,axis=1)
        # Prevent tiny spatial boundary edges collapsing in float32 UV storage.
        lengths=np.maximum(lengths,lengths.mean()*.5);angles=2*np.pi*np.r_[0,np.cumsum(lengths[:-1])]/lengths.sum()
        local_uv=np.zeros((len(verts),2));bids=np.array([lookup[int(v)] for v in boundary]);# Strictly convex rounded rectangles pack more tightly than ellipses.
        # x=(1+k/4)cos(t)-(k/4)cos(3t), y=(1+k/4)sin(t)+(k/4)sin(3t).
        # k<0.5 keeps positive curvature, avoiding exactly straight UV edges.
        k=.45;c=np.cos(angles);q=np.sin(angles)
        local_uv[bids]=np.column_stack((c*(1+k*q*q),q*(1+k*c*c)))
        # Convex rounded rectangles follow each panel's spatial aspect without concavity.
        values=np.linalg.svd(co[original_verts[verts]]-co[original_verts[verts]].mean(0),compute_uv=False)
        aspect=float(np.clip(values[0]/max(values[1],1e-12),1.,4.))
        local_uv[bids,0]*=aspect
        boundary_mask=np.zeros(len(verts),bool);boundary_mask[bids]=True;interior=np.flatnonzero(~boundary_mask)
        u=np.array([lookup[int(v)] for v in e[:,0]]);v=np.array([lookup[int(v)] for v in e[:,1]])
        row=np.r_[u,v];col=np.r_[v,u];degree=np.bincount(row,minlength=len(verts)).astype(float)
        index=np.full(len(verts),-1,np.int32);index[interior]=np.arange(len(interior));both=(index[row]>=0)&(index[col]>=0);to_boundary=(index[row]>=0)&boundary_mask[col]
        rhs=np.zeros((len(interior),2));np.add.at(rhs,index[row[to_boundary]],local_uv[col[to_boundary]])
        iterations=[]
        for axis in range(2):
            if len(interior):local_uv[interior,axis],count=cg_solve(degree[interior],index[row[both]],index[col[both]],rhs[:,axis]);iterations.append(count)
        uv[verts]=local_uv
        rows_report.append({'chart':cid,'faces':len(fs),'boundary_vertices':len(boundary),'interior_vertices':len(interior),'iterations':iterations})
    return uv[faces].reshape(-1,2),rows_report
