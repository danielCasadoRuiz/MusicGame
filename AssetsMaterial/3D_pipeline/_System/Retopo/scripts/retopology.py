"""Phase 2: RAW copy -> conservative Merge by Distance -> diagnostics -> Exoside Quad Remesher -> conservative post merge -> recalculate outside.

SOURCE remains byte/geometry-equivalent to the imported GLB and is kept untouched for baking.
HIGH is a working copy. No voxel remesh, decimate, subdivision, hole fill, conform,
or component deletion is performed. Diagnostics are warning-only.
"""
import argparse, hashlib, json, os, subprocess, sys, time, tempfile
from pathlib import Path
import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from blender_inspect import activate


def stats(obj):
    m=obj.data; m.calc_loop_triangles()
    bm=bmesh.new(); bm.from_mesh(m)
    r={
        'vertices':len(m.vertices),'faces':len(m.polygons),'triangles':len(m.loop_triangles),
        'quads':sum(len(p.vertices)==4 for p in m.polygons),
        'boundary_edges':sum(e.is_boundary for e in bm.edges),
        'nonmanifold_nonboundary_edges':sum(not e.is_manifold and not e.is_boundary for e in bm.edges),
        'wire_edges':sum(e.is_wire for e in bm.edges),
        'inconsistent_winding_edges':sum(e.is_manifold and not e.is_contiguous for e in bm.edges),
        'nonmanifold_vertices':sum(not v.is_manifold for v in bm.verts),
        'degenerate_faces':sum(f.calc_area()<1e-14 for f in bm.faces),
    }
    bm.free(); return r



def merge_by_distance(obj, distance):
    """Conservative weld on the working mesh only. Returns a diagnostic record."""
    bm=bmesh.new(); bm.from_mesh(obj.data); before_v=len(bm.verts)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=float(distance))
    after_v=len(bm.verts); bm.to_mesh(obj.data); bm.free()
    obj.data.update(); bpy.context.view_layer.update()
    return {
        'distance':float(distance),
        'vertices_before':before_v,
        'vertices_after':after_v,
        'merged_vertices':before_v-after_v,
    }


def recalculate_outside(obj):
    """Recalculate face orientation on the working mesh only; does not touch SOURCE."""
    bm=bmesh.new(); bm.from_mesh(obj.data)
    faces=list(bm.faces)
    if faces:
        bmesh.ops.recalc_face_normals(bm,faces=faces)
    bm.to_mesh(obj.data); bm.free()
    obj.data.update(); bpy.context.view_layer.update()
    return {'faces_recalculated':len(obj.data.polygons)}

def component_report(obj):
    """Face-connected component diagnostics only. Does not modify geometry."""
    bm=bmesh.new(); bm.from_mesh(obj.data); bm.faces.ensure_lookup_table()
    unseen=set(bm.faces); sizes=[]
    while unseen:
        first=unseen.pop(); stack=[first]; count=0
        while stack:
            f=stack.pop(); count += 1
            for e in f.edges:
                for nf in e.link_faces:
                    if nf in unseen:
                        unseen.remove(nf); stack.append(nf)
        sizes.append(count)
    sizes.sort(reverse=True)
    report={
        'count':len(sizes),
        'face_counts':sizes[:64],
        'single_connected_component':len(sizes)==1,
        'micro_components_le_2_faces':sum(1 for n in sizes if n<=2),
        'micro_faces_total_le_2':sum(n for n in sizes if n<=2),
    }
    bm.free(); return report


def bvh(obj):
    return BVHTree.FromObject(obj,bpy.context.evaluated_depsgraph_get(),epsilon=0.0)


def samples(obj,count=20000,seed=42):
    m=obj.data; m.calc_loop_triangles()
    if not m.loop_triangles:
        return []
    coords=np.empty(len(m.vertices)*3,dtype=np.float64); m.vertices.foreach_get('co',coords)
    coords=coords.reshape(-1,3)
    indices=np.empty(len(m.loop_triangles)*3,dtype=np.int32); m.loop_triangles.foreach_get('vertices',indices)
    tri=coords[indices.reshape(-1,3)]
    area=np.linalg.norm(np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]),axis=1)
    total=float(area.sum())
    if not np.isfinite(total) or total<=0:
        return []
    rng=np.random.default_rng(seed); chosen=rng.choice(len(tri),min(count,len(tri)),p=area/total)
    a=rng.random((len(chosen),1)); c=rng.random((len(chosen),1)); a=np.sqrt(a)
    points=(1-a)*tri[chosen,0]+a*(1-c)*tri[chosen,1]+a*c*tri[chosen,2]
    return [obj.matrix_world@Vector(p) for p in points]


def deviation(source,target,count=20000):
    report={}; src=bvh(source); dst=bvh(target)
    for name,obj,tree in [('target_to_source',target,src),('source_to_target',source,dst)]:
        pts=samples(obj,count)
        ds=[]
        for p in pts:
            hit=tree.find_nearest(p)
            if hit and hit[0] is not None:
                ds.append(hit[3])
        if not ds:
            report[name]={'samples':0}
            continue
        values=np.array(ds)
        report[name]={'samples':len(ds),'mean':float(values.mean()),
          'rms':float(np.sqrt(np.mean(values**2))),'p95':float(np.quantile(values,.95)),
          'p99':float(np.quantile(values,.99)),'max_sampled':float(values.max())}
    return report


def find_quad_remesher_engine():
    explicit=os.environ.get('QUADREMESHER_ENGINE')
    candidates=[]
    if explicit: candidates.append(Path(explicit))
    program_data=Path(os.environ.get('PROGRAMDATA', r'C:\ProgramData'))
    base=program_data/'Exoside'/'QuadRemesher'/'Datas_Blender'
    if base.is_dir():
        candidates.extend(sorted(base.glob('QuadRemesherEngine_*/xremesh.exe'), reverse=True))
    for c in candidates:
        if c.is_file(): return c
    raise RuntimeError('Quad Remesher engine no trobat. Executa Remesh It manualment una vegada o defineix QUADREMESHER_ENGINE.')


def export_selected_fbx(target,filepath):
    activate(target)
    result=bpy.ops.export_scene.fbx(filepath=str(filepath),use_selection=True,bake_anim=False)
    if result!={'FINISHED'} or not filepath.is_file():
        raise RuntimeError(f'Export FBX per Quad Remesher ha fallat: {result}')


def import_result_fbx(filepath):
    before=set(bpy.data.objects)
    result=bpy.ops.import_scene.fbx(filepath=str(filepath))
    if result!={'FINISHED'}:
        raise RuntimeError(f'Import FBX de Quad Remesher ha fallat: {result}')
    added=[o for o in bpy.data.objects if o not in before and o.type=='MESH']
    if len(added)!=1:
        raise RuntimeError(f'Quad Remesher havia de retornar una sola mesh; trobades {len(added)}.')
    return added[0]


def clear_scene_preserve_addons():
    # Do NOT read factory settings here: Quad Remesher is a user-installed add-on and must stay registered.
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def ensure_quad_remesher_addon_loaded():
    if hasattr(bpy.context.scene, 'qremesher') and hasattr(bpy.ops, 'qremesher') and hasattr(bpy.ops.qremesher, 'remesh'):
        return 'already_loaded'

    import importlib, addon_utils
    errors=[]
    candidates=[]

    # 1) Normal Blender add-on discovery, when the user install is visible to this process.
    try:
        for mod in addon_utils.modules(refresh=True):
            name=getattr(mod,'__name__','')
            bl=getattr(mod,'bl_info',{}) or {}
            label=str(bl.get('name',''))
            if (('quad' in name.lower() and 'remesh' in name.lower()) or 'quad remesher' in label.lower()):
                candidates.append(name)
        preferred=['quad_remesher_1_4']+[c for c in candidates if c!='quad_remesher_1_4']
        for module_name in preferred:
            try:
                addon_utils.enable(module_name, default_set=False, persistent=False)
                if hasattr(bpy.context.scene,'qremesher') and hasattr(bpy.ops,'qremesher') and hasattr(bpy.ops.qremesher,'remesh'):
                    print('QUAD_REMESHER_ADDON_ENABLED',module_name,flush=True)
                    return module_name
            except Exception as exc:
                errors.append(f'addon_utils {module_name}: {exc}')
    except Exception as exc:
        errors.append(f'addon_utils discovery: {exc}')

    # 2) Deterministic batch path: V90 ships the exact GPL BlenderBridge 1.4.1
    # supplied by the user, so batch execution does not depend on Blender's
    # per-user Add-ons/Extensions search path. The engine/license remains the
    # locally installed Exoside engine on the user's machine.
    vendor_root=Path(__file__).resolve().parent.parent/'vendor'
    vendor_pkg=vendor_root/'quad_remesher_1_4'
    if vendor_pkg.is_dir():
        try:
            parent=str(vendor_root)
            if parent not in sys.path:
                sys.path.insert(0,parent)
            module=importlib.import_module('quad_remesher_1_4')
            if not hasattr(bpy.context.scene,'qremesher'):
                module.register()
            if hasattr(bpy.context.scene,'qremesher') and hasattr(bpy.ops,'qremesher') and hasattr(bpy.ops.qremesher,'remesh'):
                print('QUAD_REMESHER_ADDON_REGISTERED_FROM_VENDOR',str(vendor_pkg),flush=True)
                return 'vendor:quad_remesher_1_4'
            errors.append('vendor registered but qremesher operator/property is absent')
        except Exception as exc:
            import traceback
            errors.append('vendor import/register: '+repr(exc)+' :: '+traceback.format_exc())

    # 3) Last fallback: find a manually installed copy even when Blender 4.5's
    # extension/add-on repository has not exposed it through addon_utils.
    search_roots=[]
    for env in ('APPDATA','LOCALAPPDATA'):
        val=os.environ.get(env)
        if val:
            search_roots.append(Path(val)/'Blender Foundation'/'Blender')
    seen=set()
    for base in search_roots:
        if not base.is_dir():
            continue
        try:
            for init_py in base.glob('**/quad_remesher_1_4/__init__.py'):
                pkg=init_py.parent
                parent=str(pkg.parent)
                if parent in seen:
                    continue
                seen.add(parent)
                try:
                    if parent not in sys.path:
                        sys.path.insert(0,parent)
                    module=importlib.import_module('quad_remesher_1_4')
                    if not hasattr(bpy.context.scene,'qremesher'):
                        module.register()
                    if hasattr(bpy.context.scene,'qremesher') and hasattr(bpy.ops,'qremesher') and hasattr(bpy.ops.qremesher,'remesh'):
                        print('QUAD_REMESHER_ADDON_REGISTERED_FROM_INSTALL',str(pkg),flush=True)
                        return 'installed:quad_remesher_1_4'
                except Exception as exc:
                    errors.append(f'installed {pkg}: {exc}')
        except Exception as exc:
            errors.append(f'scan {base}: {exc}')

    raise RuntimeError('No he pogut carregar Quad Remesher 1.4.1. Detectats='+repr(candidates)+' vendor='+str(vendor_pkg)+' errors='+repr(errors))


def configure_quad_remesher(target_quads, qr_config):
    ensure_quad_remesher_addon_loaded()
    if not hasattr(bpy.context.scene, 'qremesher'):
        raise RuntimeError('Quad Remesher add-on no està carregat en aquest Blender. Activa’l a Preferences > Add-ons.')
    props=bpy.context.scene.qremesher
    required=['target_count','adaptive_size','adapt_quad_count','autodetect_hard_edges']
    missing=[k for k in required if not hasattr(props,k)]
    if missing:
        raise RuntimeError('Quad Remesher add-on incompatible; falten propietats: '+', '.join(missing))
    props.target_count=int(target_quads)
    props.adaptive_size=float(qr_config['adaptive_size'])
    props.adapt_quad_count=bool(qr_config['adapt_quad_count'])
    if hasattr(props,'use_vertex_color'): props.use_vertex_color=bool(qr_config['use_vertex_color'])
    if hasattr(props,'painted_quad_density'): props.painted_quad_density=float(qr_config['quad_density'])
    if hasattr(props,'use_materials'): props.use_materials=bool(qr_config['use_materials'])
    if hasattr(props,'use_normals'): props.use_normals=bool(qr_config['use_normals_splitting'])
    props.autodetect_hard_edges=bool(qr_config['detect_hard_edges'])
    for name in ('symmetry_x','symmetry_y','symmetry_z'):
        if hasattr(props,name): setattr(props,name,bool(qr_config[name]))
    if hasattr(props,'hide_input'): props.hide_input=False
    sym=''.join(axis for axis,key in [('X','symmetry_x'),('Y','symmetry_y'),('Z','symmetry_z')] if qr_config[key])
    return {
        'target_quad_count':int(target_quads),
        'adaptive_size':float(qr_config['adaptive_size']),
        'adapt_quad_count':bool(qr_config['adapt_quad_count']),
        'exact_quad_count':not bool(qr_config['adapt_quad_count']),
        'use_vertex_color':bool(qr_config['use_vertex_color']),
        'quad_density':float(qr_config['quad_density']),
        'use_materials':bool(qr_config['use_materials']),
        'use_normals':bool(qr_config['use_normals_splitting']),
        'auto_detect_hard_edges':bool(qr_config['detect_hard_edges']),
        'symmetry':sym,
        'via':'bpy.ops.qremesher.remesh'
    }


def qr_progress_path():
    return Path(tempfile.gettempdir())/'Exoside'/'QuadRemesher'/'Blender'/'progress.txt'


def read_qr_progress():
    p=qr_progress_path()
    if not p.is_file(): return None
    try:
        txt=p.read_text(encoding='utf-8',errors='replace').strip().splitlines()
        return txt[-1].strip() if txt else ''
    except Exception:
        return None


def start_retopology_async(path,output,diagnostics,target_quads=50000,qr_config=None):
    if qr_config is None:
        qr_config={
            'adaptive_size':0.0,'adapt_quad_count':False,'use_vertex_color':False,
            'quad_density':1.0,'use_materials':False,'use_normals_splitting':False,
            'detect_hard_edges':True,'symmetry_x':False,'symmetry_y':False,'symmetry_z':False
        }
    started=time.time(); digest=hashlib.sha256(path.read_bytes()).hexdigest()
    clear_scene_preserve_addons()
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    if len(meshes)!=1: raise RuntimeError('This pipeline expects exactly one mesh instance per asset.')
    source=meshes[0]; source.name='SOURCE'; source.data.name='SOURCE_ORIGINAL'
    target=source.copy(); target.data=source.data.copy(); bpy.context.collection.objects.link(target)
    target.name='HIGH'; target.data.name='HIGH_WORKING_COPY'; activate(target)

    output.mkdir(parents=True,exist_ok=True); diagnostics.mkdir(parents=True,exist_ok=True)
    source_stats=stats(source)
    print('RAW_SOURCE_STATS',json.dumps(source_stats),flush=True)

    scale=max(target.dimensions) if max(target.dimensions)>0 else 1.0
    merge_distance=scale*1e-7
    merge_report=merge_by_distance(target,merge_distance)
    print('PRE_QR_MERGE_BY_DISTANCE',json.dumps(merge_report),flush=True)

    pre_stats=stats(target); comps=component_report(target)
    warnings=[]
    if comps['count']!=1: warnings.append(f"connected_components={comps['count']}")
    if comps['micro_components_le_2_faces']: warnings.append(f"micro_components_le_2_faces={comps['micro_components_le_2_faces']}")
    if pre_stats['boundary_edges']: warnings.append(f"boundary_edges={pre_stats['boundary_edges']}")
    if pre_stats['nonmanifold_nonboundary_edges']: warnings.append(f"nonmanifold_nonboundary_edges={pre_stats['nonmanifold_nonboundary_edges']}")
    if pre_stats['wire_edges']: warnings.append(f"wire_edges={pre_stats['wire_edges']}")
    if pre_stats['degenerate_faces']: warnings.append(f"degenerate_faces={pre_stats['degenerate_faces']}")
    diag={'stats':pre_stats,'components':comps,'warnings':warnings,'stops_pipeline':False,'auto_deleted_components':False}
    print('PRE_QR_DIAGNOSTIC',json.dumps(diag),flush=True)
    if warnings: print('PRE_QR_WARNING_ONLY','; '.join(warnings),flush=True)

    # SOURCE remains untouched. Quad Remesher runs only on HIGH through the installed Blender add-on.
    source.hide_set(True); source.hide_render=True; activate(target)
    qr_options=configure_quad_remesher(target_quads,qr_config)
    baseline={o.as_pointer() for o in bpy.context.scene.objects}
    p=qr_progress_path()
    try:
        if p.exists(): p.unlink()
    except Exception: pass
    print('QR_ACTIVE_OBJECT',json.dumps({
        'name':target.name,'selected_objects':[o.name for o in bpy.context.selected_objects],
        'mode':bpy.context.mode,'vertices':len(target.data.vertices),'faces':len(target.data.polygons),
        'target_is_active':bpy.context.active_object is target,'options':qr_options,
    }),flush=True)
    print('QUAD_REMESHER_ADDON_START',json.dumps(qr_options),flush=True)
    ret=bpy.ops.qremesher.remesh()
    print('QUAD_REMESHER_OPERATOR_RETURN',str(ret),flush=True)
    if 'RUNNING_MODAL' not in ret:
        raise RuntimeError('Quad Remesher no ha iniciat el mode modal: '+str(ret))

    state={'finished':False,'started':time.time()}

    def fail(msg):
        if state['finished']: return None
        state['finished']=True
        print('QUAD_REMESHER_ASYNC_ERROR',msg,flush=True)
        try: (diagnostics/'quad_remesher_error.txt').write_text(str(msg),encoding='utf-8')
        except Exception: pass
        bpy.ops.wm.quit_blender()
        return None

    def finalize_with(imported):
        if state['finished']: return None
        state['finished']=True
        try:
            qr_seconds=time.time()-state['started']
            print('QUAD_REMESHER_IMPORTED_OBJECT',imported.name,'seconds',round(qr_seconds,3),flush=True)
            # Put the imported result into the existing HIGH object so every later stage sees the same object name.
            high_inv=target.matrix_world.inverted(); imported_world=imported.matrix_world.copy()
            new_mesh=imported.data.copy()
            for v in new_mesh.vertices:
                v.co=high_inv @ (imported_world @ v.co)
            old_mesh=target.data; target.data=new_mesh; target.data.name='HIGH_QUAD_REMESHER_TOPOLOGY'
            bpy.data.objects.remove(imported,do_unlink=True)
            if old_mesh.users==0: bpy.data.meshes.remove(old_mesh)
            target.data.update(); bpy.context.view_layer.update(); activate(target)
            qr_stats=stats(target)
            print('QUAD_REMESHER_DONE',json.dumps({'seconds':qr_seconds,'stats':qr_stats}),flush=True)

            post_scale=max(target.dimensions) if max(target.dimensions)>0 else scale
            post_merge_distance=post_scale*1e-7
            post_merge_report=merge_by_distance(target,post_merge_distance)
            print('POST_QR_MERGE_BY_DISTANCE',json.dumps(post_merge_report),flush=True)
            normals_report=recalculate_outside(target)
            print('POST_QR_RECALCULATE_OUTSIDE',json.dumps(normals_report),flush=True)

            post_stats=stats(target); post_comps=component_report(target)
            post_warnings=[]
            if post_comps['count']!=1: post_warnings.append(f"connected_components={post_comps['count']}")
            if post_comps['micro_components_le_2_faces']: post_warnings.append(f"micro_components_le_2_faces={post_comps['micro_components_le_2_faces']}")
            if post_stats['boundary_edges']: post_warnings.append(f"boundary_edges={post_stats['boundary_edges']}")
            if post_stats['nonmanifold_nonboundary_edges']: post_warnings.append(f"nonmanifold_nonboundary_edges={post_stats['nonmanifold_nonboundary_edges']}")
            if post_stats['wire_edges']: post_warnings.append(f"wire_edges={post_stats['wire_edges']}")
            if post_stats['degenerate_faces']: post_warnings.append(f"degenerate_faces={post_stats['degenerate_faces']}")
            post_diag={'stats':post_stats,'components':post_comps,'warnings':post_warnings,'stops_pipeline':False}
            print('POST_QR_DIAGNOSTIC',json.dumps(post_diag),flush=True)
            if post_warnings: print('POST_QR_WARNING_ONLY','; '.join(post_warnings),flush=True)

            source.hide_set(False); source.hide_render=False; activate(target)
            for poly in target.data.polygons: poly.use_smooth=True
            final=stats(target); metrics=deviation(source,target)
            report={
                'asset':path.name,'source_sha256':digest,'blender':bpy.app.version_string,
                'method':'RAW -> conservative Merge by Distance -> warning-only diagnostics -> Quad Remesher 1.4.1 Blender add-on -> conservative Merge by Distance -> Recalculate Outside -> UV/Bake',
                'requested_quads':int(target_quads),'expected_triangles_if_all_quads':int(target_quads)*2,
                'adaptive_size':float(qr_config['adaptive_size']),'adapt_quad_count':bool(qr_config['adapt_quad_count']),
                'source':source_stats,'pre_qr_merge_by_distance':merge_report,'pre_qr_diagnostic':diag,
                'post_qr_merge_by_distance':post_merge_report,'post_qr_recalculate_outside':normals_report,'post_qr_diagnostic':post_diag,
                'quad_remesher_options':qr_options,'quad_remesher_seconds':qr_seconds,
                'high':final,'deviation':metrics,'quality_gate_passed':True,'pre_qr_diagnostics_are_hard_gate':False,
                'automatic_geometry_repair':False,'automatic_component_deletion':False,'automatic_hole_fill':False,
                'normal_recalculation_before_qr':False,'normal_recalculation_after_qr':True,
                'voxel_precondition_used':False,'seconds':time.time()-started,
            }
            source.hide_render=True; source.hide_set(True); activate(target)
            target['target_quads']=int(target_quads); target['target_triangles']=int(target_quads)*2
            target['source_file']=str(path); target['source_sha256']=digest
            bpy.ops.wm.save_as_mainfile(filepath=str(output/'02_retopology.blend'))
            (diagnostics/(path.stem+'.retopology.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
            if hashlib.sha256(path.read_bytes()).hexdigest()!=digest:
                return fail('Source file changed during retopology.')
            print('RETOPOLOGY_COMPLETE',path.name,final,flush=True)
            bpy.ops.wm.quit_blender()
            return None
        except Exception:
            import traceback
            return fail(traceback.format_exc())

    def poll_qr():
        if state['finished']: return None
        # The add-on imports retopo.fbx as a new mesh when its modal operator finishes.
        new_meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.as_pointer() not in baseline]
        if new_meshes:
            imported=bpy.context.view_layer.objects.active if bpy.context.view_layer.objects.active in new_meshes else new_meshes[0]
            return finalize_with(imported)
        elapsed=time.time()-state['started']
        progress=read_qr_progress()
        if progress is not None:
            print('QUAD_REMESHER_PROGRESS',progress,flush=True)
            try:
                val=float(progress.split()[0])
                if val < 0 and val not in (-2.0,):
                    return fail('Quad Remesher progress error: '+progress)
            except Exception:
                pass
        if elapsed > 57200:
            return fail('Quad Remesher timeout after 14400. Last progress: '+str(progress))
        return 0.5

    bpy.app.timers.register(poll_qr,first_interval=0.5,persistent=False)


if __name__=='__main__':
    ap=argparse.ArgumentParser(); ap.add_argument('--input',type=Path,required=True)
    ap.add_argument('--output',type=Path,default=Path('output')); ap.add_argument('--diagnostics',type=Path,default=Path('diagnostics'))
    ap.add_argument('--quads',type=int,default=50000)
    ap.add_argument('--qr-config-json',default='{}')
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    if args.quads<4: ap.error('--quads ha de ser >= 4')
    try:
        qr_config=json.loads(args.qr_config_json)
        required={
            'adaptive_size','adapt_quad_count','use_vertex_color','quad_density',
            'use_materials','use_normals_splitting','detect_hard_edges',
            'symmetry_x','symmetry_y','symmetry_z'
        }
        if set(qr_config)!=required:
            raise ValueError('QR config keys incorrectes: '+repr(sorted(set(qr_config)^required)))
        start_retopology_async(args.input.resolve(),args.output.resolve()/args.input.stem,args.diagnostics.resolve(),args.quads,qr_config)
    except Exception:
        import traceback
        print('RETOPOLOGY_START_ERROR',traceback.format_exc(),flush=True)
        try: (args.diagnostics.resolve()/'quad_remesher_error.txt').write_text(traceback.format_exc(),encoding='utf-8')
        except Exception: pass
        def _quit_after_error():
            try: bpy.ops.wm.quit_blender()
            except Exception: pass
            return None
        bpy.app.timers.register(_quit_after_error, first_interval=0.2)
