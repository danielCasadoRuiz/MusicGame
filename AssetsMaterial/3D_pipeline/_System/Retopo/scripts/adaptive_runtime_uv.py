"""Adaptive runtime UV generation.

Important policy:
- 80% occupancy is a TARGET, never a reason to kill an asset.
- Hard correctness is only UV health:
    finite, inside 0..1, 0 degenerate UV triangles, 0 positive-area overlaps.
- Try structured non-human unwraps first (few/larger charts), because they
  usually pack better than thousands of Smart-UV fragments.
- Then try moderate Smart UV angles only if useful.
- Texel density is measured against 3D triangle area and is a scoring metric.
- Select the best healthy candidate even if occupancy/density misses the ideal.
- If there is no healthy candidate, write passed=false and let the launcher
  invoke the conservative seam/repair fallback.
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
from uv_seams import (
    mark_seams,
    disk_seams,
    unwrap_pack,
    uv_audit,
    profile_for,
)
from atlas_pack import pack_object

ap = argparse.ArgumentParser()
ap.add_argument("--checkpoint", type=Path, required=True)
ap.add_argument("--diagnostics", type=Path, required=True)
ap.add_argument("--resolution", type=int, default=4096)
ap.add_argument("--gap", type=int, default=32)
ap.add_argument("--preferred-occupancy", type=float, default=0.80)
ap.add_argument("--excellent-occupancy", type=float, default=0.88)
ap.add_argument("--preferred-density-ratio", type=float, default=1.35)
ap.add_argument("--smart-angles", default="89,86,83,80,77,74")
a = ap.parse_args(sys.argv[sys.argv.index("--") + 1 :])

angles = [float(x.strip()) for x in a.smart_angles.split(",") if x.strip()]
report_path = a.diagnostics / (a.checkpoint.parent.name + ".uv.json")
base = a.diagnostics / "UV_ADAPTIVE_BASE_TRIANGULATED.blend"
shutil.copy2(a.checkpoint, base)


def load_base():
    bpy.ops.wm.open_mainfile(filepath=str(base))
    obj = bpy.data.objects["HIGH"]
    activate(obj)
    # Start from a fresh UV layer.
    for uv in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(uv)
    obj.data.uv_layers.new(name="UV_HIGH")
    return obj


def uv_health(audit):
    return (
        audit["overlap_audit_complete"]
        and audit["finite"]
        and min(audit["min"]) >= -1e-7
        and max(audit["max"]) <= 1.0000001
        and audit["positive_area_overlap_pairs"] == 0
        and audit["degenerate_uv_triangles"] == 0
    )


def density_metrics(obj):
    """Area-weighted texel density from UV triangle area / 3D triangle area."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    layer = mesh.uv_layers.active
    if layer is None:
        raise RuntimeError("No active UV layer.")

    vals = []
    weights = []
    for tri in mesh.loop_triangles:
        p = [np.array(mesh.vertices[v].co[:], dtype=np.float64) for v in tri.vertices]
        area3 = 0.5 * float(np.linalg.norm(np.cross(p[1]-p[0], p[2]-p[0])))
        if area3 <= 1e-20:
            continue
        uv = [np.array(layer.data[i].uv[:], dtype=np.float64) for i in tri.loops]
        area2 = 0.5 * abs(float(np.cross(uv[1]-uv[0], uv[2]-uv[0])))
        if area2 <= 1e-20:
            continue
        vals.append(math.sqrt(area2 / area3))
        weights.append(area3)

    vals = np.asarray(vals, dtype=np.float64)
    weights = np.asarray(weights, dtype=np.float64)
    good = np.isfinite(vals) & (vals > 0) & np.isfinite(weights) & (weights > 0)
    vals = vals[good]
    weights = weights[good]
    if len(vals) == 0:
        return {
            "samples": 0,
            "p90_p10_ratio": float("inf"),
            "weighted_cv": float("inf"),
        }

    order = np.argsort(vals)
    sv = vals[order]
    sw = weights[order]
    cdf = np.cumsum(sw)
    cdf /= cdf[-1]

    def wp(q):
        i = int(np.searchsorted(cdf, q, side="left"))
        i = min(max(i, 0), len(sv)-1)
        return float(sv[i])

    p10 = wp(0.10)
    p90 = wp(0.90)
    mean = float(np.average(vals, weights=weights))
    var = float(np.average((vals-mean)**2, weights=weights))
    cv = math.sqrt(max(var, 0.0)) / max(mean, 1e-30)

    return {
        "samples": int(len(vals)),
        "p90_p10_ratio": p90 / max(p10, 1e-30),
        "weighted_cv": cv,
    }


def normalize_density(obj):
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")


def blender_pack(obj, gap_px):
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(
        rotate=True,
        margin_method="FRACTION",
        margin=gap_px / a.resolution,
        shape_method="CONCAVE",
        scale=True,
    )
    bpy.ops.object.mode_set(mode="OBJECT")
    return {"method": "BLENDER_CONCAVE", "gap_px": gap_px}


def candidate_score(row):
    """Lower is better.

    Occupancy below preferred is penalized, but never invalidates a healthy UV.
    Density over preferred is also penalized, but is soft.
    """
    occ = row["occupancy"]
    density = row["density_p90_p10"]
    occupancy_penalty = max(0.0, a.preferred_occupancy - occ)
    density_penalty = max(0.0, density - a.preferred_density_ratio)

    return (
        occupancy_penalty * 8.0,
        density_penalty * 3.0,
        -occ,
        density,
        row["islands"],
    )


attempts = []
best = None
attempt_id = 0

def evaluate(obj, strategy, params, packing):
    global attempt_id, best
    attempt_id += 1
    audit = uv_audit(obj)
    density = density_metrics(obj)
    healthy = uv_health(audit)

    path = a.diagnostics / (
        f"UV_ADAPTIVE_ATTEMPT_{attempt_id:02d}_{strategy}.blend"
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(path))

    row = {
        "attempt": attempt_id,
        "strategy": strategy,
        **params,
        "healthy": healthy,
        "occupancy": float(audit["uv_triangle_area_sum"]),
        "islands": int(audit["islands"]["count"]),
        "degenerate_uv_triangles": int(audit["degenerate_uv_triangles"]),
        "overlaps": int(audit["positive_area_overlap_pairs"]),
        "density_p90_p10": float(density["p90_p10_ratio"]),
        "density_cv": float(density["weighted_cv"]),
        "checkpoint": str(path),
    }
    attempts.append(row)
    print("=== ADAPTIVE UV ATTEMPT ===", json.dumps(row), flush=True)

    if healthy:
        score = candidate_score(row)
        if best is None or score < best["score"]:
            best = {
                "score": score,
                "path": path,
                "audit": audit,
                "density": density,
                "packing": packing,
                "row": row,
            }

    return row


# ------------------------------------------------------------------
# A) Structured runtime unwraps: few/larger charts, not human-editability gated.
# ------------------------------------------------------------------
for method in ("MINIMUM_STRETCH", "ANGLE_BASED", "CONFORMAL", "TOPOLOGY_HARMONIC"):
    try:
        obj = load_base()
        profile = profile_for(a.checkpoint.parent.name)
        mark_seams(obj, profile)
        disk_seams(obj)

        # unwrap_pack already equalizes scale for native unwraps and uses
        # proportional chart normalization for harmonic.
        packing = unwrap_pack(
            obj,
            resolution=a.resolution,
            method=method,
            margin_pixels=a.gap,
        )
        row = evaluate(
            obj,
            f"STRUCTURED_{method}",
            {"method": method, "gap_px": a.gap},
            packing,
        )

        # If we already have an excellent healthy candidate, do not waste time.
        if (
            row["healthy"]
            and row["occupancy"] >= a.excellent_occupancy
            and row["density_p90_p10"] <= a.preferred_density_ratio
        ):
            print("=== ADAPTIVE UV EARLY EXCELLENT ===", json.dumps(row), flush=True)
            break

    except Exception as exc:
        attempts.append({
            "attempt": attempt_id + 1,
            "strategy": f"STRUCTURED_{method}",
            "healthy": False,
            "error": f"{type(exc).__name__}: {exc}",
        })
        attempt_id += 1
        print("=== ADAPTIVE UV ATTEMPT ERROR ===", json.dumps(attempts[-1]), flush=True)

# ------------------------------------------------------------------
# B) Moderate Smart UV only if structured path did not already reach target.
#    High angles avoid the 6k..30k-island explosion seen in V34.
# ------------------------------------------------------------------
need_more = (
    best is None
    or best["row"]["occupancy"] < a.preferred_occupancy
    or best["row"]["density_p90_p10"] > a.preferred_density_ratio
)

if need_more:
    for angle in angles:
        try:
            obj = load_base()
            activate(obj)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(
                angle_limit=math.radians(angle),
                island_margin=0.0,
                area_weight=0.0,
                correct_aspect=True,
                scale_to_bounds=False,
            )
            bpy.ops.uv.select_all(action="SELECT")
            bpy.ops.uv.average_islands_scale()
            bpy.ops.object.mode_set(mode="OBJECT")

            packing = blender_pack(obj, a.gap)
            row = evaluate(
                obj,
                f"SMART_A{int(angle)}",
                {"angle_degrees": angle, "gap_px": a.gap},
                packing,
            )

            if (
                row["healthy"]
                and row["occupancy"] >= a.excellent_occupancy
                and row["density_p90_p10"] <= a.preferred_density_ratio
            ):
                print("=== ADAPTIVE UV EARLY EXCELLENT ===", json.dumps(row), flush=True)
                break

        except Exception as exc:
            attempts.append({
                "attempt": attempt_id + 1,
                "strategy": f"SMART_A{int(angle)}",
                "healthy": False,
                "error": f"{type(exc).__name__}: {exc}",
            })
            attempt_id += 1
            print("=== ADAPTIVE UV ATTEMPT ERROR ===", json.dumps(attempts[-1]), flush=True)

# ------------------------------------------------------------------
# Select best HEALTHY candidate. Soft targets never cause a failure.
# ------------------------------------------------------------------
if best is not None:
    shutil.copy2(best["path"], a.checkpoint)

    result = {
        "passed": True,
        "mode": "ADAPTIVE_RUNTIME",
        "resolution": a.resolution,
        "packing_margin_pixels": a.gap,
        "preferred_occupancy": a.preferred_occupancy,
        "excellent_occupancy": a.excellent_occupancy,
        "preferred_density_p90_p10": a.preferred_density_ratio,
        "atlas_occupancy": best["audit"]["uv_triangle_area_sum"],
        "uv_audits": [best["audit"]],
        "uv_density_metrics": best["density"],
        "adaptive_uv_attempts": attempts,
        "adaptive_uv_selected": best["row"],
        "packing": best["packing"],
        "quality": (
            "excellent"
            if (
                best["row"]["occupancy"] >= a.excellent_occupancy
                and best["row"]["density_p90_p10"] <= a.preferred_density_ratio
            )
            else "good"
            if (
                best["row"]["occupancy"] >= a.preferred_occupancy
                and best["row"]["density_p90_p10"] <= a.preferred_density_ratio
            )
            else "best_available"
        ),
    }

    report_path.write_text(
        json.dumps(result, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    summary = {
        "passed": True,
        "quality": result["quality"],
        "selected": best["row"],
        "attempt_count": len(attempts),
    }
    (a.diagnostics / "ADAPTIVE_UV_SUMMARY.json").write_text(
        json.dumps(summary, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    print("=== ADAPTIVE UV FINAL OK ===", json.dumps(summary), flush=True)

else:
    # Do not crash: signal launcher to execute conservative repair fallback.
    result = {
        "passed": False,
        "mode": "ADAPTIVE_RUNTIME",
        "reason": "no_healthy_candidate",
        "adaptive_uv_attempts": attempts,
        "preferred_occupancy": a.preferred_occupancy,
        "packing_margin_pixels": a.gap,
    }
    report_path.write_text(
        json.dumps(result, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    (a.diagnostics / "ADAPTIVE_UV_SUMMARY.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    print(
        "=== ADAPTIVE UV NO HEALTHY CANDIDATE / REQUEST FALLBACK ===",
        flush=True,
    )
