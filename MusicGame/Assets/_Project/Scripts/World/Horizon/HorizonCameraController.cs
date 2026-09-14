using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns the separate "Horizon World" — a URP camera stack Base camera that renders ONLY the
/// "Horizon" layer (spectrum bars, reflections, water, procedural sky), sitting at a near-FIXED
/// position that only copies the gameplay Main Camera's PITCH and FOV every frame — never its
/// full translation, and deliberately NEVER its yaw (see Tick() — the path's own heading can
/// drift as it curves, and copying that would visibly swing the whole spectrum arc off-center).
/// The Main Camera itself becomes an Overlay stacked on top of it.
///
/// Why a second camera instead of re-centering geometry on the camera every frame (the old
/// FrequencyBackground approach): re-centering means the geometry has ZERO relative motion
/// against the camera, ever — which reads as "attached to your face" regardless of how far away
/// it visually sits. A genuinely separate camera at a fixed position, only copying view ANGLE, is
/// how a real distant horizon/skybox is actually done — by DEFAULT horizonParallaxFactor is 0, so
/// this camera's position genuinely never moves at all: exactly like a real horizon, no amount of
/// running ever gets you a single unit closer to it. horizonParallaxFactor exists as an OPT-IN if
/// you specifically want a subtle depth cue (a small fraction of the main camera's own translation
/// bleeding through) instead — that offset is hard-capped at a FRACTION of Arc Radius (see Tick())
/// so even with it enabled, a long enough run can't accumulate enough drift to catch up to/overtake
/// the "distant, unreachable" ring of bars.
///
/// HorizonRoot is a SEPARATE, permanently-fixed Transform (not this camera's own, which DOES
/// move a little with parallax) — SpectrumBars3D/HorizonWater/HorizonMountainLayers parent under
/// it, so they sit still in Horizon World space regardless of the camera's subtle parallax drift.
///
/// If enableHorizonWorld is false, Initialize() does nothing at all — the Main Camera is never
/// touched/stacked, so every pre-existing camera-driven system (MusicEnvironmentController's
/// background color included) keeps working exactly as before.
///
/// Created dynamically by GameplayManager — call Initialize() after AddComponent, same pattern as
/// every other subsystem in this project.
/// </summary>
public class HorizonCameraController : MonoBehaviour
{
    public static HorizonCameraController Instance { get; private set; }

    public const string HorizonLayerName = "Horizon";

    public Camera    HorizonCamera { get; private set; }
    public Transform HorizonRoot   { get; private set; }
    public bool       IsActive      { get; private set; }

    private HorizonConfig _config;
    private Camera         _mainCamera;
    private Vector3        _mainCameraStartPos;
    private float          _fixedYaw; // the Horizon World's own heading — NEVER updated after Initialize

    /// <summary>Raw (unscaled) translation of the gameplay Main Camera since Initialize() — other
    /// Horizon World layers (e.g. HorizonMountainLayers) multiply this by their OWN parallax
    /// factor, exactly the same idea HorizonCamera itself uses for horizonParallaxFactor.</summary>
    public Vector3 CameraDelta { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Hand the Main Camera back to Base — otherwise disabling this mid-session (or a domain
        // reload during Play) would leave it stuck as an orphaned Overlay with nothing basing it.
        if (_mainCamera != null)
        {
            var mainData = _mainCamera.GetUniversalAdditionalCameraData();
            if (mainData != null) mainData.renderType = CameraRenderType.Base;
        }
        if (HorizonCamera != null) Destroy(HorizonCamera.gameObject);
        if (HorizonRoot != null) Destroy(HorizonRoot.gameObject);
    }

    public void Initialize(HorizonConfig config)
    {
        _config = config;
        if (!config.enableHorizonWorld) return;

        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            Debug.LogWarning("[HorizonCameraController] No Main Camera found — Horizon World disabled.");
            return;
        }

        int layer = LayerMask.NameToLayer(HorizonLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[HorizonCameraController] Layer '{HorizonLayerName}' doesn't exist in " +
                             "Project Settings > Tags and Layers — Horizon World disabled. Add it (any " +
                             "free user layer slot works) and restart Play.");
            return;
        }

        _mainCameraStartPos = _mainCamera.transform.position;
        _fixedYaw           = _mainCamera.transform.eulerAngles.y;

        var rootGO = new GameObject("[Horizon World]");
        HorizonRoot = rootGO.transform;
        HorizonRoot.position = _mainCameraStartPos;

        int cullMask = 1 << layer;

        var camGO = new GameObject("[Horizon Camera]");
        camGO.transform.SetParent(HorizonRoot, false);
        camGO.transform.position = _mainCameraStartPos;
        HorizonCamera = camGO.AddComponent<Camera>();
        HorizonCamera.clearFlags     = CameraClearFlags.Skybox;
        HorizonCamera.cullingMask    = cullMask;
        HorizonCamera.nearClipPlane  = 0.05f;
        HorizonCamera.farClipPlane   = Mathf.Max(200f, config.horizonArcRadius * 4f);
        HorizonCamera.fieldOfView    = _mainCamera.fieldOfView;
        HorizonCamera.depth          = _mainCamera.depth - 1f;

        var baseData = HorizonCamera.GetUniversalAdditionalCameraData();
        baseData.renderType          = CameraRenderType.Base;
        baseData.renderPostProcessing = false; // Bloom lives on the (stacked) main camera's own Volume

        var mainData = _mainCamera.GetUniversalAdditionalCameraData();
        mainData.renderType = CameraRenderType.Overlay;
        baseData.cameraStack.Clear();
        baseData.cameraStack.Add(_mainCamera);

        // The Main Camera no longer sees the Horizon layer(s) itself — the Horizon Camera owns
        // them entirely, at its own fixed distance, so nothing draws twice.
        _mainCamera.cullingMask &= ~cullMask;

        IsActive = true;
    }

    /// <summary>
    /// Called by HorizonWorld's own LateUpdate() as the FIRST step of its deterministic per-frame
    /// sequence — deliberately NOT a Unity LateUpdate() of its own, so every other Horizon World
    /// system that depends on this camera's transform/parallax delta for THIS exact frame (e.g.
    /// HorizonMountainLayers' parallax offset) never reads a one-frame-stale value, regardless of
    /// Unity's otherwise-undefined cross-script LateUpdate ordering.
    /// </summary>
    public void Tick()
    {
        if (!IsActive || _mainCamera == null || HorizonCamera == null) return;

        HorizonCamera.fieldOfView = _mainCamera.fieldOfView;

        // YAW is deliberately NEVER copied — only PITCH (up/down look) does. The gameplay camera's
        // yaw follows the path's own heading (CameraFollow/ComputeThirdPersonRotation), which can
        // drift as the path curves; if the Horizon Camera copied that, the whole spectrum arc would
        // visibly swing off-center every time the path turns, even though the player barely
        // perceives the turn itself. Roll is always 0 — the horizon must never tilt.
        float pitch = _mainCamera.transform.eulerAngles.x;
        HorizonCamera.transform.rotation = Quaternion.Euler(pitch, _fixedYaw, 0f);

        Vector3 delta = _mainCamera.transform.position - _mainCameraStartPos;
        CameraDelta = delta;

        // Hard-capped (see horizonParallaxMaxOffsetFraction doc) — without this, a long enough run
        // keeps accumulating delta*parallaxFactor without bound, and eventually exceeds Arc Radius:
        // the "distant, unreachable" ring of bars then visually gets caught up to/overtaken.
        // Clamping the OFFSET itself (not the raw delta), to a FRACTION of Arc Radius (not a fixed
        // world-unit number), guarantees the Horizon Camera can never end up more than that fraction
        // of "however far away the ring currently is" from its start position — regardless of how
        // far the player runs, AND regardless of whatever Arc Radius happens to be tuned to (a fixed
        // absolute cap silently stopped being safe the moment Arc Radius was later tuned smaller;
        // this can't happen anymore since the cap now scales WITH Arc Radius automatically).
        Vector3 offset = delta * Mathf.Clamp01(_config.horizonParallaxFactor);
        float maxOffset = Mathf.Max(0f, _config.horizonArcRadius) * Mathf.Clamp01(_config.horizonParallaxMaxOffsetFraction);
        offset = Vector3.ClampMagnitude(offset, maxOffset);
        HorizonCamera.transform.position = HorizonRoot.position + offset;
    }
}
