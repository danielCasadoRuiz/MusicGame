using UnityEngine;

/// <summary>
/// World-visual concerns that aren't specific to the Horizon World backdrop itself: the ambient
/// background-color mood (MusicEnvironmentController) and the gameplay circuit's distance fog
/// (GameplayFogController) — plus a reference to the (much larger) HorizonConfig, following the
/// "EnvironmentConfig → HorizonConfig reference" pattern for a sub-domain big enough to deserve
/// its own asset. Deliberately NOT prefixed "MusicRunner" — none of this is runner-specific
/// gameplay, so it's a reasonable candidate for reuse by a future game mode.
/// </summary>
[CreateAssetMenu(fileName = "EnvironmentConfig", menuName = "MusicGame/Environment/Environment Config")]
public class EnvironmentConfig : ScriptableObject
{
    [Header("Dev / Live Tuning")]
    [Tooltip("OFF by default — see HorizonConfig.devLiveConfigSync for the full explanation. " +
             "This one gates GameplayFogController's static RenderSettings.fog values instead.")]
    public bool devLiveConfigSync = false;

    [Header("Horizon World")]
    public HorizonConfig horizon;

    // ── Macro Palette (shared smoothed intensity driver) ───────────────────────
    // MusicEnvironmentController computes ONE smoothed 0..1 "how intense is the music right now"
    // value from profile.GetIntensityAt + GetBuildupAt (continuous MACRO signals — never per-beat)
    // and exposes it as SmoothedMacroIntensity. Every reactive Horizon system (ProceduralSky,
    // HorizonWater, HorizonMountainLayers, HorizonHaze) reads that ONE value and blends its OWN
    // base/Intense color pair locally — MusicEnvironmentController never writes another system's
    // shader property directly, it only owns the shared driver.
    [Header("Macro Palette — Music Environment Modulation")]
    [Tooltip("Master switch for ALL of the Horizon World's macro-driven palette modulation (sky/" +
             "haze/mountain-tint/water-tint). OFF = you see exactly HorizonConfig's authored BASE " +
             "colors, untouched — the artistic look-base workflow this was built for. ON = the " +
             "whole palette gradually blends toward each system's own *Intense colors as the music " +
             "swells, and eases back down during calmer sections.")]
    public bool  enableMusicEnvironmentModulation = true;
    [Tooltip("Seconds for the shared macro-intensity driver to smooth toward its target — this is " +
             "deliberately slow (several seconds), never a per-beat flicker.")]
    public float paletteMacroSmoothingTime = 4f;
    [Tooltip("How much an Impact/Drop MacroEvent snaps the shared macro-intensity driver toward " +
             "its full-intensity target (0..1, same 'partway there' idea as macroSnapFraction " +
             "below, but for the Horizon palette instead of the legacy background color).")]
    [Range(0f, 1f)] public float paletteMacroSnapFraction = 0.4f;

    // ── Music Environment (background color) ──────────────────────────────────
    // Ambient mood driven by the same SongProfile analysis, not a per-frame FFT visualizer and
    // not a naive "note = color" lookup (a full mix is polyphonic — chroma is an energy
    // DISTRIBUTION over the 12 pitch classes). See MusicEnvironmentController for the full
    // reasoning. Two smoothing stages keep it slow/musical instead of flickery: feature-level
    // (chromaEMAAlpha, sampled every colorSampleInterval) and display-level
    // (colorSmoothingTimeConstant).
    [Header("Music Environment (background color)")]
    public bool  enableMusicEnvironment      = true;
    [Tooltip("Seconds between feature samples (chroma/intensity). NOT a per-frame update — " +
             "sampling less often than every frame is itself part of what keeps this slow and " +
             "musical instead of jittery.")]
    public float colorSampleInterval         = 0.4f;
    [Tooltip("Exponential-moving-average smoothing applied to the sampled chroma/intensity " +
             "each tick (0..1). Lower = slower-evolving target hue/brightness.")]
    [Range(0.01f, 1f)]
    public float chromaEMAAlpha              = 0.12f;
    [Tooltip("Seconds for the DISPLAYED background color to chase its (already-smoothed) " +
             "target. This is the main anti-flicker knob — higher = calmer/slower.")]
    public float colorSmoothingTimeConstant  = 3f;
    [Tooltip("Minimum harmonic clarity (0..1 — normalized chroma vector magnitude; low during a " +
             "drum break/noise/silence, high when one tonal centre clearly dominates) required " +
             "before the target hue updates. Below this, hue HOLDS its last value instead of " +
             "drifting to an arbitrary angle from a directionless chroma vector.")]
    [Range(0f, 1f)]
    public float hueConfidenceThreshold      = 0.12f;
    [Range(0f, 1f)] public float baseSaturation = 0.55f;
    [Range(0f, 1f)] public float baseValue      = 0.35f;
    [Tooltip("Saturation/value multiplier at full buildup intensity (profile.buildupCurve). " +
             "1 = buildup has no visual effect on color.")]
    public float buildupIntensityBoost       = 1.4f;
    [Tooltip("Fraction of the remaining gap to the target color that snaps instantly on an " +
             "Impact/Drop MacroEvent — reads as the transition 'culminating' at that moment.")]
    [Range(0f, 1f)]
    public float macroSnapFraction           = 0.5f;

    // ── Gameplay Fog (circuit depth-fade — a different concept from Horizon Haze) ─────────────
    // Real distance fog for the GAMEPLAY world only (ground/circuit + rings/bonuses) so distant
    // parts of the track fade into darkness instead of being perfectly visible the whole time.
    // Implemented via Unity's own RenderSettings.fog (Linear mode) + a small explicit fog blend
    // added to VertexColorLit.shader (the ground) — ring/bonus materials use the built-in URP Lit
    // shader, which already blends RenderSettings.fog automatically, no extra code needed there.
    // Deliberately NEVER touches the Horizon World: none of its shaders (HorizonBar/CheapWater/
    // ProceduralSky/mountain layers) sample fog at all, so RenderSettings.fog being globally "on"
    // has zero visual effect on them regardless — see GameplayFogController.
    [Header("Gameplay Fog — Circuit Distance Fade (NOT Horizon Haze)")]
    public bool  gameplayFogEnabled = true;
    public Color gameplayFogColor = new Color(0.04f, 0.045f, 0.10f);
    [Tooltip("World-units distance from the camera where fog starts becoming visible.")]
    public float gameplayFogStartDistance = 18f;
    [Tooltip("World-units distance from the camera where fog reaches full density.")]
    public float gameplayFogEndDistance = 75f;
    [Tooltip("Steepens (>1) or relaxes (<1) the fog falloff between Start/End Distance without " +
             "having to re-tune both distances by hand — 1 = falloff exactly as authored above.")]
    [Range(0.1f, 3f)] public float gameplayFogStrength = 1f;
}
