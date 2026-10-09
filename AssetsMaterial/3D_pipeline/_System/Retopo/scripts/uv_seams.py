"""Phase 3: garment-aware cuts, concealed longitudinal paths, unwrap and pack.

Profiles assume Blender Z-up and front=-Y for the uploaded garments. The shoe
is lengthwise along X. These assumptions are configurable, not universal.
UV overlap detection uses positive-area triangle intersections, not a raster
test. Failed UV charts receive local cuts and are unwrapped again.
"""
import argparse, heapq, json, math, sys
from pathlib import Path
import bpy, bmesh, numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import activate
from retopology import stats


def profile_for(name):
    if 'Trouse' in name:return 'trousers'
    if 'shoe' in name:return 'shoe'
    if 'Hood' in name:return 'hood'
    return 'upper'


def concealed_path(bm, face_set, axis, preferred, center, width):
    verts={v for f in face_set for v in f.verts}
    if len(verts)<10:return 0
    coords=[v.co[axis] for v in verts];low=min(coords);high=max(coords)
    if high-low<width*.02:return 0
    def penalty(v):
        # Favour the anatomically hidden facing side of the chart.
        normal_score=sum(v.normal[i]*preferred[i] for i in range(3))
        lateral=sum(((v.co[i]-center[i])/width)**2 for i in range(3) if i!=axis and abs(preferred[i])<.1)
        return 1+8*(1-normal_score)**2+8*lateral
    start=min((v for v in verts if v.co[axis]<=low+(high-low)*.035),key=penalty)
    end=min((v for v in verts if v.co[axis]>=high-(high-low)*.035),key=penalty)
    heap=[(0,start.index)];dist={start.index:0};prev={};allowed={v.index for v in verts}
    while heap:
        value,idx=heapq.heappop(heap)
        if value!=dist[idx]:continue
        if idx==end.index:break
        v=bm.verts[idx]
        for e in v.link_edges:
            w=e.other_vert(v)
            if w.index not in allowed or not any(f in face_set for f in e.link_faces):continue
            cost=e.calc_length()*(penalty(v)+penalty(w))/2
            candidate=value+cost
            if candidate<dist.get(w.index,float('inf')):
                dist[w.index]=candidate;prev[w.index]=(idx,e);heapq.heappush(heap,(candidate,w.index))
    count=0;idx=end.index
    while idx in prev:
        idx,e=prev[idx];e.seam=True;count+=1
    return count


def mark_seams(obj,profile):
    bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table();bm.faces.ensure_lookup_table();bm.normal_update()
    lo=np.min([tuple(v.co) for v in bm.verts],axis=0);hi=np.max([tuple(v.co) for v in bm.verts],axis=0)
    size=hi-lo;center=(hi+lo)/2;labels={};parts={}
    for f in bm.faces:
        p=f.calc_center_median();x=(p.x-center[0])/size[0];z=(p.z-lo[2])/size[2]
        if profile=='trousers':
            label='waist' if z>.70 else 'leg_left' if x<0 else 'leg_right'
            if z>.97 and f.normal.z>.65:label='waist_cap'
            if z<.04 and f.normal.z<-.65:label='foot_cap_left' if x<0 else 'foot_cap_right'
        elif profile=='upper':
            label='torso'
            if abs(x)>.205 and z>.53:label='arm_left' if x<0 else 'arm_right'
            if abs(x)>.475:label='cuff_left' if x<0 else 'cuff_right'
            if z>.91 and abs(x)<.16:label='collar'
            if z<.025:label='hem'
        elif profile=='shoe':
            label='sole' if z<.27 else 'upper'
            if f.normal.z<-.7:label='bottom'
        else:
            radial=np.array(p)-center
            label='lining' if float(np.dot(radial,np.array(f.normal)))<0 else 'fur'
            if z<.025:label='bottom'
        # Closed AI clothing often has inner walls and many handles. Split
        # anatomical panels along natural garment side seams before ABF; a
        # single longitudinal cut is insufficient for those surfaces.
        if profile in ('trousers','upper'):
            label+=':back' if p.y>center[1] else ':front'
            if abs(f.normal.z)>.82:label+=':horizontal'
        elif profile=='shoe':
            label+=':medial' if p.y>center[1] else ':lateral'
        elif profile=='hood':
            label+=':back' if p.y>center[1] else ':front'
        labels[f.index]=label;parts.setdefault(label,set()).add(f)
    for e in bm.edges:
        e.seam=e.is_boundary or (len(e.link_faces)==2 and labels[e.link_faces[0].index]!=labels[e.link_faces[1].index])
        # Isolate steep fold junctions, cap rims and small appendages.
        if len(e.link_faces)==2 and e.calc_face_angle()>math.radians(70):e.seam=True
    paths={}
    for name,faces in parts.items():
        if 'cap' in name or name in ['hem','bottom']:continue
        if name.startswith('arm') or name.startswith('cuff'):
            axis=0;preferred=(0,0,-1)
        elif name.startswith('leg'):
            axis=2;preferred=(1,0,0) if 'left' in name else (-1,0,0)
        elif profile=='shoe':axis=0;preferred=(0,0,-1) if name=='sole' else (0,1,0)
        else:axis=2;preferred=(0,1,0)
        paths[name]=concealed_path(bm,faces,axis,preferred,center,float(max(size)))
    count=sum(e.seam for e in bm.edges);bm.to_mesh(obj.data);bm.free();obj.data.update()
    return {'profile':profile,'front_axis':'-Y','parts':{k:len(v) for k,v in parts.items()},
            'concealed_paths_edges':paths,'seam_edges':count}


def unwrap_pack(obj,resolution=4096,method='ANGLE_BASED'):
    activate(obj)
    print('UNWRAP_START',flush=True)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    options=dict(method=method,fill_holes=True,correct_aspect=True,margin=0.001)
    if method=='MINIMUM_STRETCH':options.update(no_flip=True,iterations=30)
    bpy.ops.uv.unwrap(**options)
    bpy.ops.uv.average_islands_scale()
    print('PACK_START',flush=True)
    bpy.ops.uv.pack_islands(rotate=True,margin_method='FRACTION',margin=32/resolution,
                           shape_method='CONCAVE',scale=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    print('PACK_DONE',flush=True)


def clip_area(a,b):
    # Sutherland-Hodgman convex clipping; shared edges have zero area.
    poly=[p for p in a]
    sign=1 if np.cross(b[1]-b[0],b[2]-b[0])>=0 else -1
    for k in range(3):
        if not poly:return 0.0
        start=b[k];edge=b[(k+1)%3]-start;new=[];last=poly[-1]
        last_d=sign*np.cross(edge,last-start)
        for current in poly:
            current_d=sign*np.cross(edge,current-start)
            if (current_d>=0)!=(last_d>=0):
                denom=last_d-current_d
                if abs(denom)>1e-20:new.append(last+(current-last)*(last_d/denom))
            if current_d>=0:new.append(current)
            last=current;last_d=current_d
        poly=new
    if len(poly)<3:return 0.0
    p=np.array(poly);return abs(float(np.sum(p[:,0]*np.roll(p[:,1],-1)-p[:,1]*np.roll(p[:,0],-1))))*.5


def uv_audit(obj):
    mesh=obj.data;mesh.calc_loop_triangles();layer=mesh.uv_layers.active
    uvs=np.empty(len(mesh.loops)*2);layer.data.foreach_get('uv',uvs);uvs=uvs.reshape(-1,2)
    loops=np.empty(len(mesh.loop_triangles)*3,dtype=np.int32);mesh.loop_triangles.foreach_get('loops',loops)
    tri=uvs[loops.reshape(-1,3)];area=np.abs(np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]))*.5
    minima=tri.min(axis=1);maxima=tri.max(axis=1);grid={};pairs=set();overlaps=[]
    bins=128
    for i in range(len(tri)):
        lower=np.floor(minima[i]*bins).astype(int);upper=np.floor(maxima[i]*bins).astype(int)
        for x in range(lower[0],upper[0]+1):
            for y in range(lower[1],upper[1]+1):
                key=(x,y)
                pairs.update(j*len(tri)+i for j in grid.get(key,[]))
                grid.setdefault(key,[]).append(i)
    encoded=np.fromiter(pairs,dtype=np.int64);del pairs,grid
    for begin in range(0,len(encoded),100000):
        packed=encoded[begin:begin+100000];left=packed//len(tri);right=packed%len(tri)
        keep=np.all(np.minimum(maxima[left],maxima[right])-np.maximum(minima[left],minima[right])>1e-10,axis=1)
        left=left[keep];right=right[keep]
        if not len(left):continue
        a=tri[left];b=tri[right]
        edges=np.concatenate((a-np.roll(a,1,axis=1),b-np.roll(b,1,axis=1)),axis=1)
        axes=edges[:,:,[1,0]]*np.array([-1,1])
        pa=np.einsum('nvd,nad->nva',a,axes);pb=np.einsum('nvd,nad->nva',b,axes)
        keep=np.all(np.minimum(pa.max(1),pb.max(1))-np.maximum(pa.min(1),pb.min(1))>1e-12,axis=1)
        for i,j in zip(left[keep],right[keep]):
            intersection=clip_area(tri[i],tri[j])
            if intersection>1e-11:overlaps.append((int(i),int(j),intersection))
    face_ids=sorted({mesh.loop_triangles[i].polygon_index for a,b,_ in overlaps for i in (a,b)})
    parent=list(range(len(tri)))
    def root(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    shared={}
    for i,t in enumerate(np.round(tri,7)):
        for a,b in [(0,1),(1,2),(2,0)]:
            key=tuple(sorted((tuple(t[a]),tuple(t[b]))))
            if key in shared:parent[root(i)]=root(shared[key])
            else:shared[key]=i
    chart_sizes={}
    for i in range(len(tri)):
        r=root(i);chart_sizes[r]=chart_sizes.get(r,0)+1
    sizes=sorted(chart_sizes.values(),reverse=True)
    return {'finite':bool(np.isfinite(uvs).all()),'min':uvs.min(0).tolist(),'max':uvs.max(0).tolist(),
       'degenerate_uv_triangles':int((area<1e-12).sum()),'uv_triangle_area_sum':float(area.sum()),
       'positive_area_overlap_pairs':len(overlaps),'overlap_area_sum':sum(a[2] for a in overlaps),
       'overlap_face_ids':face_ids,'intersection_area_tolerance':1e-11,
       'islands':{'count':len(sizes),'top_20_triangles':sum(sizes[:20]),'total_triangles':len(tri),
                  'single_triangle_islands':sum(s==1 for s in sizes),'top_20_sizes':sizes[:20]}}


def uv_phase(checkpoint,output,diagnostics,resolution=4096,method='ANGLE_BASED',attempts=8,explicit_profile=None):
    bpy.ops.wm.open_mainfile(filepath=str(checkpoint))
    target=bpy.data.objects['HIGH'];source=bpy.data.objects['SOURCE'];activate(target)
    # QuadriFlow can leave small boundary loops despite closed source input.
    bm=bmesh.new();bm.from_mesh(target.data)
    isolated=[f for f in bm.faces if all(e.is_boundary for e in f.edges)]
    if isolated:bmesh.ops.delete(bm,geom=isolated,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bounds=[e for e in bm.edges if e.is_boundary]
    repaired=0
    if bounds:
        faces=bmesh.ops.holes_fill(bm,edges=bounds,sides=0)['faces'];repaired=len(faces)
        remaining=[e for e in bm.edges if e.is_boundary]
        if remaining:
            extra=bmesh.ops.triangle_fill(bm,edges=remaining,use_beauty=True)['geom']
            faces.extend(g for g in extra if isinstance(g,bmesh.types.BMFace));repaired=len(faces)
        print('REPAIR_BOUNDARY',len(bounds),'filled',repaired,'remaining',sum(e.is_boundary for e in bm.edges),flush=True)
        bmesh.ops.triangulate(bm,faces=[f for f in faces if len(f.verts)>4]);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(target.data);target.data.update()
    bm.free()
    if stats(target)['boundary_edges']:raise RuntimeError('Open HIGH after repair: '+str(stats(target)))
    for uv in list(target.data.uv_layers):target.data.uv_layers.remove(uv)
    target.data.uv_layers.new(name='UV_HIGH')
    profile=explicit_profile or profile_for(output.name);seams=mark_seams(target,profile)
    history=[]
    for attempt in range(attempts):
        unwrap_pack(target,resolution,method)
        bpy.ops.wm.save_as_mainfile(filepath=str(output/'03_uv_unverified.blend'))
        audit=uv_audit(target);history.append(audit)
        print('UV_AUDIT',attempt,json.dumps({k:v for k,v in audit.items() if k!='overlap_face_ids'}),flush=True)
        if not audit['positive_area_overlap_pairs'] and not audit['degenerate_uv_triangles']:break
        # Cut the PERIMETER of failing patches, keeping neighbours connected.
        # Isolating every face would make a technically valid but unreadable UV.
        face_set=set(audit['overlap_face_ids']);mesh=target.data
        edge_faces={}
        for face in mesh.polygons:
            for loop in face.loop_indices:edge_faces.setdefault(mesh.loops[loop].edge_index,[]).append(face.index)
        for edge_id,faces in edge_faces.items():
            if any(f in face_set for f in faces) and not all(f in face_set for f in faces):mesh.edges[edge_id].use_seam=True
        if attempt>=4 and face_set:
            # A folded/nonplanar quad may overlap its OWN UV triangulation.
            # Edge seams alone cannot fix its internal diagonal.
            bm=bmesh.new();bm.from_mesh(mesh);bm.faces.ensure_lookup_table()
            result=bmesh.ops.triangulate(bm,faces=[bm.faces[i] for i in face_set if len(bm.faces[i].verts)>3])
            for f in result.get('faces',[]):
                for e in f.edges:e.seam=True
            bm.to_mesh(mesh);bm.free();mesh.update()
    passed=(audit['finite'] and min(audit['min'])>=0 and max(audit['max'])<=1 and
            not audit['positive_area_overlap_pairs'] and not audit['degenerate_uv_triangles'])
    output.mkdir(parents=True,exist_ok=True);diagnostics.mkdir(parents=True,exist_ok=True)
    result={'asset':output.name,'seams':seams,'new_uv':'UV_HIGH','resolution':resolution,'unwrap_method':method,
            'packing_margin_uv':32/resolution,'repaired_boundary_loops':repaired,
            'removed_isolated_quadriflow_faces':len(isolated),
            'uv_audits':history,'passed':passed,'high':stats(target),
            'note':'Local face cuts may add visible seams; concealment is a heuristic, not a guarantee.'}
    (diagnostics/(output.name+'.uv.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(output/'03_uv.blend'))
    # Exact UV triangles for an external readable preview.
    mesh=target.data;mesh.calc_loop_triangles();layer=mesh.uv_layers.active
    np.savez_compressed(str(diagnostics/(output.name+'.uv_geometry.npz')),
       triangles=np.array([[list(layer.data[i].uv) for i in t.loops] for t in mesh.loop_triangles]))
    if not passed:raise RuntimeError('UV gate failed; do not bake')


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--checkpoint',type=Path,required=True)
    ap.add_argument('--diagnostics',type=Path,default=Path('diagnostics'));ap.add_argument('--resolution',type=int,default=4096)
    ap.add_argument('--method',choices=['ANGLE_BASED','CONFORMAL','MINIMUM_STRETCH'],default='ANGLE_BASED')
    ap.add_argument('--attempts',type=int,default=8)
    ap.add_argument('--profile',choices=['upper','hood','trousers','shoe'])
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    uv_phase(args.checkpoint.resolve(),args.checkpoint.resolve().parent,args.diagnostics.resolve(),args.resolution,args.method,args.attempts,args.profile)
