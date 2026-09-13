using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns the separate "Horizon World" — a URP camera stack Base camera that renders ONLY the
/// "Horizon" layer (spectrum bars, reflections, water, procedural sky), sitting at a near-FIXED
/// position that only copies the gameplay Main Camera's PITCH and FOV every frame — never its
/// full translation, and deliberately NEVER its yaw (see LateUpdate — the path's own heading can
/// drift as it curves, and copying that would visibly swing the whole spectrum arc off-center).
/// The Main Camera itself becomes an Overlay stacked on top of it.
///
/// Why a second camera instead of re-centering geometry on the camera every frame (the old
/// FrequencyBackground approach): re-centering means the geometry has ZERO relative motion
/// against the camera, ever — which reads as "attached to your face" regardless of how far away
/// it visually sits. A genuinely separate camera at a fixed position, only copying view ANGLE
/// (plus an optional small fraction of translation via horizonParallaxFactor for a subtle sense
/// of depth), is how distant backgrounds are actually done.
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
    /// <summary>Second, OPTIONAL layer holding only the neon bars — lets the bar-reflection
    /// capture camera (HorizonBarsReflectionCamera) cull to just them. If this layer doesn't
    /// exist, everything still works — bars simply stay on HorizonLayerName and the reflection
    /// feature disables itself (see HorizonBarsReflectionCamera).</summary>
    public const string BarsLayerName = "HorizonBars";

    public Camera    HorizonCamera { get; private set; }
    public Transform HorizonRoot   { get; private set; }
    public bool       IsActive      { get; private set; }
    /// <summary>Bars' own layer index, or -1 if BarsLayerName doesn't exist in this project —
    /// callers (SpectrumBars3D, HorizonBarsReflectionCamera) fall back to HorizonLayer when -1.</summary>
    public int BarsLayer { get; private set; } = -1;

    private GameplayConfig _config;
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

    public void Initialize(GameplayConfig config)
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

        BarsLayer = LayerMask.NameToLayer(BarsLayerName);
        if (BarsLayer < 0)
        {
            Debug.LogWarning($"[HorizonCameraController] Optional layer '{BarsLayerName}' doesn't " +
                              "exist — the RenderTexture bar reflection feature will stay disabled " +
                              "(bars still render normally on the '" + HorizonLayerName + "' layer). " +
                              "Add it in Project Settings > Tags and Layers to enable it.");
        }

        _mainCameraStartPos = _mainCamera.transform.position;
        _fixedYaw           = _mainCamera.transform.eulerAngles.y;

        var rootGO = new GameObject("[Horizon World]");
        HorizonRoot = rootGO.transform;
        HorizonRoot.position = _mainCameraStartPos;

        int cullMask = 1 << layer;
        if (BarsLayer >= 0) cullMask |= 1 << BarsLayer;

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

    private void LateUpdate()
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
        HorizonCamera.transform.position = HorizonRoot.position + delta * Mathf.Clamp01(_config.horizonParallaxFactor);
    }
}
