"""Deterministic bounded MaxRects packing; uniform scale and 90-degree rotations.

For convex harmonic charts with nearly rectangular contours. Uses chart bounding
boxes conservatively; no UV overlap is introduced by interlocking concavities.
All stored float32 UVs remain subject to the ordinary exact overlap audit.
"""
import math
import numpy as np
from topology_seams import cut_mesh


def place_rectangles(sizes,scale,canvas,gap,ordering,scoring):
    free=[(0.,0.,canvas,canvas)];placed={}
    order=sorted(range(len(sizes)),key=lambda i:(-(sizes[i][0]*sizes[i][1] if ordering=='area' else max(sizes[i])), -min(sizes[i]),i))
    for i in order:
        w,h=sizes[i];options=[]
        for j,(x,y,fw,fh) in enumerate(free):
            for rotated in (False,True):
                rw,rh=((h,w) if rotated else (w,h));rw=rw*scale+gap;rh=rh*scale+gap
                if rw>fw+1e-12 or rh>fh+1e-12:continue
                dw,dh=fw-rw,fh-rh
                score=(min(dw,dh),max(dw,dh)) if scoring=='short' else (fw*fh-rw*rh,min(dw,dh))
                options.append((score+(y,x,int(rotated),j),x,y,rw,rh,rotated))
        if not options:return None
        _,x,y,w,h,rotated=min(options,key=lambda v:v[0]);placed[i]=(x,y,rotated)
        right=x+w;top=y+h;split=[]
        for fx,fy,fw,fh in free:
            fr,ft=fx+fw,fy+fh
            if right<=fx+1e-12 or x>=fr-1e-12 or top<=fy+1e-12 or y>=ft-1e-12:
                split.append((fx,fy,fw,fh));continue
            if x>fx+1e-12:split.append((fx,fy,x-fx,fh))
            if right<fr-1e-12:split.append((right,fy,fr-right,fh))
            if y>fy+1e-12:split.append((fx,fy,fw,y-fy))
            if top<ft-1e-12:split.append((fx,top,fw,ft-top))
        free=[]
        for j,r in enumerate(split):
            rx,ry,rw,rh=r
            if rw<=1e-12 or rh<=1e-12:continue
            contained=False
            for k,t in enumerate(split):
                if j==k:continue
                tx,ty,tw,th=t
                if tx<=rx+1e-12 and ty<=ry+1e-12 and tx+tw>=rx+rw-1e-12 and ty+th>=ry+rh-1e-12:
                    if t!=r or k<j:contained=True;break
            if not contained:free.append(r)
    return placed


def pack_uv(uv,charts,gap):
    """UVs are face-corner arrays; charts are validated seam face components."""
    uv=np.asarray(uv,dtype=float).reshape(-1,3,2);ids=np.unique(charts)
    points=[uv[charts==cid].reshape(-1,2) for cid in ids]
    minima=np.array([p.min(0) for p in points]);sizes=np.array([np.ptp(p,axis=0) for p in points])
    if len(ids)>128:raise ValueError('MaxRects is bounded to 128 charts.')
    if not np.isfinite(uv).all() or (sizes<=0).any():raise ValueError('Invalid chart bounds for packing.')
    canvas=1-gap;upper=math.sqrt(canvas*canvas/float(np.prod(sizes,axis=1).sum()));best=None;trials=[]
    for ordering in ('area','long'):
        for scoring in ('short','area'):
            low=0.;high=upper;placement=None
            for _ in range(28):
                mid=(low+high)*.5;p=place_rectangles(sizes,mid,canvas,gap,ordering,scoring)
                if p is None:high=mid
                else:low=mid;placement=p
            if placement is None:raise ValueError('Atlas rectangles did not fit.')
            trials.append({'order':ordering,'score':scoring,'scale':low})
            if best is None or low>best[0]:best=(low,placement,ordering,scoring)
    scale,placement,ordering,scoring=best;result=np.empty_like(uv)
    for i,cid in enumerate(ids):
        local=uv[charts==cid]-minima[i];x,y,rotated=placement[i]
        if rotated:local=np.stack((sizes[i,1]-local[:,:,1],local[:,:,0]),axis=-1)
        result[charts==cid]=local*scale+np.array([x+gap,y+gap])
    return result.reshape(-1,2),{'method':'MAXRECTS','charts':len(ids),'scale':scale,'ordering':ordering,'scoring':scoring,'trials':trials,'gap_uv':gap,'border_uv':gap,'rotation_degrees':[0,90]}


def pack_object(obj,resolution,margin_pixels):
    """Call in OBJECT mode after Blender average_islands_scale."""
    m=obj.data;tri=np.empty(len(m.loops),np.int32);m.loops.foreach_get('vertex_index',tri);tri=tri.reshape(-1,3)
    le=np.empty(len(m.loops),np.int32);m.loops.foreach_get('edge_index',le)
    seams=np.empty(len(m.edges),bool);m.edges.foreach_get('use_seam',seams)
    _,_,charts,_=cut_mesh(tri,le,seams)
    uv=np.empty(len(m.loops)*2);m.uv_layers.active.data.foreach_get('uv',uv)
    result,report=pack_uv(uv,charts,margin_pixels/resolution)
    m.uv_layers.active.data.foreach_set('uv',result.ravel());m.update();return report
