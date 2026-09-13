using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Real volumetric spectrum-analyzer bars arranged in an arc inside the Horizon World — parented
/// under HorizonCameraController.Instance.HorizonRoot, so they sit at a fixed position independent
/// of the Player/gameplay camera.
///
/// Each bar is a genuine per-object cube (shared unit-cube Mesh + shared URP Lit Material, one
/// MeshRenderer per bar) instead of a single hand-baked unlit mesh — this gives real PBR
/// lighting/specular (a "plastic/acrylic" volume, not a flat-shaded neon rectangle) while keeping
/// per-bar BaseColor/EmissionColor fully independent via a single REUSED MaterialPropertyBlock
/// (Clear()'d and refilled per bar, never allocated per frame, never a Material instance per bar).
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
/// Data: reuses the SAME MusicWorldManager.NormalizedBandValue(band, time) the ground mesh and the
/// old FrequencyBackground both already used, averaged over each bar's own band range — never a
/// second analysis.
///
/// amplitude (0..1) drives BOTH height and color from the SAME value, independently:
///   amplitude -> Mathf.Lerp(minHeight, maxHeight, amplitude)      (height)
///   amplitude -> horizonBarAmplitudeGradient.Evaluate(amplitude)  (color)
/// Never the other way around (color is never derived from the final height).
/// </summary>
public class SpectrumBars3D : MonoBehaviour
{
    private static readonly int MetallicID   = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessID = Shader.PropertyToID("_Smoothness");
    private static readonly int BaseColorID     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int ReflColorID        = Shader.PropertyToID("_Color");
    private static readonly int ReflFadeDistanceID = Shader.PropertyToID("_FadeDistance");
    private static readonly int ReflFadeColorID    = Shader.PropertyToID("_FadeColor");
    private static readonly int ReflWaterLevelID   = Shader.PropertyToID("_WaterLevelWorldY");

    private HorizonConfig _config;
    private Transform     _root;
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

        _cubeMesh = BuildCubeMesh();
        _mpb = new MaterialPropertyBlock();

        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        _barMaterial = new Material(litShader) { name = "Horizon_Bars", enableInstancing = true };
        _barMaterial.EnableKeyword("_EMISSION");
        _barMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        var reflShader = Shader.Find("MusicGame/HorizonBarReflection") ?? litShader;
        _reflectionMaterial = new Material(reflShader) { name = "Horizon_BarsReflection", enableInstancing = true };

        ApplyStaticConfig();
        RebuildBarObjectsIfNeeded();
    }

    /// <summary>Pure art-direction (Metallic/Smoothness/reflection fade params) — applied once and
    /// only re-applied on demand (HorizonConfig.devLiveConfigSync). Never per-bar — these are
    /// SHARED material properties, not MaterialPropertyBlock overrides.</summary>
    public void ApplyStaticConfig()
    {
        _barMaterial.SetFloat(MetallicID, Mathf.Clamp01(_config.horizonBarMetallic));
        _barMaterial.SetFloat(SmoothnessID, Mathf.Clamp01(_config.horizonBarSmoothness));

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
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = _cubeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _barMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _barT[c] = go.transform;
            _barR[c] = mr;

            var rgo = new GameObject($"BarReflection_{c}");
            rgo.layer = Mathf.Max(0, layer);
            rgo.transform.SetParent(_root, false);
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

        // Mirror plane, expressed in the SAME local (root-relative) space bar transforms use —
        // HorizonWater.WaterLevelWorldY is the single source of truth, so the two can never drift.
        float waterLevelLocalY = _water != null
            ? _water.WaterLevelWorldY - _root.position.y
            : _config.horizonWaterLevel;

        bool reflectionsOn = _config.horizonReflectionEnabled;
        float reflBrightness = Mathf.Max(0f, _config.horizonReflectionBrightness);
        float reflStretchY   = Mathf.Max(0.01f, _config.horizonReflectionStretchY);

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
            Color gradientColor = _config.horizonBarAmplitudeGradient.Evaluate(current);
            float emissionAmount = Mathf.Min(
                _config.horizonBarMaxEmission,
                _config.horizonBarBaseEmission + current * _config.horizonBarAmplitudeEmissionBoost);

            Color hazedBase = HorizonHaze.Apply(gradientColor, _config.horizonBarVerticalOffset, 0f, macroIntensity, _config);
            hazedBase.a = 1f;
            Color emissiveColor = hazedBase * emissionAmount;
            _barColor[c] = hazedBase * (1f + emissionAmount); // pre-haze-adjusted, exposed for debug/HUD use

            float height = Mathf.Lerp(_config.horizonBarMinHeight, _config.horizonBarMaxHeight, current);

            float aCenter = Mathf.Lerp(-halfSpan, halfSpan, tCenter);
            Vector3 dir = new Vector3(Mathf.Sin(aCenter), 0f, Mathf.Cos(aCenter));
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up); // local Z = radial (depth), local X = tangential (width)

            float slice = (halfSpan * 2f / barCount) * Mathf.Clamp01(_config.horizonBarWidthFraction);
            float chordWidth = 2f * radius * Mathf.Sin(slice * 0.5f);
            float depth = Mathf.Max(0.01f, _config.horizonBarDepth);

            float yBase = _config.horizonBarVerticalOffset;
            float yTop  = yBase + height;
            float yCenter = (yBase + yTop) * 0.5f;

            var t = _barT[c];
            t.localPosition = dir * radius + Vector3.up * yCenter;
            t.localRotation = rot;
            t.localScale    = new Vector3(chordWidth, height, depth);

            _mpb.Clear();
            _mpb.SetColor(BaseColorID, hazedBase);
            _mpb.SetColor(EmissionColorID, emissiveColor);
            _barR[c].SetPropertyBlock(_mpb);

            // ── Reflection bar: mirrored across the water plane, same X/Z, same height ─────────
            var rt = _reflT[c];
            if (reflectionsOn)
            {
                _reflR[c].enabled = true;
                float reflYCenter = 2f * waterLevelLocalY - yCenter;
                rt.localPosition = dir * radius + Vector3.up * reflYCenter;
                rt.localRotation = rot;
                rt.localScale    = new Vector3(chordWidth, height * reflStretchY, depth);

                Color reflColor = (hazedBase * (1f + emissionAmount)) * reflBrightness;
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
