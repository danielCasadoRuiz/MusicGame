#!/usr/bin/env python3
from __future__ import annotations

import shutil
import time
from pathlib import Path

import requests

import producer as core
from common import safe_stem, sha256_file, view_order_for
from image_ops import (
    prepare_balanced_2x2_chroma,
    split_upscaled_grid_chroma,
)


def newest_input() -> Path | None:
    files = [
        p
        for p in core.INPUT.iterdir()
        if p.is_file() and p.suffix.lower() in core.SUPPORTED
    ]

    if not files:
        return None

    return max(
        files,
        key=lambda p: p.stat().st_mtime,
    )


def backup_and_remove_old_upscaled(
    asset: str,
) -> None:
    asset_dir = core.UPSCALED / asset

    if not asset_dir.exists():
        return

    old_files = [
        p
        for p in asset_dir.iterdir()
        if p.is_file()
        and (
            p.name.startswith("upscaled_full")
            or p.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"}
        )
    ]

    if not old_files:
        return

    debug = core.debug_stage(
        asset,
        "00_PREVIOUS_UPSCALED_BACKUP",
    )

    stamp = time.strftime("%Y%m%d_%H%M%S")

    for p in old_files:
        target = debug / f"{stamp}__{p.name}"
        shutil.copy2(p, target)

        try:
            p.unlink()
        except OSError:
            pass


def main() -> int:
    for d in (
        core.INPUT,
        core.UPSCALED,
        core.CUTS,
        core.DEBUG,
        core.STATE,
        core.TEMP,
        core.LOGS,
    ):
        d.mkdir(parents=True, exist_ok=True)

    # Setup only; no Meshy key is loaded or requested.
    claude = core.claude_executable()
    core.verify_playwright_mcp(claude)

    source = newest_input()

    if source is None:
        print("No hi ha cap imatge a Input.")
        return 1

    asset = safe_stem(source.name)
    source_hash = sha256_file(source)
    order = view_order_for(core.INPUT, source)

    print()
    print("=== PROVA COMPLETA CHROMA / SENSE MESHY ===")
    print(f"Peca: {source.name}")
    print("Nomes es processa la imatge mes recent d'Input.")
    print("NO MESHY. NO BLENDER.")
    print()

    preprocess_debug = core.debug_stage(
        asset,
        "01_PREPROCESS_CHROMA",
    )
    cut_debug = core.debug_stage(
        asset,
        "04_CUT_CHROMA",
    )

    core.write_debug_json(
        core.debug_root(asset) / "00_CHROMA_TEST_MANIFEST.json",
        {
            "source": str(source),
            "source_sha256": source_hash,
            "view_order": order,
            "test": "full_chroma_no_meshy",
            "started_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        },
    )

    # 1) Input -> isolated views -> automatic chroma -> balanced 2x2.
    temp_dir = core.TEMP / asset / "chroma_full_test"
    temp_dir.mkdir(parents=True, exist_ok=True)

    prepared = temp_dir / "leonardo_input_2x2_CHROMA.png"

    layout = prepare_balanced_2x2_chroma(
        source,
        prepared,
        island_margin_px=24,
        cell_padding_px=48,
        gap_px=96,
        outer_margin_px=32,
        max_aspect=1.50,
        debug_dir=preprocess_debug,
    )

    core.save_json(
        temp_dir / "leonardo_input_2x2_CHROMA.layout.json",
        layout,
    )

    prepared_size = tuple(layout["canvas_size"])

    print(
        "Chroma triat:",
        tuple(layout["chroma_rgb"]),
    )

    # 2) For this test we MUST send the chroma version through Leonardo.
    #    Preserve any previous canonical upscale in Debug first.
    backup_and_remove_old_upscaled(asset)

    upscaled_dir = core.UPSCALED / asset
    upscaled_dir.mkdir(parents=True, exist_ok=True)

    # Clear only stale controlled temp download for this asset.
    controlled = upscaled_dir / "upscaled_full.download"
    if controlled.exists():
        try:
            controlled.unlink()
        except OSError:
            pass

    # 3) Claude -> Leonardo Precise x4 -> download.
    upscaled = core.recover_or_generate_upscaled(
        source,
        prepared,
        prepared_size,
        upscaled_dir,
        source_hash,
    )

    if upscaled is None:
        print()
        print("Leonardo no ha deixat cap Upscaled valid.")
        print("S'atura aqui. No s'ha cridat Meshy.")
        return 1

    # 4) Actual Leonardo pixels -> chroma key -> transparent semantic cuts.
    cut_dir = core.CUTS / asset

    views = split_upscaled_grid_chroma(
        upscaled,
        layout,
        cut_dir,
        order,
        padding_px=100,
        guard_px=10,
        debug_dir=cut_debug,
    )

    now = time.time()
    for p in views.values():
        try:
            p.touch()
        except Exception:
            pass

    print()
    print("RESULTAT:")
    for name, path in views.items():
        print(f"  {name}: {path}")

    print()
    print("STOP abans de Meshy.")
    print("No s'ha enviat res a Meshy ni a Blender.")
    print(
        "Debug:",
        core.debug_root(asset),
    )
    print()

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nTest aturat.")
        raise SystemExit(130)
