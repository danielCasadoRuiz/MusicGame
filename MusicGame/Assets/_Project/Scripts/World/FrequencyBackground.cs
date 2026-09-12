using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// "Frequency Horizon": a STATIC curved LED-grid mesh reproducing the old 2D equalizer's exact
/// look (green/yellow/red tiers by ROW, small black border per cell, off cells fully
/// transparent) — geometry never deforms with amplitude; only which cells are lit (color/alpha)
/// changes. Behaves like a distant skybox: follows the CAMERA's XZ position and yaw only (never
/// the player/road/MusicDistance), so it shows no parallax and always occupies roughly the same
/// left-to-right screen area. A separate flat annular-sector mesh on the water plane fakes
/// "light reflected on water" per column (not a mirrored copy of the grid). Purely visual —
/// never touches gameplay, collision, scoring, or the sky/background-color system
/// (MusicEnvironmentController remains authoritative for that).
///
/// Data: reads the SAME MusicWorldManager.NormalizedBandValue(band, time) the ground mesh
/// already uses, remapped onto Bar Count columns (SampleBandRange) — never a second analysis.
/// Color reuses the exact same green/yellow/red fields (lowEnergyColor/midEnergyColor/
/// highEnergyColor) the ground mesh's VuColor already uses.
///
/// Cost: 3 meshes total (horizon LED grid, reflection fan, water quad) — 3 GameObjects, period.
/// Geometry (vertex positions/UVs/triangles) is only rebuilt when a STRUCTURAL parameter changes
/// (Bar Count, Vertical Block Count, Arc Span, Arc Radius, Grid Height, Reflection Length — all
/// cheap dirty-checks, not per-frame allocations); every normal frame only overwrites the
/// existing color arrays (mesh.colors) and moves the 3 transforms to track the camera. No
/// RenderTexture, no planar reflection, no reflection probe, no extra camera, no
/// Instantiate/Destroy per frame.
///
/// Created dynamically via GetOrCreate(GameObject host) — no scene GameObject/prefab needed.
/// </summary>
public class FrequencyBackground : MonoBehaviour
{
    public static FrequencyBackground Instance { get; private set; }

    public static FrequencyBackground GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<FrequencyBackground>();
    }

    private GameplayConfig _config;
    private Camera _cam;

    private Transform _horizonGO;
    private Transform _reflectionGO;
    private Transform _waterGO;

    private Mesh _horizonMesh;
    private Mesh _reflectionMesh;
    private Mesh _waterMesh;

    private Material _horizonMaterial;
    private Material _reflectionMaterial;
    private Material _waterMaterial;

    // Reused every frame — only mutated in place, never reallocated except on a topology rebuild.
    private Color[] _horizonColors;
    private Color[] _reflectionColors;
    private float[] _smoothedNorm;

    // Horizon topology dirty-check state
    private int   _hBarCount = -1, _hVertBlocks = -1;
    private float _hArcSpan = float.NaN, _hArcRadius = float.NaN, _hGridHeight = float.NaN;

    // Reflection topology dirty-check state
    private int   _rBarCount = -1;
    private float _rArcRadius = float.NaN, _rReflectionLength = float.NaN;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(GameplayConfig config)
    {
        _config = config;
        if (!config.enableFrequencyBackground) return;

        EnsureBuilt();
        EnsureBloom();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void EnsureBuilt()
    {
        if (_horizonGO != null) return;

        var ledShader        = Shader.Find("MusicGame/FrequencyHorizonLED");
        var reflectionShader = Shader.Find("MusicGame/FrequencyReflection");
        var waterShader      = Shader.Find("MusicGame/CheapWater");

        _horizonMaterial     = new Material(ledShader) { name = "FreqBg_HorizonLED" };
        _reflectionMaterial  = new Material(reflectionShader) { name = "FreqBg_Reflection" };
        _waterMaterial       = new Material(waterShader) { name = "FreqBg_Water" };

        // Ordinary transparent queues (never Overlay/4000+) — water first, then reflection, then
        // the horizon — so back-to-front distance sorting among these near-coplanar transparent
        // surfaces can't hide one behind another. Opaque road/player geometry is drawn (and its
        // depth written) long before any of this, and every shader here uses explicit
        // ZTest LEqual (never Always), so opaque geometry in front always correctly occludes these.
        _waterMaterial.renderQueue      = 3000;
        _reflectionMaterial.renderQueue = 3001;
        _horizonMaterial.renderQueue    = 3002;

        var horizonGO = new GameObject("FrequencyHorizon");
        horizonGO.transform.SetParent(transform, false);
        horizonGO.AddComponent<MeshFilter>().sharedMesh = _horizonMesh = new Mesh { name = "FreqBg_HorizonLED" };
        var horizonMR = horizonGO.AddComponent<MeshRenderer>();
        horizonMR.sharedMaterial = _horizonMaterial;
        horizonMR.shadowCastingMode = ShadowCastingMode.Off;
        horizonMR.receiveShadows = false;
        _horizonGO = horizonGO.transform;

        var reflGO = new GameObject("FrequencyHorizon_Reflection");
        reflGO.transform.SetParent(transform, false);
        reflGO.AddComponent<MeshFilter>().sharedMesh = _reflectionMesh = new Mesh { name = "FreqBg_Reflection" };
        var reflMR = reflGO.AddComponent<MeshRenderer>();
        reflMR.sharedMaterial = _reflectionMaterial;
        reflMR.shadowCastingMode = ShadowCastingMode.Off;
        reflMR.receiveShadows = false;
        _reflectionGO = reflGO.transform;

        var waterGO = new GameObject("FrequencyHorizon_Water");
        waterGO.transform.SetParent(transform, false);
        _waterMesh = BuildWaterQuad();
        waterGO.AddComponent<MeshFilter>().sharedMesh = _waterMesh;
        var waterMR = waterGO.AddComponent<MeshRenderer>();
        waterMR.sharedMaterial = _waterMaterial;
        waterMR.shadowCastingMode = ShadowCastingMode.Off;
        waterMR.receiveShadows = false;
        _waterGO = waterGO.transform;

        EnsureHorizonTopologyUpToDate();
        EnsureReflectionTopologyUpToDate();
        ApplyWaterMaterialValues();
    }

    // ── Horizon: static LED-grid geometry ───────────────────────────────────────

    private void EnsureHorizonTopologyUpToDate()
    {
        int barCount   = Mathf.Max(1, _config.freqBgBarCount);
        int vertBlocks = Mathf.Max(1, _config.freqBgVerticalBlockCount);

        if (barCount == _hBarCount && vertBlocks == _hVertBlocks &&
            Mathf.Approximately(_hArcSpan, _config.freqBgArcSpanDegrees) &&
            Mathf.Approximately(_hArcRadius, _config.freqBgArcRadius) &&
            Mathf.Approximately(_hGridHeight, _config.freqBgGridHeight))
            return;

        int cells = barCount * vertBlocks;
        var verts   = new Vector3[cells * 4];
        var uvs     = new Vector2[cells * 4];
        var colors  = new Color[cells * 4];
        var tris    = new int[cells * 6];

        float halfSpan  = _config.freqBgArcSpanDegrees * 0.5f * Mathf.Deg2Rad;
        float radius    = _config.freqBgArcRadius;
        float blockH    = _config.freqBgGridHeight / vertBlocks;

        int vi = 0, ti = 0;
        for (int c = 0; c < barCount; c++)
        {
            float aStart = Mathf.Lerp(-halfSpan, halfSpan, (float)c / barCount);
            float aEnd   = Mathf.Lerp(-halfSpan, halfSpan, (float)(c + 1) / barCount);

            Vector3 dirStart = new Vector3(Mathf.Sin(aStart) * radius, 0f, Mathf.Cos(aStart) * radius);
            Vector3 dirEnd   = new Vector3(Mathf.Sin(aEnd)   * radius, 0f, Mathf.Cos(aEnd)   * radius);

            for (int r = 0; r < vertBlocks; r++)
            {
                float yBot = r * blockH;
                float yTop = (r + 1) * blockH;
                Color tier = RowTierColor(r, vertBlocks);

                int b = vi;
                verts[b + 0] = dirStart + Vector3.up * yBot;
                verts[b + 1] = dirEnd   + Vector3.up * yBot;
                verts[b + 2] = dirStart + Vector3.up * yTop;
                verts[b + 3] = dirEnd   + Vector3.up * yTop;

                uvs[b + 0] = new Vector2(0f, 0f);
                uvs[b + 1] = new Vector2(1f, 0f);
                uvs[b + 2] = new Vector2(0f, 1f);
                uvs[b + 3] = new Vector2(1f, 1f);

                // rgb = this row's tier color (fixed forever); alpha (lit/off) is set every frame.
                colors[b + 0] = colors[b + 1] = colors[b + 2] = colors[b + 3] = tier;

                tris[ti + 0] = b + 0; tris[ti + 1] = b + 2; tris[ti + 2] = b + 1;
                tris[ti + 3] = b + 1; tris[ti + 4] = b + 2; tris[ti + 5] = b + 3;

                vi += 4; ti += 6;
            }
        }

        _horizonMesh.Clear();
        _horizonMesh.vertices  = verts;
        _horizonMesh.uv        = uvs;
        _horizonMesh.colors    = colors;
        _horizonMesh.triangles = tris;
        _horizonMesh.RecalculateBounds();
        _horizonMesh.MarkDynamic();

        _horizonColors = colors;

        _hBarCount = barCount; _hVertBlocks = vertBlocks;
        _hArcSpan = _config.freqBgArcSpanDegrees; _hArcRadius = _config.freqBgArcRadius; _hGridHeight = _config.freqBgGridHeight;
    }

    // Reuse the old 2D equalizer's exact tier split (bottom 60% green, next 20% yellow, top 20%
    // red) and the SAME config colors the ground mesh's VuColor uses — never a per-value lerp
    // for the LED grid, since real LED banks are discrete tiers, not a continuous gradient.
    private Color RowTierColor(int row, int vertBlocks)
    {
        float frac = (row + 0.5f) / vertBlocks;
        if (frac < 0.6f) return _config.lowEnergyColor;
        return frac < 0.8f ? _config.midEnergyColor : _config.highEnergyColor;
    }

    // Continuous version (for the reflection fan, which reads as blended/diffused light rather
    // than discrete LEDs) — same three config colors, same formula MusicWorldManager.VuColor uses.
    private Color BandColorContinuous(float t) => t < 0.5f
        ? Color.Lerp(_config.lowEnergyColor, _config.midEnergyColor, t * 2f)
        : Color.Lerp(_config.midEnergyColor, _config.highEnergyColor, (t - 0.5f) * 2f);

    // ── Reflection: static flat annular-sector geometry ─────────────────────────

    private void EnsureReflectionTopologyUpToDate()
    {
        int barCount = Mathf.Max(1, _config.freqBgBarCount);

        if (barCount == _rBarCount &&
            Mathf.Approximately(_rArcRadius, _config.freqBgArcRadius) &&
            Mathf.Approximately(_rReflectionLength, _config.freqBgReflectionLength))
            return;

        var verts  = new Vector3[barCount * 4];
        var colors = new Color[barCount * 4];
        var tris   = new int[barCount * 6];

        float halfSpan = _config.freqBgArcSpanDegrees * 0.5f * Mathf.Deg2Rad;
        float farR  = _config.freqBgArcRadius;
        float nearR = farR * (1f - Mathf.Clamp01(_config.freqBgReflectionLength));

        int vi = 0, ti = 0;
        for (int c = 0; c < barCount; c++)
        {
            float aStart = Mathf.Lerp(-halfSpan, halfSpan, (float)c / barCount);
            float aEnd   = Mathf.Lerp(-halfSpan, halfSpan, (float)(c + 1) / barCount);

            int b = vi;
            // Index 0/1 = FAR row (at the horizon, full opacity potential); 2/3 = NEAR row
            // (toward the camera, always alpha 0) — "extends from the horizon toward the camera".
            verts[b + 0] = new Vector3(Mathf.Sin(aStart) * farR,  0f, Mathf.Cos(aStart) * farR);
            verts[b + 1] = new Vector3(Mathf.Sin(aEnd)   * farR,  0f, Mathf.Cos(aEnd)   * farR);
            verts[b + 2] = new Vector3(Mathf.Sin(aStart) * nearR, 0f, Mathf.Cos(aStart) * nearR);
            verts[b + 3] = new Vector3(Mathf.Sin(aEnd)   * nearR, 0f, Mathf.Cos(aEnd)   * nearR);

            tris[ti + 0] = b + 0; tris[ti + 1] = b + 2; tris[ti + 2] = b + 1;
            tris[ti + 3] = b + 1; tris[ti + 4] = b + 2; tris[ti + 5] = b + 3;

            vi += 4; ti += 6;
        }

        _reflectionMesh.Clear();
        _reflectionMesh.vertices  = verts;
        _reflectionMesh.colors    = colors;
        _reflectionMesh.triangles = tris;
        _reflectionMesh.RecalculateBounds();
        _reflectionMesh.MarkDynamic();

        _reflectionColors = colors;

        _rBarCount = barCount; _rArcRadius = _config.freqBgArcRadius; _rReflectionLength = _config.freqBgReflectionLength;
    }

    // ── Per-frame update: colors/alpha + camera-relative placement only ────────

    private void Update()
    {
        if (_config == null || !_config.enableFrequencyBackground)
        {
            if (_horizonGO != null) SetActive(false);
            return;
        }
        if (_horizonGO == null) EnsureBuilt();
        SetActive(true);

        EnsureHorizonTopologyUpToDate();
        EnsureReflectionTopologyUpToDate();
        if (_smoothedNorm == null || _smoothedNorm.Length != _hBarCount)
            _smoothedNorm = new float[_hBarCount];

        var world = MusicWorldManager.Instance;
        var clock = MusicClock.Instance;
        if (world == null || clock == null) return;

        _cam ??= Camera.main;
        if (_cam == null) return;

        int barCount     = _hBarCount;
        int vertBlocks   = _hVertBlocks;
        int realBandCount = Mathf.Max(1, world.FrequencyBandsUsed);

        float dt        = Time.deltaTime;
        float smoothing = Mathf.Clamp01(_config.freqBgSmoothing);
        float lerpAlpha = 1f - Mathf.Pow(smoothing, Mathf.Max(dt, 0.0001f) * 60f);
        float gain      = Mathf.Max(0f, _config.freqBgGain);
        float emission  = Mathf.Max(0f, _config.freqBgEmission);

        for (int c = 0; c < barCount; c++)
        {
            float tCenter = (c + 0.5f) / barCount;
            float bandF   = tCenter * (realBandCount - 1);
            float rawNorm = SampleBandRange(world, bandF, realBandCount, barCount, clock.SongTime);
            float gained  = Mathf.Clamp01(rawNorm * gain);

            _smoothedNorm[c] = Mathf.Lerp(_smoothedNorm[c], gained, lerpAlpha);
            float norm = _smoothedNorm[c];

            // Horizon: light the bottom `litCount` LED blocks — geometry untouched, only alpha.
            int litCount = Mathf.Clamp(Mathf.RoundToInt(norm * vertBlocks), 0, vertBlocks);
            for (int r = 0; r < vertBlocks; r++)
            {
                int idx = (c * vertBlocks + r) * 4;
                bool lit = r < litCount;
                Color tier = RowTierColor(r, vertBlocks);
                Color rgb = lit ? tier + tier * emission : tier;
                rgb.a = lit ? 0.92f : 0f;
                _horizonColors[idx + 0] = _horizonColors[idx + 1] = _horizonColors[idx + 2] = _horizonColors[idx + 3] = rgb;
            }

            // Reflection: one blended color per column, fading from full (at the horizon) to
            // zero (toward the camera) — a light-on-water look, not a mirrored LED copy.
            Color reflColor = BandColorContinuous(norm);
            Color reflEmissive = reflColor + reflColor * (emission * norm);
            float opacity = Mathf.Clamp01(_config.freqBgReflectionOpacity) * norm;

            Color farC = reflEmissive; farC.a = opacity;
            Color nearC = reflEmissive; nearC.a = 0f;

            int ri = c * 4;
            _reflectionColors[ri + 0] = _reflectionColors[ri + 1] = farC;
            _reflectionColors[ri + 2] = _reflectionColors[ri + 3] = nearC;
        }

        _horizonMesh.colors = _horizonColors;
        _reflectionMesh.colors = _reflectionColors;

        // Cheap (a handful of SetFloat/SetColor calls, no allocation) — keeps every material-
        // level tunable (border size, water look, reflection distortion) live-editable in the
        // Inspector during Play without needing a restart.
        ApplyWaterMaterialValues();

        // Camera-relative placement — the whole point of this system: it must never track the
        // player/road/MusicDistance, only the camera's own XZ position and yaw, so it behaves
        // like a distant, parallax-free skybox instead of a world-anchored object.
        Vector3 camPos = _cam.transform.position;
        Quaternion yawOnly = Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f);

        _horizonGO.SetPositionAndRotation(
            new Vector3(camPos.x, camPos.y + _config.freqBgHorizonVerticalOffset, camPos.z), yawOnly);
        _reflectionGO.SetPositionAndRotation(
            new Vector3(camPos.x, camPos.y + _config.freqBgWaterLevel, camPos.z), yawOnly);

        float waterSize = Mathf.Max(10f, _config.freqBgArcRadius * 6f);
        _waterGO.position = new Vector3(camPos.x, camPos.y + _config.freqBgWaterLevel - 0.02f, camPos.z);
        _waterGO.rotation = Quaternion.Euler(90f, 0f, 0f); // lies flat (double-sided, so facing sign doesn't matter)
        _waterGO.localScale = new Vector3(waterSize, waterSize, 1f);
    }

    private void SetActive(bool active)
    {
        if (_horizonGO != null && _horizonGO.gameObject.activeSelf != active) _horizonGO.gameObject.SetActive(active);
        if (_reflectionGO != null && _reflectionGO.gameObject.activeSelf != active) _reflectionGO.gameObject.SetActive(active);
        if (_waterGO != null && _waterGO.gameObject.activeSelf != active) _waterGO.gameObject.SetActive(active);
    }

    /// <summary>Averages NormalizedBandValue over the real-band range this visual column covers
    /// — remapping analysis resolution to display resolution without ever re-analyzing audio.</summary>
    private float SampleBandRange(MusicWorldManager world, float bandCenter, int realBandCount, int barCount, float time)
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

    // ── Water quad (procedural, built once) ─────────────────────────────────────

    private static Mesh BuildWaterQuad()
    {
        var mesh = new Mesh { name = "FreqBg_WaterQuad" };
        var verts = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
        };
        var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        var tris = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ApplyWaterMaterialValues()
    {
        float d = Mathf.Clamp01(_config.freqBgWaterDarkness);
        Color waterColor = Color.Lerp(new Color(0.05f, 0.10f, 0.16f, 0.92f), new Color(0.005f, 0.01f, 0.03f, 0.95f), d);
        _waterMaterial.SetColor("_BaseColor", waterColor);
        _waterMaterial.SetFloat("_RippleScale", _config.freqBgWaterNoiseScale);
        _waterMaterial.SetFloat("_RippleStrength", _config.freqBgWaveStrength);
        _waterMaterial.SetFloat("_RippleSpeed", _config.freqBgWaveSpeed);
        _reflectionMaterial.SetFloat("_Distortion", _config.freqBgReflectionDistortion);
        _horizonMaterial.SetFloat("_BorderSize", _config.freqBgLedBorderSize);
    }

    private void EnsureBloom()
    {
        ApplyWaterMaterialValues();

        var cam = Camera.main;
        if (cam == null) return;

        var camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null) camData.renderPostProcessing = true;

        if (FindFirstObjectByType<Volume>() != null) return; // don't fight an existing volume setup

        var volumeGO = new GameObject("FrequencyBackground_Bloom");
        volumeGO.transform.SetParent(transform, false);
        var volume = volumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(0.85f);
        bloom.scatter.Override(0.6f);
        // Without tonemapping, HDR emission just clips per-channel at 1.0 — a bright color
        // clips toward flat white instead of a bright, still-hued color. Neutral tonemapping
        // compresses highlights while preserving hue.
        var tonemap = profile.Add<Tonemapping>(true);
        tonemap.mode.Override(TonemappingMode.Neutral);
        volume.profile = profile;
    }
}
