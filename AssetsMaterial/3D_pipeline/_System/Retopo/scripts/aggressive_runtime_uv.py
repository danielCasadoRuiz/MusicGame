"""Aggressive runtime-only UV generation.

Goals:
- No human-editability constraint and no island-count hard limit.
- Create many simple charts when useful.
- UV area should track 3D surface area (uniform texel density).
- Maximize atlas occupancy while retaining:
    * finite UVs
    * 0 degenerate UV triangles
    * 0 positive-area overlaps
    * acceptable texel-density uniformity

Every candidate starts from the exact same triangulated geometry checkpoint.
"""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

from blender_inspect import activate
from uv_seams import uv_audit
from atlas_pack import pack_object

ap = argparse.ArgumentParser()
ap.add_argument("--checkpoint", type=Path, required=True)
ap.add_argument("--diagnostics", type=Path, required=True)
ap.add_argument("--resolution", type=int, default=4096)
ap.add_argument("--angles", default="70,55,40,30,20")
ap.add_argument("--margins", default="12,16,24")
ap.add_argument("--min-occupancy", type=float, default=0.72)
ap.add_argument("--preferred-occupancy", type=float, default=0.82)
ap.add_argument("--max-density-p90-p10", type=float, default=1.50)
ap.add_argument("--max-density-cv", type=float, default=0.30)
a = ap.parse_args(sys.argv[sys.argv.index("--") + 1 :])

angles = [float(x.strip()) for x in a.angles.split(",") if x.strip()]
margins = [int(x.strip()) for x in a.margins.split(",") if x.strip()]

if not angles:
    raise RuntimeError("No aggressive UV angles configured.")
if not margins:
    raise RuntimeError("No aggressive UV margins configured.")

report_path = a.diagnostics / (a.checkpoint.parent.name + ".uv.json")
existing = {}
if report_path.exists():
    try:
        existing = json.loads(report_path.read_text(encoding="utf-8"))
    except Exception:
        existing = {}

base = a.diagnostics / "UV_AGGRESSIVE_BASE_TRIANGULATED.blend"
shutil.copy2(a.checkpoint, base)


def load_base():
    bpy.ops.wm.open_mainfile(filepath=str(base))
    obj = bpy.data.objects["HIGH"]
    activate(obj)
    return obj


def geometry_uv_density(obj):
    """Measure texel density from UV triangle area vs 3D triangle area.

    A perfectly uniform atlas has a constant sqrt(UV_area / 3D_area).
    Percentiles are area-weighted so tiny decorative triangles cannot
    dominate the quality gate.
    """
    mesh = obj.data
    mesh.calc_loop_triangles()
    layer = mesh.uv_layers.active
    if layer is None:
        raise RuntimeError("No active UV layer.")

    values = []
    weights = []

    for tri in mesh.loop_triangles:
        p = [np.array(mesh.vertices[v].co[:], dtype=np.float64) for v in tri.vertices]
        area3 = 0.5 * float(np.linalg.norm(np.cross(p[1] - p[0], p[2] - p[0])))
        if area3 <= 1e-20:
            continue

        uv = [np.array(layer.data[i].uv[:], dtype=np.float64) for i in tri.loops]
        area_uv = 0.5 * abs(float(np.cross(uv[1] - uv[0], uv[2] - uv[0])))
        if area_uv <= 1e-20:
            density = 0.0
        else:
            density = math.sqrt(area_uv / area3)

        values.append(density)
        weights.append(area3)

    values = np.asarray(values, dtype=np.float64)
    weights = np.asarray(weights, dtype=np.float64)

    good = np.isfinite(values) & (values > 0) & np.isfinite(weights) & (weights > 0)
    values = values[good]
    weights = weights[good]

    if len(values) == 0:
        raise RuntimeError("No valid density samples.")

    order = np.argsort(values)
    sv = values[order]
    sw = weights[order]
    cdf = np.cumsum(sw)
    cdf /= cdf[-1]

    def wpercentile(q):
        idx = int(np.searchsorted(cdf, q, side="left"))
        idx = min(max(idx, 0), len(sv) - 1)
        return float(sv[idx])

    p10 = wpercentile(0.10)
    p50 = wpercentile(0.50)
    p90 = wpercentile(0.90)
    ratio = p90 / max(p10, 1e-30)

    mean = float(np.average(values, weights=weights))
    variance = float(np.average((values - mean) ** 2, weights=weights))
    cv = math.sqrt(max(variance, 0.0)) / max(mean, 1e-30)

    return {
        "samples": int(len(values)),
        "weighted_p10": p10,
        "weighted_median": p50,
        "weighted_p90": p90,
        "p90_p10_ratio": ratio,
        "weighted_cv": cv,
        "passed": (
            ratio <= a.max_density_p90_p10
            and cv <= a.max_density_cv
        ),
    }


def uv_health(audit):
    return (
        audit["overlap_audit_complete"]
        and audit["finite"]
        and min(audit["min"]) >= -1e-7
        and max(audit["max"]) <= 1.0000001
        and audit["positive_area_overlap_pairs"] == 0
        and audit["degenerate_uv_triangles"] == 0
    )


def prepare_smart_uv(obj, angle_deg):
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")

    # Clear any previous UV and generate machine-oriented charts.
    bpy.ops.uv.smart_project(
        angle_limit=math.radians(angle_deg),
        island_margin=0.0,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )

    # Critical: make UV scale reflect 3D surface scale across islands.
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")


def pack_candidate(obj, packer, margin):
    activate(obj)

    if packer == "MAXRECTS":
        packing = pack_object(obj, a.resolution, margin)
        packing["requested_packer"] = packer
        packing["margin_pixels"] = margin
        return packing

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(
        rotate=True,
        margin_method="FRACTION",
        margin=margin / a.resolution,
        shape_method="CONCAVE",
        scale=True,
    )
    bpy.ops.object.mode_set(mode="OBJECT")

    return {
        "method": "BLENDER_CONCAVE",
        "requested_packer": packer,
        "margin_pixels": margin,
    }


attempts = []
best = None
attempt_id = 0

for angle in angles:
    for packer in ("MAXRECTS", "BLENDER"):
        for margin in margins:
            attempt_id += 1
            obj = load_base()

            try:
                prepare_smart_uv(obj, angle)
                packing = pack_candidate(obj, packer, margin)

                audit = uv_audit(obj)
                density = geometry_uv_density(obj)
                health = uv_health(audit)
                occupancy = float(audit["uv_triangle_area_sum"])

                passed = (
                    health
                    and density["passed"]
                    and occupancy >= a.min_occupancy
                )

                path = a.diagnostics / (
                    f"UV_AGGRESSIVE_ATTEMPT_{attempt_id:02d}_"
                    f"A{int(angle)}_{packer}_{margin}px.blend"
                )
                bpy.ops.wm.save_as_mainfile(filepath=str(path))

                row = {
                    "attempt": attempt_id,
                    "angle_degrees": angle,
                    "packer": packer,
                    "margin_pixels": margin,
                    "passed": passed,
                    "uv_health": health,
                    "occupancy": occupancy,
                    "islands": audit["islands"]["count"],
                    "degenerate_uv_triangles": audit["degenerate_uv_triangles"],
                    "overlaps": audit["positive_area_overlap_pairs"],
                    "density_p90_p10": density["p90_p10_ratio"],
                    "density_cv": density["weighted_cv"],
                    "checkpoint": str(path),
                }
                attempts.append(row)

                print("=== AGGRESSIVE UV ATTEMPT ===", json.dumps(row), flush=True)

                if passed:
                    # Preferred target: first reward occupancy up to the preferred
                    # region, then density. Beyond preferred occupancy, density wins.
                    occupancy_penalty = max(0.0, a.preferred_occupancy - occupancy)
                    score = (
                        occupancy_penalty,
                        density["p90_p10_ratio"],
                        density["weighted_cv"],
                        -occupancy,
                        audit["islands"]["count"],
                    )

                    if best is None or score < best["score"]:
                        best = {
                            "score": score,
                            "path": path,
                            "audit": audit,
                            "density": density,
                            "packing": packing,
                            "row": row,
                        }

            except Exception as exc:
                row = {
                    "attempt": attempt_id,
                    "angle_degrees": angle,
                    "packer": packer,
                    "margin_pixels": margin,
                    "passed": False,
                    "error": f"{type(exc).__name__}: {exc}",
                }
                attempts.append(row)
                print("=== AGGRESSIVE UV ATTEMPT ERROR ===", json.dumps(row), flush=True)

if best is None:
    summary = {
        "passed": False,
        "attempts": attempts,
        "min_occupancy": a.min_occupancy,
        "preferred_occupancy": a.preferred_occupancy,
        "max_density_p90_p10": a.max_density_p90_p10,
        "max_density_cv": a.max_density_cv,
    }
    (a.diagnostics / "AGGRESSIVE_UV_SUMMARY.json").write_text(
        json.dumps(summary, indent=2),
        encoding="utf-8",
    )
    raise RuntimeError(
        "Aggressive runtime UV: no candidate passed health + density + occupancy gates."
    )

shutil.copy2(best["path"], a.checkpoint)

result = {
    **existing,
    "passed": True,
    "resolution": a.resolution,
    "mode": "AGGRESSIVE_RUNTIME",
    "human_editable": False,
    "uv_audits": [best["audit"]],
    "atlas_occupancy": best["audit"]["uv_triangle_area_sum"],
    "packing": best["packing"],
    "uv_density_metrics": best["density"],
    "density_gate_passed": True,
    "aggressive_uv_attempts": attempts,
    "aggressive_uv_selected": best["row"],
    "min_occupancy": a.min_occupancy,
    "preferred_occupancy": a.preferred_occupancy,
}

report_path.write_text(
    json.dumps(result, indent=2, ensure_ascii=False),
    encoding="utf-8",
)

summary = {
    "passed": True,
    "selected": best["row"],
    "occupancy": best["audit"]["uv_triangle_area_sum"],
    "islands": best["audit"]["islands"]["count"],
    "degenerate_uv_triangles": best["audit"]["degenerate_uv_triangles"],
    "overlaps": best["audit"]["positive_area_overlap_pairs"],
    "density": best["density"],
}
(a.diagnostics / "AGGRESSIVE_UV_SUMMARY.json").write_text(
    json.dumps(summary, indent=2, ensure_ascii=False),
    encoding="utf-8",
)

print(
    "=== AGGRESSIVE UV FINAL OK ===",
    json.dumps(summary),
    flush=True,
)
