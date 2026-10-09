#!/usr/bin/env python3
from __future__ import annotations

import shutil
import sys
import time
from pathlib import Path

from PIL import Image

from common import safe_stem, view_order_for
from image_ops import (
    prepare_balanced_2x2,
    split_upscaled_grid_from_actual_background,
)


ROOT = Path(__file__).resolve().parent.parent
INPUT = ROOT / "Input"
UPSCALED = ROOT / "Upscaled"
CUTS = ROOT / "Upscaled_and_Cut"
DEBUG = ROOT / "Debug"
TEMP = ROOT / "_System" / "Temp"

SUPPORTED = {".png", ".jpg", ".jpeg", ".webp"}


def log(msg: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def valid_image(path: Path) -> bool:
    try:
        if not path.exists() or path.stat().st_size < 1024:
            return False
        with Image.open(path) as im:
            im.verify()
        return True
    except Exception:
        return False


def find_upscaled(asset: str) -> Path | None:
    folder = UPSCALED / asset
    if not folder.exists():
        return None

    candidates = []
    for ext in (".png", ".jpg", ".jpeg", ".webp"):
        p = folder / f"upscaled_full{ext}"
        if valid_image(p):
            candidates.append(p)

    if not candidates:
        return None

    return max(candidates, key=lambda p: p.stat().st_mtime)


def list_inputs() -> list[Path]:
    if not INPUT.exists():
        return []
    return sorted(
        [
            p for p in INPUT.iterdir()
            if p.is_file() and p.suffix.lower() in SUPPORTED
        ],
        key=lambda p: p.name.casefold(),
    )


def process(source: Path) -> bool:
    asset = safe_stem(source.name)
    upscaled = find_upscaled(asset)

    if upscaled is None:
        log(f"SKIP {source.name}: no hi ha Upscaled existent")
        return False

    order = view_order_for(INPUT, source)

    temp = TEMP / asset / "debug_recut"
    temp.mkdir(parents=True, exist_ok=True)
    prepared = temp / "reconstructed_leonardo_input_2x2.png"

    # Reconstruct exactly the same 2x2 geometry/layout. This does NOT call Leonardo.
    source_grid_alpha, layout = prepare_balanced_2x2(
        source,
        prepared,
        island_margin_px=24,
        cell_padding_px=48,
        gap_px=96,
        outer_margin_px=32,
        max_aspect=1.50,
        background_rgb=(238, 238, 238),
    )

    cut_dir = CUTS / asset
    debug_dir = DEBUG / asset / "DEBUG_RECUT_FROM_EXISTING_UPSCALED"

    log(f"{source.name}: recut des de {upscaled.name}")

    results = split_upscaled_grid_from_actual_background(
        upscaled,
        layout,
        cut_dir,
        order,
        padding_px=100,
        guard_px=10,
        debug_dir=debug_dir,
        source_grid_alpha=source_grid_alpha,
    )

    log(
        f"{source.name}: OK -> "
        + ", ".join(p.name for p in results.values())
    )
    return True


def main() -> int:
    print()
    print("=== DEBUG RECUT ONLY ===")
    print("NO Leonardo")
    print("NO Meshy")
    print("NO Retopo")
    print("Agafa nomes fitxers ja existents a Upscaled, retalla i s'atura.")
    print()

    processed = 0
    failed = 0

    for source in list_inputs():
        try:
            if process(source):
                processed += 1
        except Exception as exc:
            failed += 1
            log(f"ERROR {source.name}: {type(exc).__name__}: {exc}")

    print()
    print(f"FINAL: {processed} recut(s), {failed} error(s).")
    print("Mira Upscaled_and_Cut i Debug/<asset>/DEBUG_RECUT_FROM_EXISTING_UPSCALED/")
    print()
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
