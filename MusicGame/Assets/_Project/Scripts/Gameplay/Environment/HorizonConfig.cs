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
    [Tooltip("0 (the default, and the recommended value) = the Horizon Camera's position NEVER " +
             "moves — a genuine skybox: exactly like a real horizon, no matter how far or how long " +
             "you run, you can never get a single unit closer to it. Only raise this above 0 if you " +
             "specifically want a subtle parallax depth cue (a small fraction of the main camera's " +
             "own translation bleeding through) — understand that doing so means the ring CAN be " +
             "approached a little (bounded by Parallax Max Offset Fraction below), which is the " +
             "opposite of a real horizon's behavior, so only do it deliberately and check it doesn't " +
             "let a long run catch up to the ring.")]
    [Range(0f, 1f)] public float horizonParallaxFactor = 0f;
    [Tooltip("Only matters if Parallax Factor above is > 0. Expressed as a FRACTION of Arc Radius " +
             "(not a fixed world-unit number) specifically so it auto-scales safely no matter what " +
             "Arc Radius is set to — an earlier version used a fixed number here, which silently " +
             "stopped being safe the moment Arc Radius was later tuned smaller (the drift could " +
             "then exceed the new, smaller radius, and the 'unreachable' ring got caught up to). " +
             "E.g. 0.3 means the camera's parallax drift can never exceed 30% of however far away " +
             "the ring currently is — comfortably short of ever reaching it.")]
    [Range(0f, 0.9f)] public float horizonParallaxMaxOffsetFraction = 0.3f;
    [Tooltip("Bloom intensity — applied onto whichever Volume/profile the scene actually uses " +
             "(see horizonBloomForceApply).")]
    public float horizonBloomIntensity = 0.85f;
    [Tooltip("Bloom threshold — the actual gatekeeper for 'what glows'. Kept just under 1.0 so a " +
             "bar's Emission term (LEDColor * EmissionAmount, see horizonBarBaseEmission) reliably " +
             "crosses it even at low/moderate amplitude ('a bit of bloom' at rest, not only on the " +
             "loudest peaks), while every non-emissive color in the scene (sky/water/mountains/" +
             "haze, and the bars' own milky BaseColor, whose brightest channel stays under ~0.9) " +
             "still safely stays below it — so only the bars' actual LED glow blooms.")]
    [Range(0f, 2f)] public float horizonBloomThreshold = 0.95f;
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
    [Tooltip("PURE DISTANCE from the Horizon Camera to the ring — this is the ONLY thing this value " +
             "does now. Bar Width/Depth/Height are all AUTHORED WORLD-UNIT SIZES, completely " +
             "independent of this radius (unlike an earlier version, where bar width was computed " +
             "FROM radius via the angular slice — which meant shrinking the radius also shrunk the " +
             "bars by the exact same proportion, so the apparent on-screen size never changed at " +
             "all; that's why tuning this used to visibly do nothing). Now: a SMALLER radius moves " +
             "the same-sized bars CLOSER to the camera, so they look bigger; a LARGER radius moves " +
             "them further away, so they look smaller. If instead you want the bars themselves to " +
             "look bigger/smaller WITHOUT changing how far away they are, use Bar Scale below " +
             "instead — changing both at once (distance AND size, by the same proportion) is exactly " +
             "what made this field appear to do nothing before, so don't fight that by cranking both.")]
    public float horizonArcRadius = 18f;
    [Tooltip("Moves the WHOLE bar row (every bar + its reflection, as one group) by this much, on " +
             "top of everything else above — the one knob for 'just reposition the entire thing " +
             "relative to the Horizon Camera' without touching the arc's own math. X = sideways, " +
             "Y = up/down (on top of Water Level + Bar Offset Above Water — use this for a quick " +
             "reposition, prefer the water-anchored fields for the 'touches the water' guarantee), " +
             "Z = toward/away from the camera (a simple push-back, independent of Arc Radius, which " +
             "instead reshapes the arc's own curvature/distance-per-bar). 0,0,0 = no change.")]
    public Vector3 horizonBarsGroupOffset = Vector3.zero;

    [Header("Horizon World — Bars: Shape & Color")]
    [Tooltip("Single master resize knob — multiplies Width, Depth, Min Height AND Max Height below " +
             "all together, so you can shrink/grow the whole row of bars as one physical object (to " +
             "make it fit comfortably on screen, say) and then fine-tune the individual fields on " +
             "top. Deliberately does NOT touch Arc Radius (distance) — this changes the bars' actual " +
             "size, Arc Radius changes how far away that (now differently-sized) object sits; the " +
             "two are independent tools for two different things (size vs distance), not the same " +
             "knob twice.")]
    public float horizonBarScale = 1f;
    [Tooltip("Bar height at amplitude = 0 (before Bar Scale above is applied), measured UPWARD from " +
             "the water-anchored base (Water Level + Bar Offset Above Water below) — never an " +
             "independent absolute Y value.")]
    public float horizonBarMinHeight = 0.3f;
    [Tooltip("Bar height at amplitude = 1 (before Bar Scale above is applied), measured UPWARD from " +
             "the water-anchored base (Water Level + Bar Offset Above Water below) — this is " +
             "genuinely 'how tall the tallest bar gets', with no other field able to silently change " +
             "that meaning.")]
    public float horizonBarMaxHeight = 3.2f;
    [Tooltip("Each bar's width (before Bar Scale above is applied), in absolute Horizon World units " +
             "— completely independent of Arc Radius/Bar Count/Arc Span Degrees (an earlier version " +
             "derived this from the angular slice at the current radius, which is what made Arc " +
             "Radius appear to do nothing — see its own doc). Smaller than the arc's natural per-bar " +
             "spacing leaves a visible gap between bars (classic equalizer look, lets each cube's own " +
             "side faces read as volume); equal to or larger than that spacing makes neighboring " +
             "bars touch/overlap.")]
    public float horizonBarWidth = 0.5f;
    [Tooltip("Radial thickness (depth) of each bar box (before Bar Scale above is applied), in " +
             "absolute Horizon World units — already independent of Arc Radius (unlike Width used " +
             "to be).")]
    public float horizonBarDepth = 0.5f;
    [Tooltip("Bar's base height ABOVE Water Level (below) — Water Level is the SINGLE vertical " +
             "reference point for the whole bar row (and its reflection); there is no separate " +
             "'bar vertical offset' to manually keep in sync with it any more. 0 (the default) = " +
             "the bar's bottom face sits exactly AT the water surface (touching — this is also what " +
             "guarantees its reflection can never overlap it, see horizonReflectionExtraOffset). " +
             "Raising this lifts the whole row (and, symmetrically, its reflection sinks the same " +
             "amount further below the water) — it can't go negative (that would push the bar " +
             "partly underwater and make its own reflection overlap it, which should never happen).")]
    public float horizonBarOffsetAboveWater = 0f;
    [Tooltip("Amplitude (0..1) → color. Evaluated from the RAW normalized amplitude, never from " +
             "the final height — one solid color per bar per moment, no bass/mid/treble special-" +
             "casing. Freely editable as a Unity Gradient in the Inspector.")]
    public Gradient horizonBarAmplitudeGradient = DefaultAmplitudeGradient();
    [Tooltip("The bar's milky/off-white plastic shell color — this is the LIT SURFACE color, " +
             "deliberately NOT the saturated LED/amplitude color (see Plastic Tint Strength below " +
             "for how much the LED hue is allowed to 'contaminate' it). Keep this close to white/ " +
             "light-grey so raising emission can never wash it out further — it's already near " +
             "its brightest.")]
    public Color horizonBarPlasticColor = new Color(0.85f, 0.85f, 0.88f, 1f);
    [Tooltip("How much the current LED/amplitude color tints the milky plastic BaseColor itself " +
             "(0 = pure white/grey shell regardless of LED color, 1 = BaseColor becomes the LED " +
             "color outright). Live-tuned in-Editor up to 1.0 — a fully color-tinted plastic look " +
             "was preferred over the original milky-white-shell concept; lower it back down " +
             "(~0.3-0.4) if a whiter shell should return.")]
    [Range(0f, 1f)] public float horizonBarPlasticTintStrength = 1f;
    [Tooltip("URP-Lit-style Smoothness — mid-range gives a visible specular highlight (real " +
             "'volume' cue from lighting) without turning the bar into a mirror.")]
    [Range(0f, 1f)] public float horizonBarSmoothness = 0.42f;
    [Tooltip("Minimum lit amount even on the side facing away from the main light — keeps the " +
             "shell from ever going fully black/unlit, as if it's translucent enough to self-" +
             "illuminate a little from ambient/internal light.")]
    [Range(0f, 1f)] public float horizonBarAmbientFloor = 0.41f;
    [Tooltip("Fresnel exponent for the rim term below — higher = tighter/thinner rim right at the " +
             "silhouette edge, lower = a broader glow creeping further across the surface.")]
    public float horizonBarRimPower = 2.5f;
    [Tooltip("Strength of the LED-colored Fresnel rim — reads as the internal LED light 'escaping' " +
             "right at the shell's grazing edges.")]
    [Range(0f, 2f)] public float horizonBarRimStrength = 0.73f;
    [Tooltip("Strength of the fake-transmission term (a soft LED-colored glow on the side FACING " +
             "AWAY from the main light, as if the shell is thin enough to let some of the internal " +
             "LED light bleed through) — a stylized 'wrap/back-light' trick, not real Subsurface " +
             "Scattering. Live-tuned notably higher than the first pass (0.4→1.35) for a much more " +
             "visible 'glowing from inside' SSS feel.")]
    [Range(0f, 2f)] public float horizonBarTransmissionStrength = 1.35f;
    [Tooltip("Emission present even at amplitude = 0 — keeps quiet bars faintly glowing instead of " +
             "going fully dark/dull, for a consistently neon look. Kept moderate on purpose: this " +
             "is a per-instance multiplier on the ALREADY-saturated LED color (a separate Emission " +
             "term in HorizonBarPlastic.shader, never blended into BaseColor), so pushing it too " +
             "high still just brightens the same hue rather than needing to 'stay saturated' — but " +
             "it's kept moderate anyway so Bloom (the final halo) stays the dominant 'louder = " +
             "more dramatic' cue, not the raw emission number.")]
    public float horizonBarBaseEmission = 0.45f;
    [Tooltip("Extra emission ADDED on top of Base Emission, scaled by amplitude — this is the main " +
             "'punchier on louder bands' knob.")]
    public float horizonBarAmplitudeEmissionBoost = 2.2f;
    [Tooltip("Hard clamp on the final emission multiplier (Base + Amplitude*Boost), regardless of " +
             "how the two above are tuned — keeps this a moderate 'LEDColor * moderate emission' " +
             "rather than a huge multiplier that would clip/desaturate toward white under " +
             "tonemapping; Bloom (see horizonBloomIntensity) is what should carry the rest of the " +
             "'louder = more intense' feeling.")]
    public float horizonBarMaxEmission = 3f;
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
    [Tooltip("Color reflection bars fade toward with depth. Live-tuned to a lighter neutral grey " +
             "than the original near-black — reads as a lighter/hazier deep-water fade instead of " +
             "'swallowed by darkness'; go back toward near-black (e.g. 0.02,0.02,0.05) for that.")]
    public Color horizonReflectionFadeColor = new Color(0.208f, 0.208f, 0.208f);
    [Tooltip("Vertical scale multiplier applied to the mirrored reflection bar on top of its " +
             "real bar's height — 1 = exact mirror; >1 stretches it, <1 compresses it.")]
    public float horizonReflectionStretchY = 1f;
    [Tooltip("Small manual nudge applied AFTER the reflection is already correctly mirrored across " +
             "Water Level — 0 (default) means an exact mirror, which combined with Bar Offset Above " +
             "Water's own 0-default is what guarantees bar and reflection touch with zero gap and " +
             "zero overlap. This is NOT what prevents overlap (Bar Offset Above Water ≥ 0 already " +
             "does that on its own) — only use this for a small final visual correction if needed.")]
    public float horizonReflectionExtraOffset = 0f;

    [Header("Horizon World — Water")]
    // Two independently-tiling/scrolling NORMAL MAPS (assign real tileable normal-map textures
    // here — see HorizonWater.cs doc for exactly what to provide) combined for a subtle, still-
    // readable-as-water micro ripple. Falls back to Unity's flat default-normal texture if left
    // unassigned (near-perfectly flat surface, no ripple detail — still fully functional, just
    // less detailed) so the water never errors out with nothing assigned.
    [Tooltip("Vertical level (relative to the Horizon Camera) the water plane sits at — THE single " +
             "vertical reference point for the whole bar row: bars sit at Water Level + Bar Offset " +
             "Above Water (0 by default = touching the surface), and reflections mirror across this " +
             "exact height, so bars/water/reflection can never drift out of sync with each other.")]
    public float horizonWaterLevel = -1.2f;
    [Tooltip("The water's authored base tint — this IS the actual color (no hidden hardcoded hue " +
             "baked in), so pick it directly in the Inspector/from the live material. See Water " +
             "Darkness below for an optional extra darkening on top of it.")]
    public Color horizonWaterColor = new Color(0.012f, 0.004f, 0.180f, 0.92f);
    [Tooltip("Extra darkening blended on top of Water Color — 0 = exactly Water Color, 1 = that " +
             "same hue crushed down to ~15% brightness (near-black but keeping a hint of its hue). " +
             "Kept at 0 by default now that Water Color is directly authored — raise it only if you " +
             "want a darker night-water look without touching Water Color itself.")]
    [Range(0f, 1f)] public float horizonWaterDarkness = 0f;
    [Tooltip("Fresnel (view-angle rim light) tint color — was previously hardcoded in the shader " +
             "and never actually exposed here; now a real config field.")]
    public Color horizonWaterFresnelColor = new Color(0.137f, 0.482f, 0.843f, 1f);
    [Tooltip("Tileable normal map A — e.g. a 'water normal' texture from any free PBR water/ripple " +
             "pack. Left empty: falls back to a flat normal (still works, just no ripple detail).")]
    public Texture2D horizonWaterNormalMapA;
    [Tooltip("Tileable normal map B — should differ from A (different tiling/pattern) so the " +
             "combined ripple never reads as one obviously-repeating texture.")]
    public Texture2D horizonWaterNormalMapB;
    [Tooltip("UV tiling of normal map A.")]
    public float horizonWaterTilingA = 20f;
    [Tooltip("Scroll velocity of normal map A (UV units/second, both axes) — diagonal by default.")]
    public Vector2 horizonWaterScrollA = new Vector2(0.01f, 0.01f);
    [Tooltip("UV tiling of normal map B — kept different from Tiling A so the two never align.")]
    public float horizonWaterTilingB = 10f;
    [Tooltip("Scroll velocity of normal map B — a different direction than A on purpose.")]
    public Vector2 horizonWaterScrollB = new Vector2(0.05f, 0.01f);
    [Tooltip("How strongly the combined normal maps perturb the surface — small values keep the " +
             "surface reading as calm/near-flat instead of big rolling waves.")]
    [Range(0f, 2f)] public float horizonWaterNormalStrength = 0.05f;
    [Tooltip("Fresnel (view-angle rim light) power — higher = tighter/sharper rim.")]
    public float horizonWaterFresnelPower = 9.13f;
    [Tooltip("Specular highlight tightness (higher = smaller/sharper glints, lower = broader/" +
             "softer) — the water's own smoothness, independent of Fresnel.")]
    [Range(4f, 256f)] public float horizonWaterSpecularPower = 24f;
    [Tooltip("Specular highlight brightness multiplier.")]
    public float horizonWaterSpecularIntensity = 0.5f;
    [Tooltip("Faint color tint added where the water surface faces toward the horizon (grazing " +
             "angle), picking up a hint of the sky's own horizon color.")]
    public Color horizonWaterHorizonTint = new Color(0.886f, 0.055f, 0.671f);
    [Range(0f, 1f)] public float horizonWaterHorizonTintStrength = 0.07f;
    [Tooltip("How much the water's own animated normal wobble distorts what it refracts (the " +
             "reflection bars sitting below it, via URP's _CameraOpaqueTexture) — this is what " +
             "sells 'reflection seen through moving water' instead of a perfect duplicate. " +
             "Requires the URP asset's Opaque Texture setting to be enabled.")]
    [Range(0f, 0.2f)] public float horizonWaterRefractionStrength = 0.157f;
    [Tooltip("THE actual 'how visible is the reflection' knob — how much of the refracted reflection " +
             "(the bars/reflection bars sitting below the water) shows through vs. the water's own " +
             "base color. This is a property of the WATER (how much it lets you see through it), " +
             "separate from horizonReflectionBrightness/FadeColor on the reflection bars themselves " +
             "(how bright/faded THEY are before the water even gets to them). If the reflection " +
             "reads as 'too visible', lower THIS first — it mutes the reflection by blending it " +
             "toward the water's own dark color, which looks like murky/dark water; crushing the " +
             "reflection bar's own brightness instead tends to just look like a flat black shape, " +
             "since it's no longer being blended with anything.")]
    [Range(0f, 1f)] public float horizonWaterReflectionVisibility = 0f;

    [Header("Horizon World — Procedural Sky")]
    // Deliberately just a plain, cheap vertical gradient (near-black navy zenith, dark blue/
    // purple mid-sky, a subtle purple/pink/coral band right at the horizon) + a barely-there
    // noise wobble so it never reads as a perfectly flat linear ramp. The sky is a simple
    // BACKGROUND — the mountains (PNG layers) and haze below are what carry the actual visual
    // interest of the horizon line, not this shader.
    // Re-sampled directly from designReference.png's sky (a warm sunset navy→mauve→coral
    // gradient) — averaged from several screen columns clear of the bars/mountains/UI overlay.
    public Color horizonSkyZenithColor  = new Color(0.07f, 0.08f, 0.17f);
    public Color horizonSkyUpperColor   = new Color(0.15f, 0.16f, 0.32f);
    public Color horizonSkyLowerColor   = new Color(0.30f, 0.21f, 0.38f);
    public Color horizonSkyHorizonColor = new Color(0.46f, 0.25f, 0.38f);
    [Tooltip("Low-frequency noise warping the vertical gradient bands so they don't read as a " +
             "flat linear gradient. Kept subtle on purpose — this is a plain background, not a " +
             "detailed procedural sky.")]
    public float horizonSkyNoiseScale    = 5f;
    [Range(0f, 1f)] public float horizonSkyNoiseStrength = 0.08f;
    // Restored to a visible warm halo (designReference.png clearly shows one around the sun/
    // horizon) — a previous live-tuning pass had muted this to near-black/near-zero intensity.
    public Color horizonGlowColor     = new Color(0.95f, 0.45f, 0.40f);
    [Range(0f, 3f)] public float horizonGlowIntensity = 0.5f;
    public Color horizonSunColor = new Color(1f, 0.55f, 0.45f);
    [Tooltip("Sun position in the sky, degrees (0 = straight ahead/ +Z, 90 = due right).")]
    public float horizonSunAzimuthDeg   = 0f;
    [Tooltip("Sun height, degrees above the horizon.")]
    public float horizonSunElevationDeg = 2.3f;
    [Range(0.001f, 0.2f)] public float horizonSunSize = 0.005f;
    [Range(0f, 1f)] public float horizonSunGlowSize = 0.03f;
    public float horizonSunGlowIntensity = 3.21f;

    [Header("Horizon World — Macro Palette (BASE = calm/low-energy state)")]
    // BASE colors above (Zenith/Upper/Lower/Horizon/Glow/Sun sky, Haze, Mountain tints, Water
    // horizon tint) are the LOOK BASE — tune those first with music modulation off. Every reactive
    // system, sky included, blends BASE → its own explicit *Intense color by the SAME shared
    // SmoothedMacroIntensity (via horizonSkyMacroResponseCurve below) — a plain Color.Lerp, exactly
    // like Haze/Mountain/Water already do. An earlier version of the sky instead computed an
    // open-ended HSV hue ROTATION (by a raw degree amount) — that could rotate a color through
    // WHATEVER hues happen to sit along the way, occasionally landing somewhere that clashed badly
    // with the rest of the scene, with no easy way to see or bound where it would end up. A plain
    // Lerp toward an explicit, hand-picked *Intense color can never produce anything outside the
    // two colors you actually chose — pick your own in the Inspector (with the live material/game
    // view open) for full, precise, WYSIWYG control over exactly where each color is allowed to go.
    // The shipped *Intense defaults below are each the SAME hue and Value as their base (a pure
    // saturation boost, ×1.35) — guaranteed to look like a richer version of the already-approved
    // base rather than an unrelated color, so the out-of-the-box result is safe even before you
    // retune it. See EnvironmentConfig.enableMusicEnvironmentModulation to disable ALL of this and
    // see the pure base look.
    [Tooltip("Reshapes SmoothedMacroIntensity (0..1) before every Base→Intense Lerp below: " +
             "response = intensity^this. 1 = linear (in practice the raw driver rarely gets near " +
             "1.0, so the sky barely seemed to move at all). Below 1 (e.g. 0.5-0.6) front-loads the " +
             "curve so even a moderate intensity already shows a clearly visible amount of the " +
             "shift, while 0 and 1 still map to exactly 0 and 1 either way.")]
    [Range(0.1f, 2f)] public float horizonSkyMacroResponseCurve = 0.55f;
    public Color horizonSkyZenithColorIntense  = new Color(0.035f, 0.049f, 0.17f);
    public Color horizonSkyUpperColorIntense   = new Color(0.0905f, 0.104f, 0.32f);
    public Color horizonSkyLowerColorIntense   = new Color(0.272f, 0.151f, 0.38f);
    public Color horizonSkyHorizonColorIntense = new Color(0.46f, 0.1765f, 0.352f);
    public Color horizonGlowColorIntense       = new Color(0.95f, 0.275f, 0.2075f);
    public Color horizonSunColorIntense        = new Color(1f, 0.3925f, 0.2575f);
    [Tooltip("Multiplies horizonGlowIntensity at full macro intensity — 1 = no change. Independent " +
             "of the colors above — this only affects how BRIGHT the horizon glow band gets.")]
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
