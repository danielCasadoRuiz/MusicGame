"""Explicit session opt-in for alternate retopo preparation."""
import json
import math
import sys
import time


def validate_config(data):
    if isinstance(data, dict) and data.get('mode') == 'clean_surface':
        required = {'mode', 'voxel_size', 'solidify_thickness', 'target_triangles',
                    'fragment_area_fraction', 'max_intermediate_faces', 'max_voxel_cells'}
        optional_bools = {'merge_by_distance_enabled', 'dissolve_degenerate_enabled',
            'remove_loose_geometry_enabled', 'remove_fragments_enabled', 'repair_excess_faces_enabled',
            'fill_small_holes_enabled', 'split_pinched_vertices_enabled', 'recalculate_normals_enabled',
            'smooth_shading_enabled', 'solidify_enabled', 'voxel_enabled', 'decimate_enabled',
            'final_cleanup_enabled', 'require_closed_manifold'}
        optional_numbers = {'merge_distance', 'degenerate_distance', 'fragment_max_vertices',
            'final_fragment_max_vertices', 'max_excess_faces', 'max_excess_face_area',
            'max_hole_diameter', 'max_hole_edges'}
        if not required <= set(data) or set(data) - required - optional_bools - optional_numbers - {'_help'}:
            raise ValueError('retopo_preprocess_config.json: claus incorrectes')
        if '_help' in data and not isinstance(data['_help'], dict):
            raise ValueError('_help ha de ser un objecte')
        for key in optional_bools & set(data):
            if not isinstance(data[key], bool):
                raise ValueError(key + ' ha de ser true o false')
        for key in optional_numbers & set(data):
            value = data[key]
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
                raise ValueError('Valor invalid: ' + key)
            if key in {'fragment_max_vertices', 'final_fragment_max_vertices', 'max_excess_faces', 'max_hole_edges'} and int(value) != value:
                raise ValueError(key + ' ha de ser un enter >= 0')
        for key in required - {'mode'}:
            value = data[key]
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ValueError('Valor invalid: ' + key)
        if data['voxel_size'] <= 0 or data['solidify_thickness'] < 0:
            raise ValueError('voxel_size > 0 i solidify_thickness >= 0 requerits')
        if not 0 <= data['fragment_area_fraction'] < 1:
            raise ValueError('fragment_area_fraction ha de ser >= 0 i < 1')
        for key in ('target_triangles', 'max_intermediate_faces', 'max_voxel_cells'):
            if int(data[key]) != data[key] or data[key] <= 0:
                raise ValueError(key + ' ha de ser un enter > 0')
        return data
    required = {'voxel_size', 'subdivision_levels', 'decimate_ratio', 'pre_decimate_ratio', 'max_intermediate_faces'}
    if not isinstance(data, dict) or set(data) != required:
        raise ValueError('retopo_preprocess_config.json: claus incorrectes')
    for key in required:
        if isinstance(data[key], bool) or not isinstance(data[key], (int, float)) or not math.isfinite(data[key]):
            raise ValueError('Valor invalid: ' + key)
    if data['voxel_size'] <= 0:
        raise ValueError('voxel_size ha de ser > 0 (unitats locals de Blender)')
    if data['subdivision_levels'] not in (0, 1, 2):
        raise ValueError('subdivision_levels ha de ser 0, 1 o 2')
    if not 0 < data['pre_decimate_ratio'] <= 1:
        raise ValueError('pre_decimate_ratio ha de ser > 0 i <= 1')
    if not 0 < data['decimate_ratio'] <= 1:
        raise ValueError('decimate_ratio ha de ser > 0 i <= 1')
    if int(data['max_intermediate_faces']) != data['max_intermediate_faces'] or data['max_intermediate_faces'] < 0:
        raise ValueError('max_intermediate_faces ha de ser un enter >= 0; 0 desactiva el limit')
    return data


def ask_preprocess(timeout=10, clock=time.monotonic, sleep=time.sleep, console=None):
    print('Activar preprocess configurat a retopo_preprocess_config.json per aquesta peca?')
    print('S = si, N = no. Sense resposta en 10 segons: retopo normal.', flush=True)
    if console is None:
        if not sys.stdin.isatty():
            print('Entrada no interactiva: retopo normal.', flush=True)
            return False
        import msvcrt
        console = msvcrt
    deadline = clock() + timeout
    while clock() < deadline:
        if console.kbhit():
            char = console.getwch()
            if char == '\x03':
                raise KeyboardInterrupt
            if char in ('\x00', '\xe0'):
                if console.kbhit(): console.getwch()
                continue
            if char.lower() in ('s', 'y'):
                print('SI: preprocess alternatiu activat.', flush=True)
                return True
            if char.lower() == 'n' or char == '\r':
                print('NO: retopo normal.', flush=True)
                return False
        sleep(0.05)
    print('Temps esgotat: retopo normal.', flush=True)
    return False


def select_preprocess(root):
    if not ask_preprocess():
        return None
    path = root / 'retopo_preprocess_config.json'
    return validate_config(json.loads(path.read_text(encoding='utf-8-sig')))
