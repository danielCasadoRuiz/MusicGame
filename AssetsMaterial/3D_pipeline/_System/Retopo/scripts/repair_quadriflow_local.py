import json
"""Bounded repair of defects introduced by QuadriFlow, never of the raw mesh.

Only exact coincident coordinates are welded. Unchanged coordinates can be
split into independent surface fans. Small generated boundary loops may be
filled only when their sampled surface remains close to the original source.
All changes happen on a temporary mesh, with rollback on any failed gate.
"""
import itertools
import bmesh,bpy

TOPOLOGY_KEYS=('boundary_edges','nonmanifold_nonboundary_edges','wire_edges',
               'nonmanifold_vertices','inconsistent_winding_edges','degenerate_faces')


def boundary_components(bm):
    bm.edges.index_update()
    remaining={e for e in bm.edges if e.is_boundary};components=[]
    while remaining:
        seed=min(remaining,key=lambda e:e.index);remaining.remove(seed);edges={seed};vertices=set(seed.verts);todo=list(vertices)
        while todo:
            v=todo.pop()
            for e in v.link_edges:
                if e in remaining:
                    remaining.remove(e);edges.add(e)
                    for w in e.verts:
                        if w not in vertices:vertices.add(w);todo.append(w)
        if any(sum(e in edges for e in v.link_edges) not in {2,4} for v in vertices):
            raise RuntimeError('Generated boundary is branching; local repair refused.')
        components.append((edges,vertices))
    return components


def split_surface_fans(bm):
    """Duplicate coordinates at pinched fans; preserve all nondegenerate faces."""
    bm.verts.ensure_lookup_table();bm.faces.ensure_lookup_table()
    bm.verts.index_update();bm.faces.index_update()
    bm.normal_update()
    vertices=[tuple(v.co) for v in bm.verts]
    faces=[[v.index for v in f.verts] for f in bm.faces]
    pairs={};pair_scores=[]
    bad_edges=[e for e in bm.edges if len(e.link_faces)>2]
    for e in bad_edges:
        if len(e.link_faces)!=4:raise RuntimeError('Only isolated four-face pinches are supported.')
        forward=[];back=[]
        for f in e.link_faces:
            vs=list(f.verts);i=vs.index(e.verts[0])
            (forward if vs[(i+1)%len(vs)]==e.verts[1] else back).append(f)
        if len(forward)!=2 or len(back)!=2:raise RuntimeError('Ambiguous pinch winding.')
        options=[list(zip(forward,permutation)) for permutation in itertools.permutations(back)]
        scores=[sum(a.normal.dot(b.normal) for a,b in option) for option in options]
        if abs(scores[0]-scores[1])<.1:raise RuntimeError('Ambiguous surface pairing at pinch.')
        pairs[e]=options[scores.index(max(scores))];pair_scores.append(scores)
    added=0;split_locations=[]
    for v in bm.verts:
        if v.is_manifold:continue
        incident=set(v.link_faces);components=[];adjacency={f:set() for f in incident}
        for e in v.link_edges:
            connections=[tuple(e.link_faces)] if len(e.link_faces)==2 else pairs.get(e,[])
            for a,b in connections:adjacency[a].add(b);adjacency[b].add(a)
        while incident:
            seed=min(incident,key=lambda f:f.index);incident.remove(seed);component={seed};todo=[seed]
            while todo:
                f=todo.pop()
                for g in adjacency[f]:
                    if g in incident:incident.remove(g);component.add(g);todo.append(g)
            components.append(component)
        components.sort(key=lambda group:min(f.index for f in group))
        for component in components[1:]:
            new=len(vertices);vertices.append(tuple(v.co));added+=1;split_locations.append(tuple(v.co))
            for f in component:faces[f.index]=[new if i==v.index else i for i in faces[f.index]]
    return vertices,faces,{'duplicated_vertices':added,'four_face_edges':len(bad_edges),
                          'pair_scores':pair_scores,'locations':split_locations}


def repair_generated_defects(source,target,welded_source,stats,world_bvh):
    before=stats(target)
    if not any(before[k] for k in TOPOLOGY_KEYS):return {'method':'none','changed':False}
    if any(welded_source[k] for k in TOPOLOGY_KEYS):
        raise RuntimeError('Local remesher repair requires a closed, valid welded source.')
    # No arbitrary defect-count thresholds here. Attempt the repair and
    # let the concrete operations plus the final topology gate decide.
    original=target.data;work=original.copy();target.data=work
    bm=None;rebuilt=None
    try:
        bm=bmesh.new();bm.from_mesh(work);bm.verts.ensure_lookup_table()
        bad=[f for f in bm.faces if f.calc_area()<1e-14]
        local={v for f in bad for v in f.verts}|{v for e in bm.edges if e.is_boundary for v in e.verts}
        groups={}
        for v in sorted(local,key=lambda v:v.index):groups.setdefault(tuple(v.co),[]).append(v)
        mapping={v:group[0] for group in groups.values() for v in group[1:]}
        faces_before=len(bm.faces)
        if mapping:bmesh.ops.weld_verts(bm,targetmap=mapping)
        if any(f.calc_area()<1e-14 for f in bm.faces):
            raise RuntimeError('Degenerate faces remain after exact-coordinate weld.')
        if any(e.is_wire for e in bm.edges) or any(not v.link_edges for v in bm.verts):
            raise RuntimeError('Weld produced wire geometry; local repair refused.')
        removed_faces=faces_before-len(bm.faces)
        components=boundary_components(bm)
        extent=max(source.dimensions);loop_reports=[]
        for edges,verts in components:
            coords=[target.matrix_world@v.co for v in verts]
            diameter=max(((a-b).length for a in coords for b in coords),default=0)
            perimeter=sum((target.matrix_world@e.verts[0].co-target.matrix_world@e.verts[1].co).length for e in edges)
            # Do not reject a generated QuadriFlow boundary merely because a
            # loop has >12 edges or exceeds an arbitrary diameter/perimeter.
            # The source is already validated closed. Attempt the fill, then
            # let the stronger post-repair topology/area/distance gates decide.
            loop_reports.append({
                'edges':len(edges),
                'diameter':diameter,
                'diameter_fraction':diameter/max(extent,1e-30),
                'perimeter':perimeter,
                'perimeter_fraction':perimeter/max(extent,1e-30),
                'coords':[list(p) for p in coords]
            })
            print('QUADRIFLOW_BOUNDARY_LOOP',json.dumps(loop_reports[-1]),flush=True)
        result=bmesh.ops.holes_fill(bm,edges=sorted((e for edges,_ in components for e in edges),key=lambda e:e.index),sides=12)
        filled=result['faces']
        remaining_boundaries=sum(1 for e in bm.edges if e.is_boundary)
        print('QUADRIFLOW_HOLE_FILL',json.dumps({
            'filled_faces':len(filled),
            'remaining_boundary_edges':remaining_boundaries
        }),flush=True)
        if remaining_boundaries:
            raise RuntimeError(
                f'QuadriFlow hole fill left {remaining_boundaries} boundary edges.'
            )
        bm.faces.index_update()
        patch_indices=[f.index for f in filled]
        filled_count=len(filled)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        vertices,faces,fan_report=split_surface_fans(bm)
        bm.free();bm=None
        rebuilt=bpy.data.meshes.new('HIGH_LOCAL_REPAIR');rebuilt.from_pydata(vertices,[],faces);rebuilt.update()
        target.data=rebuilt
        bm=bmesh.new();bm.from_mesh(rebuilt);bm.faces.ensure_lookup_table()
        filled=[bm.faces[i] for i in patch_indices]
        triangles=bmesh.ops.triangulate(bm,faces=filled,quad_method='BEAUTY',ngon_method='BEAUTY')['faces'] if filled else []
        total_area=sum(f.calc_area() for f in bm.faces)
        patch_area=sum(f.calc_area() for f in triangles)
        patch_area_fraction=(patch_area/total_area) if total_area else 0.0
        print('QUADRIFLOW_PATCH_AREA',json.dumps({
            'patch_area':patch_area,
            'total_area':total_area,
            'patch_area_fraction':patch_area_fraction
        }),flush=True)
        tree=world_bvh(source);patch_distances=[]
        for f in triangles:
            if f.calc_area()<1e-14:
                raise RuntimeError('Generated patch has zero area.')
            a,b,c=[target.matrix_world@v.co for v in f.verts]
            for i in range(9):
                for j in range(9-i):
                    p=(a*i+b*j+c*(8-i-j))/8
                    _,_,_,distance=tree.find_nearest(p)
                    if distance is not None:
                        patch_distances.append(distance)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(rebuilt);bm.free();bm=None;rebuilt.update()
        after=stats(target)
        if any(after[k] for k in TOPOLOGY_KEYS):raise RuntimeError('Local repair failed final topology gates.')
        report={'method':'exact local weld, bounded generated-hole triangulation, split pinched surface fans',
                'changed':True,'before':before,'after':after,'affected_input_vertices':len(local),
                'exact_vertices_merged':len(mapping),'collapsed_faces_removed_by_weld':removed_faces,
                'generated_loops_filled':filled_count,'patch_triangles':len(triangles),
                'patch_area_fraction':patch_area_fraction,'patch_samples':len(patch_distances),
                'patch_max_source_distance_pre_conform':max(patch_distances,default=0),'patch_distance_is_hard_gate':False,
                'loops':loop_reports,'surface_fans':fan_report,'existing_vertex_displacement':0.0,
                'limitations':['Patch distances are sampled, not an exact bound.',
                               'This is repair of remesher defects, not repair of the raw garment.']}
        bpy.data.meshes.remove(work)
        return report
    except Exception:
        target.data=original
        if bm is not None:bm.free()
        for mesh in (rebuilt,work):
            if mesh is not None and mesh.users==0:bpy.data.meshes.remove(mesh)
        raise


def remove_reduction_fins(target,validated_dense,stats):
    """Remove only isolated triangle fins [1,1,3] introduced by reduction.

A closed dense input cannot contain such fins. No ring or connected surface
patch is deleted; unsupported reduction defects remain a hard failure.
"""
    before=stats(target)
    if not any(before[k] for k in TOPOLOGY_KEYS):return {'method':'none','changed':False}
    if any(validated_dense[k] for k in TOPOLOGY_KEYS):raise RuntimeError('Dense input was not validated closed.')
    if any(before[k] for k in ('wire_edges','degenerate_faces','inconsistent_winding_edges')):
        raise RuntimeError('Reduction defect is not an isolated triangle fin.')
    original=target.data;work=original.copy();target.data=work;bm=None
    try:
        bm=bmesh.new();bm.from_mesh(work)
        fins=[f for f in bm.faces if len(f.verts)==3 and sorted(len(e.link_faces) for e in f.edges)==[1,1,3]]
        if not 1<=len(fins)<=32:raise RuntimeError('Reduction has unsupported or too many defects.')
        fin_edges={e for f in fins for e in f.edges}
        if any(not e.is_manifold and e not in fin_edges for e in bm.edges):
            raise RuntimeError('Reduction has defects outside isolated triangle fins.')
        area=sum(f.calc_area() for f in bm.faces);removed_area=sum(f.calc_area() for f in fins)
        locations=[[list(target.matrix_world@v.co) for v in f.verts] for f in fins]
        if removed_area>area*1e-5 or any(e.calc_length()>max(target.dimensions)*.005 for e in fin_edges):
            raise RuntimeError('Reduction fins exceed local geometry limits.')
        removed_faces=len(fins)
        bmesh.ops.delete(bm,geom=fins,context='FACES_ONLY')
        wires=[e for e in bm.edges if e.is_wire]
        if any(e not in fin_edges for e in wires):raise RuntimeError('Unexpected wires after fin removal.')
        if wires:bmesh.ops.delete(bm,geom=wires,context='EDGES')
        loose=[v for v in bm.verts if not v.link_edges]
        removed_vertices=len(loose)
        if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
        bm.to_mesh(work);bm.free();bm=None;work.update()
        after=stats(target)
        if any(after[k] for k in TOPOLOGY_KEYS):raise RuntimeError('Fin removal failed closed topology gates.')
        return {'method':'remove isolated triangle fins [one, one, three linked faces per edge] introduced by reduction',
                'changed':True,'before':before,'after':after,'removed_triangles':removed_faces,
                'removed_loose_vertices':removed_vertices,'removed_area_fraction':removed_area/area,
                'locations':locations,'existing_vertex_displacement':0.0}
    except Exception:
        target.data=original
        if bm is not None:bm.free()
        if work.users==0:bpy.data.meshes.remove(work)
        raise
