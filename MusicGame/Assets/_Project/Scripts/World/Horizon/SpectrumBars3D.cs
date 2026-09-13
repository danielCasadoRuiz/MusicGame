using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Real volumetric spectrum-analyzer bars (box geometry, not flat LED quads) arranged in an arc
/// inside the Horizon World — parented under HorizonCameraController.Instance.HorizonRoot, so they
/// sit at a fixed position independent of the Player/gameplay camera.
///
/// Data: reuses the SAME MusicWorldManager.NormalizedBandValue(band, time) the ground mesh and the
/// old FrequencyBackground both already used, averaged over each bar's own band range — never a
/// second analysis.
///
/// amplitude (0..1) drives BOTH height and color from the SAME value, independently:
///   amplitude -> Mathf.Lerp(minHeight, maxHeight, amplitude)      (height)
///   amplitude -> horizonBarAmplitudeGradient.Evaluate(amplitude)  (color)
/// Never the other way around (color is never derived from the final height). The water's
/// reflection no longer reads BarColors directly — HorizonBarsReflectionCamera captures this
/// mesh's own rendered pixels into a RenderTexture instead, so it's automatically exact.
///
/// One shared Mesh + one shared Material for every bar (no per-bar GameObject, no per-bar Material
/// instance) — a single draw call for the whole spectrum, cheaper than even GPU-instancing this
/// many small boxes would be. Geometry (all vertex positions, since bar HEIGHT changes every
/// frame) and vertex colors are both rewritten in place into cached arrays each frame — no
/// per-frame heap allocation beyond Unity's own Mesh API bookkeeping.
/// </summary>
public class SpectrumBars3D : MonoBehaviour
{
    private const int FacesPerBar = 4;   // front (inner), top, left, right — back/bottom are never visible
    private const int VertsPerFace = 4;
    private const int VertsPerBar = FacesPerBar * VertsPerFace;
    private const int TrisPerFace = 2;
    private const int IndicesPerBar = FacesPerBar * TrisPerFace * 3;

    private GameplayConfig _config;
    private Mesh     _mesh;
    private Material _material;

    private Vector3[] _verts;
    private Color[]   _colors;
    private int[]     _tris;

    private float[] _smoothedAmplitude;
    private Color[] _barColor;   // one solid color per bar, this frame — exposed for debug/HUD use

    private int   _builtBarCount = -1;
    private float _builtArcSpan = float.NaN, _builtArcRadius = float.NaN, _builtWidthFrac = float.NaN, _builtDepth = float.NaN;

    public IReadOnlyList<Color> BarColors => _barColor;
    public int BarCount => _config != null ? Mathf.Max(1, _config.horizonBarCount) : 0;

    public void Initialize(GameplayConfig config, Transform root)
    {
        _config = config;

        // Prefer the dedicated bars-only layer (lets HorizonBarsReflectionCamera cull to just
        // this mesh) — falls back to the general Horizon layer if that optional layer doesn't
        // exist, same graceful-degrade pattern used everywhere else in this system.
        int barsLayer = HorizonCameraController.Instance != null ? HorizonCameraController.Instance.BarsLayer : -1;
        int layer = barsLayer >= 0 ? barsLayer : LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);

        var shader = Shader.Find("MusicGame/HorizonBar") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _material  = new Material(shader) { name = "Horizon_Bars" };

        var go = new GameObject("SpectrumBars3D");
        go.layer = Mathf.Max(0, layer);
        go.transform.SetParent(root, false);

        _mesh = new Mesh { name = "Horizon_Bars" };
        _mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        RebuildTopologyIfNeeded();
    }

    private void RebuildTopologyIfNeeded()
    {
        int barCount = Mathf.Max(1, _config.horizonBarCount);
        if (barCount == _builtBarCount &&
            Mathf.Approximately(_builtArcSpan, _config.horizonArcSpanDegrees) &&
            Mathf.Approximately(_builtArcRadius, _config.horizonArcRadius) &&
            Mathf.Approximately(_builtWidthFrac, _config.horizonBarWidthFraction) &&
            Mathf.Approximately(_builtDepth, _config.horizonBarDepth))
            return;

        _verts  = new Vector3[barCount * VertsPerBar];
        _colors = new Color[barCount * VertsPerBar];
        _tris   = new int[barCount * IndicesPerBar];

        for (int c = 0; c < barCount; c++)
        {
            int vi = c * VertsPerBar, ti = c * IndicesPerBar;
            for (int f = 0; f < FacesPerBar; f++)
                FillQuadIndices(_tris, ti + f * TrisPerFace * 3, vi + f * VertsPerFace);
        }

        _smoothedAmplitude = new float[barCount];
        _barColor           = new Color[barCount];

        _builtBarCount = barCount;
        _builtArcSpan = _config.horizonArcSpanDegrees;
        _builtArcRadius = _config.horizonArcRadius;
        _builtWidthFrac = _config.horizonBarWidthFraction;
        _builtDepth = _config.horizonBarDepth;

        // Clear() first — reassigning a SMALLER vertex array while the mesh still holds the
        // previous (possibly larger) triangle indices would throw; Clear() drops both safely
        // before either is set again below.
        _mesh.Clear();
        _mesh.vertices  = _verts;
        _mesh.colors    = _colors;
        _mesh.triangles = _tris;
    }

    private static void FillQuadIndices(int[] tris, int t, int b)
    {
        tris[t + 0] = b + 0; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
        tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
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

        RebuildTopologyIfNeeded();

        int barCount      = _builtBarCount;
        int realBandCount = Mathf.Max(1, world.FrequencyBandsUsed);
        float halfSpan    = _config.horizonArcSpanDegrees * 0.5f * Mathf.Deg2Rad;
        float innerR      = _config.horizonArcRadius;
        float outerR      = innerR + Mathf.Max(0.01f, _config.horizonBarDepth);
        float gain        = Mathf.Max(0f, _config.horizonBarGain);
        float dt          = Time.deltaTime;
        float attackAlpha  = 1f - Mathf.Pow(Mathf.Clamp01(_config.horizonBarAttack), Mathf.Max(dt, 0.0001f) * 60f);
        float releaseAlpha = 1f - Mathf.Pow(Mathf.Clamp01(_config.horizonBarRelease), Mathf.Max(dt, 0.0001f) * 60f);

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
            Color baseColor = _config.horizonBarAmplitudeGradient.Evaluate(current);
            float emissionAmount = Mathf.Min(
                _config.horizonBarMaxEmission,
                _config.horizonBarBaseEmission + current * _config.horizonBarAmplitudeEmissionBoost);
            Color emissive = baseColor * (1f + emissionAmount);
            _barColor[c] = emissive; // pre-haze, exposed for debug/HUD use

            float height = Mathf.Lerp(_config.horizonBarMinHeight, _config.horizonBarMaxHeight, current);

            float aCenter = Mathf.Lerp(-halfSpan, halfSpan, tCenter);
            float slice   = (halfSpan * 2f / barCount) * 0.5f * Mathf.Clamp01(_config.horizonBarWidthFraction);
            float a0 = aCenter - slice, a1 = aCenter + slice;

            Vector3 dirA0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
            Vector3 dirA1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));

            float yBase = _config.horizonBarVerticalOffset;
            float yTop  = yBase + height;

            Vector3 innerBL = dirA0 * innerR + Vector3.up * yBase;
            Vector3 innerBR = dirA1 * innerR + Vector3.up * yBase;
            Vector3 innerTL = dirA0 * innerR + Vector3.up * yTop;
            Vector3 innerTR = dirA1 * innerR + Vector3.up * yTop;
            Vector3 outerBL = dirA0 * outerR + Vector3.up * yBase;
            Vector3 outerBR = dirA1 * outerR + Vector3.up * yBase;
            Vector3 outerTL = dirA0 * outerR + Vector3.up * yTop;
            Vector3 outerTR = dirA1 * outerR + Vector3.up * yTop;

            int vi = c * VertsPerBar;

            // Face 0: front (inner, facing the camera/center)
            _verts[vi + 0] = innerBL; _verts[vi + 1] = innerBR; _verts[vi + 2] = innerTL; _verts[vi + 3] = innerTR;
            // Face 1: top
            _verts[vi + 4] = innerTL; _verts[vi + 5] = innerTR; _verts[vi + 6] = outerTL; _verts[vi + 7] = outerTR;
            // Face 2: left side
            _verts[vi + 8] = innerBL; _verts[vi + 9] = outerBL; _verts[vi + 10] = innerTL; _verts[vi + 11] = outerTL;
            // Face 3: right side
            _verts[vi + 12] = innerBR; _verts[vi + 13] = outerBR; _verts[vi + 14] = innerTR; _verts[vi + 15] = outerTR;

            // Cheap fake-bevel: a flat per-face brightness multiplier baked straight into vertex
            // color (unlit shader — no real lighting/normals needed for this to read as volume).
            // Haze is applied AFTER the bevel, at the bar's OWN base height (closest to the
            // horizon line) — see HorizonHaze's own doc for why this is baked here instead of a
            // shader/global-uniform pass.
            Color top   = HorizonHaze.Apply(emissive * 1.15f, yBase, _config); top.a = 1f;
            Color front = HorizonHaze.Apply(emissive,         yBase, _config); front.a = 1f;
            Color side  = HorizonHaze.Apply(emissive * 0.82f, yBase, _config); side.a = 1f;

            _colors[vi + 0] = _colors[vi + 1] = _colors[vi + 2] = _colors[vi + 3] = front;
            _colors[vi + 4] = _colors[vi + 5] = _colors[vi + 6] = _colors[vi + 7] = top;
            _colors[vi + 8] = _colors[vi + 9] = _colors[vi + 10] = _colors[vi + 11] = side;
            _colors[vi + 12] = _colors[vi + 13] = _colors[vi + 14] = _colors[vi + 15] = side;
        }

        _mesh.vertices = _verts;
        _mesh.colors   = _colors;
        _mesh.RecalculateBounds();
    }
}
