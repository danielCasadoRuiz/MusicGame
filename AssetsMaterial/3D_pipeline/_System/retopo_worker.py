#!/usr/bin/env python3
from __future__ import annotations
import hashlib, json, os, shutil, subprocess, sys, time
from datetime import datetime
from pathlib import Path

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
    RAW.mkdir(parents=True,exist_ok=True)
    packages=[]
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
        if any(x in txt for x in ("Traceback","RuntimeError","AssertionError","Error:")):
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
    """_W and hidden finalizing folders are NEVER process state."""
    if WORK_ROOT.exists():
        shutil.rmtree(WORK_ROOT, ignore_errors=True)
    WORK_ROOT.mkdir(parents=True, exist_ok=True)

    if OUTPUT.exists():
        for p in OUTPUT.iterdir():
            if p.is_dir() and p.name.startswith(".") and p.name.endswith(".finalizing"):
                shutil.rmtree(p, ignore_errors=True)


def _clear_work(digest):
    """_W is scratch only (the failure detail was already printed to the console): success or failure."""
    for p in WORK_ROOT.glob(f"{digest}__*"):
        if p.is_dir(): shutil.rmtree(p,ignore_errors=True)
    if WORK_ROOT.is_dir() and not any(WORK_ROOT.iterdir()):
        WORK_ROOT.rmdir()   # recreated by the next job


def process_package(package_dir,raw,targets,attempted):
    asset=raw.stem
    start_hash=sha256(raw)
    signature=(str(raw.resolve()),start_hash)
    if signature in attempted:
        return False, []
    attempted.add(signature)
    digest=start_hash[:8]
    final_dir=OUTPUT/asset
    staging=OUTPUT/f".{asset}.finalizing"
    if final_dir.exists():
        log(f"FAILED {asset}: Output/{asset} ja existeix; no sobreescric.")
        return True,[f"{asset}:output_exists"]
    if staging.exists(): shutil.rmtree(staging,ignore_errors=True)
    staging.mkdir(parents=True)
    asset_failed=[]
    generated=[]
    for t in targets:
        if not raw.is_file():
            asset_failed.append("raw_disappeared")
            log(f"{asset}: RAW ha desaparegut; aborto asset.")
            break
        current_hash=sha256(raw)
        if current_hash != start_hash:
            asset_failed.append("raw_changed")
            log(f"{asset}: RAW ha canviat durant el procés; descarto parcials i es reprocessarà des de HIGH.")
            attempted.discard(signature)
            break
        label,quads=t["label"],t["quads"]
        job=WORK_ROOT/f"{digest}__{label.lower()}__{quads}q"
        result=job/"result"
        if job.exists(): shutil.rmtree(job)
        result.mkdir(parents=True)
        log(f"{raw.name}: START {label} {quads:,} QUADS FROM CURRENT RAW")
        cmd=[sys.executable,str(ENGINE),str(raw),
             "--quads",str(quads),
             "--output",str(result),
             "--profile","auto",
             "--no-resume","--no-preview",
             "--blender",str(BLENDER)]
        log("CMD: "+" ".join(cmd))
        started=time.time()
        rc=subprocess.run(cmd,cwd=str(ROOT)).returncode
        elapsed=time.time()-started
        if rc:
            log(f"FAILED {raw.name} {label}: exit={rc}; elapsed={elapsed:.1f}s")
            show_failure(result)

            asset_failed.append(f"{label}:exit={rc}")
            break
        if not raw.is_file() or sha256(raw) != start_hash:
            asset_failed.append("raw_changed")
            log(f"{asset}: RAW ha canviat mentre es feia {label}; descarto resultat.")
            attempted.discard(signature)
            break
        internal=result/f"{raw.stem}_HIGH.glb"
        if not internal.exists():
            log(f"FAILED {raw.name} {label}: GLB intern no trobat")
            asset_failed.append(f"{label}:missing_glb")
            break
        final_name=lod_name(asset,label,quads)
        staged_glb=staging/final_name
        shutil.copy2(internal,staged_glb)
        generated.append(staged_glb)
        shutil.rmtree(job, ignore_errors=True)
        log(f"OK {raw.name} {label}: {final_name}; elapsed={elapsed:.1f}s")
    if asset_failed or len(generated)!=len(targets):
        shutil.rmtree(staging,ignore_errors=True)
        _clear_work(digest)
        log(f"{asset}: NO FINALITZAT. Raw3D queda com única pista de pendent.")
        return True,[f"{asset}:{x}" for x in asset_failed]
    preprocess_target=staging/"preProcess"
    try:
        if not raw.is_file() or sha256(raw) != start_hash:
            raise RuntimeError("RAW changed before finalization")
        if package_dir is not None:
            # Preserve whatever related files happen to exist beside the GLB
            # (input_original, images, etc.). None of them were prerequisites.
            shutil.move(str(package_dir),str(preprocess_target))
        else:
            # Legacy/loose Raw3D/<asset>.glb: the GLB alone is sufficient.
            preprocess_target.mkdir(parents=True,exist_ok=False)
            shutil.move(str(raw),str(preprocess_target/raw.name))
        if final_dir.exists():
            raise RuntimeError(f"Output destination appeared during finalize: {final_dir}")
        staging.replace(final_dir)
    except Exception as exc:
        moved_pre=staging/"preProcess"
        if moved_pre.exists():
            try:
                if package_dir is not None and not package_dir.exists():
                    shutil.move(str(moved_pre),str(package_dir))
                elif package_dir is None and not raw.exists():
                    candidate=moved_pre/raw.name
                    if candidate.exists(): shutil.move(str(candidate),str(raw))
            except Exception:
                pass
        if staging.exists(): shutil.rmtree(staging,ignore_errors=True)
        _clear_work(digest)
        log(f"FAILED {asset} FINALIZE: {type(exc).__name__}: {exc}")
        return True,[f"{asset}:finalize:{type(exc).__name__}"]
    _clear_work(digest)
    log(f"{asset}: TANCAT -> Output/{asset}/ (preProcess + 5 LODs)")
    return True,[]

def main():
    targets=load_targets()
    preflight(targets)
    OUTPUT.mkdir(exist_ok=True)
    reset_session_scratch()

    poll_seconds=5.0
    attempted=set()
    failures=[]

    print()
    print("=== V98 RAW3D GLB-ONLY + QUAD REMESHER + ADAPTIVE CAGE ===")
    print("Vigila Raw3D contínuament i processa qualsevol .glb que hi aparegui (també dins subcarpetes).")
    print("Assets OK -> MOVE a Output/<asset>/.")
    print("Assets fallits es queden a Raw3D i no es reintenten en bucle en aquesta execució.")
    print(f"Polling Raw3D cada {poll_seconds:.0f}s. Ctrl+C per sortir.")
    for t in targets:print(f"  {t['label']}: {t['quads']:,} quads")
    print()

    try:
        while True:
            packages=raw_packages()
            did_work=False

            for package_dir,raw in packages:
                # Same unchanged RAW that already failed/was attempted this run:
                # leave it pending, but don't hammer it repeatedly.
                signature=(str(raw.resolve()),sha256(raw))
                if signature in attempted:
                    continue

                processed,errs=process_package(package_dir,raw,targets,attempted)
                did_work = did_work or processed
                failures.extend(errs)

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
