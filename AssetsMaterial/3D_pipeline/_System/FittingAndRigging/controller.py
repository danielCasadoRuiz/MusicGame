from __future__ import annotations
import argparse, hashlib, json, os, shutil, subprocess, sys, time, uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from incremental_outputs import publish_additions, lod_files, LOD_TARGETS, model_lod_rank
from garment_profiles import resolve as resolve_garment
LOD_ORDER = ["ULTRA_50k", "HIGH_30k", "MIDHIGH_20k", "MEDIUM_10k", "LOW_5k"]
LOD_OUTPUT_FOLDERS = {
    "ULTRA_50K": "ULTRA",
    "HIGH_30K": "HIGH",
    "MIDHIGH_20K": "MID-HIGH",
    "MEDIUM_20K": "MID-HIGH",
    "MID-HIGH_20K": "MID-HIGH",
    "MEDIUM_10K": "MEDIUM",
    "LOW_5K": "LOW",
}


def lod_output_folder(path: Path):
    rank = model_lod_rank(path)
    if rank < 5:
        return ['ULTRA', 'HIGH', 'MID-HIGH', 'MEDIUM', 'LOW'][rank]
    upper = path.stem.upper()
    for token, folder in LOD_OUTPUT_FOLDERS.items():
        if token in upper:
            return folder
    return path.stem



def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def save_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    # Windows refuses the replace while Blender is reading the file (command.json is polled every
    # 0.25 s): retry briefly instead of aborting the whole run.
    for attempt in range(40):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            if attempt == 39:
                raise
            time.sleep(0.05)


def find_avatar(avatar_dir: Path, config):
    wanted = (config.get("avatar_file") or "MakeHuman_Canonical.fbx").strip()
    p = avatar_dir / wanted
    return p if p.is_file() else None


def lod_rank(path: Path):
    return model_lod_rank(path)


def find_lods(asset_dir: Path, exts: set[str]):
    models = []
    for p in asset_dir.rglob("*"):
        if not p.is_file() or p.suffix.lower() not in exts:
            continue
        low_parts = [x.lower() for x in p.parts]
        if "rigged" in low_parts or "preprocess" in low_parts or "_work" in low_parts:
            continue
        if "_rigged" in p.stem.lower():
            continue
        if lod_rank(p) < 999:
            models.append(p)
    models.sort(key=lambda p: (lod_rank(p), p.name.lower()))
    return models


def rigged_fbx_path(asset_dir: Path, config, model: Path):
    return asset_dir / config.get("rigged_subdir", "Rigged") / lod_output_folder(model) / f"{model.stem}_rigged.fbx"


def missing_rigged_lods(asset_dir: Path, config, lods):
    directory = asset_dir / config.get('rigged_subdir', 'Rigged')
    return [model for model in lods
            if not lod_files(directory, LOD_TARGETS[lod_rank(model)], '.fbx')]


def report_rigged_levels(output: Path, config):
    if not output.is_dir():
        return
    for directory in sorted(output.iterdir()):
        if not directory.is_dir() or directory.name.startswith('.'):
            continue
        missing = [target['label'] for target in LOD_TARGETS
                   if not lod_files(directory / config['rigged_subdir'], target, '.fbx')]
        status = 'falten ' + ', '.join(missing) if missing else 'complet'
        print(f"RIGGING {directory.name}: {5-len(missing)}/5 FBX; {status}.")
        for target in LOD_TARGETS:
            if target['label'] in missing and not lod_files(directory, target):
                print(f"  {target['label']}: falta tambe el GLB; executa primer 02_RETOPO.bat.")


def discover_assets(output: Path, exts: set[str]):
    if not output.exists():
        return []
    out = []
    for d in sorted((p for p in output.iterdir() if p.is_dir()), key=lambda p: p.name.lower()):
        if d.name.startswith('.') or d.name.lower() in {"rigged", "_work"}:
            continue
        lods = find_lods(d, exts)
        if not lods:
            continue
        if not missing_rigged_lods(d, {}, lods):
            continue
        out.append((d, lods))
    return out


def resolve_requested_asset(root: Path, output: Path, requested: str | None, exts: set[str]):
    if not requested:
        return None
    p = Path(requested)
    if not p.is_absolute():
        direct = (root / p).resolve()
        p = direct if direct.exists() else (output / p).resolve()
    if p.is_file():
        lods = find_lods(p.parent, exts)
        if not lods:
            raise RuntimeError(f"No hi ha LODs reconeguts a {p.parent}")
        return p.parent, lods
    if p.is_dir():
        lods = find_lods(p, exts)
        if not lods:
            raise RuntimeError(f"No hi ha LODs reconeguts a {p}")
        return p, lods
    raise RuntimeError(f"Asset no trobat: {requested}")


def sha256_file(path: Path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def fit_record_path(asset_dir: Path, config):
    """The saved fitting travels with the asset: Output/<asset>/Rigged/fit_record.json
    (garment transform + rig pose captured at commit, plus fingerprints of the fitted ULTRA/avatar)."""
    return asset_dir / config["rigged_subdir"] / "fit_record.json"


def load_json_or_none(path: Path):
    try:
        return load_json(path) if path.is_file() else None
    except Exception:
        return None


def send_command(session_dir: Path, command: str, **payload):
    cid = str(uuid.uuid4())
    save_json(session_dir / "command.json", {
        "id": cid, "command": command, "payload": payload, "time": time.time()
    })
    ack_path = session_dir / "ack.json"
    while True:
        time.sleep(0.2)
        if not ack_path.exists():
            continue
        try:
            ack = load_json(ack_path)
        except Exception:
            continue
        if ack.get("id") != cid:
            continue
        if ack.get("status") != "ok":
            raise RuntimeError(ack.get("message") or f"Blender command failed: {command}")
        return ack


def wait_blender_ready(session_dir: Path, proc):
    ready = session_dir / "ready.json"
    while not ready.exists():
        if proc.poll() is not None:
            raise RuntimeError(f"Blender s'ha tancat abans d'inicialitzar-se (code {proc.returncode}).")
        time.sleep(0.25)
    info = load_json(ready)
    if info.get("status") != "ok":
        raise RuntimeError(info.get("message", "Error inicialitzant Blender"))
    return info


def yes_no(prompt: str, default=True):
    suffix = " [Y/n] " if default else " [y/N] "
    while True:
        v = input(prompt + suffix).strip().lower()
        if not v:
            return default
        if v in ("y", "yes", "s", "si", "sí"):
            return True
        if v in ("n", "no"):
            return False


def clear_work(root: Path, config, name: str):
    """_work/<asset> only holds intermediate steps (manual-fit .blend, per-LOD checkpoints, IPC/state/log
    files): removed after every run, successful or not (the console already shows what happened)."""
    if config.get("keep_work_files", False):
        return
    work_root = root / config["work_dir"]
    target = work_root / name
    shutil.rmtree(target, ignore_errors=True)
    parent = target.parent
    while parent != work_root and parent.is_dir() and not any(parent.iterdir()):
        parent.rmdir()
        parent = parent.parent
    if work_root.is_dir() and not any(work_root.iterdir()):
        work_root.rmdir()


def process_asset(root: Path, config, asset_dir: Path, lods: list[Path], avatar: Path):
    ctx = {}
    try:
        _process_asset(root, config, asset_dir, lods, avatar, ctx)
    finally:
        proc = ctx.get("proc")
        if proc is not None and proc.poll() is None:   # failed / cancelled run: Blender still holds the files
            proc.kill()
            try:
                proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                pass
        if ctx.get('work_relative'):
            clear_work(root, config, ctx['work_relative'])


def _process_asset(root: Path, config, asset_dir: Path, lods: list[Path], avatar: Path, ctx: dict):
    name = asset_dir.name
    primary = lods[0]  # ULTRA_50k whenever present because of LOD_ORDER
    pending = missing_rigged_lods(asset_dir, config, lods)
    if not pending:
        print(f"{name}: tots els FBX ja existeixen; no es modifica cap resultat.")
        return
    has_existing = any((asset_dir / config['rigged_subdir']).rglob('*.fbx'))
    print(f"{name}: FBX pendents: {', '.join(p.name for p in pending)}")
    work_relative = str(Path(name) / uuid.uuid4().hex[:12])
    ctx['work_relative'] = work_relative
    session_dir = root / config["work_dir"] / work_relative
    session_dir.mkdir(parents=True, exist_ok=True)

    # Saved fitting of a previous run (optional reuse), stored with the asset.
    fit_record = load_json_or_none(fit_record_path(asset_dir, config))
    previous_shapes = bool((fit_record or {}).get("needs_blendshapes", True))
    reuse_fit = None
    if fit_record and fit_record.get("fit_snapshot"):
        when = fit_record.get("committed_at", "?")
        stale = None
        if fit_record.get("model_sha256") != sha256_file(primary):
            stale = f"{primary.name} ha canviat des del fitting"
        elif fit_record.get("avatar_sha256") != sha256_file(avatar):
            stale = f"{avatar.name} ha canviat des del fitting"
        if stale:
            raise RuntimeError(f"No puc completar els LODs amb el fitting antic: {stale}. Els outputs i el fitting desat es conserven.")
        else:
            reuse_fit = {"fit_snapshot": fit_record["fit_snapshot"], "garment_vertices": fit_record.get("garment_vertices")}

    if (has_existing or fit_record_path(asset_dir, config).exists()) and not reuse_fit:
        raise RuntimeError("Hi ha FBX finals pero falta un fit_record.json valid. "
                           "No es fa un fitting nou incompatible; els resultats existents queden intactes.")
    if reuse_fit:
        symmetry = bool(reuse_fit["fit_snapshot"].get("symmetry"))
    else:
        print(f"\nAsset: {name}")
        symmetry = yes_no(
            "Vols crear una copia simetrica amb Mirror X? "
            "(una sola sabata/peca; no un parell ja complet)", default=False)

    for f in ("command.json", "ack.json", "ready.json", "state.json"):
        try:
            (session_dir / f).unlink()
        except FileNotFoundError:
            pass

    final_out_dir = asset_dir / config["rigged_subdir"]
    out_dir = session_dir / "publish"
    out_dir.mkdir(parents=True, exist_ok=True)
    manual_blend = session_dir / f"{primary.stem}_manual_fit.blend"
    garment = resolve_garment(name, asset_dir)

    session = {
        "asset_name": name,
        "model": str(primary.resolve()),
        "lod_models": [str(p.resolve()) for p in lods],
        "avatar": str(avatar.resolve()),
        "output_dir": str(out_dir.resolve()),
        "manual_blend": str(manual_blend.resolve()),
        "session_dir": str(session_dir.resolve()),
        "config": config,
        "garment_type": garment["garment_type"],
        "garment_type_source": garment["source"],
        "garment_overrides": garment["overrides"],
        "reuse_fit": reuse_fit,
        "symmetry": symmetry,
    }
    save_json(session_dir / "session.json", session)

    blender = Path(config["blender"])
    if not blender.exists():
        raise RuntimeError(f"No trobo Blender: {blender}")
    worker = HERE / "blender_worker.py"
    cmd = [str(blender), "--python", str(worker), "--", "--session", str(session_dir / "session.json")]

    print("\n" + "=" * 78)
    print(f"FITTING & RIGGING: {name}")
    print("=" * 78)
    print(f"Fitting manual sobre: {primary.name}")
    print("LODs que es processaran:")
    for p in pending:
        print(f"  - {p.name}")
    print(f"Avatar: {avatar.name}")
    print(f"Tipus de peça (pesos): {garment['garment_type']} (font: {garment['source']}; "
          f"es pot forçar amb {asset_dir.name}/garment.json)")

    if symmetry:
        print("Simetria X activada: ajusta una sola peça; l'altra es reflecteix al pla central fix.")
    proc = subprocess.Popen(cmd)
    ctx["proc"] = proc
    info = wait_blender_ready(session_dir, proc)
    print(f"Body: {info.get('body', '?')} | Rig: {info.get('armature', '?')}")
    print(f"Blendshapes BODY: {info.get('body_blendshapes', 0)}")
    fit_source = "reused" if info.get("fit_reused") else "manual"
    if info.get("fit_reused"):
        print("Fitting desat verificat (mateix avatar i mateixa malla ULTRA) i aplicat.")
    else:
        if info.get("fit_reuse_rejected") and has_existing:
            raise RuntimeError("Blender ha rebutjat el fitting desat; no es publica cap resultat nou.")
        if info.get("fit_reuse_rejected"):
            print("\nEl fitting desat NO s'ha aplicat:")
            for reason in info["fit_reuse_rejected"].get("reasons", []):
                print(f"  - {reason}")
        print(f"Checkpoint manual: {info.get('manual_blend', manual_blend)}")

        input(
            "\n[BLENDER] Ajusta NOMÉS aquest ULTRA una vegada:\n"
            "  - escala / posició / rotació de la peça\n"
            "  - posa el rig/body en la mateixa postura que la peça si cal\n"
            "  - comprova visualment que body i peça encaixen\n\n"
            "Quan estigui bé, fes Ctrl+S, torna aquí i prem ENTER... "
        )

    needs_shapes = previous_shapes if reuse_fit else yes_no(
        "\nAquesta peça necessita blendshapes del BODY?", default=bool(previous_shapes))

    print(f"\nProcessant ULTRA amb el fitting {'desat' if fit_source == 'reused' else 'manual'}...")
    commit = send_command(session_dir, "commit_manual_fit").get("result", {})
    # The fitting + fingerprints of what was fitted, so a later run can reuse it safely.
    if not fit_record_path(asset_dir, config).exists():
        save_json(out_dir / "fit_record.json", {
            "model": primary.name, "model_sha256": sha256_file(primary),
            "avatar": avatar.name, "avatar_sha256": sha256_file(avatar),
            "garment_vertices": commit.get("garment_vertices"), "fit_snapshot": commit.get("fit_snapshot"),
            "fit_source": fit_source, "needs_blendshapes": bool(needs_shapes),
            "symmetry": bool((commit.get("fit_snapshot") or {}).get("symmetry")),
            "committed_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        })
    if primary in pending and needs_shapes:
        send_command(session_dir, "generate_blendshapes")
    else:
        send_command(session_dir, "skip_blendshapes")
    if primary in pending:
        first = send_command(session_dir, "export_current")
        first_result = first.get("result", {})
        if first_result.get("fbx"):
            print("  OK", first_result.get("fbx"))
    else:
        print(f"  {primary.name}: referencia calculada en memoria; FBX existent conservat.")

    for model in (p for p in pending if p != primary):
        print(f"\nPreprocessant i riggejant automàticament {model.name} amb EXACTAMENT el fitting de l'ULTRA...")
        ack = send_command(
            session_dir,
            "process_lod",
            model=str(model.resolve()),
            needs_blendshapes=bool(needs_shapes),
        )
        result = ack.get("result", {})
        if result.get("fbx"):
            print("  OK", result.get("fbx"))

    send_command(session_dir, "finish")
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass


    # Publish only after every missing LOD succeeded. Collision checks protect
    # existing FBX, BLEND, textures and fitting records, including partial folders.
    for model in pending:
        expected = out_dir / lod_output_folder(model) / f"{model.stem}_rigged.fbx"
        if not expected.is_file() or not expected.stat().st_size:
            raise RuntimeError(f"Blender no ha generat el FBX pendent: {expected}")
    publish_additions(out_dir, final_out_dir)
    print("\nCOMPLETAT. Nous resultats (els anteriors es conserven):")
    for model in pending:
        folder = final_out_dir / lod_output_folder(model)
        print("  ", folder / f"{model.stem}_rigged.fbx")
        print("  ", folder / f"{model.stem}_rigged.blend")
        print("  ", folder / "Textures")


def choose_asset_to_regenerate(root: Path, config, output: Path, exts: set[str]):
    """Already rigged assets that have a saved fitting can be regenerated (weights recalculated) on request."""
    if not output.exists():
        return []
    candidates = []
    for d in sorted((p for p in output.iterdir() if p.is_dir()), key=lambda p: p.name.lower()):
        lods = find_lods(d, exts)
        if lods and (load_json_or_none(fit_record_path(d, config)) or {}).get("fit_snapshot"):
            candidates.append((d, lods))
    if not candidates:
        return []
    print("\nAssets ja riggejats amb un fitting desat (es poden regenerar amb la configuració de pesos actual):")
    for i, (d, lods) in enumerate(candidates, 1):
        print(f"  {i}. {d.name} ({len(lods)} LODs)")
    choice = input("Número de l'asset a regenerar (ENTER per sortir): ").strip()
    if not choice.isdigit() or not 1 <= int(choice) <= len(candidates):
        return []
    return [candidates[int(choice) - 1]]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--asset", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    root = Path(args.root).resolve()
    config = load_json(HERE / "config.json")
    output = root / config["output_dir"]
    report_rigged_levels(output, config)
    exts = set(config["supported_models"])

    avatar = find_avatar(root / config["avatar_dir"], config)
    if not avatar:
        print("\nERROR: falta l'avatar MakeHuman de referència.")
        print("Falta exactament:")
        print(" ", root / config["avatar_dir"] / config.get("avatar_file", "MakeHuman_Canonical.fbx"))
        return 2

    requested = resolve_requested_asset(root, output, args.asset, exts)
    assets = [requested] if requested else discover_assets(output, exts)
    if not assets:
        print(f"No s'ha trobat cap asset pendent dins {output}")
        return 0

    if args.dry_run:
        for asset_dir, lods in assets:
            pending = missing_rigged_lods(asset_dir, config, lods)
            print(f"{asset_dir.name}: {', '.join(p.name for p in pending) or 'complet'}")
        return 0
    for asset_dir, lods in assets:
        try:
            process_asset(root, config, asset_dir, lods, avatar)
        except KeyboardInterrupt:
            print("\nCancel·lat per usuari.")
            return 130
        except Exception as e:
            print(f"\nERROR {asset_dir.name}: {e}")
            print("S'atura aquí; no s'obre el següent asset.")
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
