"""Garment category + weight-refinement profile (plain Python, no bpy: used by controller.py and blender_worker.py).

The category only says WHAT is being processed; the actual weight distribution is decided by the
geometry (garment_weights.py measures length, looseness, openings and panels on the real mesh).

Category sources, in order:
  1. Output/<asset>/garment.json  {"garment_type": "trousers" | "top" | "shoe" | "headwear" | "generic", ...}
     (optional; may also override any profile key under "garment_weights").
  2. The retopo stage's own filename classifier (Retopo/meshy_to_blender.auto_profile), refined with
     a few extra keywords (jackets / coats / shirts in Catalan, Spanish and English).
"""
from __future__ import annotations
import json, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

GARMENT_TYPES = ("trousers", "top", "shoe", "headwear", "generic")

_TOP_WORDS = ("jacket", "coat", "shirt", "vest", "tunic", "dress", "abric", "jaqueta", "camisa", "armilla",
              "chaqueta", "abrigo", "levita", "frac", "casaca", "robe", "toga")
_HEAD_WORDS = ("hat", "cap", "barret", "gorra", "sombrero")

# Every key of a profile is documented in config.json ("garment_weights" section).
DEFAULT_PROFILE = {
    "lower_refine": True,          # region-aware leg/pelvis distribution (legs, crotch, skirt panels)
    "region_top_above_hip_m": 0.15,
    "split_min_share": 0.15,       # each side must hold >= this share of the slab to count as a separate leg/panel
    "smooth_radius_m": 0.06,       # diffusion radius of the leg influence inside one leg/panel...
    "smooth_radius_per_gap": 0.5,  # ...plus this * median cloth-to-skin gap (looser garment = wider spread)
    "smooth_radius_max_m": 0.16,
    "seam_band_m": 0.06,           # half-width of the left/right blend around the crotch / panel-junction seam...
    "seam_band_per_gap": 1.0,      # ...plus this * median gap
    "seam_band_max_m": 0.22,
    "seam_pelvis_share": 0.35,     # share of the leg influence handed to the pelvis right on the seam (fades with the band)
    "leg_influence_scale": 1.0,    # 1.0 = keep the legs' overall influence (skirts keep following the legs)
    "leg_share_cap": None,         # optional cap of the total leg influence (None = no cap)
    "lod_post_smooth_m": 0.015,    # light smoothing after the ULTRA -> LOD transfer
    # When to act: no problem in the seed weights -> keep them untouched; problems on a close-fitting
    # garment (median cloth-to-skin gap < loose_gap_m) -> repair only within local_radius_m of them;
    # problems on a loose garment -> full region-aware redistribution.
    "loose_gap_m": 0.03,
    "local_radius_m": 0.08,
}

TYPE_DEFAULTS = {
    # Tuned on the baggy Monteverdi trousers (metrics + renders): wider crotch bands spread the stretch even
    # more but make the crotch lag behind the moving thigh, so the body pokes through in kicks / runs; an
    # extra pelvis share on the low-hanging crotch anchors it and creates stretch peaks in kicks. The pelvis
    # still takes part at the waist / upper crotch through the body's own weights (seed).
    "trousers": {"seam_pelvis_share": 0.0},
    "top": {"seam_pelvis_share": 0.3},
    "shoe": {"lower_refine": True, "seam_pelvis_share": 0.0},
    "headwear": {"lower_refine": False},
    "generic": {},
}


def _retopo_auto_profile(name: str):
    try:
        retopo = HERE.parent / "Retopo"
        if str(retopo) not in sys.path:
            sys.path.insert(0, str(retopo))
        from meshy_to_blender import auto_profile   # the pipeline's existing filename classifier
        return auto_profile(Path(name))
    except Exception:
        return None


def classify_name(name: str) -> str:
    base = _retopo_auto_profile(name)
    if base == "trousers":
        return "trousers"
    if base == "shoe":
        return "shoe"
    if base == "wig":
        return "headwear"
    low = name.casefold()
    if any(w in low for w in _HEAD_WORDS):
        return "headwear"
    if any(w in low for w in _TOP_WORDS):
        return "top"
    return "generic"


def read_asset_override(asset_dir: Path) -> dict:
    p = Path(asset_dir) / "garment.json"
    if not p.is_file():
        return {}
    data = json.loads(p.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"{p}: ha de ser un objecte JSON")
    if "garment_type" in data and data["garment_type"] not in GARMENT_TYPES:
        raise ValueError(f"{p}: garment_type ha de ser un de {GARMENT_TYPES}")
    return data


def resolve(asset_name: str, asset_dir: Path | None) -> dict:
    """{'garment_type', 'source', 'overrides'} for the session file."""
    override = read_asset_override(asset_dir) if asset_dir else {}
    if override.get("garment_type"):
        return {"garment_type": override["garment_type"], "source": "garment.json",
                "overrides": override.get("garment_weights", {})}
    return {"garment_type": classify_name(asset_name), "source": "name", "overrides": override.get("garment_weights", {})}


def profile_for(garment_type: str, config_section: dict | None, overrides: dict | None = None) -> dict:
    """DEFAULT_PROFILE <- TYPE_DEFAULTS <- config.json profiles (default, then type) <- garment.json overrides."""
    prof = dict(DEFAULT_PROFILE)
    prof.update(TYPE_DEFAULTS.get(garment_type, {}))
    profiles = (config_section or {}).get("profiles", {})
    prof.update(profiles.get("default", {}))
    prof.update(profiles.get(garment_type, {}))
    prof.update(overrides or {})
    return prof
