using UnityEngine;

/// <summary>
/// Shared, stateless haze-blend helper — used by SpectrumBars3D and HorizonMountainLayers so the
/// atmosphere reads the same way on every element that touches the horizon line. Baked directly
/// into vertex color at mesh-build time (bars) or applied as a material tint (mountain layers) —
/// no shader/global-uniform plumbing needed — rather than a real distance-fog pass. This is a
/// Horizon-World-only effect, deliberately never touching Unity's global RenderSettings.fog
/// (which drives the SEPARATE Gameplay Fog concept — see GameplayFogController — and would also
/// blur the nearby gameplay world if reused here).
///
/// Two independent contributions are combined for real atmospheric depth (not just a single
/// vertical gradient): HEIGHT (low/near-horizon elements get pulled further into the haze; tall
/// elements stay clear) and an explicit, caller-supplied normalized DISTANCE (0 = close, e.g. the
/// bars or the near mountain layer; 1 = far, e.g. the far mountain layer) — this is what lets two
/// elements at the SAME height still haze differently depending on how "far away" they represent,
/// which a height-only blend could never do.
///
/// MACRO reactivity: the haze's own target color subtly blends toward
/// HorizonConfig.horizonHazeColorIntense by the caller-supplied `macroIntensity` (0..1, read ONCE
/// per Tick from MusicEnvironmentController.Instance.SmoothedMacroIntensity by the caller, never
/// re-derived here) — this class stays a pure, stateless function; it owns the haze BLEND math,
/// not the shared intensity driver itself.
/// </summary>
public static class HorizonHaze
{
    /// <summary>Back-compatible overload — no distance contribution, no macro blend.</summary>
    public static Color Apply(Color color, float localHeight, HorizonConfig config) =>
        Apply(color, localHeight, 0f, 0f, config);

    /// <summary>Back-compatible overload — no macro blend.</summary>
    public static Color Apply(Color color, float localHeight, float distance01, HorizonConfig config) =>
        Apply(color, localHeight, distance01, 0f, config);

    /// <summary>
    /// Blends `color` toward the (macro-blended) haze color based on `localHeight` (Horizon World
    /// units, relative to the bar/ridge baseline — NOT world Y) AND `distance01` (0..1, caller-
    /// defined "how far away does this element represent") — low/near elements get pulled further
    /// into the haze; tall/high/close elements stay clearer. An extra exponential boost near
    /// height 0 (the horizon line itself) lets that seam read as deliberately hazy even when
    /// Start/End Height are set wide, plus an optional warm/pink tint blended in only at that seam.
    /// </summary>
    public static Color Apply(Color color, float localHeight, float distance01, float macroIntensity, HorizonConfig config)
    {
        float start = config.horizonHazeStartHeight;
        float end   = Mathf.Max(start + 0.01f, config.horizonHazeEndHeight);
        float t     = Mathf.Clamp01(Mathf.InverseLerp(end, start, localHeight)) * Mathf.Clamp01(config.horizonHazeDensity);

        float distanceBoost = Mathf.Clamp01(distance01) * Mathf.Clamp01(config.horizonHazeDistanceIntensity);

        float horizonBoost = Mathf.Exp(-Mathf.Abs(localHeight) * 0.5f) * Mathf.Clamp01(config.horizonHazeHorizonIntensity);
        float amount = Mathf.Clamp01(t + distanceBoost * (1f - t) + horizonBoost);

        Color baseHaze = Color.Lerp(config.horizonHazeColor, config.horizonHazeColorIntense, Mathf.Clamp01(macroIntensity));
        Color hazeColor = Color.Lerp(baseHaze, config.horizonHazeHorizonTintColor,
            horizonBoost * Mathf.Clamp01(config.horizonHazeHorizonTintAmount));

        return Color.Lerp(color, hazeColor, amount);
    }
}
