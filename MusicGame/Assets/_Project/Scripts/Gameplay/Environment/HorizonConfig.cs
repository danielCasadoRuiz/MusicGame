using UnityEngine;

/// <summary>
/// A separate, camera-stacked 3D world (HorizonCameraController/SpectrumBars3D/HorizonWater/
/// ProceduralSky/HorizonMountainLayers, all in Assets/_Project/Scripts/World/Horizon) holding real
/// volumetric spectrum bars (URP Lit, plastic/acrylic look) + mirrored reflection-bar geometry +
/// water + procedural sky + PNG mountain layers. Rendered by its OWN camera (Base of a URP camera
/// stack; the gameplay Main Camera becomes an Overlay
/// with Depth Only clear) sitting at a near-FIXED position (only horizonParallaxFactor of the
/// main camera's own movement bleeds through) that copies the main camera's rotation/FOV every
/// frame — genuinely distant/parallax-free, unlike re-centering geometry on the camera every
/// frame (which still reads as "attached", since it never has ANY relative motion at all).
/// Bar amplitude reads the SAME MusicWorldManager.NormalizedBandValue data the ground mesh
/// already uses (remapped onto Bar Count columns) — never a second analysis.
///
/// Deliberately NOT prefixed "MusicRunner" — this is a generic "distant background world"
/// system with no runner-specific gameplay coupling, so it's a reasonable candidate for reuse by
/// a future game mode (see EnvironmentConfig, which references this).
/// </summary>
[CreateAssetMenu(fileName = "HorizonConfig", menuName = "MusicGame/Environment/Horizon Config")]
public class HorizonConfig : ScriptableObject
{
    [Header("Horizon World — Dev / Live Tuning")]
    [Tooltip("OFF by default: STATIC shader/material properties (tiling, Fresnel, tint bases, " +
             "mountain placement, fog-unrelated water params, etc.) are applied ONCE when each " +
             "system initializes and never resent every frame — so you can tweak the live " +
             "Material in Play Mode and the change actually sticks instead of being overwritten " +
             "60 times a second. Turn this ON only while you're actively editing THIS config " +
             "asset's values in the Inspector during Play and want them to re-apply live without " +
             "stopping Play — never leave it on as the normal running mode.")]
    public bool devLiveConfigSync = false;

    [Header("Horizon World — Enable")]
    public bool enableHorizonWorld = true;
    [Tooltip("0 = Horizon Camera position never moves (pure skybox-like distant backdrop). Small " +
             "values (e.g. 0.02-0.1) let a fraction of the main camera's own translation bleed " +
             "through for a subtle sense of depth/parallax, without ever looking 'attached'.")]
    [Range(0f, 1f)] public float horizonParallaxFactor = 0.03f;
    [Tooltip("Bloom intensity — applied onto whichever Volume/profile the scene actually uses " +
             "(see horizonBloomForceApply).")]
    public float horizonBloomIntensity = 0.85f;
    [Tooltip("Bloom threshold — the actual gatekeeper for 'what glows'. Kept ABOVE 1.0 on purpose: " +
             "every non-emissive color in this scene (sky/water/mountains/haze) is authored in the " +
             "normal 0..1 range and should NEVER cross this, so only genuinely HDR-emissive things " +
             "(bar emission, optionally ring emission) bloom at all — that's what keeps the world " +
             "dark and makes the neon read as selectively bright instead of everything glowing.")]
    [Range(0f, 2f)] public float horizonBloomThreshold = 1.05f;
    [Tooltip("Bloom scatter (URP's blur-radius-like spread) — kept modest/tight so the halo reads " +
             "as a glow AROUND the bar, not a wash that eats its cube shape.")]
    [Range(0f, 1f)] public float horizonBloomScatter = 0.5f;
    [Tooltip("Applied every frame onto WHATEVER Volume/profile the scene actually uses for Bloom " +
             "(hand-authored 'Global Volume' included) — HorizonWorld no longer silently skips " +
             "applying these values just because a Volume already exists in the scene.")]
    public bool horizonBloomForceApply = true;

    [Header("Horizon World — Bars: Arc Shape")]
    [Tooltip("Number of vertical bars around the arc — independent from the real analysis " +
             "resolution (AudioAnalysisConfig.visualBandCount); real band data is averaged/" +
             "remapped onto however many bars this is.")]
    [Range(6, 64)] public int horizonBarCount = 48;
    [Tooltip("Total angular span of the arc, centered directly ahead. 180 = a full semicircle.")]
    public float horizonArcSpanDegrees = 180f;
    [Tooltip("Radius of the arc (Horizon World units — this is its OWN small, fixed coordinate " +
             "space, not the gameplay world, so this can stay small/manageable regardless of how " +
             "long the song/path is).")]
    public float horizonArcRadius = 40f;
    [Tooltip("Vertical offset of the whole bar row from the Horizon Camera's own fixed height.")]
    public float horizonBarVerticalOffset = -2f;

    [Header("Horizon World — Bars: Shape & Color")]
    [Tooltip("Bar height at amplitude = 0.")]
    public float horizonBarMinHeight = 0.3f;
    [Tooltip("Bar height at amplitude = 1.")]
    public float horizonBarMaxHeight = 6f;
    [Tooltip("Fraction (0..1) of each bar's own angular slice actually filled by geometry — lower " +
             "than 1 leaves a visible gap between bars (classic equalizer look). Kept close to 1 " +
             "so the arc reads as densely packed, still individually readable bars.")]
    [Range(0.1f, 1f)] public float horizonBarWidthFraction = 0.94f;
    [Tooltip("Radial thickness (depth) of each bar box, in Horizon World units.")]
    public float horizonBarDepth = 1.5f;
    [Tooltip("Amplitude (0..1) → color. Evaluated from the RAW normalized amplitude, never from " +
             "the final height — one solid color per bar per moment, no bass/mid/treble special-" +
             "casing. Freely editable as a Unity Gradient in the Inspector.")]
    public Gradient horizonBarAmplitudeGradient = DefaultAmplitudeGradient();
    [Tooltip("URP Lit Metallic — kept LOW (near 0) for a plastic/acrylic look, not a metal one.")]
    [Range(0f, 1f)] public float horizonBarMetallic = 0.05f;
    [Tooltip("URP Lit Smoothness — mid-range gives a visible specular highlight (real 'volume' " +
             "cue from lighting) without turning the bar into a mirror.")]
    [Range(0f, 1f)] public float horizonBarSmoothness = 0.55f;
    [Tooltip("Emission present even at amplitude = 0 — keeps quiet bars faintly glowing instead of " +
             "going fully dark/dull, for a consistently neon look.")]
    public float horizonBarBaseEmission = 0.6f;
    [Tooltip("Extra emission ADDED on top of Base Emission, scaled by amplitude — this is the main " +
             "'punchier on louder bands' knob.")]
    public float horizonBarAmplitudeEmissionBoost = 3.5f;
    [Tooltip("Hard clamp on the final emission multiplier (Base + Amplitude*Boost), regardless of " +
             "how the two above are tuned — keeps Bloom from blowing out to flat white.")]
    public float horizonBarMaxEmission = 6f;
    [Tooltip("Multiplies the normalized band value before height/color — >1 makes quiet moments " +
             "read as louder, <1 tames overly hot signals.")]
    public float horizonBarGain = 1f;
    [Tooltip("Exponential rise-speed toward a LOUDER target amplitude each frame (0 = instant, " +
             "close to 1 = very lazy) — kept fast/low by default so bars punch on the beat.")]
    [Range(0f, 0.99f)] public float horizonBarAttack = 0.15f;
    [Tooltip("Exponential fall-speed toward a QUIETER target amplitude each frame — kept slower " +
             "than Attack by default so bars have a brief decay tail instead of snapping down.")]
    [Range(0f, 0.99f)] public float horizonBarRelease = 0.6f;

    [Header("Horizon World — Bar Reflection (mirrored geometry, no camera/RenderTexture)")]
    [Tooltip("Each bar gets exactly one matching 'reflection bar' — same mesh, mirrored across " +
             "the water plane's real world Y (HorizonWater.WaterLevelWorldY), same X/Z, same " +
             "height, reading the SAME amplitude/color already computed for its real bar this " +
             "frame. Deterministic geometry, not a camera capture — see SpectrumBars3D's own doc.")]
    public bool horizonReflectionEnabled = true;
    [Tooltip("Multiplies the reflection bar's color+emission relative to its real bar — kept < 1 " +
             "so the reflection reads as dimmer than the real thing, never a perfect duplicate.")]
    [Range(0f, 1f)] public float horizonReflectionBrightness = 0.45f;
    [Tooltip("World-units distance below the water surface at which a reflection bar has fully " +
             "faded to Reflection Fade Color — independent of any single bar's own height, so a " +
             "tall bar's deep bottom fades the same as a short bar's.")]
    public float horizonReflectionFadeDistance = 3f;
    [Tooltip("Color reflection bars fade toward with depth — keep this dark/near-black so it " +
             "reads as 'swallowed by the dark water', never a visible flat color.")]
    public Color horizonReflectionFadeColor = new Color(0.02f, 0.02f, 0.05f);
    [Tooltip("Vertical scale multiplier applied to the mirrored reflection bar on top of its " +
             "real bar's height — 1 = exact mirror; >1 stretches it, <1 compresses it.")]
    public float horizonReflectionStretchY = 1f;

    [Header("Horizon World — Water")]
    // Two independently-tiling/scrolling NORMAL MAPS (assign real tileable normal-map textures
    // here — see HorizonWater.cs doc for exactly what to provide) combined for a subtle, still-
    // readable-as-water micro ripple. Falls back to Unity's flat default-normal texture if left
    // unassigned (near-perfectly flat surface, no ripple detail — still fully functional, just
    // less detailed) so the water never errors out with nothing assigned.
    [Tooltip("Vertical level (relative to the Horizon Camera) the water plane sits at — " +
             "independent from Bar Vertical Offset, which only affects the bars.")]
    public float horizonWaterLevel = -1.4f;
    [Tooltip("Overall darkness of the water base color — near 1 reads as almost-black at night.")]
    [Range(0f, 1f)] public float horizonWaterDarkness = 0.92f;
    [Tooltip("Tileable normal map A — e.g. a 'water normal' texture from any free PBR water/ripple " +
             "pack. Left empty: falls back to a flat normal (still works, just no ripple detail).")]
    public Texture2D horizonWaterNormalMapA;
    [Tooltip("Tileable normal map B — should differ from A (different tiling/pattern) so the " +
             "combined ripple never reads as one obviously-repeating texture.")]
    public Texture2D horizonWaterNormalMapB;
    [Tooltip("UV tiling of normal map A.")]
    public float horizonWaterTilingA = 6f;
    [Tooltip("Scroll velocity of normal map A (UV units/second, both axes) — diagonal by default.")]
    public Vector2 horizonWaterScrollA = new Vector2(0.035f, 0.015f);
    [Tooltip("UV tiling of normal map B — kept different from Tiling A so the two never align.")]
    public float horizonWaterTilingB = 17f;
    [Tooltip("Scroll velocity of normal map B — a different direction than A on purpose.")]
    public Vector2 horizonWaterScrollB = new Vector2(-0.012f, 0.028f);
    [Tooltip("How strongly the combined normal maps perturb the surface — small values keep the " +
             "surface reading as calm/near-flat instead of big rolling waves.")]
    [Range(0f, 2f)] public float horizonWaterNormalStrength = 0.4f;
    [Tooltip("Fresnel (view-angle rim light) power — higher = tighter/sharper rim.")]
    public float horizonWaterFresnelPower = 5f;
    [Tooltip("Specular highlight tightness (higher = smaller/sharper glints, lower = broader/" +
             "softer) — the water's own smoothness, independent of Fresnel.")]
    [Range(4f, 256f)] public float horizonWaterSpecularPower = 48f;
    [Tooltip("Specular highlight brightness multiplier.")]
    public float horizonWaterSpecularIntensity = 0.6f;
    [Tooltip("Faint color tint added where the water surface faces toward the horizon (grazing " +
             "angle), picking up a hint of the sky's own horizon color.")]
    public Color horizonWaterHorizonTint = new Color(0.9f, 0.5f, 0.4f);
    [Range(0f, 1f)] public float horizonWaterHorizonTintStrength = 0.25f;
    [Tooltip("How much the water's own animated normal wobble distorts what it refracts (the " +
             "reflection bars sitting below it, via URP's _CameraOpaqueTexture) — this is what " +
             "sells 'reflection seen through moving water' instead of a perfect duplicate. " +
             "Requires the URP asset's Opaque Texture setting to be enabled.")]
    [Range(0f, 0.2f)] public float horizonWaterRefractionStrength = 0.08f;

    [Header("Horizon World — Procedural Sky")]
    // Deliberately just a plain, cheap vertical gradient (near-black navy zenith, dark blue/
    // purple mid-sky, a subtle purple/pink/coral band right at the horizon) + a barely-there
    // noise wobble so it never reads as a perfectly flat linear ramp. The sky is a simple
    // BACKGROUND — the mountains (PNG layers) and haze below are what carry the actual visual
    // interest of the horizon line, not this shader.
    public Color horizonSkyZenithColor  = new Color(0.015f, 0.015f, 0.035f);
    public Color horizonSkyUpperColor   = new Color(0.05f, 0.05f, 0.12f);
    public Color horizonSkyLowerColor   = new Color(0.16f, 0.09f, 0.22f);
    public Color horizonSkyHorizonColor = new Color(0.55f, 0.28f, 0.34f);
    [Tooltip("Low-frequency noise warping the vertical gradient bands so they don't read as a " +
             "flat linear gradient. Kept subtle on purpose — this is a plain background, not a " +
             "detailed procedural sky.")]
    public float horizonSkyNoiseScale    = 1.2f;
    [Range(0f, 1f)] public float horizonSkyNoiseStrength = 0.04f;
    public Color horizonGlowColor     = new Color(1f, 0.6f, 0.5f);
    [Range(0f, 3f)] public float horizonGlowIntensity = 1f;
    public Color horizonSunColor = new Color(1f, 0.85f, 0.7f);
    [Tooltip("Sun position in the sky, degrees (0 = straight ahead/ +Z, 90 = due right).")]
    public float horizonSunAzimuthDeg   = 0f;
    [Tooltip("Sun height, degrees above the horizon.")]
    public float horizonSunElevationDeg = 12f;
    [Range(0.001f, 0.2f)] public float horizonSunSize = 0.03f;
    [Range(0f, 1f)] public float horizonSunGlowSize = 0.25f;
    public float horizonSunGlowIntensity = 1.2f;

    [Header("Horizon World — Macro Palette (BASE = calm/low-energy state)")]
    // BASE colors above (Zenith/Upper/Lower/Horizon sky, Haze, Mountain tints, Water horizon
    // tint) are the LOOK BASE — tune those first with music modulation off. The *Intense fields
    // below define the HIGH-ENERGY end of the blend; MusicEnvironmentController computes a single
    // slow-smoothed 0..1 "how intense is the music right now" value (from profile intensity +
    // buildup, NOT per-beat) and every reactive system here does its OWN
    // Color.Lerp(base, intense, thatValue) — never a full replacement, always a modulation of the
    // artistic base. See EnvironmentConfig.enableMusicEnvironmentModulation to disable this
    // entirely and see the pure base look.
    [Tooltip("Sky zenith color at full macro intensity.")]
    public Color horizonSkyZenithColorIntense  = new Color(0.03f, 0.03f, 0.10f);
    [Tooltip("Sky upper-band color at full macro intensity.")]
    public Color horizonSkyUpperColorIntense   = new Color(0.16f, 0.06f, 0.30f);
    [Tooltip("Sky lower-band color at full macro intensity.")]
    public Color horizonSkyLowerColorIntense   = new Color(0.55f, 0.10f, 0.42f);
    [Tooltip("Sky horizon-band color at full macro intensity.")]
    public Color horizonSkyHorizonColorIntense = new Color(0.95f, 0.35f, 0.20f);
    [Tooltip("Multiplies horizonGlowIntensity at full macro intensity — 1 = no change.")]
    public float horizonGlowIntensityIntenseMultiplier = 1.6f;
    [Tooltip("Horizon Haze color at full macro intensity — subtle, see horizonHazeColor above.")]
    public Color horizonHazeColorIntense = new Color(0.30f, 0.16f, 0.42f);
    [Tooltip("Far mountain tint at full macro intensity — subtle.")]
    public Color horizonMountainFarTintIntense  = new Color(0.40f, 0.18f, 0.50f, 1f);
    [Tooltip("Near mountain tint at full macro intensity — subtle.")]
    public Color horizonMountainNearTintIntense = new Color(0.10f, 0.05f, 0.14f, 1f);
    [Tooltip("Water horizon tint at full macro intensity — subtle.")]
    public Color horizonWaterHorizonTintIntense = new Color(0.5f, 0.25f, 0.55f);

    [Header("Horizon World — Mountains (PNG silhouette layers)")]
    // No procedural mesh/noise anymore — each layer is a flat textured quad using a hand-authored
    // PNG silhouette WITH ALPHA that you assign below, tinted/scaled/positioned from here. Assign
    // NOTHING and a layer simply doesn't render (no procedural fallback texture is generated —
    // see HorizonMountainLayers.cs doc for exactly what kind of PNG to provide).
    public bool  horizonMountainsEnabled = true;
    [Tooltip("Far-layer silhouette PNG (alpha channel = shape). Assign from Assets — none = this " +
             "layer doesn't render.")]
    public Texture2D horizonMountainFarTexture;
    [Tooltip("Near-layer silhouette PNG (alpha channel = shape).")]
    public Texture2D horizonMountainNearTexture;
    [Tooltip("Tint multiplied over the FAR texture — less contrast, blue/violet, reads as " +
             "integrated with the haze/sky.")]
    public Color horizonMountainFarTint = new Color(0.30f, 0.24f, 0.48f, 1f);
    [Tooltip("Tint multiplied over the NEAR texture — darker, more contrast, almost-black " +
             "silhouette.")]
    public Color horizonMountainNearTint = new Color(0.05f, 0.04f, 0.08f, 1f);
    [Range(0f, 1f)] public float horizonMountainFarOpacity  = 0.85f;
    [Range(0f, 1f)] public float horizonMountainNearOpacity = 1f;
    [Tooltip("Uniform scale of the far layer's quad, in Horizon World units (width, height).")]
    public Vector2 horizonMountainFarScale  = new Vector2(90f, 16f);
    [Tooltip("Uniform scale of the near layer's quad, in Horizon World units (width, height).")]
    public Vector2 horizonMountainNearScale = new Vector2(90f, 14f);
    [Tooltip("Vertical offset of the far layer above the base horizon line.")]
    public float horizonMountainFarVerticalOffset = 0f;
    [Tooltip("Vertical offset of the near layer above the base horizon line.")]
    public float horizonMountainNearVerticalOffset = -1f;
    [Tooltip("Brightness multiplier applied to the far layer, after tint.")]
    public float horizonMountainFarBrightness = 1f;
    [Tooltip("Brightness multiplier applied to the near layer, after tint.")]
    public float horizonMountainNearBrightness = 1f;
    [Tooltip("Distance of the far layer from center, as a multiple of Arc Radius — kept further " +
             "out than the bars so opaque depth-testing alone puts it behind them.")]
    public float horizonMountainFarDistance  = 1.6f;
    [Tooltip("Distance of the near layer from center, as a multiple of Arc Radius.")]
    public float horizonMountainNearDistance = 1.3f;
    [Tooltip("Fraction (0..1) of the Horizon Camera's own parallax translation the far layer " +
             "additionally bleeds through — a very subtle extra sense of depth between layers. " +
             "0 = perfectly locked to the horizon like the bars/sky.")]
    [Range(0f, 1f)] public float horizonMountainFarParallax  = 0.015f;
    [Tooltip("Parallax fraction for the near layer — kept higher than Far so the near layer " +
             "drifts slightly more, selling the two-layer depth separation.")]
    [Range(0f, 1f)] public float horizonMountainNearParallax = 0.05f;

    [Header("Horizon World — Atmosphere / Haze")]
    // Specific to the Horizon World ONLY — never Unity's global RenderSettings.fog (that's the
    // separate Gameplay Fog concept, see EnvironmentConfig, for the circuit). Baked into the
    // bars' own vertex colors (SpectrumBars3D) and the mountain layers' tint
    // (HorizonMountainLayers) via HorizonHaze.Apply — cheap, no per-pixel distance-to-camera
    // needed since everything here already sits within a small, known-radius space.
    public Color horizonHazeColor = new Color(0.16f, 0.14f, 0.30f);
    [Range(0f, 1f)] public float horizonHazeDensity = 0.35f;
    [Tooltip("Height (Horizon World units, relative to the bar baseline) below which haze is at " +
             "full density.")]
    public float horizonHazeStartHeight = 0f;
    [Tooltip("Height above which haze has fully cleared.")]
    public float horizonHazeEndHeight = 10f;
    [Tooltip("Extra haze boost right at the horizon line, on top of the height-based density.")]
    [Range(0f, 1f)] public float horizonHazeHorizonIntensity = 0.3f;
    [Tooltip("Optional warm/pink tint blended in ONLY at the horizon line itself (on top of the " +
             "cooler Haze Color used everywhere else) — a small artistic touch, not the haze's " +
             "main color.")]
    public Color horizonHazeHorizonTintColor = new Color(0.55f, 0.30f, 0.38f);
    [Range(0f, 1f)] public float horizonHazeHorizonTintAmount = 0.35f;
    [Tooltip("How much a caller-supplied normalized DISTANCE (0 = close, e.g. the bars/near " +
             "mountains; 1 = far, e.g. far mountains) adds on top of the height-based haze amount " +
             "— this is what gives genuine atmospheric depth between layers instead of a single " +
             "flat vertical gradient applied identically to everything.")]
    [Range(0f, 1f)] public float horizonHazeDistanceIntensity = 0.5f;

    private static Gradient DefaultAmplitudeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.10f, 0.95f, 0.85f), 0.00f), // turquoise/cyan
                new GradientColorKey(new Color(0.15f, 0.55f, 0.98f), 0.25f), // cyan/blue
                new GradientColorKey(new Color(0.55f, 0.25f, 0.95f), 0.45f), // violet
                new GradientColorKey(new Color(0.90f, 0.20f, 0.75f), 0.60f), // pink/magenta
                new GradientColorKey(new Color(1.00f, 0.45f, 0.30f), 0.75f), // coral/orange
                new GradientColorKey(new Color(1.00f, 0.20f, 0.15f), 1.00f), // red-orange
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
