using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Real volumetric spectrum-analyzer bars arranged in an arc inside the Horizon World. Every bar
/// AND its reflection are parented under a single dedicated child transform, _barsRoot (itself a
/// child of HorizonCameraController.Instance.HorizonRoot) — so they sit at a fixed position
/// independent of the Player/gameplay camera, AND the whole row can be repositioned as one group
/// (see horizonBarsGroupOffset) by moving just _barsRoot, instead of every other Horizon World
/// system that shares HorizonRoot directly.
///
/// Each bar is a genuine per-object cube (shared unit-cube Mesh + shared Material, one
/// MeshRenderer per bar) instead of a single hand-baked unlit mesh — this gives real lit
/// diffuse+specular volume. The material (MusicGame/HorizonBarPlastic) is a hand-written simple
/// Lit shader, NOT stock URP Lit — it deliberately keeps the surface's BaseColor a milky
/// off-white (barely tinted by the LED hue) and drives all the actual color from a SEPARATE LED
/// term (rim + fake-transmission + emission), so raising emission for louder amplitudes can never
/// wash the surface white — see the shader's own doc for the full "milky plastic lamp" reasoning.
/// Per-bar LEDColor/EmissionAmount are set via a single REUSED MaterialPropertyBlock (Clear()'d
/// and refilled per bar, never allocated per frame, never a Material instance per bar).
///
/// REFLECTION: each bar has exactly one matching "reflection bar" — the SAME mesh, a SEPARATE
/// simple unlit+opaque material (MusicGame/HorizonBarReflection), positioned by mirroring the real
/// bar's center across HorizonWater.WaterLevelWorldY (same X/Z, same height, exactly reflected Y)
/// — deterministic geometry, no camera/RenderTexture involved. It reads the SAME amplitude/color
/// this frame already computed for its real bar (darkened via horizonReflectionBrightness), never
/// a second analysis. Being real OPAQUE geometry, it's captured by URP's _CameraOpaqueTexture,
/// which CheapWater.shader samples+distorts — so the water surface sitting above it is what sells
/// the "reflection", not the reflection bar's own geometry looking imperfect.
///
/// VERTICAL PLACEMENT: Water Level (HorizonConfig) is the SINGLE reference point in Y for the
/// whole bar row — there is no separate "bar vertical offset" to manually keep in sync with it
/// any more (that used to drift out of sync, which is exactly what caused a bar to overlap its
/// own reflection). Each bar's base = waterLevelLocalY + horizonBarOffsetAboveWater, clamped >= 0
/// in code, so 0 (the default) means "touching the water, zero gap" and it can never go negative
/// (which would push the bar underwater and make the mirrored reflection overlap it). The
/// reflection mirrors across waterLevelLocalY itself (never across the bar's own offset base),
/// plus a separate, purely cosmetic horizonReflectionExtraOffset nudge.
///
/// SIZE vs DISTANCE: bar Width/Depth/Min/Max Height (HorizonConfig) are AUTHORED ABSOLUTE WORLD-
/// UNIT SIZES, totally independent of Arc Radius, and all four are further multiplied by the
/// single horizonBarScale master knob. An earlier version computed width FROM radius (via the
/// angular slice each bar occupies) — which meant a smaller radius shrunk the bars by the exact
/// same proportion it moved them closer, so the apparent on-screen size never actually changed.
/// Now Arc Radius purely controls distance-from-camera (and therefore apparent size, since the
/// bars themselves stay a fixed physical size) — horizonBarScale is the separate, single "resize
/// the whole row of bars" knob for when you want a smaller/bigger object at the SAME distance.
///
/// GROUP REPOSITIONING: horizonBarsGroupOffset (applied to _barsRoot's own localPosition in
/// ApplyStaticConfig) moves every bar + reflection as one rigid group, on top of everything above
/// — the single knob for "just move the whole thing" (X sideways, Y up/down on top of the water
/// anchor, Z toward/away from the camera) without touching the arc's own per-bar math.
///
/// Data: reuses the SAME MusicWorldManager.NormalizedBandValue(band, time) the ground mesh and the
/// old FrequencyBackground both already used, averaged over each bar's own band range — never a
/// second analysis.
///
/// amplitude (0..1) drives BOTH height and color from the SAME value, independently:
///   amplitude -> Mathf.Lerp(minHeight, maxHeight, amplitude)      (height, above the water anchor)
///   amplitude -> horizonBarAmplitudeGradient.Evaluate(amplitude)  (color)
/// Never the other way around (color is never derived from the final height).
/// </summary>
public class SpectrumBars3D : MonoBehaviour
{
    private static readonly int PlasticColorID        = Shader.PropertyToID("_PlasticColor");
    private static readonly int PlasticTintStrengthID = Shader.PropertyToID("_PlasticTintStrength");
    private static readonly int SmoothnessID          = Shader.PropertyToID("_Smoothness");
    private static readonly int AmbientFloorID        = Shader.PropertyToID("_AmbientFloor");
    private static readonly int RimPowerID            = Shader.PropertyToID("_RimPower");
    private static readonly int RimStrengthID         = Shader.PropertyToID("_RimStrength");
    private static readonly int TransmissionStrengthID = Shader.PropertyToID("_TransmissionStrength");
    private static readonly int LEDColorID         = Shader.PropertyToID("_LEDColor");
    private static readonly int EmissionAmountID   = Shader.PropertyToID("_EmissionAmount");
    private static readonly int ReflColorID        = Shader.PropertyToID("_Color");
    private static readonly int ReflFadeDistanceID = Shader.PropertyToID("_FadeDistance");
    private static readonly int ReflFadeColorID    = Shader.PropertyToID("_FadeColor");
    private static readonly int ReflWaterLevelID   = Shader.PropertyToID("_WaterLevelWorldY");

    private HorizonConfig _config;
    private Transform     _root;
    private Transform     _barsRoot; // child of _root — every bar/reflection parents under THIS,
                                      // so horizonBarsGroupOffset can reposition the whole row at once
    private HorizonWater  _water;

    private Mesh     _cubeMesh;
    private Material _barMaterial;
    private Material _reflectionMaterial;
    private MaterialPropertyBlock _mpb; // reused every call — Clear()'d, never allocated per frame

    private Transform[] _barT;
    private Renderer[]  _barR;
    private Transform[] _reflT;
    private Renderer[]  _reflR;

    private float[] _smoothedAmplitude;
    private Color[] _barColor;   // one solid color per bar, this frame — exposed for debug/HUD use

    private int _builtBarCount = -1;

    public IReadOnlyList<Color> BarColors => _barColor;
    public int BarCount => _config != null ? Mathf.Max(1, _config.horizonBarCount) : 0;

    public void Initialize(HorizonConfig config, Transform root, HorizonWater water)
    {
        _config = config;
        _root = root;
        _water = water;

        var barsRootGO = new GameObject("SpectrumBarsRoot");
        barsRootGO.transform.SetParent(root, false);
        _barsRoot = barsRootGO.transform;

        _cubeMesh = BuildCubeMesh();
        _mpb = new MaterialPropertyBlock();

        var barShader = Shader.Find("MusicGame/HorizonBarPlastic") ?? Shader.Find("Universal Render Pipeline/Lit");
        _barMaterial = new Material(barShader) { name = "Horizon_Bars", enableInstancing = true };

        var reflShader = Shader.Find("MusicGame/HorizonBarReflection") ?? barShader;
        _reflectionMaterial = new Material(reflShader) { name = "Horizon_BarsReflection", enableInstancing = true };

        ApplyStaticConfig();
        RebuildBarObjectsIfNeeded();
    }

    /// <summary>Pure art-direction (plastic base color/tint/smoothness/rim/transmission/reflection
    /// fade params) — applied once and only re-applied on demand (HorizonConfig.devLiveConfigSync).
    /// Never per-bar — these are SHARED material properties, not MaterialPropertyBlock overrides.</summary>
    public void ApplyStaticConfig()
    {
        _barsRoot.localPosition = _config.horizonBarsGroupOffset;

        _barMaterial.SetColor(PlasticColorID, _config.horizonBarPlasticColor);
        _barMaterial.SetFloat(PlasticTintStrengthID, Mathf.Clamp01(_config.horizonBarPlasticTintStrength));
        _barMaterial.SetFloat(SmoothnessID, Mathf.Clamp01(_config.horizonBarSmoothness));
        _barMaterial.SetFloat(AmbientFloorID, Mathf.Clamp01(_config.horizonBarAmbientFloor));
        _barMaterial.SetFloat(RimPowerID, Mathf.Max(0.01f, _config.horizonBarRimPower));
        _barMaterial.SetFloat(RimStrengthID, Mathf.Max(0f, _config.horizonBarRimStrength));
        _barMaterial.SetFloat(TransmissionStrengthID, Mathf.Max(0f, _config.horizonBarTransmissionStrength));

        _reflectionMaterial.SetFloat(ReflFadeDistanceID, Mathf.Max(0.01f, _config.horizonReflectionFadeDistance));
        _reflectionMaterial.SetColor(ReflFadeColorID, _config.horizonReflectionFadeColor);
        if (_water != null) _reflectionMaterial.SetFloat(ReflWaterLevelID, _water.WaterLevelWorldY);
    }

    private void RebuildBarObjectsIfNeeded()
    {
        int barCount = Mathf.Max(1, _config.horizonBarCount);
        if (barCount == _builtBarCount) return;

        DestroyBarObjects();

        int layer = LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);

        _barT = new Transform[barCount];
        _barR = new Renderer[barCount];
        _reflT = new Transform[barCount];
        _reflR = new Renderer[barCount];
        _smoothedAmplitude = new float[barCount];
        _barColor = new Color[barCount];

        for (int c = 0; c < barCount; c++)
        {
            var go = new GameObject($"Bar_{c}");
            go.layer = Mathf.Max(0, layer);
            go.transform.SetParent(_barsRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = _cubeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _barMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _barT[c] = go.transform;
            _barR[c] = mr;

            var rgo = new GameObject($"BarReflection_{c}");
            rgo.layer = Mathf.Max(0, layer);
            rgo.transform.SetParent(_barsRoot, false);
            rgo.AddComponent<MeshFilter>().sharedMesh = _cubeMesh;
            var rmr = rgo.AddComponent<MeshRenderer>();
            rmr.sharedMaterial = _reflectionMaterial;
            rmr.shadowCastingMode = ShadowCastingMode.Off;
            rmr.receiveShadows = false;
            _reflT[c] = rgo.transform;
            _reflR[c] = rmr;
        }

        _builtBarCount = barCount;
    }

    private void DestroyBarObjects()
    {
        if (_barT != null)
            foreach (var t in _barT) if (t != null) Destroy(t.gameObject);
        if (_reflT != null)
            foreach (var t in _reflT) if (t != null) Destroy(t.gameObject);
    }

    /// <summary>Averages NormalizedBandValue over the real-band range this bar covers — remapping
    /// analysis resolution to display resolution without ever re-analyzing audio.</summary>
    private static float SampleBandRange(MusicWorldManager world, float bandCenter, int realBandCount, int barCount, float time)
    {
        if (realBandCount <= 1) return world.NormalizedBandValue(0, time);

        float span = Mathf.Max(1f, (float)realBandCount / barCount);
        int lo = Mathf.Clamp(Mathf.FloorToInt(bandCenter - span * 0.5f), 0, realBandCount - 1);
        int hi = Mathf.Clamp(Mathf.CeilToInt(bandCenter + span * 0.5f), 0, realBandCount - 1);
        if (hi <= lo) return world.NormalizedBandValue(Mathf.Clamp(Mathf.RoundToInt(bandCenter), 0, realBandCount - 1), time);

        float sum = 0f;
        int count = 0;
        for (int b = lo; b <= hi; b++) { sum += world.NormalizedBandValue(b, time); count++; }
        return count > 0 ? sum / count : 0f;
    }

    public void Tick(MusicWorldManager world, float songTime)
    {
        if (_config == null || world == null) return;

        RebuildBarObjectsIfNeeded();
        if (_config.devLiveConfigSync) ApplyStaticConfig();

        int barCount      = _builtBarCount;
        int realBandCount = Mathf.Max(1, world.FrequencyBandsUsed);
        float halfSpan    = _config.horizonArcSpanDegrees * 0.5f * Mathf.Deg2Rad;
        float radius      = _config.horizonArcRadius;
        float gain        = Mathf.Max(0f, _config.horizonBarGain);
        float dt          = Time.deltaTime;
        float attackAlpha  = 1f - Mathf.Pow(Mathf.Clamp01(_config.horizonBarAttack), Mathf.Max(dt, 0.0001f) * 60f);
        float releaseAlpha = 1f - Mathf.Pow(Mathf.Clamp01(_config.horizonBarRelease), Mathf.Max(dt, 0.0001f) * 60f);
        // Read the SHARED macro-intensity driver ONCE per Tick (not per-bar) — see
        // MusicEnvironmentController's own doc on why it's the sole owner/writer of this value.
        float macroIntensity = MusicEnvironmentController.Instance != null
            ? MusicEnvironmentController.Instance.SmoothedMacroIntensity
            : 0f;

        // Mirror plane, expressed in the SAME local (_barsRoot-relative) space bar transforms use —
        // HorizonWater.WaterLevelWorldY is the single source of truth, so the two can never drift.
        // Relative to _barsRoot (not _root), since horizonBarsGroupOffset can move _barsRoot's own
        // world Y away from _root's — using _barsRoot.position.y keeps this correct regardless.
        float waterLevelLocalY = _water != null
            ? _water.WaterLevelWorldY - _barsRoot.position.y
            : _config.horizonWaterLevel;

        bool reflectionsOn = _config.horizonReflectionEnabled;
        float reflBrightness = Mathf.Max(0f, _config.horizonReflectionBrightness);
        float reflStretchY   = Mathf.Max(0.01f, _config.horizonReflectionStretchY);

        // Single master resize knob (see horizonBarScale doc) — multiplies Width/Depth/Min/Max
        // Height together, deliberately NOT Arc Radius (distance): this is the "make the physical
        // bars bigger/smaller" tool, Arc Radius is the separate "how far away are they" tool.
        float barScale = Mathf.Max(0.001f, _config.horizonBarScale);

        for (int c = 0; c < barCount; c++)
        {
            float tCenter = (c + 0.5f) / barCount;
            float bandF   = tCenter * (realBandCount - 1);
            float rawNorm = SampleBandRange(world, bandF, realBandCount, barCount, songTime);
            float target  = Mathf.Clamp01(rawNorm * gain);

            float current = _smoothedAmplitude[c];
            float alpha   = target > current ? attackAlpha : releaseAlpha;
            current = Mathf.Lerp(current, target, alpha);
            _smoothedAmplitude[c] = current;

            // COLOR comes from the amplitude directly — never from the final height. Emission =
            // a constant floor (so quiet bars still glow a little) PLUS an amplitude-scaled boost
            // (the main "louder = brighter" knob), hard-clamped so Bloom never blows out to white.
            // This LED color stays FULLY SATURATED all the way through — it is NEVER the bar's
            // BaseColor (see HorizonBarPlastic.shader's own doc): it only drives the rim/
            // transmission/emission terms, so raising emission can never wash the surface itself
            // toward white.
            Color gradientColor = _config.horizonBarAmplitudeGradient.Evaluate(current);
            float emissionAmount = Mathf.Min(
                _config.horizonBarMaxEmission,
                _config.horizonBarBaseEmission + current * _config.horizonBarAmplitudeEmissionBoost);

            // Water-anchored base (see horizonBarOffsetAboveWater doc) — Water Level is the SINGLE
            // vertical reference point; clamped >= 0 so the bar can never sink below the water
            // line, which is what guarantees its reflection (an exact mirror across the same
            // plane) can never overlap it either.
            float barBase = waterLevelLocalY + Mathf.Max(0f, _config.horizonBarOffsetAboveWater);

            Color hazedLED = HorizonHaze.Apply(gradientColor, barBase, 0f, macroIntensity, _config);
            hazedLED.a = 1f;
            _barColor[c] = hazedLED * (1f + emissionAmount); // exposed for debug/HUD use only

            float height = Mathf.Lerp(_config.horizonBarMinHeight, _config.horizonBarMaxHeight, current) * barScale;

            float aCenter = Mathf.Lerp(-halfSpan, halfSpan, tCenter);
            Vector3 dir = new Vector3(Mathf.Sin(aCenter), 0f, Mathf.Cos(aCenter));
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up); // local Z = radial (depth), local X = tangential (width)

            // Width/Depth are AUTHORED ABSOLUTE SIZES now — completely independent of Arc Radius
            // (see horizonArcRadius's own doc for why deriving width from the angular slice at a
            // given radius made changing the radius appear to do nothing).
            float width = Mathf.Max(0.01f, _config.horizonBarWidth) * barScale;
            float depth = Mathf.Max(0.01f, _config.horizonBarDepth) * barScale;

            float yTop    = barBase + height;
            float yCenter = (barBase + yTop) * 0.5f;

            var t = _barT[c];
            t.localPosition = dir * radius + Vector3.up * yCenter;
            t.localRotation = rot;
            t.localScale    = new Vector3(width, height, depth);

            _mpb.Clear();
            _mpb.SetColor(LEDColorID, hazedLED);
            _mpb.SetFloat(EmissionAmountID, emissionAmount);
            _barR[c].SetPropertyBlock(_mpb);

            // ── Reflection bar: mirrored across the water plane, same X/Z, same height ─────────
            // Represents the LIGHT of the bar, not its milky shell — pure saturated LEDColor,
            // never the plastic BaseColor, darkened/faded relative to the real bar. Mirrored
            // EXACTLY across waterLevelLocalY (never across the bar's own possibly-offset base),
            // then horizonReflectionExtraOffset is added as a separate, purely cosmetic nudge —
            // overlap-prevention comes entirely from horizonBarOffsetAboveWater being clamped >= 0
            // above, not from anything here.
            var rt = _reflT[c];
            if (reflectionsOn)
            {
                _reflR[c].enabled = true;
                float reflYCenter = (2f * waterLevelLocalY - yCenter) + _config.horizonReflectionExtraOffset;
                rt.localPosition = dir * radius + Vector3.up * reflYCenter;
                rt.localRotation = rot;
                rt.localScale    = new Vector3(width, height * reflStretchY, depth);

                Color reflColor = (hazedLED * (1f + emissionAmount)) * reflBrightness;
                _mpb.Clear();
                _mpb.SetColor(ReflColorID, reflColor);
                _reflR[c].SetPropertyBlock(_mpb);
            }
            else
            {
                _reflR[c].enabled = false;
            }
        }
    }

    private void OnDestroy() => DestroyBarObjects();

    /// <summary>Unity's own built-in cube primitive mesh, copied once — guaranteed-correct
    /// winding/normals/UVs with zero hand-derived geometry risk, shared by every bar AND its
    /// reflection (never per-bar/per-instance).</summary>
    private static Mesh BuildCubeMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
        mesh.name = "HorizonBarCube";
        Object.Destroy(temp);
        return mesh;
    }
}
