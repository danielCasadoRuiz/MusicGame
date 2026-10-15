"""Repair retopo working geometry only. Never pass the original bake source."""
import json
import bpy
import bmesh
from mathutils import Vector


def clean_surface(obj, fraction=0.001, max_fragment_vertices=100,
                  hole_diameter=0.02, hole_edges=24, options=None):
    options = options or {}
    enabled = lambda key: options.get(key, True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    report = {}
    try:
        if enabled('dissolve_degenerate_enabled'):
            bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=options.get('degenerate_distance', 1e-8))
        loose = [e for e in bm.edges if not e.link_faces] if enabled('remove_loose_geometry_enabled') else []
        report['removed_wire_edges'] = len(loose)
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='EDGES')
        loose = [v for v in bm.verts if not v.link_faces] if enabled('remove_loose_geometry_enabled') else []
        report['removed_loose_vertices'] = len(loose)
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        unseen = set(bm.verts)
        groups = []
        while unseen:
            stack = [unseen.pop()]; verts = set()
            while stack:
                v = stack.pop(); verts.add(v)
                for e in v.link_edges:
                    other = e.other_vert(v)
                    if other in unseen:
                        unseen.remove(other); stack.append(other)
            faces = {f for v in verts for f in v.link_faces}
            groups.append((verts, sum(f.calc_area() for f in faces)))
        groups.sort(key=lambda g: g[1], reverse=True)
        small = [g for g in groups[1:] if enabled('remove_fragments_enabled') and g[1] < groups[0][1] * fraction
                 and (max_fragment_vertices == 0 or len(g[0]) <= max_fragment_vertices)]
        report['removed_fragments'] = len(small)
        remove = [v for g in small for v in g[0]]
        if remove:
            bmesh.ops.delete(bm, geom=remove, context='VERTS')
        # Local beard repair: remove only microscopic excess faces on over-connected edges.
        deleted = 0
        while enabled('repair_excess_faces_enabled') and deleted < options.get('max_excess_faces', 150):
            bad = [e for e in bm.edges if len(e.link_faces) > 2]
            if not bad:
                break
            edge = min(bad, key=lambda e: e.calc_length())
            face = min(edge.link_faces, key=lambda f: f.calc_area())
            if face.calc_area() > options.get('max_excess_face_area', 1e-5):
                break  # The configured voxel reconstruction handles larger defects.
            bmesh.ops.delete(bm, geom=[face], context='FACES_ONLY')
            deleted += 1
        report['deleted_tiny_excess_faces'] = deleted
        loose = [v for v in bm.verts if not v.link_faces] if enabled('remove_loose_geometry_enabled') else []
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        # Only closed tiny loops are capped. Open garment borders remain open.
        unseen = {e for e in bm.edges if e.is_boundary} if enabled('fill_small_holes_enabled') else set()
        filled = 0
        while unseen:
            stack = [unseen.pop()]; edges = set(); verts = set()
            while stack:
                e = stack.pop(); edges.add(e)
                for v in e.verts:
                    verts.add(v)
                    for other in v.link_edges:
                        if other in unseen:
                            unseen.remove(other); stack.append(other)
            diameter = (Vector(tuple(max(v.co[i] for v in verts) for i in range(3))) -
                        Vector(tuple(min(v.co[i] for v in verts) for i in range(3)))).length
            if len(edges) <= hole_edges and diameter < hole_diameter and all(
                    sum(e in edges for e in v.link_edges) == 2 for v in verts):
                faces = bmesh.ops.holes_fill(bm, edges=list(edges), sides=hole_edges)['faces']
                filled += len(faces)
                if faces:
                    bmesh.ops.triangulate(bm, faces=faces)
        report['filled_small_hole_faces'] = filled
        if enabled('split_pinched_vertices_enabled'):
            # Separate face fans pinched together at a single vertex without opening good edges.
            bm.verts.index_update(); bm.faces.index_update()
            coords = [tuple(v.co) for v in bm.verts]; mapping = {}; split = 0
            for v in bm.verts:
                remaining = set(v.link_faces); fan_index = 0
                while remaining:
                    stack = [remaining.pop()]; fan = []
                    while stack:
                        f = stack.pop(); fan.append(f)
                        for e in f.edges:
                            if v in e.verts:
                                for other in e.link_faces:
                                    if other in remaining:
                                        remaining.remove(other); stack.append(other)
                    index = v.index
                    if fan_index:
                        index = len(coords); coords.append(tuple(v.co)); split += 1
                    for f in fan:
                        mapping[v.index, f.index] = index
                    fan_index += 1
            faces = [[mapping[v.index, f.index] for v in f.verts] for f in bm.faces]
            mesh = bpy.data.meshes.new(obj.data.name + '_Clean')
            mesh.from_pydata(coords, [], faces); mesh.update()
            for material in obj.data.materials:
                mesh.materials.append(material)
            for p, f in zip(mesh.polygons, bm.faces):
                p.material_index = f.material_index; p.use_smooth = True if options.get('smooth_shading_enabled', True) else f.smooth
            old = obj.data; obj.data = mesh
            if old.users == 0:
                bpy.data.meshes.remove(old)
            report['split_pinched_vertices'] = split
        else:
            bm.to_mesh(obj.data)
            report['split_pinched_vertices'] = 0
    finally:
        bm.free()
    bm = bmesh.new(); bm.from_mesh(obj.data)
    if enabled('recalculate_normals_enabled'):
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if options.get('smooth_shading_enabled', True):
        for f in bm.faces:
            f.smooth = True
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    print('PREPROCESS_SURFACE_CLEANUP', json.dumps(report), flush=True)
    return report


def apply_clean_preprocess(target, config, stats, activate):
    activate(target)
    # Legacy clean_surface configs retain their previous behavior.
    defaults = {
        'solidify_enabled': True, 'voxel_enabled': True, 'decimate_enabled': True,
        'final_cleanup_enabled': True, 'require_closed_manifold': True,
        'fragment_max_vertices': 100, 'final_fragment_max_vertices': 0,
        'max_hole_diameter': 0.02, 'max_hole_edges': 24,
    }
    cfg = dict(defaults, **config)
    report = {'config': config, 'before': stats(target)}
    def cleanup(final=False):
        return clean_surface(target, cfg['fragment_area_fraction'],
            cfg['final_fragment_max_vertices'] if final else cfg['fragment_max_vertices'],
            cfg['max_hole_diameter'], cfg['max_hole_edges'], options=cfg)
    report['initial_cleanup'] = cleanup()
    if not target.data.polygons:
        raise RuntimeError('Preprocess cleanup produced an empty mesh')
    limit = int(cfg['max_intermediate_faces'])
    if cfg['voxel_enabled']:
        voxel = float(cfg['voxel_size'])
        extent = [max(v.co[i] for v in target.data.vertices) - min(v.co[i] for v in target.data.vertices) for i in range(3)]
        cells = 1
        thickness = cfg['solidify_thickness'] if cfg['solidify_enabled'] else 0
        for size in extent:
            cells *= int((size + 2 * thickness) / voxel) + 5
        if cells > cfg['max_voxel_cells']:
            raise RuntimeError(f'Preprocess voxel grid limit: {cells} cells; increase voxel_size (limit {cfg["max_voxel_cells"]})')
    if cfg['solidify_enabled'] and cfg['solidify_thickness'] > 0:
        estimated = len(target.data.polygons) * 2 + report['before']['boundary_edges']
        if estimated > limit:
            raise RuntimeError(f'Preprocess solidify geometry limit: estimated {estimated}, maximum {limit}')
        print('PREPROCESS_SOLIDIFY_START', cfg['solidify_thickness'], flush=True)
        mod = target.modifiers.new('RepairThickness', 'SOLIDIFY')
        mod.thickness = cfg['solidify_thickness']; mod.offset = 0
        mod.use_even_offset = False; mod.use_quality_normals = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        print('PREPROCESS_SOLIDIFY_APPLIED', len(target.data.polygons), flush=True)
    if cfg['voxel_enabled']:
        print('PREPROCESS_VOXEL_START', voxel, flush=True)
        mod = target.modifiers.new('RepairVoxel', 'REMESH')
        mod.mode = 'VOXEL'; mod.voxel_size = voxel; mod.adaptivity = 0
        mod.use_remove_disconnected = False
        bpy.ops.object.modifier_apply(modifier=mod.name)
        count = len(target.data.polygons)
        print('PREPROCESS_VOXEL_APPLIED', count, flush=True)
        if not count or count > limit:
            raise RuntimeError(f'Preprocess voxel geometry limit: {count} faces, maximum {limit}; increase voxel_size')
    if cfg['decimate_enabled']:
        target.data.calc_loop_triangles()
        triangles = len(target.data.loop_triangles)
        if not triangles:
            raise RuntimeError('Preprocess produced an empty mesh')
        ratio = min(1.0, cfg['target_triangles'] / triangles)
        print('PREPROCESS_DECIMATE_START', ratio, flush=True)
        if ratio < 1:
            mod = target.modifiers.new('RepairReduce', 'DECIMATE')
            mod.decimate_type = 'COLLAPSE'; mod.ratio = ratio; mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
        print('PREPROCESS_DECIMATE_APPLIED', len(target.data.polygons), flush=True)
    if cfg['final_cleanup_enabled']:
        report['final_cleanup'] = cleanup(final=True)
    report['after'] = stats(target)
    bad = ('boundary_edges', 'nonmanifold_nonboundary_edges', 'wire_edges',
           'nonmanifold_vertices', 'inconsistent_winding_edges', 'degenerate_faces')
    print('ALTERNATE_PREPROCESS_COMPLETE', json.dumps(report), flush=True)
    if not target.data.polygons:
        raise RuntimeError('Preprocess produced an empty mesh')
    if any(report['after'][key] for key in bad):
        print('PREPROCESS_TOPOLOGY_WARNING', json.dumps(report['after']), flush=True)
        if cfg['require_closed_manifold']:
            raise RuntimeError('Preprocess did not produce a closed manifold surface: ' + json.dumps(report['after']))
    return report
