from __future__ import annotations

import hashlib
import json
import os
import re
import unicodedata
from pathlib import Path
from typing import Any, Dict, Optional, Sequence, Tuple


DEFAULT_VIEW_ORDER = ["front", "back", "right", "left"]
DEFAULT_VIEW_USAGE = {"front": True, "back": True, "right": True, "left": True}
VALID_VIEWS = {"front", "back", "right", "left"}
VALID_PROFILES = {"auto", "standard", "body", "wig", "trousers", "shoe", "vest", "coat"}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def save_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp, path)


def load_json(path: Path, default=None):
    if not path.exists():
        return default
    return json.loads(path.read_text(encoding="utf-8"))


def safe_stem(name: str) -> str:
    stem = Path(name).stem.strip()
    stem = re.sub(r'[<>:"/\\|?*]+', "_", stem)
    stem = stem.rstrip(" .")
    return stem[:150] or "asset"


def _norm_text(value: str) -> str:
    value = unicodedata.normalize("NFKC", value)
    value = value.replace("’", "'").replace("‘", "'").replace("`", "'")
    value = re.sub(r"\s+", " ", value).strip().casefold()
    return value


def _copy_suffix_stripped(stem: str) -> str:
    # Explorer / generation duplicates such as "(1)" or trailing " 1".
    s = _norm_text(stem)
    s = re.sub(r"\s*\(\d+\)\s*$", "", s)
    s = re.sub(r"\s+\d+\s*$", "", s)
    return s.strip()


def load_view_orders(input_dir: Path) -> dict:
    path = input_dir / "view_orders.json"
    if not path.exists():
        return {"default_order": DEFAULT_VIEW_ORDER, "images": {}}
    try:
        data = load_json(path, {})
        if not isinstance(data, dict):
            raise ValueError("root must be an object")
        data.setdefault("default_order", DEFAULT_VIEW_ORDER)
        data.setdefault("images", {})
        return data
    except Exception as exc:
        raise RuntimeError(f"Invalid Input/view_orders.json: {exc}") from exc


def lookup_asset_entry(input_dir: Path, filename_or_stem: str) -> dict:
    cfg = load_view_orders(input_dir)
    images = cfg.get("images") or {}
    raw = str(filename_or_stem)

    # 1) exact normalized filename
    exact = _norm_text(raw)
    for key, entry in images.items():
        if _norm_text(key) == exact:
            return dict(entry or {})

    # 2) exact normalized stem
    stem = _norm_text(Path(raw).stem)
    exact_stem_matches = [
        dict(entry or {})
        for key, entry in images.items()
        if _norm_text(Path(key).stem) == stem
    ]
    if len(exact_stem_matches) == 1:
        return exact_stem_matches[0]

    # 3) tolerant Explorer-copy suffix match, only when unambiguous.
    stripped = _copy_suffix_stripped(Path(raw).stem)
    tolerant = [
        dict(entry or {})
        for key, entry in images.items()
        if _copy_suffix_stripped(Path(key).stem) == stripped
    ]
    if len(tolerant) == 1:
        return tolerant[0]

    return {}


def view_order_for(input_dir: Path, image_path: Path) -> list[str]:
    cfg = load_view_orders(input_dir)
    entry = lookup_asset_entry(input_dir, image_path.name)
    order = entry.get("order") or cfg.get("default_order") or DEFAULT_VIEW_ORDER
    order = [str(v).strip().lower() for v in order]

    if len(order) != 4 or any(v not in VALID_VIEWS for v in order):
        raise RuntimeError(
            f"Invalid view order for {image_path.name}: {order}. "
            "It must contain four labels using front/back/right/left. "
            "Duplicates are allowed for duplicated side images."
        )
    if "front" not in order:
        raise RuntimeError(f"{image_path.name}: the view order must contain a front view.")
    return order


def view_usage_for(input_dir: Path, image_path: Path) -> dict[str, bool]:
    """Return which semantic views are sent to Meshy.

    Defaults to all True. A root-level default_use_views may override the
    global defaults, and an image entry's use_views may override only the
    fields it specifies.
    """
    cfg = load_view_orders(input_dir)
    entry = lookup_asset_entry(input_dir, image_path.name)

    usage = dict(DEFAULT_VIEW_USAGE)

    for source_name, raw in (
        ("default_use_views", cfg.get("default_use_views")),
        ("use_views", entry.get("use_views")),
    ):
        if raw is None:
            continue
        if not isinstance(raw, dict):
            raise RuntimeError(
                f"{image_path.name}: {source_name} must be an object "
                "with front/back/right/left boolean fields."
            )
        unknown = sorted(set(raw) - VALID_VIEWS)
        if unknown:
            raise RuntimeError(
                f"{image_path.name}: unknown fields in {source_name}: {unknown}"
            )
        for name, enabled in raw.items():
            if type(enabled) is not bool:
                raise RuntimeError(
                    f"{image_path.name}: {source_name}.{name} must be true or false."
                )
            usage[name] = enabled

    return usage


def profile_for(input_dir: Path, raw_glb: Path) -> str:
    entry = lookup_asset_entry(input_dir, raw_glb.stem)
    profile = str(entry.get("profile") or "auto").strip().lower()
    return profile if profile in VALID_PROFILES else "auto"


def read_env_file(path: Path) -> dict[str, str]:
    out = {}
    if not path.exists():
        return out
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        out[key.strip()] = value.strip()
    return out


def write_env_file(path: Path, values: dict[str, str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = "\n".join(f"{k}={v}" for k, v in values.items()) + "\n"
    path.write_text(text, encoding="utf-8")
