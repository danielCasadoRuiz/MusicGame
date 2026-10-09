from __future__ import annotations
import bpy, json, shutil, sys, traceback, time
from pathlib import Path
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

# ----------------------------- bootstrap -----------------------------

def args_after_double_dash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def parse_args():
    args = args_after_double_dash()
    out = {}
    i = 0
    while i < len(args):
        if args[i].startswith("--"):
            key = args[i][2:]
            val = args[i+1] if i+1 < len(args) else ""
            out[key] = val
            i += 2
        else:
            i += 1
    return out

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))
import garment_weights
from garment_profiles import resolve as resolve_garment

ARGS = parse_args()
# Without --session the module is imported as a library (headless tests of the weight stage);
# the interactive pipeline always passes --session.
LIBRARY_MODE = "session" not in ARGS
if not LIBRARY_MODE:
    SESSION_PATH = Path(ARGS["session"]).resolve()
    SESSION = json.loads(SESSION_PATH.read_text(encoding="utf-8"))
    SESSION_DIR = Path(SESSION["session_dir"])
    CFG = SESSION["config"]
    COMMAND_PATH = SESSION_DIR / "command.json"
    ACK_PATH = SESSION_DIR / "ack.json"
    READY_PATH = SESSION_DIR / "ready.json"
    STATE_PATH = SESSION_DIR / "state.json"
    LOG_PATH = SESSION_DIR / "fitting_rigging.log"
else:
    SESSION, CFG = {}, {}
    SESSION_DIR = COMMAND_PATH = ACK_PATH = READY_PATH = STATE_PATH = LOG_PATH = None

ARM = None
BODY = None
GARMENT = None
REFERENCE_AREA = None
REFERENCE_AREA_TYPE = None
LAST_COMMAND_ID = None
STATE = {"weights_done": False, "blendshapes_done": False}
GW_REFERENCE = None   # garment-aware weights of the ULTRA, reused by every other LOD of the session

def world_vertex_coords(obj):
    mw = obj.matrix_world.copy()
    return [mw @ v.co for v in obj.data.vertices]


def world_bounds(obj):
    pts = world_vertex_coords(obj)
    if not pts:
        return None
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    return {
        "min": (min(xs), min(ys), min(zs)),
        "max": (max(xs), max(ys), max(zs)),
        "center": (
            (min(xs) + max(xs)) * 0.5,
            (min(ys) + max(ys)) * 0.5,
            (min(zs) + max(zs)) * 0.5,
        ),
    }


def max_bound_delta(a, b):
    if not a or not b:
        return 0.0
    vals = []
    for key in ("min", "max", "center"):
        vals.extend(abs(float(x) - float(y)) for x, y in zip(a[key], b[key]))
    return max(vals) if vals else 0.0


def bake_object_transform_preserve_world(obj):
    """Bake loc/rot/scale into mesh coordinates without changing visual world-space."""
    if obj.type != 'MESH':
        raise RuntimeError("El garment ha de ser MESH per bakejar transforms.")

    before = world_bounds(obj)
    world_coords = world_vertex_coords(obj)

    for v, co in zip(obj.data.vertices, world_coords):
        v.co = co

    obj.matrix_world = Matrix.Identity(4)
    obj.data.update()
    bpy.context.view_layer.update()

    after = world_bounds(obj)
    delta = max_bound_delta(before, after)
    if delta > 1e-5:
        raise RuntimeError(
            f"El bake de transforms ha mogut visualment la peça (delta={delta:.8f})."
        )
    log(f"TRANSFORM bake world-preserving OK delta={delta:.8f}")


def parent_to_armature_preserve_world(obj, arm):
    """Parent garment to armature while keeping the exact current world transform."""
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = 'OBJECT'
    obj.matrix_parent_inverse = arm.matrix_world.inverted_safe()
    obj.matrix_world = world
    bpy.context.view_layer.update()
    obj.matrix_world = world
    bpy.context.view_layer.update()

LOD_OUTPUT_FOLDERS = {
    "ULTRA_50K": "ULTRA",
    "HIGH_30K": "HIGH",
    "MIDHIGH_20K": "MID-HIGH",
    "MEDIUM_10K": "MEDIUM",
    "LOW_5K": "LOW",
}


def lod_output_folder(model_path: Path):
    upper = model_path.stem.upper()
    for token, folder in LOD_OUTPUT_FOLDERS.items():
        if token in upper:
            return folder
    return model_path.stem


def iter_material_images(obj):
    seen = set()
    for slot in obj.material_slots:
        mat = slot.material
        if mat is None or not mat.use_nodes or mat.node_tree is None:
            continue
        for node in mat.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or node.image is None:
                continue
            img = node.image
            key = img.as_pointer()
            if key in seen:
                continue
            seen.add(key)
            yield img


def safe_texture_name(name: str):
    cleaned = ''.join(c if c.isalnum() or c in '._- ' else '_' for c in name).strip()
    return cleaned or 'texture'


def export_textures(obj, textures_dir: Path):
    textures_dir.mkdir(parents=True, exist_ok=True)
    for old in textures_dir.iterdir():      # this folder belongs to the export: rewrite it, never accumulate
        if old.is_file():
            old.unlink()
    exported=[]
    for img in iter_material_images(obj):
        base=safe_texture_name(Path(img.name).stem)
        src=None
        try:
            if img.filepath:
                src=Path(bpy.path.abspath(img.filepath))
        except Exception:
            src=None
        ext=(src.suffix.lower() if src and src.suffix else '.png')
        if ext not in {'.png','.jpg','.jpeg','.tga','.bmp','.tif','.tiff','.exr'}:
            ext='.png'
        dst=textures_dir/f'{base}{ext}'
        n=2
        while dst.exists():
            dst=textures_dir/f'{base}_{n}{ext}'; n+=1
        try:
            if src is not None and src.is_file() and not img.packed_file:
                shutil.copy2(src,dst)
            else:
                old_path=img.filepath_raw
                old_fmt=img.file_format
                try:
                    img.filepath_raw=str(dst)
                    fmt={'.png':'PNG','.jpg':'JPEG','.jpeg':'JPEG','.tga':'TARGA','.bmp':'BMP','.tif':'TIFF','.tiff':'TIFF','.exr':'OPEN_EXR'}.get(ext,'PNG')
                    img.file_format=fmt
                    img.save()
                finally:
                    img.filepath_raw=old_path
                    img.file_format=old_fmt
            exported.append(str(dst))
        except Exception as e:
            log(f'TEXTURE WARNING {img.name}: {e}')
    log(f'TEXTURES exported count={len(exported)} dir={textures_dir}')
    return exported


def save_final_blend_copy(path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        path.unlink()                       # overwrite without leaving a .blend1 backup in Output
    bpy.ops.wm.save_as_mainfile(filepath=str(path), copy=True)
    backup = path.with_suffix(".blend1")
    if backup.exists():
        backup.unlink()
    log(f'BLEND final saved {path}')


def export_current_fbx_package():
    refresh_scene_refs(require_garment=True)
    model=CURRENT_MODEL
    out_root=Path(SESSION['output_dir'])
    lod_dir=out_root/lod_output_folder(model)
    textures_dir=lod_dir/'Textures'
    lod_dir.mkdir(parents=True, exist_ok=True)
    stem=model.stem
    fbx_path=lod_dir/f'{stem}_rigged.fbx'
    blend_path=lod_dir/f'{stem}_rigged.blend'

    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    reset_armature_pose()
    ensure_armature_modifier()
    ensure_armature_parent()

    save_final_blend_copy(blend_path)
    textures=export_textures(GARMENT,textures_dir)

    bpy.ops.object.select_all(action='DESELECT')
    ARM.hide_set(False); GARMENT.hide_set(False)
    ARM.select_set(True); GARMENT.select_set(True)
    bpy.context.view_layer.objects.active=GARMENT

    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types={'ARMATURE','MESH'},
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True,
        axis_forward='-Z',
        axis_up='Y',
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=False,
        path_mode='COPY',
        embed_textures=True,
        use_mesh_modifiers=True,
        mesh_smooth_type='OFF',
    )
    log(f'EXPORT FBX package done {fbx_path}')
    return {'fbx':str(fbx_path),'blend':str(blend_path),'textures':textures,'folder':str(lod_dir)}

FIT_SNAPSHOT = None
CURRENT_MODEL = Path(SESSION["model"]).resolve() if SESSION.get("model") else None


def _object_is_alive(obj):
    if obj is None:
        return False
    try:
        name = obj.name
        return bpy.data.objects.get(name) is obj
    except ReferenceError:
        return False


def _find_role(role, preferred_name=None, obj_type=None):
    if preferred_name:
        obj = bpy.data.objects.get(preferred_name)
        if obj and (obj_type is None or obj.type == obj_type):
            try:
                if obj.get("FAR_ROLE") == role or role == "GARMENT":
                    return obj
            except ReferenceError:
                pass
    matches = []
    for obj in bpy.data.objects:
        try:
            if obj.get("FAR_ROLE") == role and (obj_type is None or obj.type == obj_type):
                matches.append(obj)
        except ReferenceError:
            continue
    if not matches:
        return None
    # If the user duplicated the garment during manual fitting, prefer the visible,
    # selected/active candidate and otherwise the largest mesh.
    active = bpy.context.view_layer.objects.active
    if active in matches:
        return active
    visible = [o for o in matches if not o.hide_get()]
    if visible:
        matches = visible
    if obj_type == 'MESH':
        return max(matches, key=lambda o: len(o.data.vertices))
    return matches[0]


def refresh_scene_refs(require_garment=True):
    """Re-resolve Blender objects after arbitrary manual editing.

    Manual fitting may delete, duplicate, rename or replace objects. Never trust
    the Python StructRNA references captured when Blender first opened.
    """
    global ARM, BODY, GARMENT

    if not _object_is_alive(ARM):
        ARM = _find_role("AVATAR", preferred_name="Armature", obj_type="ARMATURE")
        if ARM is None:
            arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
            ARM = max(arms, key=lambda o: len(o.data.bones)) if arms else None

    if not _object_is_alive(BODY):
        # BODY is tagged AVATAR together with eyes/etc.; choose the mesh with
        # the strongest overlap with deform bones.
        meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.get("FAR_ROLE") == "AVATAR"]
        if ARM and meshes:
            deform_names = {b.name for b in ARM.data.bones if b.use_deform}
            def score(o):
                groups = {g.name for g in o.vertex_groups}
                overlap = len(groups & deform_names)
                shapes = len(o.data.shape_keys.key_blocks) if o.data.shape_keys else 0
                return (overlap, shapes, len(o.data.vertices))
            BODY = max(meshes, key=score)
        else:
            BODY = bpy.data.objects.get("Body")

    if not _object_is_alive(GARMENT):
        GARMENT = _find_role("GARMENT", preferred_name="GARMENT", obj_type="MESH")
        if GARMENT is None:
            # A duplicate normally preserves FAR_ROLE=GARMENT. As a final safe
            # fallback, choose a non-avatar mesh only when it is unambiguous.
            candidates = [
                o for o in bpy.data.objects
                if o.type == 'MESH' and o != BODY and o.get("FAR_ROLE") != "AVATAR"
            ]
            if len(candidates) == 1:
                GARMENT = candidates[0]
                GARMENT["FAR_ROLE"] = "GARMENT"
                GARMENT.name = "GARMENT"

    if ARM is None:
        raise RuntimeError("L'Armature del MakeHuman ja no existeix a l'escena.")
    if BODY is None:
        raise RuntimeError("El BODY del MakeHuman ja no existeix a l'escena.")
    if require_garment and GARMENT is None:
        raise RuntimeError(
            "La peça GARMENT ha estat eliminada durant el fitting i no puc "
            "identificar de forma segura quin mesh l'ha substituït. Torna al "
            "checkpoint manual i conserva/duplica la peça en lloc d'eliminar-la."
        )


def log(msg):
    text = f"[{time.strftime('%H:%M:%S')}] {msg}"
    print(text, flush=True)
    if LOG_PATH is None:
        return
    with LOG_PATH.open("a", encoding="utf-8") as f:
        f.write(text + "\n")


def write_json(path: Path, data):
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    # Windows refuses the replace while the controller is reading ack.json: retry briefly.
    for attempt in range(40):
        try:
            tmp.replace(path)
            return
        except PermissionError:
            if attempt == 39:
                raise
            time.sleep(0.05)

def matrix_to_list(m):
    return [[float(m[r][c]) for c in range(4)] for r in range(4)]


def matrix_from_list(rows):
    return Matrix(rows)


def capture_fit_snapshot():
    global FIT_SNAPSHOT
    refresh_scene_refs(require_garment=True)
    FIT_SNAPSHOT = {
        "garment_matrix_world": matrix_to_list(GARMENT.matrix_world.copy()),
        "pose_basis": {pb.name: matrix_to_list(pb.matrix_basis.copy()) for pb in ARM.pose.bones},
    }
    log(f"FIT snapshot captured bones={len(FIT_SNAPSHOT['pose_basis'])}")


def restore_fit_snapshot_to_current_garment():
    if FIT_SNAPSHOT is None:
        raise RuntimeError("No hi ha cap fitting manual capturat de l'ULTRA.")
    reset_armature_pose()
    for name, rows in FIT_SNAPSHOT["pose_basis"].items():
        pb = ARM.pose.bones.get(name)
        if pb:
            pb.matrix_basis = matrix_from_list(rows)
    GARMENT.matrix_world = matrix_from_list(FIT_SNAPSHOT["garment_matrix_world"])
    bpy.context.view_layer.update()


def remove_current_garment():
    global GARMENT
    refresh_scene_refs(require_garment=False)
    if _object_is_alive(GARMENT):
        obj = GARMENT
        GARMENT = None
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.view_layer.update()


def import_lod_as_garment(model_path: Path):
    global GARMENT, CURRENT_MODEL
    remove_current_garment()
    imported = import_asset(model_path)
    for o in imported:
        o["FAR_ROLE"] = "GARMENT_SOURCE"
    GARMENT = join_garment_meshes(imported)
    GARMENT["FAR_ROLE"] = "GARMENT"
    CURRENT_MODEL = model_path.resolve()
    restore_fit_snapshot_to_current_garment()
    return GARMENT


def save_state():
    if STATE_PATH is not None:
        write_json(STATE_PATH, STATE)


def save_preprocess_checkpoint(model_path: Path):
    """Save a recoverable .blend after applying the ULTRA fitting snapshot,
    but BEFORE weight transfer / pose normalization / blendshape generation.
    """
    refresh_scene_refs(require_garment=True)
    pre_dir = SESSION_DIR / "preprocessed"
    pre_dir.mkdir(parents=True, exist_ok=True)
    path = pre_dir / f"{model_path.stem}_preprocessed.blend"

    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        try:
            bpy.ops.object.mode_set(mode='OBJECT')
        except Exception:
            pass

    bpy.ops.wm.save_as_mainfile(filepath=str(path), copy=True)
    log(f"PREPROCESS checkpoint saved {path}")
    return path


def ack(cmd_id, status="ok", message="", result=None):
    write_json(ACK_PATH, {"id": cmd_id, "status": status, "message": message, "result": result or {}})

# ----------------------------- import/setup -----------------------------

def clear_scene():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        try: bpy.ops.object.mode_set(mode='OBJECT')
        except Exception: pass
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def import_asset(path: Path):
    before = set(bpy.data.objects)
    ext = path.suffix.lower()
    if ext == ".blend":
        with bpy.data.libraries.load(str(path), link=False) as (src, dst):
            dst.objects = src.objects
        for obj in dst.objects:
            if obj and obj.name not in bpy.context.scene.objects:
                bpy.context.collection.objects.link(obj)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(path))
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    elif ext == ".obj":
        # Blender 4.x operator
        try: bpy.ops.wm.obj_import(filepath=str(path))
        except Exception: bpy.ops.import_scene.obj(filepath=str(path))
    else:
        raise RuntimeError(f"Format no suportat: {ext}")
    return [o for o in bpy.data.objects if o not in before]


def choose_armature(objects):
    explicit = (CFG.get("avatar_armature_name") or "").strip()
    if explicit:
        obj = bpy.data.objects.get(explicit)
        if obj and obj.type == 'ARMATURE': return obj
    arms = [o for o in objects if o.type == 'ARMATURE']
    if not arms: arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if not arms: raise RuntimeError("No s'ha trobat cap Armature a l'avatar.")
    return max(arms, key=lambda o: len(o.data.bones))


def choose_body(objects):
    explicit = (CFG.get("avatar_body_name") or "").strip()
    if explicit:
        obj = bpy.data.objects.get(explicit)
        if obj and obj.type == 'MESH':
            return obj
        raise RuntimeError(f"avatar_body_name='{explicit}' no existeix o no és MESH.")

    meshes = [o for o in objects if o.type == 'MESH']
    if not meshes:
        raise RuntimeError("No s'ha trobat cap mesh al MakeHuman FBX.")

    deform_names = {b.name for b in ARM.data.bones if b.use_deform}

    def score(o):
        groups = {g.name for g in o.vertex_groups}
        overlap = len(groups & deform_names)
        shape_count = len(o.data.shape_keys.key_blocks) - 1 if o.data.shape_keys else 0
        # Rig compatibility is the strongest signal, then morphs, then size.
        return (overlap, shape_count, len(o.data.vertices))

    body = max(meshes, key=score)
    overlap = len({g.name for g in body.vertex_groups} & deform_names)
    if overlap == 0:
        raise RuntimeError(
            "He trobat meshes a MakeHuman_Canonical.fbx però cap comparteix vertex groups "
            "amb els deform bones del rig. No puc identificar el BODY de forma segura."
        )
    return body


def validate_avatar_import(avatar_path: Path):
    if avatar_path.suffix.lower() != '.fbx':
        raise RuntimeError(f"L'avatar canònic ha de ser FBX: {avatar_path.name}")
    deform_names = {b.name for b in ARM.data.bones if b.use_deform}
    if not deform_names:
        raise RuntimeError("L'Armature importat no té cap deform bone.")
    body_groups = {g.name for g in BODY.vertex_groups}
    matching_groups = sorted(body_groups & deform_names)
    if not matching_groups:
        raise RuntimeError("El BODY no té vertex groups que coincideixin amb els deform bones.")
    shape_count = len(BODY.data.shape_keys.key_blocks) - 1 if BODY.data.shape_keys else 0
    info = {
        'avatar_format': 'FBX',
        'deform_bones': len(deform_names),
        'body_deform_groups': len(matching_groups),
        'body_blendshapes': max(0, shape_count),
    }
    log(
        f"AVATAR OK format=FBX armature={ARM.name} body={BODY.name} "
        f"deform_bones={info['deform_bones']} body_groups={info['body_deform_groups']} "
        f"blendshapes={info['body_blendshapes']}"
    )
    if shape_count == 0:
        log("WARNING: MakeHuman_Canonical.fbx no ha importat cap blendshape/Shape Key. "
            "El skinning funcionarà, però les peces marcades amb blendshapes fallaran fins que l'FBX contingui morph targets.")
    return info


def join_garment_meshes(objects):
    meshes = [o for o in objects if o.type == 'MESH']
    if not meshes: raise RuntimeError("El model de roba no conté cap mesh.")
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    g = bpy.context.view_layer.objects.active
    g.name = "GARMENT"
    return g


def set_reference_image(path_str):
    global REFERENCE_AREA, REFERENCE_AREA_TYPE
    if not path_str:
        return
    p = Path(path_str)
    if not p.exists():
        return
    try:
        image = bpy.data.images.load(str(p), check_existing=True)
        # Reuse a non-3D editor first; preserve the main viewport.
        screen = bpy.context.window.screen
        candidates = [a for a in screen.areas if a.type in {'OUTLINER','PROPERTIES','DOPESHEET_EDITOR','CONSOLE'}]
        if not candidates:
            candidates = [a for a in screen.areas if a.type != 'VIEW_3D']
        if not candidates:
            return
        area = max(candidates, key=lambda a: a.width * a.height)
        REFERENCE_AREA, REFERENCE_AREA_TYPE = area, area.type
        area.type = 'IMAGE_EDITOR'
        area.spaces.active.image = image
        log(f"Reference image loaded in Blender: {p.name}")
    except Exception as e:
        log(f"WARNING reference image: {e}")


def close_reference_image():
    global REFERENCE_AREA, REFERENCE_AREA_TYPE
    if REFERENCE_AREA and REFERENCE_AREA_TYPE:
        try: REFERENCE_AREA.type = REFERENCE_AREA_TYPE
        except Exception: pass
    REFERENCE_AREA = None
    REFERENCE_AREA_TYPE = None


def frame_scene():
    for area in bpy.context.window.screen.areas:
        if area.type == 'VIEW_3D':
            try:
                region = next(r for r in area.regions if r.type == 'WINDOW')
                with bpy.context.temp_override(area=area, region=region):
                    bpy.ops.view3d.view_axis(type='FRONT', align_active=False)
                    bpy.ops.view3d.view_all(center=False)
            except Exception:
                pass


# ----------------------------- reuse of a saved fitting -----------------------------
#
# The saved fitting is the same snapshot commit_manual_fit() captures and already re-applies to every
# LOD (garment matrix_world + rig pose basis). It lives with the asset, in
# Output/<asset>/Rigged/fit_record.json, together with the fingerprints of the ULTRA + avatar files
# it was made on (checked by the controller before offering it; re-checked here).

def _saved_fit_problems(fit):
    problems = []
    nverts = len(GARMENT.data.vertices)
    if fit.get("garment_vertices") not in (None, nverts):
        problems.append(f"la malla de l'ULTRA actual ({nverts} vèrtexs) no és la del fitting desat "
                        f"({fit.get('garment_vertices')} vèrtexs)")
    bones = set(fit.get("fit_snapshot", {}).get("pose_basis", {}))
    missing = bones - {b.name for b in ARM.data.bones}
    if not bones or missing:
        problems.append(f"l'esquelet de l'avatar no conté els ossos del fitting desat ({len(missing)} absents)")
    return problems


def initial_setup():
    global ARM, BODY, GARMENT, FIT_SNAPSHOT
    clear_scene()
    avatar_objs = import_asset(Path(SESSION["avatar"]))
    for o in avatar_objs:
        o["FAR_ROLE"] = "AVATAR"
    ARM = choose_armature(avatar_objs)
    BODY = choose_body(avatar_objs)
    avatar_info = validate_avatar_import(Path(SESSION["avatar"]))
    ARM.name = ARM.name  # preserve canonical bone/rig naming
    BODY.name = BODY.name

    garment_objs = import_asset(Path(SESSION["model"]))
    for o in garment_objs:
        o["FAR_ROLE"] = "GARMENT_SOURCE"
    GARMENT = join_garment_meshes(garment_objs)
    GARMENT["FAR_ROLE"] = "GARMENT"

    # Make visual inspection easy.
    BODY.display_type = 'SOLID'
    BODY.show_in_front = False
    ARM.show_in_front = True
    bpy.ops.object.select_all(action='DESELECT')
    GARMENT.select_set(True)
    bpy.context.view_layer.objects.active = GARMENT
    frame_scene()

    rejected = None
    reuse = SESSION.get("reuse_fit")
    if reuse:
        problems = _saved_fit_problems(reuse)
        if problems:
            rejected = {"reasons": problems}
            log(f"SAVED FIT rejected: {problems}")
        else:
            FIT_SNAPSHOT = reuse["fit_snapshot"]
            restore_fit_snapshot_to_current_garment()
            save_state()
            write_json(READY_PATH, {'status': 'ok', 'body': BODY.name, 'armature': ARM.name, 'garment': GARMENT.name,
                                    'fit_reused': True, **avatar_info})
            log("READY (saved fitting reused: garment transform + rig pose)")
            return

    # Pre-create the manual fitting checkpoint. Ctrl+S now saves exactly here.
    manual_blend = Path(SESSION.get('manual_blend', SESSION_DIR / 'manual_fit.blend'))
    manual_blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(manual_blend))

    save_state()
    write_json(READY_PATH, {
        'status':'ok', 'body':BODY.name, 'armature':ARM.name, 'garment':GARMENT.name,
        'manual_blend':str(manual_blend), **avatar_info,
        **({'fit_reuse_rejected': rejected} if rejected else {})
    })
    log(f"READY body={BODY.name} armature={ARM.name} garment={GARMENT.name} checkpoint={manual_blend}")

# ----------------------------- geometry math -----------------------------

def barycentric(p, a, b, c):
    v0, v1, v2 = b-a, c-a, p-a
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    denom = d00*d11 - d01*d01
    if abs(denom) < 1e-20:
        return (1.0, 0.0, 0.0)
    v = (d11*d20 - d01*d21) / denom
    w = (d00*d21 - d01*d20) / denom
    u = 1.0 - v - w
    return (u, v, w)


def evaluated_mesh_world(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = ev.to_mesh()
    try:
        me.calc_loop_triangles()
        verts = [ev.matrix_world @ v.co for v in me.vertices]
        tris = [tuple(t.vertices) for t in me.loop_triangles]
    finally:
        ev.to_mesh_clear()
    return verts, tris


def body_vertex_weights():
    deform_names = {b.name for b in ARM.data.bones if b.use_deform}
    group_names = {i:g.name for i,g in enumerate(BODY.vertex_groups) if g.name in deform_names}
    out = []
    for v in BODY.data.vertices:
        d = {}
        for ge in v.groups:
            name = group_names.get(ge.group)
            if name and ge.weight > 0:
                d[name] = ge.weight
        out.append(d)
    return out


def transfer_weights_from_current_body_pose(reference_mode="standalone"):
    """Seed = nearest posed-body surface (unchanged), then the garment-aware refinement.
    reference_mode: "build" (ULTRA: refine + keep as reference), "use" (other LODs: transfer from the
    ULTRA reference), "standalone" (refine this mesh on its own)."""
    global GARMENT
    log("WEIGHTS transfer from current posed body")
    bverts, tris = evaluated_mesh_world(BODY)
    if not tris: raise RuntimeError("Body sense triangles.")
    bvh = BVHTree.FromPolygons(bverts, tris, all_triangles=True)
    src_weights = body_vertex_weights()

    # Replace any previous binding groups with canonical deform groups.
    deform = {b.name for b in ARM.data.bones if b.use_deform}
    for vg in list(GARMENT.vertex_groups):
        if vg.name in deform:
            GARMENT.vertex_groups.remove(vg)
    groups = {name: GARMENT.vertex_groups.new(name=name) for name in deform}

    max_inf = int(CFG.get("max_influences", 4))
    min_w = float(CFG.get("min_weight", 0.0001))
    unmatched = 0
    for gv in GARMENT.data.vertices:
        pw = GARMENT.matrix_world @ gv.co
        loc, normal, tri_idx, dist = bvh.find_nearest(pw)
        if tri_idx is None:
            unmatched += 1
            continue
        ia, ib, ic = tris[tri_idx]
        u,v,w = barycentric(loc, bverts[ia], bverts[ib], bverts[ic])
        acc = {}
        for idx, coef in ((ia,u),(ib,v),(ic,w)):
            for name, sw in src_weights[idx].items():
                acc[name] = acc.get(name,0.0) + coef*sw
        items = [(n,max(0.0,x)) for n,x in acc.items() if x >= min_w]
        items.sort(key=lambda t:t[1], reverse=True)
        items = items[:max_inf]
        total = sum(x for _,x in items)
        if total <= 1e-12:
            unmatched += 1
            continue
        for name,x in items:
            groups[name].add([gv.index], x/total, 'REPLACE')
    log(f"WEIGHTS done vertices={len(GARMENT.data.vertices)} unmatched={unmatched}")
    refine_garment_weights(reference_mode, max_inf, min_w)
    STATE["weights_done"] = True
    STATE["weight_unmatched"] = unmatched
    save_state()


def garment_type_info():
    if SESSION.get("garment_type"):
        return SESSION["garment_type"], SESSION.get("garment_overrides") or {}
    name = CURRENT_MODEL.stem if CURRENT_MODEL else GARMENT.name
    info = resolve_garment(name, CURRENT_MODEL.parent if CURRENT_MODEL else None)
    return info["garment_type"], info["overrides"]


def refine_garment_weights(reference_mode, max_inf, min_w):
    global GW_REFERENCE
    section = CFG.get("garment_weights", {})
    if not section.get("enabled", True):
        log("GARMENT WEIGHTS disabled (config garment_weights.enabled=false): seed weights kept")
        return
    gtype, overrides = garment_type_info()
    use_ref = GW_REFERENCE if reference_mode == "use" else None
    if reference_mode == "use" and use_ref is None:
        log("GARMENT WEIGHTS WARNING: no ULTRA reference in this session, refining this LOD on its own")
    report, ref = garment_weights.refine(
        GARMENT, ARM, BODY, section, gtype, overrides, max_inf, min_w,
        reference=use_ref, build_reference=(reference_mode == "build"), log=log)
    if ref is not None:
        GW_REFERENCE = ref
    key = CURRENT_MODEL.name if CURRENT_MODEL else GARMENT.name
    STATE.setdefault("garment_weights", {})[key] = report


def write_deformation_report():
    """Optional test poses on the final rest-pose garment (config garment_weights.validation = "report").
    The poses are temporary: the armature is always back in REST afterwards."""
    section = CFG.get("garment_weights", {})
    if section.get("validation", "report") != "report":
        return None
    try:
        rep = garment_weights.deformation_report(GARMENT, ARM, BODY)
    finally:
        reset_armature_pose()
    st = rep["static"]
    log(f"DEFORMATION REPORT L/R jumps={st['edges_direct_L_to_R_leg_jump']} wrong_side={st['leg_vertices_on_wrong_side_>3cm']} "
        + " ".join(f"{k}:max={v['max']}/energy={v['stretch_energy']}" for k, v in rep["poses"].items()))
    if SESSION_DIR is not None and CURRENT_MODEL is not None:
        out = SESSION_DIR / "weight_reports" / f"{CURRENT_MODEL.stem}.json"
        out.parent.mkdir(parents=True, exist_ok=True)
        write_json(out, {"model": CURRENT_MODEL.name, "weights": STATE.get("garment_weights", {}).get(CURRENT_MODEL.name), **rep})
    return rep


def weighted_deform_matrix(vertex):
    group_index_to_name = {g.index:g.name for g in GARMENT.vertex_groups}
    mats = []
    total = 0.0
    for ge in vertex.groups:
        name = group_index_to_name.get(ge.group)
        if not name or name not in ARM.pose.bones: continue
        pb = ARM.pose.bones[name]
        # Armature-space rest -> current pose transform.
        d = pb.matrix @ pb.bone.matrix_local.inverted()
        mats.append((d, ge.weight))
        total += ge.weight
    if total <= 1e-12:
        return Matrix.Identity(4)
    rows = [[0.0]*4 for _ in range(4)]
    for m,w in mats:
        wn = w/total
        for r in range(4):
            for c in range(4):
                rows[r][c] += m[r][c]*wn
    return Matrix(rows)


def normalize_garment_from_pose_to_rest():
    log("POSE normalize garment current pose -> armature REST")
    arm_inv = ARM.matrix_world.inverted()
    g_inv = GARMENT.matrix_world.inverted()
    for v in GARMENT.data.vertices:
        p_world = GARMENT.matrix_world @ v.co
        p_arm_pose = arm_inv @ p_world
        blend = weighted_deform_matrix(v)
        try: p_arm_rest = blend.inverted() @ p_arm_pose
        except Exception: p_arm_rest = blend.inverted_safe() @ p_arm_pose
        p_world_rest = ARM.matrix_world @ p_arm_rest
        v.co = g_inv @ p_world_rest
    GARMENT.data.update()
    reset_armature_pose()
    ensure_armature_modifier()
    log("POSE normalization done")


def reset_armature_pose():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        try: bpy.ops.object.mode_set(mode='OBJECT')
        except Exception: pass
    for pb in ARM.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def ensure_armature_modifier():
    for m in list(GARMENT.modifiers):
        if m.type == 'ARMATURE' and m.object == ARM:
            return m
    mod = GARMENT.modifiers.new(name="MakeHuman Armature", type='ARMATURE')
    mod.object = ARM
    mod.use_deform_preserve_volume = False
    return mod

# ----------------------------- blendshape transfer -----------------------------

def set_all_body_shapes_zero():
    sk = BODY.data.shape_keys
    if not sk: return
    for kb in sk.key_blocks:
        if kb.name != 'Basis': kb.value = 0.0
    bpy.context.view_layer.update()


def eval_body_world_positions():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = BODY.evaluated_get(deps)
    me = ev.to_mesh()
    try:
        return [ev.matrix_world @ v.co for v in me.vertices]
    finally:
        ev.to_mesh_clear()


def rest_body_triangles():
    BODY.data.calc_loop_triangles()
    return [tuple(t.vertices) for t in BODY.data.loop_triangles]


def frame_coeff(p, a, b, c):
    e1, e2 = b-a, c-a
    n = e1.cross(e2)
    if n.length < 1e-12:
        return Vector((0,0,0))
    n.normalize()
    basis = Matrix((e1, e2, n)).transposed()
    try: return basis.inverted() @ (p-a)
    except Exception: return basis.inverted_safe() @ (p-a)


def reconstruct(coeff, a, b, c):
    e1, e2 = b-a, c-a
    n = e1.cross(e2)
    if n.length < 1e-12: return a + coeff.x*e1 + coeff.y*e2
    n.normalize()
    return a + coeff.x*e1 + coeff.y*e2 + coeff.z*n


def generate_blendshapes():
    refresh_scene_refs(require_garment=True)
    if not BODY.data.shape_keys or len(BODY.data.shape_keys.key_blocks) <= 1:
        raise RuntimeError("El BODY no té Shape Keys per transferir.")
    reset_armature_pose()
    set_all_body_shapes_zero()
    basis_body = eval_body_world_positions()
    tris = rest_body_triangles()
    bvh = BVHTree.FromPolygons(basis_body, tris, all_triangles=True)
    mappings = []
    far = 0
    max_dist = float(CFG.get("blendshape_max_distance", 0.25))
    for gv in GARMENT.data.vertices:
        pw = GARMENT.matrix_world @ gv.co
        loc, normal, tri_idx, dist = bvh.find_nearest(pw)
        if tri_idx is None:
            mappings.append(None); far += 1; continue
        ia,ib,ic = tris[tri_idx]
        coeff = frame_coeff(pw, basis_body[ia], basis_body[ib], basis_body[ic])
        mappings.append((tri_idx, coeff, dist))
        if dist is not None and dist > max_dist: far += 1

    # Start clean while preserving Basis if present.
    if GARMENT.data.shape_keys:
        for kb in list(GARMENT.data.shape_keys.key_blocks)[1:]:
            GARMENT.shape_key_remove(kb)
    else:
        GARMENT.shape_key_add(name="Basis", from_mix=False)

    exclude = set(CFG.get("exclude_shape_keys", []))
    source_keys = [kb for kb in BODY.data.shape_keys.key_blocks if kb.name != 'Basis' and kb.name not in exclude]
    generated = []
    for idx, src in enumerate(source_keys, 1):
        set_all_body_shapes_zero()
        src.value = 1.0
        bpy.context.view_layer.update()
        deformed = eval_body_world_positions()
        dst = GARMENT.shape_key_add(name=src.name, from_mix=False)
        inv_g = GARMENT.matrix_world.inverted()
        for vi, mapping in enumerate(mappings):
            if mapping is None:
                dst.data[vi].co = GARMENT.data.vertices[vi].co
                continue
            tri_idx, coeff, dist = mapping
            ia,ib,ic = tris[tri_idx]
            q_world = reconstruct(coeff, deformed[ia], deformed[ib], deformed[ic])
            dst.data[vi].co = inv_g @ q_world
        generated.append(src.name)
        log(f"SHAPE {idx}/{len(source_keys)} {src.name}")
    set_all_body_shapes_zero()
    STATE["blendshapes_done"] = True
    STATE["blendshapes"] = generated
    STATE["blendshape_far_vertices"] = far
    save_state()
    log(f"BLENDSHAPES done count={len(generated)} far_vertices={far}")

def commit_manual_fit():
    """Commit the single manual fitting checkpoint.

    User may have transformed the garment object and posed the avatar armature.
    We bake garment object transforms, transfer weights against the currently posed
    body, inverse-skin the garment back to the armature rest pose, then restore the
    canonical rig pose.
    """
    log("MANUAL FIT commit start")
    refresh_scene_refs(require_garment=True)
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        try: bpy.ops.object.mode_set(mode='OBJECT')
        except Exception: pass

    # Capture the one manual fitting before anything is baked/reset.
    capture_fit_snapshot()

    # Ensure body morphs do not contaminate the fitting/weight projection.
    set_all_body_shapes_zero()

    # Preserve the ULTRA manual preprocess exactly as the user left it,
    # before any destructive rigging calculations.
    pre = save_preprocess_checkpoint(CURRENT_MODEL)
    STATE.setdefault("preprocessed_checkpoints", {})[CURRENT_MODEL.name] = str(pre)
    save_state()

    # The user's ULTRA placement is the source of truth.
    # Bake it into mesh data without changing the visible world-space result.
    bpy.ops.object.select_all(action='DESELECT')
    GARMENT.hide_set(False)
    GARMENT.select_set(True)
    bpy.context.view_layer.objects.active = GARMENT
    bake_object_transform_preserve_world(GARMENT)

    # Body is still evaluated in the user-defined temporary bone pose here.
    transfer_weights_from_current_body_pose(reference_mode="build")
    normalize_garment_from_pose_to_rest()
    write_deformation_report()

    # Save a post-normalization checkpoint as well.
    normalized = SESSION_DIR / 'normalized_rest_pose.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(normalized))
    STATE['manual_fit_committed'] = True
    STATE['normalized_checkpoint'] = str(normalized)
    save_state()
    log("MANUAL FIT commit done")

# ----------------------------- UI commands -----------------------------

def apply_fit():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    GARMENT.select_set(True)
    bpy.context.view_layer.objects.active = GARMENT
    # Bake the user's world placement into mesh coordinates.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    log("FIT transforms applied")


def enter_pose_adjust():
    close_reference_image()
    bpy.ops.object.select_all(action='DESELECT')
    ARM.hide_set(False)
    ARM.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.ops.object.mode_set(mode='POSE')
    for b in ARM.data.bones:
        b.select = False
    log("POSE ADJUST mode enabled")


def pose_ok():
    close_reference_image()
    reset_armature_pose()
    log("POSE already matches REST")


def bind_and_normalize():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    transfer_weights_from_current_body_pose()
    normalize_garment_from_pose_to_rest()


def transfer_weights_rest():
    reset_armature_pose()
    transfer_weights_from_current_body_pose()
    ensure_armature_modifier()


def ensure_armature_parent():
    parent_to_armature_preserve_world(GARMENT, ARM)

def export_current_glb():
    # Legacy command name: now exports FBX + BLEND + textures.
    return export_current_fbx_package()

def process_additional_lod(model_path: Path, needs_blendshapes: bool):
    log(f"LOD start {model_path.name}")
    import_lod_as_garment(model_path)

    # The imported LOD receives exactly the same object fitting and temporary rig pose
    # that the user established once on ULTRA.
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    set_all_body_shapes_zero()

    # This LOD already has the exact ULTRA object transform and temporary rig pose.
    # Save that state before weights / REST normalization / blendshapes.
    pre = save_preprocess_checkpoint(model_path)
    STATE.setdefault("preprocessed_checkpoints", {})[model_path.name] = str(pre)
    save_state()

    bpy.ops.object.select_all(action='DESELECT')
    GARMENT.select_set(True)
    bpy.context.view_layer.objects.active = GARMENT
    bake_object_transform_preserve_world(GARMENT)

    transfer_weights_from_current_body_pose(reference_mode="use")
    normalize_garment_from_pose_to_rest()
    write_deformation_report()
    if needs_blendshapes:
        generate_blendshapes()
    else:
        STATE["blendshapes_done"] = False
        save_state()

    result = export_current_glb()
    log(f"LOD done {model_path.name}")
    return result


def process_command(cmd):
    refresh_scene_refs(require_garment=False)
    c = cmd.get("command")
    if c == "commit_manual_fit":
        commit_manual_fit()
        return {"fit_snapshot": FIT_SNAPSHOT, "garment_vertices": len(GARMENT.data.vertices)}
    elif c == "apply_fit": apply_fit()
    elif c == "pose_ok": pose_ok()
    elif c == "pose_adjust": enter_pose_adjust()
    elif c == "bind_and_normalize": bind_and_normalize()
    elif c == "transfer_weights_rest": transfer_weights_rest()
    elif c == "generate_blendshapes": generate_blendshapes()
    elif c == "skip_blendshapes":
        STATE["blendshapes_done"] = False; save_state(); log("BLENDSHAPES skipped")
    elif c == "export_current": return export_current_glb()
    elif c == "process_lod":
        payload = cmd.get("payload") or {}
        model = Path(payload.get("model", "")).resolve()
        if not model.is_file():
            raise RuntimeError(f"LOD no trobat: {model}")
        return process_additional_lod(model, bool(payload.get("needs_blendshapes", True)))
    elif c == "finish":
        log("FINISH")
        bpy.ops.wm.quit_blender()
    else: raise RuntimeError(f"Command desconegut: {c}")
    return {}


def poll_commands():
    global LAST_COMMAND_ID
    try:
        if COMMAND_PATH.exists():
            cmd = json.loads(COMMAND_PATH.read_text(encoding="utf-8"))
            cid = cmd.get("id")
            if cid and cid != LAST_COMMAND_ID:
                LAST_COMMAND_ID = cid
                try:
                    result = process_command(cmd)
                    ack(cid, "ok", result=result)
                except Exception as e:
                    log("COMMAND ERROR " + traceback.format_exc())
                    ack(cid, "error", str(e))
    except Exception:
        log("POLL ERROR " + traceback.format_exc())
    return 0.25

if not LIBRARY_MODE:
    try:
        initial_setup()
        bpy.app.timers.register(poll_commands, first_interval=0.25, persistent=True)
    except Exception as e:
        log("INIT ERROR " + traceback.format_exc())
        write_json(READY_PATH, {"status":"error", "message":str(e)})
