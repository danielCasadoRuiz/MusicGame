from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Sequence, Tuple

import cv2
import numpy as np
from PIL import Image, ImageDraw


@dataclass(frozen=True)
class Box:
    x0: int
    y0: int
    x1: int
    y1: int

    @property
    def width(self) -> int:
        return self.x1 - self.x0

    @property
    def height(self) -> int:
        return self.y1 - self.y0


def _rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def _has_alpha(img: Image.Image) -> bool:
    a = np.asarray(img.getchannel("A"), dtype=np.uint8)
    return np.count_nonzero(a < 250) > max(16, int(a.size * 0.001))


def _fallback_mask(img: Image.Image) -> Image.Image:
    rgb = np.asarray(img, dtype=np.uint8)[..., :3]
    h, w, _ = rgb.shape
    border = max(2, min(h, w) // 100)
    samples = np.concatenate(
        [
            rgb[:border].reshape(-1, 3),
            rgb[-border:].reshape(-1, 3),
            rgb[:, :border].reshape(-1, 3),
            rgb[:, -border:].reshape(-1, 3),
        ],
        axis=0,
    ).astype(np.float32)

    bg = np.median(samples, axis=0)
    delta = rgb.astype(np.float32) - bg[None, None, :]
    dist = np.sqrt(np.sum(delta * delta, axis=2))

    bd = samples - bg[None, :]
    border_dist = np.sqrt(np.sum(bd * bd, axis=1))
    threshold = max(18.0, float(np.percentile(border_dist, 99.5)) + 8.0)

    mask = (dist > threshold).astype(np.uint8) * 255
    k = max(3, (min(h, w) // 500) | 1)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((k, k), np.uint8))
    return Image.fromarray(mask, mode="L")


def source_alpha(img: Image.Image) -> Image.Image:
    return img.getchannel("A") if _has_alpha(img) else _fallback_mask(img)


def _bbox(alpha: Image.Image, threshold: int = 4) -> Box:
    arr = np.asarray(alpha, dtype=np.uint8)
    ys, xs = np.where(arr > threshold)
    if len(xs) == 0:
        raise RuntimeError("No foreground pixels detected.")
    return Box(int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def _runs(active: np.ndarray) -> List[Tuple[int, int]]:
    active = active.astype(bool)
    if not active.any():
        return []
    padded = np.pad(active.astype(np.int8), (1, 1))
    d = np.diff(padded)
    starts = np.where(d == 1)[0]
    ends = np.where(d == -1)[0]
    return [(int(a), int(b)) for a, b in zip(starts, ends)]


def _merge_nearest(runs: List[Tuple[int, int]], target: int) -> List[Tuple[int, int]]:
    runs = sorted(runs)
    while len(runs) > target:
        gaps = [runs[i + 1][0] - runs[i][1] for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps))
        runs = runs[:i] + [(runs[i][0], runs[i + 1][1])] + runs[i + 2 :]
    return runs


def detect_four_view_boxes(alpha: Image.Image) -> List[Box]:
    """
    Detect the four PHYSICAL left-to-right view groups.
    Horizontal occupancy keeps disconnected buttons/straps/details attached
    to the same view instead of treating every connected component as a view.
    """
    mask = np.asarray(alpha, dtype=np.uint8) > 4
    h, w = mask.shape

    projection = np.count_nonzero(mask, axis=0)
    active = (projection > max(1, int(h * 0.0005))).astype(np.uint8) * 255

    close_w = max(3, int(round(w * 0.0025)))
    if close_w % 2 == 0:
        close_w += 1

    active = cv2.morphologyEx(
        active.reshape(1, -1),
        cv2.MORPH_CLOSE,
        np.ones((1, close_w), np.uint8),
    ).reshape(-1)

    runs = _runs(active > 0)
    total = max(1, int(np.count_nonzero(mask)))
    runs = [
        (x0, x1)
        for x0, x1 in runs
        if np.count_nonzero(mask[:, x0:x1]) >= max(24, int(total * 0.003))
    ]

    if len(runs) > 4:
        runs = _merge_nearest(runs, 4)

    if len(runs) != 4:
        raise RuntimeError(
            f"Expected 4 separated view groups but detected {len(runs)}."
        )

    boxes = []
    for x0, x1 in runs:
        ys, xs = np.where(mask[:, x0:x1])
        boxes.append(
            Box(
                x0 + int(xs.min()),
                int(ys.min()),
                x0 + int(xs.max()) + 1,
                int(ys.max()) + 1,
            )
        )
    boxes.sort(key=lambda b: b.x0)
    return boxes


def _mid(a: int, b: int) -> int:
    return (a + b) // 2


def _extract_isolated_views(
    source: Path,
    island_margin_px: int = 24,
) -> tuple[list[Image.Image], list[Image.Image], dict]:
    """
    Extract four independent RGBA view crops with GUARANTEED non-overlapping
    source territories. Margins are clipped at the midpoint between neighbours,
    so a neighbouring silhouette can never leak into a crop.
    """
    img = _rgba(source)
    alpha = source_alpha(img)
    boxes = detect_four_view_boxes(alpha)
    w, h = img.size

    rgba_views: list[Image.Image] = []
    alpha_views: list[Image.Image] = []
    extracted_boxes = []

    for i, b in enumerate(boxes):
        left_limit = 0 if i == 0 else _mid(boxes[i - 1].x1, b.x0)
        right_limit = w if i == 3 else _mid(b.x1, boxes[i + 1].x0)

        x0 = max(left_limit, b.x0 - island_margin_px)
        x1 = min(right_limit, b.x1 + island_margin_px)
        y0 = max(0, b.y0 - island_margin_px)
        y1 = min(h, b.y1 + island_margin_px)

        rgb_crop = img.crop((x0, y0, x1, y1)).convert("RGBA")
        mask_crop = alpha.crop((x0, y0, x1, y1)).convert("L")

        # Force a true transparent background even if the original was white.
        rgb_crop.putalpha(mask_crop)

        rgba_views.append(rgb_crop)
        alpha_views.append(mask_crop)
        extracted_boxes.append([x0, y0, x1, y1])

    return rgba_views, alpha_views, {
        "source_size": [w, h],
        "detected_boxes": [[b.x0, b.y0, b.x1, b.y1] for b in boxes],
        "extracted_boxes": extracted_boxes,
    }


def prepare_balanced_2x2(
    source: Path,
    destination: Path,
    island_margin_px: int = 24,
    cell_padding_px: int = 48,
    gap_px: int = 96,
    outer_margin_px: int = 32,
    max_aspect: float = 1.50,
    background_rgb: tuple[int, int, int] = (238, 238, 238),
    debug_dir: Path | None = None,
):
    """
    Repack the four source views from a long 1x4 strip into a roomy 2x2 grid:

        view0   view1

        view2   view3

    Important properties:
    - all four views keep EXACTLY the same pixel scale relative to one another;
    - each view is physically isolated before placement;
    - every cell has its own transparent margin;
    - a large transparent central gap prevents Leonardo from visually merging
      neighbouring views;
    - the overall canvas is padded toward a balanced aspect ratio without
      scaling individual views differently.

    Returns:
      composite_alpha: L mask matching the saved composite
      metadata: layout used later for deterministic post-upscale splitting
    """
    views, masks, source_meta = _extract_isolated_views(
        source, island_margin_px=island_margin_px
    )

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)

        # Exact source snapshot used for this run.
        src_img = _rgba(source)
        src_img.save(debug_dir / "00_source_snapshot.png", "PNG", optimize=True)

        # Full source alpha/mask used by detection.
        full_alpha = source_alpha(src_img)
        full_alpha.save(debug_dir / "01_source_foreground_mask.png", "PNG")

        # Detection overlay, useful to see what the island detector thought.
        overlay = src_img.convert("RGB")
        draw = ImageDraw.Draw(overlay)
        for i, box in enumerate(source_meta["detected_boxes"]):
            x0, y0, x1, y1 = box
            draw.rectangle((x0, y0, x1, y1), outline=(255, 0, 0), width=3)
            draw.text((x0 + 4, y0 + 4), f"view {i}", fill=(255, 0, 0))
        for i, box in enumerate(source_meta["extracted_boxes"]):
            x0, y0, x1, y1 = box
            draw.rectangle((x0, y0, x1, y1), outline=(0, 255, 0), width=2)
        overlay.save(debug_dir / "02_detection_overlay.jpg", "JPEG", quality=95)

        for i, (view, mask) in enumerate(zip(views, masks)):
            view.save(debug_dir / f"03_isolated_view_{i}.png", "PNG", optimize=True)
            mask.save(debug_dir / f"04_isolated_mask_{i}.png", "PNG")

    max_w = max(v.width for v in views)
    max_h = max(v.height for v in views)

    cell_w = max_w + 2 * cell_padding_px
    cell_h = max_h + 2 * cell_padding_px

    grid_w = 2 * cell_w + gap_px
    grid_h = 2 * cell_h + gap_px

    base_w = grid_w + 2 * outer_margin_px
    base_h = grid_h + 2 * outer_margin_px

    # Balance the total canvas by adding transparent padding only.
    canvas_w, canvas_h = base_w, base_h
    if canvas_w / canvas_h > max_aspect:
        canvas_h = int(np.ceil(canvas_w / max_aspect))
    elif canvas_h / canvas_w > max_aspect:
        canvas_w = int(np.ceil(canvas_h / max_aspect))

    balance_x = (canvas_w - base_w) // 2
    balance_y = (canvas_h - base_h) // 2

    grid_origin_x = balance_x + outer_margin_px
    grid_origin_y = balance_y + outer_margin_px

    # Leonardo receives a solid neutral RGB image instead of transparency.
    # This prevents hidden RGB/alpha fringe colours from becoming visible
    # when the service returns a flattened JPG.
    canvas = Image.new("RGB", (canvas_w, canvas_h), background_rgb)
    alpha_canvas = Image.new("L", (canvas_w, canvas_h), 0)

    slots = [
        (0, 0),  # original physical leftmost
        (1, 0),
        (0, 1),
        (1, 1),  # original physical rightmost
    ]

    tile_boxes = []
    content_boxes = []

    for i, (col, row) in enumerate(slots):
        cell_x0 = grid_origin_x + col * (cell_w + gap_px)
        cell_y0 = grid_origin_y + row * (cell_h + gap_px)
        cell_x1 = cell_x0 + cell_w
        cell_y1 = cell_y0 + cell_h

        view = views[i]
        mask = masks[i]

        # Center each isolated source crop in its cell WITHOUT rescaling it.
        px = cell_x0 + (cell_w - view.width) // 2
        py = cell_y0 + (cell_h - view.height) // 2

        canvas.paste(view.convert("RGB"), (px, py), mask)
        alpha_canvas.paste(mask, (px, py))

        tile_boxes.append([cell_x0, cell_y0, cell_x1, cell_y1])
        content_boxes.append([px, py, px + view.width, py + view.height])

    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, "PNG", optimize=True)

    if debug_dir is not None:
        canvas.save(debug_dir / "05_leonardo_input_exact.png", "PNG", optimize=True)
        alpha_canvas.save(debug_dir / "06_leonardo_input_alpha.png", "PNG")

    metadata = {
        **source_meta,
        "layout": "2x2",
        "canvas_size": [canvas_w, canvas_h],
        "grid_size": [grid_w, grid_h],
        "cell_size": [cell_w, cell_h],
        "gap_px": gap_px,
        "cell_padding_px": cell_padding_px,
        "outer_margin_px": outer_margin_px,
        "max_aspect": max_aspect,
        "background_rgb": list(background_rgb),
        "tile_boxes": tile_boxes,
        "content_boxes": content_boxes,
        "slot_order": [0, 1, 2, 3],
    }

    if debug_dir is not None:
        (debug_dir / "07_layout.json").write_text(
            __import__("json").dumps(metadata, indent=2, ensure_ascii=False),
            encoding="utf-8",
        )

    return alpha_canvas, metadata


def _scale_box_list(box, sx: float, sy: float) -> Box:
    return Box(
        int(np.floor(box[0] * sx)),
        int(np.floor(box[1] * sy)),
        int(np.ceil(box[2] * sx)),
        int(np.ceil(box[3] * sy)),
    )


def split_upscaled_grid(
    upscaled_path: Path,
    source_grid_alpha: Image.Image,
    layout: dict,
    output_dir: Path,
    order: Sequence[str],
    padding_px: int = 100,
    guard_px: int = 8,
    alpha_dilate_px: int = 0,
    debug_dir: Path | None = None,
) -> Dict[str, Path]:
    """
    Deterministically split a Leonardo-upscaled 2x2 composite using the known
    cell layout. No re-detection of "four columns" happens after upscaling.

    `order` still labels the ORIGINAL physical views 0..3, so existing
    view_orders.json remains valid.

    Duplicate semantic labels are omitted after their first occurrence.
    """
    if len(order) != 4:
        raise ValueError("Exactly four source labels are required.")

    tile_boxes = layout.get("tile_boxes")
    if not isinstance(tile_boxes, list) or len(tile_boxes) != 4:
        raise RuntimeError("Invalid 2x2 layout metadata: expected four tile_boxes.")

    output_dir.mkdir(parents=True, exist_ok=True)
    for stale in ("front.png", "back.png", "right.png", "left.png"):
        p = output_dir / stale
        if p.exists():
            p.unlink()

    upscaled = Image.open(upscaled_path).convert("RGB")
    uw, uh = upscaled.size
    sw, sh = source_grid_alpha.size
    sx, sy = uw / sw, uh / sh

    alpha = source_grid_alpha.resize((uw, uh), Image.Resampling.LANCZOS)
    alpha_arr = np.asarray(alpha, dtype=np.uint8)

    if alpha_dilate_px > 0:
        k = alpha_dilate_px * 2 + 1
        alpha_arr = cv2.dilate(
            alpha_arr, np.ones((k, k), np.uint8), iterations=1
        )

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)
        # Exact Leonardo image as read by the splitter (lossless debug copy).
        upscaled.save(debug_dir / "00_upscaled_as_read.png", "PNG")
        Image.fromarray(alpha_arr, mode="L").save(
            debug_dir / "01_scaled_original_alpha.png", "PNG"
        )

    results: Dict[str, Path] = {}
    seen = set()

    for i, label in enumerate(order):
        if label in seen:
            continue
        seen.add(label)

        tile = _scale_box_list(tile_boxes[i], sx, sy)

        # Restrict alpha to THIS cell only. The central gap means no neighbour
        # can leak in even if Leonardo hallucinates a little around an edge.
        tile_alpha_arr = alpha_arr[tile.y0:tile.y1, tile.x0:tile.x1]
        ys, xs = np.where(tile_alpha_arr > 4)
        if len(xs) == 0:
            raise RuntimeError(f"No foreground found in upscaled grid cell {i}.")

        x0 = tile.x0 + max(0, int(xs.min()) - guard_px)
        y0 = tile.y0 + max(0, int(ys.min()) - guard_px)
        x1 = tile.x0 + min(tile.width, int(xs.max()) + 1 + guard_px)
        y1 = tile.y0 + min(tile.height, int(ys.max()) + 1 + guard_px)

        raw_rgb = upscaled.crop((x0, y0, x1, y1)).convert("RGB")
        alpha_crop = Image.fromarray(
            alpha_arr[y0:y1, x0:x1], mode="L"
        )
        rgb_crop = raw_rgb.convert("RGBA")
        rgb_crop.putalpha(alpha_crop)

        if debug_dir is not None:
            raw_rgb.save(
                debug_dir / f"02_cell_{i}_{label}_raw_from_leonardo.jpg",
                "JPEG",
                quality=97,
            )
            alpha_crop.save(
                debug_dir / f"03_cell_{i}_{label}_mask.png",
                "PNG",
            )
            rgb_crop.save(
                debug_dir / f"04_cell_{i}_{label}_after_alpha.png",
                "PNG",
                optimize=True,
            )

        canvas = Image.new(
            "RGBA",
            (
                rgb_crop.width + 2 * padding_px,
                rgb_crop.height + 2 * padding_px,
            ),
            (0, 0, 0, 0),
        )
        canvas.alpha_composite(rgb_crop, dest=(padding_px, padding_px))

        out = output_dir / f"{label}.png"
        canvas.save(out, "PNG", optimize=True)
        if debug_dir is not None:
            canvas.save(
                debug_dir / f"05_final_{label}_with_padding.png",
                "PNG",
                optimize=True,
            )
        results[label] = out

    if "front" not in results:
        raise RuntimeError("No front.png was produced.")
    if len(results) < 2:
        raise RuntimeError("Meshy needs at least two useful views.")

    return results


# Backward-compatible aliases retained for old scripts/tests, but V7 producer
# uses only prepare_balanced_2x2 + split_upscaled_grid.
def make_global_crop(source: Path, destination: Path, margin_px: int = 32):
    img = _rgba(source)
    alpha = source_alpha(img)
    b = _bbox(alpha)
    w, h = img.size
    x0 = max(0, b.x0 - margin_px)
    y0 = max(0, b.y0 - margin_px)
    x1 = min(w, b.x1 + margin_px)
    y1 = min(h, b.y1 + margin_px)
    destination.parent.mkdir(parents=True, exist_ok=True)
    crop = img.crop((x0, y0, x1, y1))
    crop.save(destination, "PNG", optimize=True)
    return alpha.crop((x0, y0, x1, y1)), {
        "source_size": [w, h],
        "crop_box": [x0, y0, x1, y1],
        "cropped_size": [x1 - x0, y1 - y0],
    }


def split_upscaled(*args, **kwargs):
    raise RuntimeError(
        "V7 no longer uses the old 1x4 split. Use split_upscaled_grid()."
    )


# =====================================================================
# V12 CHROMA TEST
# =====================================================================

CHROMA_CANDIDATES_RGB = [
    (255, 0, 255),   # magenta
    (0, 255, 0),     # green
    (0, 255, 255),   # cyan
    (0, 64, 255),    # saturated blue
    (255, 96, 0),    # orange
    (255, 255, 0),   # yellow
]


def _rgb_pixels_to_lab(pixels_rgb: np.ndarray) -> np.ndarray:
    arr = np.asarray(pixels_rgb, dtype=np.uint8).reshape(-1, 1, 3)
    return cv2.cvtColor(arr, cv2.COLOR_RGB2LAB).reshape(-1, 3).astype(np.float32)


def choose_safe_chroma(
    views: Sequence[Image.Image],
    masks: Sequence[Image.Image],
) -> tuple[tuple[int, int, int], dict]:
    """
    Pick a saturated chroma colour that is far from the source foreground.

    Foreground membership comes from the source masks, NOT from colour.
    The score is the 1st-percentile Lab distance to garment pixels, so a
    candidate is rejected if even a small meaningful part of the garment is
    close to that chroma.
    """
    samples = []

    for view, mask in zip(views, masks):
        rgb = np.asarray(view.convert("RGB"), dtype=np.uint8)
        a = np.asarray(mask.convert("L"), dtype=np.uint8)

        ys, xs = np.where(a >= 128)
        if len(xs) == 0:
            continue

        step = max(1, len(xs) // 15000)
        pix = rgb[ys[::step], xs[::step]]
        samples.append(pix)

    if not samples:
        return (255, 0, 255), {"reason": "fallback_no_foreground_samples"}

    fg_rgb = np.concatenate(samples, axis=0)
    fg_lab = _rgb_pixels_to_lab(fg_rgb)

    scored = []

    for cand in CHROMA_CANDIDATES_RGB:
        cand_lab = _rgb_pixels_to_lab(np.array([cand], dtype=np.uint8))[0]
        dist = np.sqrt(
            np.sum((fg_lab - cand_lab[None, :]) ** 2, axis=1)
        )

        score = float(np.percentile(dist, 1.0))
        median = float(np.median(dist))

        scored.append(
            {
                "rgb": list(cand),
                "score_p01_lab": score,
                "median_lab": median,
            }
        )

    scored.sort(key=lambda x: x["score_p01_lab"], reverse=True)
    chosen = tuple(scored[0]["rgb"])

    return chosen, {
        "chosen_rgb": list(chosen),
        "candidates": scored,
    }


def prepare_balanced_2x2_chroma(
    source: Path,
    destination: Path,
    island_margin_px: int = 24,
    cell_padding_px: int = 48,
    gap_px: int = 96,
    outer_margin_px: int = 32,
    max_aspect: float = 1.50,
    debug_dir: Path | None = None,
) -> dict:
    """
    Repack the four detected source views into the same roomy 2x2 layout, but
    on an automatically selected saturated chroma background.
    """
    views, masks, source_meta = _extract_isolated_views(
        source,
        island_margin_px=island_margin_px,
    )

    chroma_rgb, chroma_analysis = choose_safe_chroma(views, masks)

    max_w = max(v.width for v in views)
    max_h = max(v.height for v in views)

    cell_w = max_w + 2 * cell_padding_px
    cell_h = max_h + 2 * cell_padding_px

    grid_w = 2 * cell_w + gap_px
    grid_h = 2 * cell_h + gap_px

    base_w = grid_w + 2 * outer_margin_px
    base_h = grid_h + 2 * outer_margin_px

    canvas_w, canvas_h = base_w, base_h

    if canvas_w / canvas_h > max_aspect:
        canvas_h = int(np.ceil(canvas_w / max_aspect))
    elif canvas_h / canvas_w > max_aspect:
        canvas_w = int(np.ceil(canvas_h / max_aspect))

    balance_x = (canvas_w - base_w) // 2
    balance_y = (canvas_h - base_h) // 2

    grid_origin_x = balance_x + outer_margin_px
    grid_origin_y = balance_y + outer_margin_px

    canvas = Image.new("RGB", (canvas_w, canvas_h), chroma_rgb)

    slots = [
        (0, 0),
        (1, 0),
        (0, 1),
        (1, 1),
    ]

    tile_boxes = []
    content_boxes = []

    for i, (col, row) in enumerate(slots):
        cell_x0 = grid_origin_x + col * (cell_w + gap_px)
        cell_y0 = grid_origin_y + row * (cell_h + gap_px)
        cell_x1 = cell_x0 + cell_w
        cell_y1 = cell_y0 + cell_h

        view = views[i]
        mask = masks[i]

        px = cell_x0 + (cell_w - view.width) // 2
        py = cell_y0 + (cell_h - view.height) // 2

        canvas.paste(view.convert("RGB"), (px, py), mask)

        tile_boxes.append([cell_x0, cell_y0, cell_x1, cell_y1])
        content_boxes.append([px, py, px + view.width, py + view.height])

    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, "PNG", optimize=True)

    metadata = {
        **source_meta,
        "layout": "2x2_chroma",
        "canvas_size": [canvas_w, canvas_h],
        "grid_size": [grid_w, grid_h],
        "cell_size": [cell_w, cell_h],
        "gap_px": gap_px,
        "cell_padding_px": cell_padding_px,
        "outer_margin_px": outer_margin_px,
        "max_aspect": max_aspect,
        "tile_boxes": tile_boxes,
        "content_boxes": content_boxes,
        "slot_order": [0, 1, 2, 3],
        "chroma_rgb": list(chroma_rgb),
        "chroma_analysis": chroma_analysis,
    }

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)

        _rgba(source).save(
            debug_dir / "00_source_snapshot.png",
            "PNG",
            optimize=True,
        )

        for i, (view, mask) in enumerate(zip(views, masks)):
            view.save(
                debug_dir / f"01_isolated_view_{i}.png",
                "PNG",
                optimize=True,
            )
            mask.save(
                debug_dir / f"02_isolated_mask_{i}.png",
                "PNG",
            )

        canvas.save(
            debug_dir / "03_LEONARDO_INPUT_CHROMA.png",
            "PNG",
            optimize=True,
        )

        (debug_dir / "04_chroma_layout.json").write_text(
            __import__("json").dumps(
                metadata,
                indent=2,
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )

    return metadata


def _estimate_actual_chroma_from_border(
    tile_rgb: np.ndarray,
    nominal_chroma_rgb: tuple[int, int, int],
) -> tuple[tuple[int, int, int], dict]:
    """
    Leonardo/JPEG may slightly change the nominal chroma.
    Estimate the ACTUAL matte colour from the outer cell border, selecting only
    border pixels reasonably close to the nominal chroma.
    """
    h, w, _ = tile_rgb.shape
    bw = max(10, int(round(min(h, w) * 0.04)))
    bw = min(bw, max(10, min(h, w) // 5))

    border = np.concatenate(
        [
            tile_rgb[:bw, :, :].reshape(-1, 3),
            tile_rgb[-bw:, :, :].reshape(-1, 3),
            tile_rgb[:, :bw, :].reshape(-1, 3),
            tile_rgb[:, -bw:, :].reshape(-1, 3),
        ],
        axis=0,
    )

    border_lab = _rgb_pixels_to_lab(border)
    nominal_lab = _rgb_pixels_to_lab(
        np.array([nominal_chroma_rgb], dtype=np.uint8)
    )[0]

    d = np.sqrt(
        np.sum((border_lab - nominal_lab[None, :]) ** 2, axis=1)
    )

    # Keep the border cluster closest to the intended chroma.
    cutoff = max(18.0, float(np.percentile(d, 60.0)))
    keep = d <= cutoff

    if int(np.count_nonzero(keep)) < 100:
        keep = np.ones(len(border), dtype=bool)

    actual_rgb = np.median(
        border[keep].astype(np.float32),
        axis=0,
    )
    actual_rgb = np.clip(
        np.round(actual_rgb),
        0,
        255,
    ).astype(np.uint8)

    return tuple(int(x) for x in actual_rgb), {
        "nominal_chroma_rgb": list(nominal_chroma_rgb),
        "estimated_actual_chroma_rgb": [int(x) for x in actual_rgb],
        "border_width_px": int(bw),
        "border_samples_used": int(np.count_nonzero(keep)),
    }


def _chroma_distance_lab(
    rgb: np.ndarray,
    chroma_rgb: tuple[int, int, int],
) -> np.ndarray:
    lab = cv2.cvtColor(
        np.asarray(rgb, dtype=np.uint8),
        cv2.COLOR_RGB2LAB,
    ).astype(np.float32)

    c_lab = _rgb_pixels_to_lab(
        np.array([chroma_rgb], dtype=np.uint8)
    )[0]

    return np.sqrt(
        np.sum((lab - c_lab[None, None, :]) ** 2, axis=2)
    )


def chroma_key_actual_output(
    tile_rgb: np.ndarray,
    nominal_chroma_rgb: tuple[int, int, int],
    transparent_distance_lab: float = 16.0,
    opaque_distance_lab: float = 38.0,
    edge_shrink_px: float = 0.55,
    edge_feather_px: float = 1.35,
) -> tuple[np.ndarray, np.ndarray, dict]:
    """
    Key the actual Leonardo output.

    Important properties:
    - No assumption that white/black/grey means background.
    - Enclosed holes work naturally because they still contain chroma.
    - The actual chroma is estimated from Leonardo's own output border.
    - A tiny inward edge refinement removes matte contamination.
    """
    rgb = np.asarray(tile_rgb, dtype=np.uint8)

    actual_chroma, diag = _estimate_actual_chroma_from_border(
        rgb,
        nominal_chroma_rgb,
    )

    nominal_lab = _rgb_pixels_to_lab(
        np.array([nominal_chroma_rgb], dtype=np.uint8)
    )[0]
    actual_lab = _rgb_pixels_to_lab(
        np.array([actual_chroma], dtype=np.uint8)
    )[0]
    nominal_actual_distance = float(
        np.sqrt(np.sum((nominal_lab - actual_lab) ** 2))
    )
    diag["nominal_actual_chroma_distance_lab"] = nominal_actual_distance

    if nominal_actual_distance > 48.0:
        raise RuntimeError(
            "Aquest Upscaled no sembla haver-se generat amb el chroma actual "
            f"(distancia Lab {nominal_actual_distance:.1f}). "
            "Si es un checkpoint antic gris/negre, elimina Upscaled/<peca> "
            "i Upscaled_and_Cut/<peca> i torna a executar."
        )

    dist = _chroma_distance_lab(rgb, actual_chroma)

    alpha_f = (
        dist - float(transparent_distance_lab)
    ) / max(
        float(opaque_distance_lab - transparent_distance_lab),
        1e-6,
    )
    alpha_f = np.clip(alpha_f, 0.0, 1.0)

    # Preserve all colour-derived holes; only refine the foreground boundary.
    hard_fg = (alpha_f >= 0.50).astype(np.uint8)

    if np.any(hard_fg):
        d_inside = cv2.distanceTransform(
            hard_fg,
            cv2.DIST_L2,
            5,
        )

        inward = (
            d_inside - float(edge_shrink_px)
        ) / max(float(edge_feather_px), 1e-6)

        inward = np.clip(inward, 0.0, 1.0)
        alpha_f = np.minimum(alpha_f, inward)

    alpha = np.round(alpha_f * 255.0).astype(np.uint8)
    alpha[alpha < 6] = 0
    alpha[alpha > 249] = 255

    # --------------------------------------------------------------
    # Conservative despill.
    # Do not alter opaque interior pixels. Only boundary pixels whose
    # colour is partially mixed with chroma are corrected.
    # --------------------------------------------------------------
    out_rgb = rgb.astype(np.float32).copy()

    transition = (alpha > 0) & (alpha < 250)

    if np.any(transition):
        interior = (alpha >= 250).astype(np.uint8)

        helper = out_rgb.copy()
        known = interior.copy()

        kernel = np.ones((3, 3), dtype=np.float32)

        # Propagate nearby opaque subject colour a few pixels outward.
        for _ in range(8):
            target = transition & (known == 0)
            if not np.any(target):
                break

            k = known.astype(np.float32)

            counts = cv2.filter2D(
                k,
                -1,
                kernel,
                borderType=cv2.BORDER_REPLICATE,
            )

            candidate = np.zeros_like(helper)

            for c in range(3):
                sums = cv2.filter2D(
                    helper[..., c] * k,
                    -1,
                    kernel,
                    borderType=cv2.BORDER_REPLICATE,
                )

                candidate[..., c] = np.where(
                    counts > 0,
                    sums / np.maximum(counts, 1e-6),
                    0,
                )

            fill = target & (counts > 0)

            helper[fill] = candidate[fill]
            known[fill] = 1

        # Stronger replacement where alpha is low.
        w = (
            1.0 - alpha.astype(np.float32) / 255.0
        ) * 0.75

        w = np.where(
            transition,
            w,
            0.0,
        )[..., None]

        out_rgb = (
            out_rgb * (1.0 - w)
            + helper * w
        )

    out_rgb = np.clip(
        np.round(out_rgb),
        0,
        255,
    ).astype(np.uint8)

    diag.update(
        {
            "transparent_distance_lab": float(transparent_distance_lab),
            "opaque_distance_lab": float(opaque_distance_lab),
            "edge_shrink_px": float(edge_shrink_px),
            "edge_feather_px": float(edge_feather_px),
        }
    )

    return alpha, out_rgb, diag


def split_upscaled_grid_chroma(
    upscaled_path: Path,
    layout: dict,
    output_dir: Path,
    order: Sequence[str],
    padding_px: int = 100,
    guard_px: int = 10,
    debug_dir: Path | None = None,
) -> Dict[str, Path]:
    """
    Split known 2x2 cells and key the actual chroma in Leonardo's output.
    """
    if len(order) != 4:
        raise ValueError("Exactly four source labels are required.")

    tile_boxes = layout.get("tile_boxes")
    canvas_size = layout.get("canvas_size")
    chroma_raw = layout.get("chroma_rgb")

    if not isinstance(tile_boxes, list) or len(tile_boxes) != 4:
        raise RuntimeError("Invalid chroma layout: four tile_boxes required.")

    if not isinstance(canvas_size, list) or len(canvas_size) != 2:
        raise RuntimeError("Invalid chroma layout: canvas_size missing.")

    if not isinstance(chroma_raw, list) or len(chroma_raw) != 3:
        raise RuntimeError("Invalid chroma layout: chroma_rgb missing.")

    chroma_rgb = tuple(int(x) for x in chroma_raw)

    output_dir.mkdir(parents=True, exist_ok=True)

    for stale in ("front.png", "back.png", "right.png", "left.png"):
        p = output_dir / stale
        if p.exists():
            p.unlink()

    upscaled = Image.open(upscaled_path).convert("RGB")
    up_arr = np.asarray(upscaled, dtype=np.uint8)

    uw, uh = upscaled.size
    sw, sh = canvas_size

    sx = uw / sw
    sy = uh / sh

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)
        upscaled.save(
            debug_dir / "00_UPSCALED_RAW_AS_READ.png",
            "PNG",
        )

    results: Dict[str, Path] = {}
    diagnostics = []
    seen = set()

    for i, label in enumerate(order):
        if label in seen:
            continue
        seen.add(label)

        tile = _scale_box_list(
            tile_boxes[i],
            sx,
            sy,
        )

        tile_arr = up_arr[
            tile.y0:tile.y1,
            tile.x0:tile.x1,
            :
        ]

        alpha, clean_rgb, diag = chroma_key_actual_output(
            tile_arr,
            chroma_rgb,
        )

        ys, xs = np.where(alpha > 8)

        if len(xs) == 0:
            raise RuntimeError(
                f"No foreground after chroma key in cell {i}."
            )

        lx0 = max(0, int(xs.min()) - guard_px)
        ly0 = max(0, int(ys.min()) - guard_px)
        lx1 = min(
            tile.width,
            int(xs.max()) + 1 + guard_px,
        )
        ly1 = min(
            tile.height,
            int(ys.max()) + 1 + guard_px,
        )

        crop_rgb = clean_rgb[
            ly0:ly1,
            lx0:lx1,
        ]
        crop_alpha = alpha[
            ly0:ly1,
            lx0:lx1,
        ]

        rgba = Image.fromarray(
            crop_rgb,
            mode="RGB",
        ).convert("RGBA")

        rgba.putalpha(
            Image.fromarray(
                crop_alpha,
                mode="L",
            )
        )

        canvas = Image.new(
            "RGBA",
            (
                rgba.width + 2 * padding_px,
                rgba.height + 2 * padding_px,
            ),
            (0, 0, 0, 0),
        )

        canvas.alpha_composite(
            rgba,
            dest=(padding_px, padding_px),
        )

        out = output_dir / f"{label}.png"
        canvas.save(
            out,
            "PNG",
            optimize=True,
        )

        results[label] = out

        diagnostics.append(
            {
                "cell_index": i,
                "semantic_label": label,
                "tile_box_upscaled": [
                    tile.x0,
                    tile.y0,
                    tile.x1,
                    tile.y1,
                ],
                "crop_box_in_tile": [
                    lx0,
                    ly0,
                    lx1,
                    ly1,
                ],
                **diag,
            }
        )

        if debug_dir is not None:
            Image.fromarray(
                tile_arr,
                mode="RGB",
            ).save(
                debug_dir / f"10_{i}_{label}_CELL_RAW.png",
                "PNG",
            )

            actual_chroma = tuple(
                diag["estimated_actual_chroma_rgb"]
            )

            dist = _chroma_distance_lab(
                tile_arr,
                actual_chroma,
            )

            dvis = np.clip(
                dist / max(
                    diag["opaque_distance_lab"],
                    1e-6,
                ),
                0,
                1,
            )

            dvis = np.round(
                dvis * 255.0
            ).astype(np.uint8)

            Image.fromarray(
                dvis,
                mode="L",
            ).save(
                debug_dir / f"11_{i}_{label}_CHROMA_DISTANCE.png",
                "PNG",
            )

            Image.fromarray(
                alpha,
                mode="L",
            ).save(
                debug_dir / f"12_{i}_{label}_ALPHA_FINAL.png",
                "PNG",
            )

            Image.fromarray(
                clean_rgb,
                mode="RGB",
            ).save(
                debug_dir / f"13_{i}_{label}_DESPILLED.png",
                "PNG",
            )

            canvas.save(
                debug_dir / f"14_FINAL_{label}.png",
                "PNG",
                optimize=True,
            )

    if "front" not in results:
        raise RuntimeError("No front.png produced.")

    if debug_dir is not None:
        (debug_dir / "20_CHROMA_DIAGNOSTICS.json").write_text(
            __import__("json").dumps(
                {
                    "upscaled": str(upscaled_path),
                    "upscaled_size": [uw, uh],
                    "layout_canvas_size": [sw, sh],
                    "scale_xy": [sx, sy],
                    "nominal_chroma_rgb": list(chroma_rgb),
                    "cells": diagnostics,
                },
                indent=2,
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )

    return results


# =====================================================================
# V18 SIMPLE CHROMA SPLIT
# =====================================================================

def split_upscaled_grid_keep_chroma(
    upscaled_path: Path,
    layout: dict,
    output_dir: Path,
    order: Sequence[str],
    debug_dir: Path | None = None,
) -> Dict[str, Path]:
    """
    Split the Leonardo-upscaled 2x2 composite into the four known cell
    rectangles and KEEP the chroma background.

    No segmentation.
    No alpha.
    No mask alignment.
    No despill.
    No colour-based crop.

    Each semantic view is simply the exact corresponding upscaled cell.
    This preserves:
      - consistent scale across views,
      - consistent chroma background,
      - zero risk of eating white/pale/interior garment pixels.
    """
    if len(order) != 4:
        raise ValueError("Exactly four source labels are required.")

    tile_boxes = layout.get("tile_boxes")
    canvas_size = layout.get("canvas_size")

    if not isinstance(tile_boxes, list) or len(tile_boxes) != 4:
        raise RuntimeError("Invalid layout: expected four tile_boxes.")

    if not isinstance(canvas_size, list) or len(canvas_size) != 2:
        raise RuntimeError("Invalid layout: missing canvas_size.")

    output_dir.mkdir(parents=True, exist_ok=True)

    # Remove stale semantic views before recreating them.
    for stale in ("front.png", "back.png", "right.png", "left.png"):
        p = output_dir / stale
        if p.exists():
            p.unlink()

    upscaled = Image.open(upscaled_path).convert("RGB")
    uw, uh = upscaled.size
    sw, sh = canvas_size

    sx = uw / sw
    sy = uh / sh

    if debug_dir is not None:
        debug_dir.mkdir(parents=True, exist_ok=True)
        upscaled.save(
            debug_dir / "00_UPSCALED_RAW_AS_READ.png",
            "PNG",
        )

    results: Dict[str, Path] = {}
    seen = set()
    diagnostics = []

    for i, label in enumerate(order):
        # Duplicate semantic labels in view_orders.json are intentionally
        # emitted only once.
        if label in seen:
            continue
        seen.add(label)

        tile = _scale_box_list(
            tile_boxes[i],
            sx,
            sy,
        )

        crop = upscaled.crop(
            (
                tile.x0,
                tile.y0,
                tile.x1,
                tile.y1,
            )
        ).convert("RGB")

        out = output_dir / f"{label}.png"
        crop.save(
            out,
            "PNG",
            optimize=True,
        )

        results[label] = out

        diagnostics.append(
            {
                "cell_index": i,
                "semantic_label": label,
                "tile_box_source": tile_boxes[i],
                "tile_box_upscaled": [
                    tile.x0,
                    tile.y0,
                    tile.x1,
                    tile.y1,
                ],
                "output_size": [crop.width, crop.height],
            }
        )

        if debug_dir is not None:
            crop.save(
                debug_dir / f"10_FINAL_{i}_{label}_WITH_CHROMA.png",
                "PNG",
                optimize=True,
            )

    if "front" not in results:
        raise RuntimeError("No front.png was produced.")

    if debug_dir is not None:
        (debug_dir / "20_RECTANGULAR_SPLIT.json").write_text(
            __import__("json").dumps(
                {
                    "upscaled": str(upscaled_path),
                    "upscaled_size": [uw, uh],
                    "layout_canvas_size": [sw, sh],
                    "scale_xy": [sx, sy],
                    "chroma_rgb": layout.get("chroma_rgb"),
                    "cells": diagnostics,
                    "alpha_used": False,
                    "chroma_removed": False,
                },
                indent=2,
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )

    return results
