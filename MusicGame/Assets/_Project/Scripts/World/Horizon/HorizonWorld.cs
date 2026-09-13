using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Thin composition root for the Horizon World — creates and wires HorizonCameraController,
/// SpectrumBars3D, HorizonBarsReflectionCamera, HorizonWater, ProceduralSkyController and
/// HorizonMountainLayers, and drives their per-frame Tick() calls. Deliberately does none of the
/// actual work itself (positions/meshes/shaders/camera stacking all live in their own single-
/// responsibility component) — this is just the one place GameplayManager talks to.
///
/// Created dynamically via GetOrCreate(GameObject host) — no scene GameObject/prefab needed, same
/// pattern the rest of this project already uses.
/// </summary>
public class HorizonWorld : MonoBehaviour
{
    public static HorizonWorld Instance { get; private set; }

    public static HorizonWorld GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<HorizonWorld>();
    }

    private GameplayConfig _config;
    private HorizonCameraController      _cameraController;
    private SpectrumBars3D               _bars;
    private HorizonBarsReflectionCamera  _reflectionCamera;
    private HorizonWater                 _water;
    private ProceduralSkyController      _sky;
    private HorizonMountainLayers        _mountains;

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
        if (!config.enableHorizonWorld) return;

        _cameraController = gameObject.AddComponent<HorizonCameraController>();
        _cameraController.Initialize(config);
        if (!_cameraController.IsActive) return; // e.g. missing "Horizon" layer — already logged

        var root = _cameraController.HorizonRoot;

        _bars = gameObject.AddComponent<SpectrumBars3D>();
        _bars.Initialize(config, root);

        _reflectionCamera = gameObject.AddComponent<HorizonBarsReflectionCamera>();
        _reflectionCamera.Initialize(config, root, _cameraController.HorizonCamera);

        _water = gameObject.AddComponent<HorizonWater>();
        _water.Initialize(config, root, _reflectionCamera);

        _sky = gameObject.AddComponent<ProceduralSkyController>();
        _sky.Initialize(config, _cameraController.HorizonCamera);

        _mountains = gameObject.AddComponent<HorizonMountainLayers>();
        _mountains.Initialize(config, root);

        EnsureBloom();
    }

    // Bloom is what turns the bars'/sun's HDR emission into an actual neon glow. Enabled on the
    // Main Camera (now the Overlay that composites the final stacked image, so its post-processing
    // covers both the gameplay layer AND the Horizon layer beneath it) — NOT on the Horizon Camera
    // itself (HorizonCameraController already turns renderPostProcessing off there, avoiding
    // applying Bloom twice).
    //
    // IMPORTANT: unlike a previous version of this method, this ALWAYS applies config's Bloom
    // values onto whichever Volume/profile the scene actually uses (creating one only if truly
    // none exists) — a hand-authored "Global Volume" with its own default/weak Bloom settings
    // (e.g. threshold=1, intensity=0.25) used to make the whole HDR-emission setup silently do
    // nothing, since the old code bailed out the moment ANY Volume already existed. Bloom is not
    // optional here — it's the entire "very dark world + bright neon" visual direction.
    private void EnsureBloom()
    {
        var mainCam = Camera.main;
        if (mainCam == null) return;

        var camData = mainCam.GetUniversalAdditionalCameraData();
        if (camData != null) camData.renderPostProcessing = true;

        if (!_config.horizonBloomForceApply) return;

        var volume = FindFirstObjectByType<Volume>();
        VolumeProfile profile;
        if (volume == null)
        {
            var volumeGO = new GameObject("HorizonWorld_Bloom");
            volumeGO.transform.SetParent(transform, false);
            volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;
        }
        else
        {
            // Mutate whatever profile is already assigned (scene-authored or otherwise) in place
            // — this is a runtime-only change (never saved back to the asset), same as any other
            // Volume override tweak made from script.
            profile = volume.profile;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                volume.profile = profile;
            }
        }

        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.intensity.Override(_config.horizonBloomIntensity);
        bloom.threshold.Override(_config.horizonBloomThreshold);
        bloom.scatter.Override(_config.horizonBloomScatter);

        // Without tonemapping, HDR emission just clips per-channel at 1.0 — a bright color clips
        // toward flat white instead of a bright, still-hued color. Neutral tonemapping compresses
        // highlights while preserving hue.
        if (!profile.TryGet(out Tonemapping tonemap)) tonemap = profile.Add<Tonemapping>(true);
        tonemap.active = true;
        if (tonemap.mode.value == TonemappingMode.None) tonemap.mode.Override(TonemappingMode.Neutral);
    }

    private void Update()
    {
        if (_cameraController == null || !_cameraController.IsActive) return;

        var world = MusicWorldManager.Instance;
        var clock = MusicClock.Instance;
        if (world == null || clock == null) return;

        _bars.Tick(world, clock.SongTime);
        _reflectionCamera.Tick();
        _water.Tick();
        _sky.Tick();
        _mountains.Tick();
    }
}
