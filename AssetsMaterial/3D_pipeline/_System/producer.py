#!/usr/bin/env python3
from __future__ import annotations

import base64
import getpass
import json
import mimetypes
import os
import shutil
import subprocess
import sys
import time
import traceback
from pathlib import Path
from typing import Dict, Optional

import requests
from PIL import Image

from common import (
    load_json,
    read_env_file,
    safe_stem,
    save_json,
    sha256_file,
    view_order_for,
    view_usage_for,
    write_env_file,
)
from image_ops import (
    prepare_balanced_2x2_chroma,
    split_upscaled_grid_chroma,
    split_upscaled_grid_keep_chroma,
)


ROOT = Path(__file__).resolve().parent.parent
SYSTEM = ROOT / "_System"

INPUT = ROOT / "Input"
UPSCALED = ROOT / "Upscaled"
CUTS = ROOT / "Upscaled_and_Cut"
RAW = ROOT / "Raw3D"
OUTPUT = ROOT / "Output"
DEBUG = ROOT / "Debug"

STATE = SYSTEM / "State" / "ProducerV5"
TEMP = SYSTEM / "Temp"
LOGS = SYSTEM / "Logs"

CONFIG = load_json(SYSTEM / "config.json", {}) or {}
PIPELINE_CONTROL_FILE = ROOT / "pipeline_steps.json"
MANUAL_LEONARDO = ROOT / "Manual_Leonardo"
LEONARDO_URL = CONFIG.get(
    "leonardo_url", "https://app.leonardo.ai/universal-upscaler"
)
CLAUDE_CFG = CONFIG.get("claude") or {}
LEONARDO_CFG = CONFIG.get("leonardo") or {}
if "factor" not in LEONARDO_CFG:
    raise RuntimeError("Falta _System/config.json -> leonardo.factor")
try:
    LEONARDO_SCALE_FACTOR = float(LEONARDO_CFG["factor"])
except (TypeError, ValueError) as exc:
    raise RuntimeError("_System/config.json -> leonardo.factor ha de ser numeric") from exc
if LEONARDO_SCALE_FACTOR <= 0:
    raise RuntimeError("_System/config.json -> leonardo.factor ha de ser > 0")
LEONARDO_SCALE_LABEL = (
    str(int(LEONARDO_SCALE_FACTOR))
    if LEONARDO_SCALE_FACTOR.is_integer()
    else str(LEONARDO_SCALE_FACTOR).rstrip("0").rstrip(".")
)
MESHY_SETTINGS = CONFIG.get("meshy") or {}

MESHY_API = "https://api.meshy.ai/openapi/v1"
SUPPORTED = {".png", ".jpg", ".jpeg", ".webp"}

# Prevent repeated recovery prompts in the same running process.
LEONARDO_ATTEMPTED_THIS_RUN: set[str] = set()
MESHY_SUBMIT_ATTEMPTED_THIS_RUN: set[str] = set()


CLAUDE_SESSION_DIR = SYSTEM / "State" / "ClaudeBrowser"
CLAUDE_SESSION_FILE = CLAUDE_SESSION_DIR / "session.json"


def load_claude_session() -> dict:
    data = load_json(CLAUDE_SESSION_FILE, {}) or {}
    # V10 never resumes an old/uncertain session. Only a session explicitly
    # marked after a validated upscaled file may be reused.
    if not data.get("validated_success"):
        return {}
    return data


def save_claude_session(session_id: str, increment: bool = True) -> None:
    if not session_id:
        return
    old = load_json(CLAUDE_SESSION_FILE, {}) or {}
    uses = int(old.get("uses") or 0)
    if increment:
        uses += 1
    save_json(
        CLAUDE_SESSION_FILE,
        {
            "session_id": session_id,
            "uses": uses,
            "updated_at": time.time(),
            "validated_success": True,
        },
    )


def clear_claude_session() -> None:
    try:
        CLAUDE_SESSION_FILE.unlink()
    except FileNotFoundError:
        pass


def has_claude_session() -> bool:
    data = load_claude_session()
    return bool(data.get("session_id"))


STAGE_ORDER = [
    ("prepare_chroma", "Preparar 2x2 amb chroma"),
    ("leonardo_upscale", "Leonardo upscale"),
    ("cut_views", "Tall de les vistes"),
    ("meshy_raw3d", "Meshy Raw3D"),
]


def load_pipeline_control() -> dict:
    if not PIPELINE_CONTROL_FILE.exists():
        raise RuntimeError(
            "Falta pipeline_steps.json al root del pipeline."
        )

    try:
        data = json.loads(
            PIPELINE_CONTROL_FILE.read_text(encoding="utf-8")
        )
    except Exception as exc:
        raise RuntimeError(
            f"pipeline_steps.json no es valid: {exc}"
        ) from exc

    steps = data.get("steps")
    if not isinstance(steps, dict):
        raise RuntimeError(
            "pipeline_steps.json necessita un objecte 'steps'."
        )

    for key, _ in STAGE_ORDER:
        if key not in steps:
            raise RuntimeError(
                f"Falta steps.{key} a pipeline_steps.json"
            )
        if not isinstance(steps[key], bool):
            raise RuntimeError(
                f"steps.{key} ha de ser true o false."
            )

    settings = data.get("settings") or {}
    if not isinstance(settings, dict):
        raise RuntimeError(
            "settings ha de ser un objecte JSON."
        )

    leonardo_mode = str(
        settings.get("leonardo_mode", "manual")
    ).strip().lower()
    if leonardo_mode not in {"manual", "claude"}:
        raise RuntimeError(
            "settings.leonardo_mode ha de ser 'manual' o 'claude'."
        )

    return {
        "steps": steps,
        "settings": {
            "process_only_newest_input": bool(
                settings.get("process_only_newest_input", False)
            ),
            "exit_after_one_pass_when_stopped": bool(
                settings.get(
                    "exit_after_one_pass_when_stopped",
                    True,
                )
            ),
            "leonardo_mode": leonardo_mode,
        },
    }


def first_false_stage(control: dict) -> Optional[str]:
    for key, _ in STAGE_ORDER:
        if not control["steps"][key]:
            return key
    return None


def stage_allowed(control: dict, target: str) -> bool:
    """
    A stage is allowed only if all stages up to and including it are true.
    Any values after the first false are intentionally irrelevant.
    """
    for key, _ in STAGE_ORDER:
        if not control["steps"][key]:
            return False
        if key == target:
            return True

    raise KeyError(target)


def stage_label(key: Optional[str]) -> str:
    if key is None:
        return "cap (pipeline complet)"
    return dict(STAGE_ORDER).get(key, key)


def log(msg: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def checked(r: requests.Response, label: str) -> requests.Response:
    if r.ok:
        return r
    try:
        body = json.dumps(r.json(), indent=2, ensure_ascii=False)
    except Exception:
        body = r.text[:5000]
    raise RuntimeError(f"{label}: HTTP {r.status_code}\n{body}")


def _first_dict(value):
    if isinstance(value, dict):
        return value
    if isinstance(value, list):
        for item in value:
            found = _first_dict(item)
            if found is not None:
                return found
    return None


def _find_key(value, key):
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = _find_key(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = _find_key(child, key)
            if found is not None:
                return found
    return None


def _response_preview(value, max_chars=4000):
    try:
        raw = json.dumps(value, indent=2, ensure_ascii=False)
    except Exception:
        raw = repr(value)
    return raw[:max_chars]


def debug_root(asset: str) -> Path:
    d = DEBUG / asset
    d.mkdir(parents=True, exist_ok=True)
    return d


def debug_stage(asset: str, name: str) -> Path:
    d = debug_root(asset) / name
    d.mkdir(parents=True, exist_ok=True)
    return d


def write_debug_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(data, indent=2, ensure_ascii=False, default=str),
        encoding="utf-8",
    )


def state_path(asset: str) -> Path:
    return STATE / f"{asset}.json"


def load_state(asset: str) -> dict:
    return load_json(state_path(asset), {}) or {}


def save_state(asset: str, state: dict) -> None:
    save_json(state_path(asset), state)


def append_error_log(source: Path, exc: BaseException) -> None:
    # Console only: the pipeline does not keep log files around.
    print("\n" + "=" * 88)
    print(time.strftime("%Y-%m-%d %H:%M:%S"), source.name)
    print(traceback.format_exc(), flush=True)


def load_meshy_key() -> str:
    env_path = SYSTEM / "secrets.env"
    values = read_env_file(env_path)

    key = os.environ.get("MESHY_API_KEY") or values.get("MESHY_API_KEY")
    if key:
        return key.strip()

    print()
    print("Primera execucio: falta la Meshy API key.")
    print("Es guardara localment a _System\\secrets.env.")
    key = getpass.getpass("Meshy API key: ").strip()
    if not key:
        raise RuntimeError("La Meshy API key no pot estar buida.")

    # Preserve any other pre-existing entries in secrets.env.
    values["MESHY_API_KEY"] = key
    write_env_file(env_path, values)
    return key


def list_inputs() -> list[Path]:
    now = time.time()
    result = []
    if not INPUT.exists():
        return result

    for p in INPUT.iterdir():
        if not p.is_file() or p.suffix.lower() not in SUPPORTED:
            continue
        # Avoid processing while Explorer is still copying a file.
        if now - p.stat().st_mtime < 3:
            continue
        result.append(p)

    return sorted(result, key=lambda p: (p.stat().st_mtime, p.name.casefold()))


def valid_image(path: Path) -> bool:
    try:
        if not path.exists() or path.stat().st_size < 1024:
            return False
        with Image.open(path) as im:
            im.verify()
        return True
    except Exception:
        return False


def image_size(path: Path) -> tuple[int, int]:
    with Image.open(path) as im:
        return im.size


def upscaled_candidates(asset_dir: Path) -> list[Path]:
    candidates = []
    for ext in (".png", ".jpg", ".jpeg", ".webp"):
        p = asset_dir / f"upscaled_full{ext}"
        if valid_image(p):
            candidates.append(p)
    return sorted(candidates, key=lambda p: p.stat().st_mtime, reverse=True)


def validate_upscaled_geometry(
    path: Path, crop_size: tuple[int, int]
) -> bool:
    """
    Leonardo Pro should preserve aspect ratio and be close to the configured
    scale in _System/config.json -> leonardo.factor.
    Tolerance protects against small service-side dimension adjustments.
    """
    if not valid_image(path):
        return False

    cw, ch = crop_size
    uw, uh = image_size(path)
    if cw <= 0 or ch <= 0:
        return False

    sx, sy = uw / cw, uh / ch
    ratio_src = cw / ch
    ratio_out = uw / uh

    if abs(ratio_src - ratio_out) / max(ratio_src, 1e-9) > 0.025:
        return False

    lo = LEONARDO_SCALE_FACTOR * 0.88
    hi = LEONARDO_SCALE_FACTOR * 1.12
    return lo <= sx <= hi and lo <= sy <= hi


def find_ready_upscaled(
    asset_dir: Path,
    source: Path,
    crop_size: tuple[int, int],
) -> Optional[Path]:
    for p in upscaled_candidates(asset_dir):
        # Source newer than upscale => source was replaced/edited.
        if p.stat().st_mtime + 0.5 < source.stat().st_mtime:
            continue
        if validate_upscaled_geometry(p, crop_size):
            return p
    return None


def normalize_download(
    download_path: Path,
    asset_dir: Path,
    source: Path,
    crop_size: tuple[int, int],
) -> Path:
    """
    Claude saves the browser download to 'upscaled_full.download'.
    Detect the real image format and publish it atomically with the right extension.
    """
    if not download_path.exists() or download_path.stat().st_size < 1024:
        raise RuntimeError("Claude ha acabat pero el download de Leonardo no existeix.")

    try:
        with Image.open(download_path) as im:
            fmt = (im.format or "").upper()
            size = im.size
            im.verify()
    except Exception as exc:
        raise RuntimeError(
            f"El fitxer descarregat de Leonardo no es una imatge valida: {exc}"
        ) from exc

    ext_map = {
        "PNG": ".png",
        "JPEG": ".jpg",
        "JPG": ".jpg",
        "WEBP": ".webp",
    }
    ext = ext_map.get(fmt)
    if not ext:
        raise RuntimeError(f"Format d'imatge de Leonardo no suportat: {fmt}")

    # Check geometry before replacing an older known-good file.
    cw, ch = crop_size
    uw, uh = size
    sx, sy = uw / cw, uh / ch
    lo = LEONARDO_SCALE_FACTOR * 0.88
    hi = LEONARDO_SCALE_FACTOR * 1.12
    if not (lo <= sx <= hi and lo <= sy <= hi):
        raise RuntimeError(
            "Leonardo ha descarregat una imatge amb dimensions inesperades: "
            f"{uw}x{uh}, esperavem aproximadament x{LEONARDO_SCALE_LABEL} "
            f"de {cw}x{ch}."
        )

    asset_dir.mkdir(parents=True, exist_ok=True)
    target = asset_dir / f"upscaled_full{ext}"
    tmp = asset_dir / f"upscaled_full{ext}.part"

    shutil.copy2(download_path, tmp)
    os.replace(tmp, target)

    # Remove old alternate formats only after the new one is safely published.
    for old in asset_dir.glob("upscaled_full.*"):
        if old == target or old.name == "upscaled_full.download":
            continue
        if old.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"}:
            try:
                old.unlink()
            except OSError:
                pass

    try:
        download_path.unlink()
    except OSError:
        pass

    # Make the new output logically newer than the source if filesystem times
    # were copied from a browser temp file.
    now = time.time()
    os.utime(target, (now, now))
    return target


def claude_executable() -> str:
    requested = str(CLAUDE_CFG.get("executable") or "claude")
    found = shutil.which(requested)
    if not found:
        raise RuntimeError(
            "No trobo Claude Code al PATH. Obre un terminal i comprova `claude --version`."
        )
    return found


def verify_playwright_mcp(claude: str) -> None:
    """
    Ensure Claude Code has the Playwright MCP browser bridge.
    V6 configures it automatically on first run instead of asking the user
    to type `claude mcp add ...` manually.
    """
    server = str(CLAUDE_CFG.get("playwright_server_name") or "playwright")

    def _list_mcp():
        return subprocess.run(
            [claude, "mcp", "list"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=45,
        )

    try:
        proc = _list_mcp()
    except Exception as exc:
        raise RuntimeError(f"No puc consultar els MCP de Claude: {exc}") from exc

    combined = (proc.stdout or "") + "\n" + (proc.stderr or "")
    if proc.returncode == 0 and server.casefold() in combined.casefold():
        return

    # Playwright MCP is distributed through npm/npx.
    npx = shutil.which("npx")
    if not npx:
        raise RuntimeError(
            "Falta Node.js/npx. Claude necessita un pont de navegador per poder "
            "clicar, pujar i descarregar fitxers de Leonardo.\n\n"
            "Instal.la Node.js 20+ una sola vegada i torna a executar el BAT. "
            "El pipeline configurara Playwright automaticament."
        )

    print()
    print("Primera execucio: configurant Playwright per Claude automaticament...")
    print("(No cal executar cap comanda manual.)")
    print()

    try:
        add = subprocess.run(
            [claude, "mcp", "add", server, "npx", "@playwright/mcp@latest"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
        )
    except Exception as exc:
        raise RuntimeError(
            f"No s'ha pogut configurar Playwright automaticament: {exc}"
        ) from exc

    if add.returncode != 0:
        raise RuntimeError(
            "No s'ha pogut configurar Playwright automaticament.\n\n"
            "Claude stdout:\n"
            + (add.stdout or "")
            + "\nClaude stderr:\n"
            + (add.stderr or "")
        )

    # Verify the newly-added MCP is visible to Claude.
    verify = _list_mcp()
    verification = (verify.stdout or "") + "\n" + (verify.stderr or "")
    if verify.returncode != 0 or server.casefold() not in verification.casefold():
        raise RuntimeError(
            "Claude ha acceptat la configuracio de Playwright, pero encara no "
            "apareix a `claude mcp list`.\n\n"
            + verification
        )

    print("Playwright configurat correctament.")
    print()


def _to_windows_friendly_posix(path: Path) -> str:
    # Node/Playwright accepts forward-slash absolute Windows paths and they are
    # much harder for an LLM to accidentally mis-escape inside JavaScript.
    return str(path.resolve()).replace("\\", "/")


def build_claude_prompt(
    source: Path,
    prepared: Path,
    download_target: Path,
    recovery_only: bool,
    compact_repeat: bool = False,
) -> str:
    url = LEONARDO_URL
    input_path = _to_windows_friendly_posix(prepared)
    output_path = _to_windows_friendly_posix(download_target)
    source_name = source.name

    if recovery_only:
        return f"""
Use ONLY Playwright MCP.

RECOVERY MODE: do NOT start a new upscale.

Leonardo:
{url}

Asset:
{source_name}

Expected output:
{output_path}

A previous run may have been interrupted after starting Leonardo.
Open/reuse the Leonardo Pro Upscaler page, look only for the already-existing
recent result for this job, wait if it is still processing, and download it to
the exact output path above.

NEVER click a control that starts, retries, regenerates or spends credits.
If there is no confidently identifiable existing result, stop.

Final answer exactly:
RECOVERED
or
NOT_FOUND: <reason>
or
ERROR: <reason>
""".strip()

    if compact_repeat:
        return f"""
Repeat EXACTLY the same Leonardo browser workflow you successfully used earlier
in this same Claude session. Do not rediscover the UI unless a known control is
missing.

NEW INPUT:
{input_path}

NEW OUTPUT:
{output_path}

Asset:
{source_name}

Same settings as before:
Pro Upscaler -> Precise -> x{LEONARDO_SCALE_LABEL} -> Detailed.
The input is already arranged as a clean 2x2 grid on a solid chroma
background. Do not crop, rearrange, split, recolor or edit it.

Use the current/login-persistent Leonardo browser state when possible.
Submit EXACTLY ONE upscale, wait for that same job, then save the browser
download to the exact NEW OUTPUT path.

If the UI expresses Detailed with "Fix AI Image Artifacts", it must be OFF.

Final answer exactly one of:
SUCCESS
ERROR_BEFORE_GENERATE: <reason>
ERROR_AFTER_GENERATE: <reason>
ERROR: <reason>
""".strip()

    return f"""
You are the browser operator for one stage of a local deterministic 3D pipeline.

USE ONLY Playwright MCP. Do not use shell/filesystem tools.

Process EXACTLY ONE image in Leonardo and NEVER submit more than one upscale job.

Leonardo URL:
{url}

Upload:
{input_path}

The upload is ALREADY PREPARED as a spacious 2x2 grid on a solid chroma
background:
    view0   view1
    view2   view3
Do NOT crop, split, rearrange, rotate, recolor or edit it.

Required settings:
- Pro Upscaler
- Precise
- x{LEONARDO_SCALE_LABEL}
- Detailed
- If Detailed is represented by "Fix AI Image Artifacts", that toggle MUST be OFF
- Do NOT use Creative
- Do NOT use Ultra

Output:
{output_path}

Procedure:
1. Open the Leonardo URL.
2. If login is required, wait up to 5 minutes for the human to complete it in
   the visible browser.
3. Upload the exact input path.
4. Explicitly set/verify Pro + Precise + x{LEONARDO_SCALE_LABEL} + Detailed.
5. Start the upscale ONCE.
6. Wait for that SAME job to finish.
7. Download it and save exactly to the output path above. Prefer a Playwright
   download event + download.saveAs(exact_path).
8. If download handling fails after generation, do NOT generate again; only try
   to retrieve the already-created result.

Remember the controls/flow you used, because subsequent messages in THIS SAME
Claude session will ask you to repeat the identical operation with only new
input/output paths.

Final answer exactly one of:
SUCCESS
ERROR_BEFORE_GENERATE: <reason>
ERROR_AFTER_GENERATE: <reason>
ERROR: <reason>
""".strip()

def run_claude_browser(
    prompt: str,
    asset: str,
    reuse_session: bool = True,
    debug_dir: Path | None = None,
) -> tuple[int, str, str | None]:
    claude = claude_executable()

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)
        (debug_dir / "00_prompt.txt").write_text(prompt, encoding="utf-8")

    session = load_claude_session() if reuse_session else {}
    session_id = str(session.get("session_id") or "").strip()

    full_max_turns = int(CLAUDE_CFG.get("max_turns") or 70)
    repeat_max_turns = int(CLAUDE_CFG.get("repeat_max_turns") or 32)
    timeout_minutes = int(CLAUDE_CFG.get("timeout_minutes") or 40)

    max_turns = repeat_max_turns if session_id else full_max_turns

    cmd = [
        claude,
        "-p",
    ]
    if session_id:
        cmd += ["--resume", session_id]

    cmd += [
        "--output-format", "json",
        "--max-turns", str(max_turns),
        # Claude Code MCP permissions do NOT support glob patterns.
        # Passing only the server name authorizes all tools from that MCP.
        "--allowedTools", "mcp__playwright",
        "--permission-mode", "dontAsk",
    ]

    log_path = debug_stage(asset, "02_leonardo_claude") / "claude.log"

    started = time.time()
    try:
        proc = subprocess.run(
            cmd,
            input=prompt,
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_minutes * 60,
        )
    except subprocess.TimeoutExpired as exc:
        with log_path.open("a", encoding="utf-8", errors="replace") as fh:
            fh.write("\n=== CLAUDE TIMEOUT ===\n")
            fh.write(str(exc))
        return 124, (
            "ERROR_AFTER_GENERATE: Claude/Playwright timeout; result state uncertain"
        ), session_id or None

    stdout = proc.stdout or ""
    stderr = proc.stderr or ""

    result_text = stdout.strip()
    returned_session_id = session_id or None

    try:
        parsed = json.loads(stdout)
        if isinstance(parsed, dict):
            if isinstance(parsed.get("result"), str):
                result_text = parsed["result"].strip()
            if parsed.get("session_id"):
                returned_session_id = str(parsed["session_id"])
    except Exception:
        parsed = None

    with log_path.open("a", encoding="utf-8", errors="replace") as fh:
        fh.write("\n\n" + "=" * 88 + "\n")
        fh.write(time.strftime("%Y-%m-%d %H:%M:%S") + "\n")
        fh.write(f"duration_seconds={time.time() - started:.1f}\n")
        fh.write(f"resumed_session={bool(session_id)}\n")
        fh.write(f"session_id={returned_session_id or ''}\n")
        fh.write("COMMAND:\n" + subprocess.list2cmdline(cmd) + "\n")
        fh.write("\nSTDOUT:\n" + stdout + "\n")
        fh.write("\nSTDERR:\n" + stderr + "\n")

    # IMPORTANT V10: do NOT persist the session here.
    # It is persisted only after a real Leonardo output file has been found,
    # validated and published into Upscaled.
    if debug_dir is not None:
        (debug_dir / "01_result.txt").write_text(
            result_text or "", encoding="utf-8"
        )
        write_debug_json(
            debug_dir / "02_run_meta.json",
            {
                "exit_code": proc.returncode,
                "session_id": returned_session_id,
                "resumed_session": bool(session_id),
                "duration_seconds": round(time.time() - started, 3),
                "command": cmd,
                "stdout_raw": stdout,
                "stderr_raw": stderr,
            },
        )

    return proc.returncode, result_text, returned_session_id



def find_recent_leonardo_download(
    started_at: float,
    prepared_size: tuple[int, int],
) -> Optional[Path]:
    """
    Recover a Leonardo browser download even if Claude hit max_turns after
    clicking Download. Search only recent image files and validate the configured
    geometry, so unrelated old downloads are ignored.
    """
    candidate_dirs = []
    home = Path.home()

    for d in (
        home / "Downloads",
        home / "Descargas",
        ROOT / "Downloads",
        SYSTEM / "BrowserDownloads",
    ):
        if d.exists() and d.is_dir():
            candidate_dirs.append(d)

    candidates = []
    seen = set()

    for d in candidate_dirs:
        try:
            for p in d.iterdir():
                if not p.is_file():
                    continue
                if p.suffix.lower() not in {".png", ".jpg", ".jpeg", ".webp"}:
                    continue
                key = str(p.resolve()).casefold()
                if key in seen:
                    continue
                seen.add(key)

                try:
                    mt = p.stat().st_mtime
                except OSError:
                    continue

                if mt < started_at - 20:
                    continue

                if not validate_upscaled_geometry(p, prepared_size):
                    continue

                name_l = p.name.casefold()
                leonardo_bonus = 1 if (
                    "universalupscaler" in name_l
                    or "upscaler" in name_l
                    or "leonardo" in name_l
                ) else 0

                candidates.append((leonardo_bonus, mt, p))
        except OSError:
            pass

    if not candidates:
        return None

    candidates.sort(key=lambda x: (x[0], x[1]), reverse=True)
    return candidates[0][2]


def adopt_recent_leonardo_download(
    started_at: float,
    prepared_size: tuple[int, int],
    asset_dir: Path,
    source: Path,
) -> Optional[Path]:
    found = find_recent_leonardo_download(started_at, prepared_size)
    if found is None:
        return None

    asset = safe_stem(source.name)
    leonardo_debug = debug_stage(asset, "03_leonardo_output")
    shutil.copy2(
        found,
        leonardo_debug / ("00_browser_download_original" + found.suffix.lower()),
    )

    asset_dir.mkdir(parents=True, exist_ok=True)
    temp_download = asset_dir / "upscaled_full.download"
    shutil.copy2(found, temp_download)

    ready = normalize_download(
        temp_download,
        asset_dir,
        source,
        prepared_size,
    )
    log(f"{source.name}: download Leonardo recuperat de {found}")
    return ready


def _copy_manual_upload_input(
    source: Path,
    prepared: Path,
    layout: dict,
) -> Path:
    """
    Publish the exact chroma composite to a visible human-handoff folder.
    """
    asset = safe_stem(source.name)
    folder = MANUAL_LEONARDO / asset
    folder.mkdir(parents=True, exist_ok=True)

    upload = folder / "UPLOAD_THIS_TO_LEONARDO.png"
    shutil.copy2(prepared, upload)

    write_debug_json(
        folder / "layout.json",
        {
            "source": source.name,
            "expected_input_size": list(layout["canvas_size"]),
            "expected_scale": LEONARDO_SCALE_FACTOR,
            "instructions": (
                "Puja UPLOAD_THIS_TO_LEONARDO.png a Leonardo Pro Upscaler "
                f"amb Precise + x{LEONARDO_SCALE_LABEL} + Detailed. Despres copia el resultat "
                "descarregat en aquesta mateixa carpeta. No cal renombrar-lo."
            ),
        },
    )

    return upload


def _manual_result_candidates(
    folder: Path,
    prepared_size: tuple[int, int],
) -> list[Path]:
    candidates = []

    if not folder.exists():
        return candidates

    excluded = {
        "upload_this_to_leonardo.png",
    }

    for p in folder.iterdir():
        if not p.is_file():
            continue
        if p.name.casefold() in excluded:
            continue
        if p.suffix.lower() not in {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
        }:
            continue
        if not validate_upscaled_geometry(
            p,
            prepared_size,
        ):
            continue
        candidates.append(p)

    candidates.sort(
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    return candidates


def wait_for_manual_leonardo(
    source: Path,
    prepared: Path,
    layout: dict,
    asset_dir: Path,
) -> Optional[Path]:
    """
    Human handoff:
      1. publish prepared chroma image visibly
      2. wait for the user to do Leonardo manually
      3. user copies downloaded result into same folder
      4. ENTER validates and publishes canonical Upscaled

    No Claude process is started.
    """
    asset = safe_stem(source.name)
    prepared_size = tuple(layout["canvas_size"])

    folder = MANUAL_LEONARDO / asset
    upload = _copy_manual_upload_input(
        source,
        prepared,
        layout,
    )

    print()
    print("=" * 72)
    print("LEONARDO MANUAL")
    print("=" * 72)
    print(f"Peca: {source.name}")
    print()
    print("1) Puja aquest fitxer a Leonardo:")
    print(f"   {upload}")
    print()
    print("2) Configura:")
    print(f"   Pro Upscaler / Precise / x{LEONARDO_SCALE_LABEL} / Detailed")
    print()
    print("3) Descarrega el resultat i COPIA'L en aquesta carpeta:")
    print(f"   {folder}")
    print()
    print("   No cal renombrar-lo.")
    print("   Pot ser .jpg, .jpeg, .png o .webp.")
    print()
    print("4) Torna aqui i prem ENTER.")
    print("   Escriu Q + ENTER si vols cancel·lar aquesta peca.")
    print("=" * 72)

    while True:
        # If the result already exists physically (e.g. after restarting BAT),
        # accept it immediately. Physical file only; no state/recovery JSON.
        candidates = _manual_result_candidates(
            folder,
            prepared_size,
        )

        if candidates:
            found = candidates[0]

            asset_dir.mkdir(parents=True, exist_ok=True)
            controlled = asset_dir / "upscaled_full.download"
            shutil.copy2(found, controlled)

            ready = normalize_download(
                controlled,
                asset_dir,
                source,
                prepared_size,
            )

            debug_dir = debug_stage(
                asset,
                "03_leonardo_output",
            )
            shutil.copy2(
                found,
                debug_dir / (
                    "00_manual_leonardo_original"
                    + found.suffix.lower()
                ),
            )
            shutil.copy2(
                ready,
                debug_dir / (
                    "01_upscaled_exact"
                    + ready.suffix.lower()
                ),
            )

            log(
                f"{source.name}: Leonardo manual validat "
                f"-> {ready.name}"
            )
            return ready

        answer = input(
            "\nQuan hagis copiat el resultat, prem ENTER "
            "(Q per cancel·lar): "
        ).strip()

        if answer.casefold() == "q":
            log(
                f"{source.name}: Leonardo manual cancel·lat per l'usuari."
            )
            return None

        # Loop and validate again.
        print(
            f"No trobo encara cap resultat x{LEONARDO_SCALE_LABEL} valid en "
            f"{folder}"
        )


def recover_or_generate_upscaled(
    source: Path,
    prepared: Path,
    prepared_size: tuple[int, int],
    asset_dir: Path,
    source_hash: str,
) -> Optional[Path]:
    """
    V14: NO persistent recovery state.

    This function is called only when canonical Upscaled does NOT exist.
    It performs at most one paid Leonardo attempt for this asset during the
    current BAT execution.

    On restart, only Upscaled/<asset>/upscaled_full.* counts. No RUNNING JSON,
    Claude session or previous prompt state is ever resumed.
    """
    asset = safe_stem(source.name)

    if asset in LEONARDO_ATTEMPTED_THIS_RUN:
        return None
    LEONARDO_ATTEMPTED_THIS_RUN.add(asset)

    asset_dir.mkdir(parents=True, exist_ok=True)

    claude_debug = debug_stage(asset, "02_claude_leonardo")
    leonardo_debug = debug_stage(asset, "03_leonardo_output")
    download_target = asset_dir / "upscaled_full.download"

    # A stale partial file is NOT a checkpoint.
    try:
        download_target.unlink()
    except FileNotFoundError:
        pass

    prompt = build_claude_prompt(
        source,
        prepared,
        download_target,
        recovery_only=False,
        compact_repeat=False,
    )

    log(f"{source.name}: Claude -> Leonardo Pro / Precise / x{LEONARDO_SCALE_LABEL} / Detailed")
    started_at = time.time()

    # Always a FRESH Claude conversation. No --resume.
    code, answer, _ = run_claude_browser(
        prompt,
        asset,
        reuse_session=False,
        debug_dir=claude_debug,
    )

    ready = None

    # Preferred: Claude saved to the controlled path.
    if download_target.exists():
        try:
            shutil.copy2(
                download_target,
                leonardo_debug / "00_download_exact_before_normalize.bin",
            )
            ready = normalize_download(
                download_target,
                asset_dir,
                source,
                prepared_size,
            )
        except Exception as exc:
            write_debug_json(
                leonardo_debug / "download_validation_error.json",
                {"error": str(exc)},
            )

    # Same-attempt physical download fallback only.
    # This never acts as a restart checkpoint.
    if ready is None:
        try:
            ready = adopt_recent_leonardo_download(
                started_at,
                prepared_size,
                asset_dir,
                source,
            )
        except Exception as exc:
            write_debug_json(
                leonardo_debug / "recent_download_error.json",
                {"error": str(exc)},
            )
            ready = None

    if ready is not None:
        shutil.copy2(
            ready,
            leonardo_debug / ("01_upscaled_exact" + ready.suffix.lower()),
        )
        log(f"{source.name}: Leonardo -> {ready.name}")
        return ready

    log(f"{source.name}: no hi ha cap checkpoint Upscaled valid.")
    log(f"{source.name}: Claude exit code: {code}")
    if answer:
        compact = str(answer).replace("\\r", " ").replace("\\n", " ").strip()
        log(f"{source.name}: Claude: {compact[:1200]}")
    return None


def expected_unique_views(order: list[str]) -> list[str]:
    out = []
    for v in order:
        if v not in out:
            out.append(v)
    return out


def cuts_are_ready(
    asset_dir: Path,
    expected: list[str],
    upscaled: Path,
) -> bool:
    if not expected:
        return False

    orders_path = INPUT / "view_orders.json"
    prerequisite_mtime = upscaled.stat().st_mtime
    if orders_path.exists():
        prerequisite_mtime = max(prerequisite_mtime, orders_path.stat().st_mtime)

    for name in expected:
        p = asset_dir / f"{name}.png"
        if not valid_image(p):
            return False
        if p.stat().st_mtime + 0.5 < prerequisite_mtime:
            return False
    return True


def file_to_data_uri(path: Path) -> str:
    mime = mimetypes.guess_type(path.name)[0] or "image/png"
    return (
        f"data:{mime};base64,"
        + base64.b64encode(path.read_bytes()).decode("ascii")
    )


def meshy_headers(key: str) -> dict:
    return {
        "Authorization": f"Bearer {key}",
        "Content-Type": "application/json",
    }


def meshy_start(
    session: requests.Session,
    key: str,
    views: Dict[str, Path],
    debug_dir: Path | None = None,
) -> str:
    ordered_names = [
        name
        for name in ("front", "back", "right", "left")
        if name in views
    ]
    if not ordered_names:
        raise RuntimeError("No hi ha cap vista habilitada per enviar a Meshy.")
    ordered = [views[name] for name in ordered_names]

    body = dict(MESHY_SETTINGS)
    body["image_urls"] = [file_to_data_uri(p) for p in ordered]

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)
        write_debug_json(
            debug_dir / "00_request.json",
            {
                "endpoint": f"{MESHY_API}/multi-image-to-3d",
                "settings": MESHY_SETTINGS,
                "ordered_view_labels": ordered_names,
                "ordered_views": [str(p) for p in ordered],
                "view_sha256": {
                    name: sha256_file(views[name])
                    for name in ordered_names
                },
                "note": "Base64 image payload omitted from debug JSON; exact PNGs are in Upscaled_and_Cut and Debug/04_cut.",
            },
        )

    r = session.post(
        f"{MESHY_API}/multi-image-to-3d",
        headers=meshy_headers(key),
        json=body,
        timeout=300,
    )
    checked(r, "Meshy Multi-Image to 3D")
    payload = r.json()

    if debug_dir is not None:
        write_debug_json(debug_dir / "01_create_response.json", payload)

    task_id = _find_key(payload, "result")
    if isinstance(task_id, dict):
        task_id = task_id.get("id")

    if not task_id:
        raise RuntimeError(
            "Meshy no ha retornat task id:\n" + _response_preview(payload)
        )
    return str(task_id)


def meshy_wait(
    session: requests.Session,
    key: str,
    task_id: str,
    timeout_seconds: int = 3600,
    debug_dir: Path | None = None,
):
    deadline = time.monotonic() + timeout_seconds
    url = f"{MESHY_API}/multi-image-to-3d/{task_id}"

    while time.monotonic() < deadline:
        r = session.get(url, headers=meshy_headers(key), timeout=60)

        if r.status_code == 429:
            wait = float(r.headers.get("Retry-After") or 10)
            log(f"Meshy rate limit; retry en {wait:g}s")
            time.sleep(wait)
            continue

        checked(r, "Meshy status")
        root = r.json()

        if debug_dir is not None:
            write_debug_json(debug_dir / "02_latest_status.json", root)

        data = _first_dict(root)

        if not isinstance(data, dict):
            raise RuntimeError(
                "Resposta Meshy inesperada:\n" + _response_preview(root)
            )

        status = str(data.get("status") or "").upper()

        if status == "SUCCEEDED":
            if debug_dir is not None:
                write_debug_json(debug_dir / "03_succeeded_response.json", root)
            return data

        if status in {"FAILED", "CANCELED", "CANCELLED"}:
            raise RuntimeError(
                f"Meshy ha acabat com {status}:\n{_response_preview(root)}"
            )

        progress = data.get("progress")
        suffix = f" {progress}%" if progress is not None else ""
        log(f"Meshy: {status or 'PENDING'}{suffix}")
        time.sleep(10)

    raise TimeoutError("Meshy ha superat el temps maxim.")


def download_glb(
    session: requests.Session,
    url: str,
    target: Path,
) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp = target.with_suffix(".glb.part")

    with session.get(url, stream=True, timeout=600) as r:
        checked(r, "Meshy GLB download")
        with tmp.open("wb") as fh:
            for chunk in r.iter_content(1024 * 1024):
                if chunk:
                    fh.write(chunk)

    if tmp.stat().st_size < 4096:
        raise RuntimeError("El GLB descarregat de Meshy es massa petit.")
    os.replace(tmp, target)


def raw_is_ready(raw: Path, views: Dict[str, Path]) -> bool:
    if not raw.exists() or raw.stat().st_size < 4096:
        return False
    newest_cut = max(p.stat().st_mtime for p in views.values())
    return raw.stat().st_mtime + 0.5 >= newest_cut


def meshy_signature(views: Dict[str, Path]) -> str:
    payload = {
        "settings": MESHY_SETTINGS,
        "views": {
            name: sha256_file(path)
            for name, path in sorted(views.items())
        },
    }
    return json.dumps(payload, sort_keys=True, separators=(",", ":"))


def ensure_raw3d(
    session: requests.Session,
    meshy_key: str,
    source: Path,
    views: Dict[str, Path],
) -> Optional[Path]:
    """
    Meshy downloads first to legacy-flat Raw3D/<asset>.glb as a transient
    publish target. process_one() immediately packages it into
    Raw3D/<asset>/<asset>.glb together with the exact useful preprocess inputs.
    """
    asset = safe_stem(source.name)
    packaged = RAW / asset / f"{asset}.glb"
    if packaged.exists() and packaged.stat().st_size >= 4096:
        return packaged

    raw = RAW / f"{asset}.glb"
    if raw.exists() and raw.stat().st_size >= 4096:
        return raw

    if asset in MESHY_SUBMIT_ATTEMPTED_THIS_RUN:
        return None
    MESHY_SUBMIT_ATTEMPTED_THIS_RUN.add(asset)

    meshy_debug = debug_stage(asset, "05_meshy")

    log(f"{source.name}: Meshy 7.1 / Geometry 2K / Texture 2K / T-Pose")

    task_id = meshy_start(
        session,
        meshy_key,
        views,
        debug_dir=meshy_debug,
    )

    # Diagnostic only.
    (meshy_debug / "task_id.txt").write_text(
        str(task_id),
        encoding="utf-8",
    )

    task = meshy_wait(
        session,
        meshy_key,
        task_id,
        debug_dir=meshy_debug,
    )

    glb_url = (task.get("model_urls") or {}).get("glb")
    if not glb_url:
        raise RuntimeError("Meshy ha acabat pero model_urls.glb no existeix.")

    download_glb(session, glb_url, raw)

    shutil.copy2(
        raw,
        meshy_debug / "04_raw3d_exact.glb",
    )

    log(f"{source.name}: Raw3D -> {raw.name}")
    return raw


def _valid_raw_checkpoint(asset: str) -> Optional[Path]:
    packaged = RAW / asset / f"{asset}.glb"
    if packaged.exists() and packaged.stat().st_size >= 4096:
        return packaged

    # Legacy flat checkpoint from older runs: process_one will package it.
    flat = RAW / f"{asset}.glb"
    if flat.exists() and flat.stat().st_size >= 4096:
        return flat
    return None


def _cut_checkpoint(
    asset: str,
    expected: list[str],
) -> Optional[Dict[str, Path]]:
    folder = CUTS / asset
    result: Dict[str, Path] = {}

    for name in expected:
        p = folder / f"{name}.png"
        if not valid_image(p):
            return None
        result[name] = p

    return result


def _upscaled_checkpoint(asset: str) -> Optional[Path]:
    folder = UPSCALED / asset
    candidates = upscaled_candidates(folder)
    return candidates[0] if candidates else None


def _cleanup_transient(asset: str) -> None:
    # Debug + visible checkpoints stay. Temp/partials do not.
    temp_dir = TEMP / asset
    if temp_dir.exists():
        shutil.rmtree(temp_dir, ignore_errors=True)

    asset_dir = UPSCALED / asset
    if asset_dir.exists():
        for p in asset_dir.glob("*.download"):
            try:
                p.unlink()
            except OSError:
                pass
        for p in asset_dir.glob("*.part"):
            try:
                p.unlink()
            except OSError:
                pass

    part = RAW / f"{asset}.glb.part"
    if part.exists():
        try:
            part.unlink()
        except OSError:
            pass

    staging = RAW / f".{asset}.packaging"
    if staging.exists():
        # A completed package is never touched here. Only stale transient
        # packaging from a failed move attempt is considered.
        shutil.rmtree(staging, ignore_errors=True)


def _cleanup_after_package(asset: str) -> None:
    """Raw3D/<asset>/ is complete (glb + input_original + the views sent to Meshy): drop everything that
    only served to produce it - Debug/<asset> (unless config behavior.keep_debug), the Manual_Leonardo
    hand-off folder and the Playwright browser logs. A failed asset never gets here, so its debug stays."""
    behavior = CONFIG.get("behavior") or {}
    targets = [MANUAL_LEONARDO / asset, ROOT / ".playwright-mcp"]
    if not behavior.get("keep_debug", False):
        targets.append(DEBUG / asset)
    for target in targets:
        if target.exists():
            shutil.rmtree(target, ignore_errors=True)
    for folder in (DEBUG, MANUAL_LEONARDO):
        try:
            if folder.is_dir() and not any(folder.iterdir()):
                folder.rmdir()
        except OSError:
            pass


def _cleanup_after_failure(asset: str) -> None:
    """A failed attempt leaves nothing behind but the kept checkpoints (Input, Upscaled_and_Cut, Raw3D).
    Single exception: while no cut views exist yet, the Leonardo upscale is the only result of a paid
    step and the producer retries every few seconds - deleting it would order a new upscale."""
    _cleanup_transient(asset)
    cuts = CUTS / asset
    has_cuts = cuts.is_dir() and any(cuts.glob("*.png"))
    has_upscale = bool(upscaled_candidates(UPSCALED / asset)) if (UPSCALED / asset).is_dir() else False
    targets = [DEBUG / asset, TEMP / asset, ROOT / ".playwright-mcp"]
    if has_cuts or not has_upscale:
        targets.append(UPSCALED / asset)   # redundant once cut, or holding no valid result at all
    if has_cuts or has_upscale:
        targets.append(MANUAL_LEONARDO / asset)
    for target in targets:
        if target.exists():
            shutil.rmtree(target, ignore_errors=True)
    for folder in (DEBUG, MANUAL_LEONARDO):
        try:
            if folder.is_dir() and not any(folder.iterdir()):
                folder.rmdir()
        except OSError:
            pass


def _prepare_chroma_layout(
    source: Path,
) -> tuple[Path, dict]:
    asset = safe_stem(source.name)

    temp_dir = TEMP / asset
    temp_dir.mkdir(parents=True, exist_ok=True)

    prepared = temp_dir / "leonardo_input_2x2_CHROMA.png"

    layout = prepare_balanced_2x2_chroma(
        source,
        prepared,
        island_margin_px=24,
        cell_padding_px=25,
        gap_px=50,
        outer_margin_px=20,
        max_aspect=1.50,
        debug_dir=debug_stage(asset, "01_preprocess_chroma"),
    )

    write_debug_json(
        debug_stage(asset, "01_preprocess_chroma") / "05_layout_used.json",
        layout,
    )

    return prepared, layout



def _move_raw_package(
    source: Path,
    views: Dict[str, Path],
    raw: Path,
) -> Path:
    """Close Meshy preprocess state into Raw3D/<asset>/ using MOVE semantics.

    Final package:
      Raw3D/<asset>/
        input_original.<ext>
        upscaled_and_cut/<used views>.png
        <asset>.glb

    The Leonardo full-upscale checkpoint and any leftover cut folder are
    deliberately removed after successful packaging. Debug is independent.
    """
    asset = safe_stem(source.name)
    final_dir = RAW / asset
    final_raw = final_dir / f"{asset}.glb"

    # Already packaged: nothing else should remain active for this asset.
    if final_raw.exists() and final_raw.stat().st_size >= 4096:
        if source.exists():
            raise RuntimeError(
                f"{asset}: Raw3D package already exists but Input source still exists; "
                "refusing to guess which copy is authoritative."
            )
        return final_raw

    if not raw.exists() or raw.stat().st_size < 4096:
        raise RuntimeError(f"{asset}: RAW Meshy invalid before packaging.")
    if not source.exists():
        raise RuntimeError(f"{asset}: input original missing before packaging.")
    for name, p in views.items():
        if not p.exists():
            raise RuntimeError(f"{asset}: used cut view missing before packaging: {name}")

    staging = RAW / f".{asset}.packaging"
    if staging.exists():
        shutil.rmtree(staging, ignore_errors=True)
    (staging / "upscaled_and_cut").mkdir(parents=True, exist_ok=False)

    moved = []
    try:
        input_target = staging / ("input_original" + source.suffix.lower())
        shutil.move(str(source), str(input_target))
        moved.append((input_target, source))

        # Preserve only the exact views passed to Meshy.
        for name, p in sorted(views.items()):
            target = staging / "upscaled_and_cut" / f"{name}{p.suffix.lower()}"
            shutil.move(str(p), str(target))
            moved.append((target, p))

        raw_target = staging / f"{asset}.glb"
        shutil.move(str(raw), str(raw_target))
        moved.append((raw_target, raw))

        if final_dir.exists():
            raise RuntimeError(
                f"{asset}: destination Raw3D package already exists: {final_dir}"
            )

        # Same-project rename: publish package as one directory.
        os.replace(staging, final_dir)

    except Exception:
        # Best-effort rollback so a packaging failure does not strand an asset
        # outside the active process folders.
        for moved_path, original_path in reversed(moved):
            candidate = moved_path
            if not candidate.exists() and final_dir.exists():
                try:
                    candidate = final_dir / moved_path.relative_to(staging)
                except Exception:
                    pass
            if candidate.exists() and not original_path.exists():
                original_path.parent.mkdir(parents=True, exist_ok=True)
                try:
                    shutil.move(str(candidate), str(original_path))
                except Exception:
                    pass
        if staging.exists():
            shutil.rmtree(staging, ignore_errors=True)
        if final_dir.exists() and not final_raw.exists():
            shutil.rmtree(final_dir, ignore_errors=True)
        raise

    # Successful package: remove process residues that are intentionally NOT
    # part of preProcess.
    cut_dir = CUTS / asset
    if cut_dir.exists():
        shutil.rmtree(cut_dir, ignore_errors=True)

    upscale_dir = UPSCALED / asset
    if upscale_dir.exists():
        shutil.rmtree(upscale_dir, ignore_errors=True)

    temp_dir = TEMP / asset
    if temp_dir.exists():
        shutil.rmtree(temp_dir, ignore_errors=True)

    log(f"{asset}: paquet Raw3D tancat -> {final_dir}")
    return final_raw

def process_one(
    session: requests.Session,
    meshy_key: str,
    source: Path,
    control: dict,
) -> None:
    asset = safe_stem(source.name)
    order = view_order_for(INPUT, source)
    use_views = view_usage_for(INPUT, source)
    expected = expected_unique_views(order)

    write_debug_json(
        debug_root(asset) / "00_manifest.json",
        {
            "source": str(source),
            "source_name": source.name,
            "source_sha256": sha256_file(source),
            "view_order": order,
            "use_views": use_views,
            "pipeline_version": "v21_selectable_meshy_views",
            "pipeline_steps": control["steps"],
            "first_false": first_false_stage(control),
            "updated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        },
    )

    # Persistent state = visible physical checkpoints only.
    raw = _valid_raw_checkpoint(asset)
    views = _cut_checkpoint(asset, expected)
    upscaled = _upscaled_checkpoint(asset)

    if raw is not None:
        # New packaged checkpoint means this source should no longer exist in
        # Input. Legacy flat checkpoint is migrated now.
        if raw.parent == RAW:
            if views is None:
                raise RuntimeError(
                    f"{source.name}: RAW legacy exists but the exact used cuts are missing; "
                    "cannot build the requested Raw3D package safely."
                )
            raw = _move_raw_package(source, views, raw)
        log(f"{source.name}: Raw3D package ja existeix -> COMPLET")
        _cleanup_transient(asset)
        _cleanup_after_package(asset)
        return

    # --------------------------------------------------------------
    # STEP 1 — PREPARE CHROMA
    # --------------------------------------------------------------
    if not stage_allowed(control, "prepare_chroma"):
        log(
            f"{source.name}: STOP abans de prepare_chroma "
            "(pipeline_steps.json)"
        )
        return

    prepared = None
    layout = None

    # If cuts already exist, no layout reconstruction is needed.
    if views is None:
        prepared, layout = _prepare_chroma_layout(source)

    # --------------------------------------------------------------
    # STEP 2 — LEONARDO
    # --------------------------------------------------------------
    if not stage_allowed(control, "leonardo_upscale"):
        log(
            f"{source.name}: STOP abans de Leonardo "
            "(pipeline_steps.json)"
        )
        return

    if views is None and upscaled is None:
        assert prepared is not None and layout is not None

        leonardo_mode = control["settings"]["leonardo_mode"]

        if leonardo_mode == "manual":
            upscaled = wait_for_manual_leonardo(
                source,
                prepared,
                layout,
                UPSCALED / asset,
            )
        else:
            upscaled = recover_or_generate_upscaled(
                source,
                prepared,
                tuple(layout["canvas_size"]),
                UPSCALED / asset,
                sha256_file(source),
            )

        if upscaled is None:
            return

    elif views is None and upscaled is not None:
        log(
            f"{source.name}: Upscaled ja existeix -> salto Leonardo"
        )

    # --------------------------------------------------------------
    # STEP 3 — CUT VIEWS
    # --------------------------------------------------------------
    if not stage_allowed(control, "cut_views"):
        log(
            f"{source.name}: STOP abans del tall "
            "(pipeline_steps.json)"
        )
        return

    if views is None:
        if upscaled is None:
            raise RuntimeError(
                "No hi ha Upscaled disponible per fer el tall."
            )

        if layout is None:
            prepared, layout = _prepare_chroma_layout(source)

        views = split_upscaled_grid_keep_chroma(
        upscaled,
        layout,
        CUTS / asset,
        order,
        debug_dir=debug_stage(
            asset,
            "04_split_keep_chroma",
        ),
    )

        log(
            f"{source.name}: Upscaled_and_Cut (CHROMA CONSERVAT) -> "
            + ", ".join(views.keys())
        )
    else:
        log(
            f"{source.name}: Upscaled_and_Cut ja existeix "
            "-> salto el tall"
        )

    # --------------------------------------------------------------
    # STEP 4 — MESHY
    # --------------------------------------------------------------
    # The cut stage always produces/reuses all semantic views available from
    # the 2x2 source. Only this filtered dict is uploaded to Meshy and later
    # preserved in Raw3D/<asset>/upscaled_and_cut/.
    meshy_views = {
        name: path
        for name, path in views.items()
        if use_views.get(name, True)
    }
    if not meshy_views:
        raise RuntimeError(
            f"{source.name}: totes les vistes disponibles estan desactivades "
            "a view_orders.json."
        )

    requested_but_unavailable = [
        name
        for name in ("front", "back", "right", "left")
        if use_views.get(name, True) and name not in views
    ]
    if requested_but_unavailable:
        log(
            f"{source.name}: vistes habilitades però no disponibles pel seu order: "
            + ", ".join(requested_but_unavailable)
        )

    log(
        f"{source.name}: Meshy usarà "
        + ", ".join(meshy_views.keys())
    )

    if not stage_allowed(control, "meshy_raw3d"):
        log(
            f"{source.name}: STOP abans de Meshy "
            "(pipeline_steps.json)"
        )
        return

    if not meshy_key:
        raise RuntimeError(
            "Meshy esta habilitat pero no hi ha API key carregada."
        )

    raw = ensure_raw3d(
        session,
        meshy_key,
        source,
        meshy_views,
    )

    if raw is not None:
        raw = _move_raw_package(source, meshy_views, raw)
        _cleanup_transient(asset)
        _cleanup_after_package(asset)
        log(f"{asset}: COMPLET -> Raw3D/{asset}/{raw.name}")


def main() -> None:
    for d in (
        INPUT,
        UPSCALED,
        CUTS,
        RAW,
        OUTPUT,
        TEMP,
    ):
        d.mkdir(parents=True, exist_ok=True)

    control = load_pipeline_control()
    stop_stage = first_false_stage(control)
    settings = control["settings"]

    print()
    print("=== GENERADOR V16 / CONTROLLED PIPELINE ===")
    print()
    print("pipeline_steps.json:")

    stop_index = None
    if stop_stage is not None:
        stop_index = [k for k, _ in STAGE_ORDER].index(stop_stage)

    for i, (key, label) in enumerate(STAGE_ORDER):
        value = control["steps"][key]
        suffix = ""

        if key == stop_stage:
            suffix = "  <-- PRIMER FALSE / STOP"
        elif stop_index is not None and i > stop_index:
            suffix = "  (ignorat)"

        print(
            f"  {key}: {str(value).lower()}{suffix}"
        )

    print()
    print(f"Punt de parada: {stage_label(stop_stage)}")
    print(f"Leonardo mode: {settings['leonardo_mode']}")
    print(
        "Input mode: "
        + (
            "nomes la imatge mes recent"
            if settings["process_only_newest_input"]
            else "totes les imatges"
        )
    )
    print()

    # Only initialize Claude/Playwright if Leonardo automation is explicitly
    # requested. Manual mode has zero Claude dependency.
    if (
        stage_allowed(control, "leonardo_upscale")
        and settings["leonardo_mode"] == "claude"
    ):
        claude = claude_executable()
        verify_playwright_mcp(claude)

    meshy_key = ""
    if stage_allowed(control, "meshy_raw3d"):
        meshy_key = load_meshy_key()

    session = requests.Session()
    session.headers.update(
        {"User-Agent": "MeshyPipelineControlledV16/1.0"}
    )

    def selected_inputs() -> list[Path]:
        inputs = list_inputs()

        if not inputs:
            return []

        if settings["process_only_newest_input"]:
            newest = max(
                inputs,
                key=lambda p: (
                    p.stat().st_mtime,
                    p.name.casefold(),
                ),
            )
            return [newest]

        return inputs

    one_pass = (
        stop_stage is not None
        and settings["exit_after_one_pass_when_stopped"]
    )

    while True:
        inputs = selected_inputs()

        if not inputs:
            log("No hi ha cap imatge processable a Input.")

        for source in inputs:
            try:
                process_one(
                    session,
                    meshy_key,
                    source,
                    control,
                )
            except KeyboardInterrupt:
                raise
            except Exception as exc:
                append_error_log(source, exc)
                log(
                    f"ERROR {source.name}: "
                    f"{type(exc).__name__}: {exc}"
                )
                _cleanup_after_failure(safe_stem(source.name))

        if one_pass:
            print()
            print(
                "STOP configurat: fi de la passada. "
                "El BAT es tanca."
            )
            return

        time.sleep(3)


def _entrypoint() -> None:
    try:
        main()
    except KeyboardInterrupt:
        print("\\nProducer aturat.")


if __name__ == "__main__":
    _entrypoint()
