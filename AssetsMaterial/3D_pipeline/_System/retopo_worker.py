#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, os, shutil, subprocess, sys, time, uuid
from retopo_preprocess import select_preprocess
from incremental_outputs import missing_lods, publish_additions
from datetime import datetime
from pathlib import Path

from persistent_logs import TaskLog
TASK_LOG = TaskLog(Path(__file__).resolve().parent, "retopo")
TASK_LOG.install_console()

ROOT=Path(__file__).resolve().parent.parent
SYSTEM=ROOT/"_System"
RAW=ROOT/"Raw3D"
OUTPUT=ROOT/"Output"
WORK_ROOT=ROOT/"_W"
CONFIG=ROOT/"retopo_config.json"
ENGINE=SYSTEM/"Retopo"/"meshy_to_blender.py"
BLENDER=Path(r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe")

def log(msg):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {msg}",flush=True)

def sha256(path):
    h=hashlib.sha256()
    with path.open("rb") as f:
        for b in iter(lambda:f.read(1024*1024),b""):h.update(b)
    return h.hexdigest()

def load_targets():
    data=json.loads(CONFIG.read_text(encoding="utf-8"))
    rows=data["targets"]
    out=[]
    for r in rows:
        label=str(r["label"]).upper().strip()
        quads=int(r["quads"])
        if quads<1000:raise RuntimeError(f"Target invalid: {label} {quads}")
        out.append({"label":label,"quads":quads})
    out.sort(key=lambda x:x["quads"],reverse=True)
    return out

def raw_packages():
    """Return every visible GLB found under Raw3D.

    Retopo depends ONLY on the GLB. Extra package files are optional and are
    carried to preProcess after success if they happen to share its folder.
    """
    packages=[]
    if not RAW.is_dir():
        return packages
    for raw in sorted(RAW.rglob("*.glb"), key=lambda p:str(p).casefold()):
        try:
            rel=raw.relative_to(RAW)
        except ValueError:
            continue
        if any(part.startswith(".") for part in rel.parts):
            continue
        if raw.is_file():
            # A subfolder is just optional packaging. Its name/content is NOT
            # validated and is never required to start retopo.
            package_dir = raw.parent if raw.parent != RAW else None
            packages.append((package_dir,raw))
    return packages

def lod_name(asset,label,quads):
    token=f"{quads//1000}k" if quads%1000==0 else str(quads)
    return f"{asset}_{label}_{token}.glb"

def show_failure(result_root):
    logs=sorted(result_root.rglob("*.log"),key=lambda p:p.stat().st_mtime,reverse=True)
    for p in logs:
        txt=p.read_text(encoding="utf-8",errors="replace")
        if any(x in txt for x in ("Traceback","RuntimeError","AssertionError","Error:","QUAD_REMESHER_ASYNC_ERROR")):
            print("\n"+"="*80)
            print("FAIL DETAIL:",p)
            print("-"*80)
            print("\n".join(txt.splitlines()[-120:]))
            print("="*80+"\n")
            return

def preflight(targets):
    if not ENGINE.exists():raise RuntimeError(f"Falta {ENGINE}")
    if not BLENDER.exists():raise RuntimeError(f"No trobo Blender: {BLENDER}")
    required=["retopology.py","uv_seams.py","repair_uv.py","prepare_triangles.py",
              "bake_export.py","verify_export.py","validate_final_geometry.py",
              "recover_geometry.py","fit_surface.py"]
    for n in required:
        if not (ENGINE.parent/"scripts"/n).exists():raise RuntimeError(f"Falta script original: {n}")
    if [t["quads"] for t in targets] != [50000,30000,20000,10000,5000]:
        raise RuntimeError("Targets inesperats a retopo_config.json")
    program_data=Path(os.environ.get('PROGRAMDATA', r'C:\\ProgramData'))
    qr_base=program_data/'Exoside'/'QuadRemesher'/'Datas_Blender'
    explicit=os.environ.get('QUADREMESHER_ENGINE')
    qr_candidates=([Path(explicit)] if explicit else []) + (sorted(qr_base.glob('QuadRemesherEngine_*/xremesh.exe'), reverse=True) if qr_base.is_dir() else [])
    if not any(p.is_file() for p in qr_candidates):
        raise RuntimeError('Quad Remesher engine no trobat. Executa Remesh It manualment una vegada abans del batch.')
    log("PRE-FLIGHT OK: Quad Remesher engine + Raw3D + direct 50k/30k/20k/10k/5k QUADS.")


def reset_session_scratch():
    """Own a unique scratch directory; leave other runs and all outputs alone."""
    global WORK_ROOT
    WORK_ROOT = ROOT / "_W" / ("incremental_" + uuid.uuid4().hex[:12])
    WORK_ROOT.mkdir(parents=True, exist_ok=True)


def archived_packages(targets):
    """Find original RAWs archived by earlier runs, including renamed assets."""
    if not OUTPUT.is_dir():
        return []
    packages = []
    for directory in sorted(OUTPUT.iterdir(), key=lambda p: p.name.casefold()):
        if not directory.is_dir() or directory.name.startswith("."):
            continue
        if not missing_lods(directory.name, directory, targets):
            continue
        archive = directory / "preProcess"
        exact = archive / (directory.name + ".glb")
        if exact.is_file():
            raw = exact
        else:
            direct = list(archive.glob("*.glb")) if archive.is_dir() else []
            candidates = direct or (list(archive.rglob("*.glb")) if archive.is_dir() else [])
            if len(candidates) != 1:
                log(f"{directory.name}: falten LODs pero no hi ha un RAW unic a preProcess; no toco els resultats.")
                continue
            raw = candidates[0]
        packages.append((None, raw, directory.name, True))
    return packages


def pending_packages(targets):
    """Output is inspected first and independently of the Raw3D queue."""
    archived = archived_packages(targets)
    archived_assets = {asset for _, _, asset, _ in archived}
    fresh = [(directory, raw, raw.stem, False)
             for directory, raw in raw_packages()
             if raw.stem not in archived_assets]
    return archived + fresh


def report_output_levels(targets):
    if not OUTPUT.is_dir():
        return
    for directory in sorted(OUTPUT.iterdir()):
        if not directory.is_dir() or directory.name.startswith('.'):
            continue
        missing = missing_lods(directory.name, directory, targets)
        status = 'falten ' + ', '.join(t['label'] for t in missing) if missing else 'complet'
        log(f"RETOPO {directory.name}: {len(targets)-len(missing)}/{len(targets)} GLB; {status}.")


def _clear_work(digest):
    """_W is scratch only (the failure detail was already printed to the console): success or failure."""
    for p in WORK_ROOT.glob(f"{digest}__*"):
        if p.is_dir(): shutil.rmtree(p,ignore_errors=True)
    if WORK_ROOT.is_dir() and not any(WORK_ROOT.iterdir()):
        WORK_ROOT.rmdir()   # recreated by the next job


@TASK_LOG.scope("retopo", "raw")
def process_package(package_dir, raw, targets, attempted, asset_name=None, archived=False):
    asset = asset_name or raw.stem
    final_dir = OUTPUT / asset
    pending = missing_lods(asset, final_dir, targets)
    if not pending:
        return False, []
    start_hash = sha256(raw)
    signature = (str(raw.resolve()), start_hash)
    if signature in attempted:
        return False, []
    attempted.add(signature)
    # Never combine an unrelated new RAW with retained LODs of an older RAW.
    retained_models = len(pending) < len(targets) or any((final_dir / 'Rigged').rglob('*.fbx'))
    if retained_models and not archived:
        saved_raws = [r for _, r, a, _ in archived_packages(targets) if a == asset]
        if not saved_raws or sha256(saved_raws[0]) != start_hash:
            log(f"{asset}: el RAW no coincideix amb preProcess; els outputs existents queden protegits.")
            return True, [f"{asset}:raw_mismatch"]
    print(f"\nAsset: {asset} | RAW: {raw.name}", flush=True)
    preprocess = select_preprocess(ROOT)
    TASK_LOG.write("PREPROCESS SELECTION", asset=asset, enabled=bool(preprocess), config=preprocess)
    digest = start_hash[:8]
    staging = WORK_ROOT / (digest + "__publish__" + uuid.uuid4().hex[:8])
    staging.mkdir(parents=True)
    failures = []
    generated = []
    log(f"{asset}: falten {', '.join(t['label'] for t in pending)}; la resta es conserva.")
    try:
        for target in pending:
            if not raw.is_file() or sha256(raw) != start_hash:
                raise RuntimeError("RAW ha canviat o desaparegut durant el proces")
            label, quads = target["label"], target["quads"]
            job = WORK_ROOT / f"{digest}__{label.lower()}__{quads}q"
            result = job / "result"
            result.mkdir(parents=True)
            cmd = [sys.executable, str(ENGINE), str(raw), "--quads", str(quads),
                   "--output", str(result), "--profile", "auto", "--no-resume",
                   "--no-preview", "--blender", str(BLENDER)]
            if preprocess is not None:
                cmd += ["--preprocess-json", json.dumps(preprocess, separators=(",", ":"))]
            log(f"{raw.name}: START {label} {quads:,} QUADS FROM ARCHIVED/CURRENT RAW")
            TASK_LOG.asset = asset
            TASK_LOG.stage = "retopo_" + label
            rc = TASK_LOG.run(cmd, cwd=str(ROOT)).returncode
            if rc:
                show_failure(result)
                raise RuntimeError(f"{label}: exit={rc}")
            if not raw.is_file() or sha256(raw) != start_hash:
                raise RuntimeError("RAW ha canviat durant el retopo")
            internal = result / f"{raw.stem}_HIGH.glb"
            if not internal.is_file():
                raise RuntimeError(f"{label}: GLB intern no trobat")
            output = staging / lod_name(asset, label, quads)
            shutil.copy2(internal, output)
            generated.append(output)
            shutil.rmtree(job)
        if not archived and not (final_dir / "preProcess").exists():
            archive = staging / "preProcess"
            # Copy first; do not move/delete the RAW until publication succeeds.
            if package_dir is not None:
                shutil.copytree(package_dir, archive)
            else:
                archive.mkdir()
                shutil.copy2(raw, archive / raw.name)
        if not raw.is_file() or sha256(raw) != start_hash:
            raise RuntimeError("RAW ha canviat abans de publicar")
        publish_additions(staging, final_dir)
        if not archived:
            if package_dir is not None:
                shutil.rmtree(package_dir)
            else:
                raw.unlink()
        log(f"{asset}: AFEGITS {len(generated)} LODs; outputs previs i Rigged intactes.")
    except Exception as exc:
        import traceback
        TASK_LOG.write(traceback.format_exc(), exception=type(exc).__name__)
        log(f"FAILED {asset}: {exc}; no se sobreescriu cap output existent.")
        failures.append(f"{asset}:{exc}")
    finally:
        for diagnostic in WORK_ROOT.glob(f"{digest}__*/**/*.log"):
            TASK_LOG.preserve_tail(diagnostic)
        shutil.rmtree(staging, ignore_errors=True)
        _clear_work(digest)
    return True, failures


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--once", action="store_true", help="Una passada, sense watcher")
    ap.add_argument("--dry-run", action="store_true", help="Mostra els LODs pendents sense modificar fitxers")
    args = ap.parse_args()
    targets=load_targets()
    report_output_levels(targets)
    if args.dry_run:
        jobs = pending_packages(targets)
        for _, raw, asset, _ in jobs:
            pending = missing_lods(asset, OUTPUT / asset, targets)
            log(f"{asset}: {', '.join(t['label'] for t in pending) or 'complet'}; RAW={raw}")
        if not jobs:
            log("Cap LOD pendent amb RAW disponible.")
        return 0
    preflight(targets)
    OUTPUT.mkdir(exist_ok=True)
    RAW.mkdir(exist_ok=True)
    reset_session_scratch()

    poll_seconds=5.0
    attempted=set()
    failures=[]

    print()
    print("=== V98 RAW3D GLB-ONLY + QUAD REMESHER + ADAPTIVE CAGE ===")
    print("Primer revisa Output i completa els cinc LODs des de preProcess, encara que Raw3D estigui buit.")
    print("Despres revisa els nous RAW de Raw3D. Repeteix les dues comprovacions continuament.")
    print("Completa nomes LODs absents; recupera el RAW de Output/<asset>/preProcess si cal.")
    print("Assets fallits es queden a Raw3D i no es reintenten en bucle en aquesta execució.")
    print(f"Polling Raw3D cada {poll_seconds:.0f}s. Ctrl+C per sortir.")
    for t in targets:print(f"  {t['label']}: {t['quads']:,} quads")
    print()

    try:
        while True:
            packages=pending_packages(targets)
            did_work=False

            for package_dir,raw,asset_name,archived in packages:
                # Same unchanged RAW that already failed/was attempted this run:
                # leave it pending, but don't hammer it repeatedly.
                signature=(str(raw.resolve()),sha256(raw))
                if signature in attempted:
                    continue

                processed,errs=process_package(package_dir,raw,targets,attempted,asset_name,archived)
                did_work = did_work or processed
                failures.extend(errs)

            if args.once:
                return 1 if failures else 0
            if not did_work:
                time.sleep(poll_seconds)

    except KeyboardInterrupt:
        print()
        log("Watcher aturat per l'usuari.")
        if failures:
            print("Han fallat durant aquesta execució: "+", ".join(failures))
        return 0

if __name__=="__main__":
    raise SystemExit(main())
