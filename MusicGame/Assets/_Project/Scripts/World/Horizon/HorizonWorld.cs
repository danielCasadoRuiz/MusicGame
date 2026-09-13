using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Thin composition root for the Horizon World — creates and wires HorizonCameraController,
/// HorizonWater, SpectrumBars3D (which also owns the mirrored reflection bars — see its own
/// doc), ProceduralSkyController and HorizonMountainLayers, and drives their per-frame Tick()
/// calls IN A FIXED, DETERMINISTIC ORDER from ITS OWN LateUpdate() — never relying on Unity's
/// ambiguous cross-script Update/LateUpdate ordering. Deliberately does none of the actual visual
/// work itself (positions/meshes/shaders/camera stacking all live in their own single-
/// responsibility component) — this is just the one place GameplayManager talks to, and the one
/// place that decides WHEN each of those runs.
///
/// Order matters: HorizonCameraController.Tick() (positions the Horizon Camera for THIS frame)
/// runs FIRST since HorizonMountainLayers' parallax offset depends on its CameraDelta being
/// current. [DefaultExecutionOrder] guarantees this whole LateUpdate also runs AFTER
/// CameraFollow's (which moves the real gameplay Main Camera that HorizonCameraController reads
/// from), since two different MonoBehaviours' default LateUpdate order is otherwise undefined.
///
/// Created dynamically via GetOrCreate(GameObject host) — no scene GameObject/prefab needed, same
/// pattern the rest of this project already uses.
/// </summary>
[DefaultExecutionOrder(500)]
public class HorizonWorld : MonoBehaviour
{
    public static HorizonWorld Instance { get; private set; }

    public static HorizonWorld GetOrCreate(GameObject host)
    {
        if (Instance != null) return Instance;
        return host.AddComponent<HorizonWorld>();
    }

    private HorizonConfig _config;
    private HorizonCameraController      _cameraController;
    private HorizonWater                 _water;
    private SpectrumBars3D               _bars;
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

    public void Initialize(HorizonConfig config)
    {
        _config = config;
        if (!config.enableHorizonWorld) return;

        _cameraController = gameObject.AddComponent<HorizonCameraController>();
        _cameraController.Initialize(config);
        if (!_cameraController.IsActive) return; // e.g. missing "Horizon" layer — already logged

        var root = _cameraController.HorizonRoot;

        // Water FIRST — SpectrumBars3D reads its WaterLevelWorldY (single source of truth) to
        // mirror each bar's own reflection bar across the same plane the water actually renders
        // at, so the two can never drift apart.
        _water = gameObject.AddComponent<HorizonWater>();
        _water.Initialize(config, root);

        _bars = gameObject.AddComponent<SpectrumBars3D>();
        _bars.Initialize(config, root, _water);

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

    private void LateUpdate()
    {
        if (_cameraController == null || !_cameraController.IsActive) return;

        var world = MusicWorldManager.Instance;
        var clock = MusicClock.Instance;
        if (world == null || clock == null) return;

        // Order is deliberate — see class doc. Camera FIRST (HorizonMountainLayers' parallax
        // depends on its CameraDelta being current this frame), everything else after.
        _cameraController.Tick();
        _water.Tick();
        _bars.Tick(world, clock.SongTime);
        _sky.Tick();
        _mountains.Tick();
    }
}
