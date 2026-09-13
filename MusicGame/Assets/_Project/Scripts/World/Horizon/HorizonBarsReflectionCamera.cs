using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders ONLY the neon bars (their own dedicated layer — HorizonCameraController.BarsLayerName)
/// into a small RenderTexture, from a camera that is a classic MIRRORED reflection of the Horizon
/// Camera across the water plane — the textbook cheap planar-reflection technique. CheapWater.shader
/// samples this RT using an explicit reflection view-projection matrix (pushed globally each frame
/// here), so the reflected image is automatically the exact same shape/color/height as the real
/// bars, perfectly synced, with zero separate reflection geometry to keep in sync by hand.
///
/// Deliberately NOT a full-scene planar reflection — the capture camera's cullingMask only ever
/// includes the bars layer, so mountains/sky/water never appear in the reflection, keeping this
/// cheap (default 512x256) regardless of how much the rest of the Horizon World grows.
///
/// Gracefully disables itself (IsActive stays false) if BarsLayer doesn't exist or the feature is
/// turned off in config — HorizonWater/HorizonWorld both already handle IsActive == false by
/// simply not enabling the reflection sample in the shader, same graceful-degrade pattern used
/// throughout this system.
/// </summary>
public class HorizonBarsReflectionCamera : MonoBehaviour
{
    private static readonly int ReflectionVPID = Shader.PropertyToID("_HorizonReflectionVP");

    private GameplayConfig _config;
    private Transform      _root;
    private Camera         _mainHorizonCamera;
    private Camera         _reflectCamera;
    private RenderTexture  _rt;

    public bool IsActive { get; private set; }
    public RenderTexture ReflectionTexture => _rt;

    public void Initialize(GameplayConfig config, Transform root, Camera mainHorizonCamera)
    {
        _config = config;
        _root = root;
        _mainHorizonCamera = mainHorizonCamera;

        int barsLayer = HorizonCameraController.Instance != null ? HorizonCameraController.Instance.BarsLayer : -1;
        if (!config.horizonReflectionEnabled || barsLayer < 0 || mainHorizonCamera == null)
        {
            IsActive = false;
            return;
        }

        var size = config.horizonReflectionRTSize;
        int w = Mathf.Max(32, size.x), h = Mathf.Max(32, size.y);
        _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR) { name = "Horizon_BarsReflectionRT" };

        var camGO = new GameObject("[Horizon Bars Reflection Camera]");
        camGO.transform.SetParent(root, false);
        _reflectCamera = camGO.AddComponent<Camera>();
        _reflectCamera.targetTexture = _rt;
        _reflectCamera.cullingMask   = 1 << barsLayer;
        _reflectCamera.clearFlags    = CameraClearFlags.SolidColor;
        _reflectCamera.backgroundColor = Color.clear;
        _reflectCamera.nearClipPlane  = mainHorizonCamera.nearClipPlane;
        _reflectCamera.farClipPlane   = mainHorizonCamera.farClipPlane;
        _reflectCamera.fieldOfView    = mainHorizonCamera.fieldOfView;

        var camData = _reflectCamera.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = false;
        camData.renderShadows        = false;

        IsActive = true;
    }

    public void Tick()
    {
        if (!IsActive) return;

        var size = _config.horizonReflectionRTSize;
        int w = Mathf.Max(32, size.x), h = Mathf.Max(32, size.y);
        if (_rt.width != w || _rt.height != h)
        {
            _rt.Release();
            _rt.width = w;
            _rt.height = h;
        }

        // Mirror the Horizon Camera's current (parallax-adjusted) transform across the water
        // plane's WORLD Y — HorizonRoot never rotates, so its position.y + the water's local
        // offset is exactly the plane's world height.
        float waterLevelWorldY = _root.position.y + _config.horizonWaterLevel;

        Vector3 pos = _mainHorizonCamera.transform.position;
        pos.y = 2f * waterLevelWorldY - pos.y;

        Vector3 euler = _mainHorizonCamera.transform.eulerAngles;
        euler.x = -euler.x;
        euler.z = 0f;

        _reflectCamera.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
        _reflectCamera.fieldOfView = _mainHorizonCamera.fieldOfView;
        _reflectCamera.aspect      = _mainHorizonCamera.aspect;

        // GetGPUProjectionMatrix(..., true) — the standard "this matrix's output NDC maps
        // correctly onto a RenderTexture" helper, so no extra platform Y-flip is needed on the
        // shader side when sampling _rt with this matrix's projected UV (see CheapWater.shader).
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(_reflectCamera.projectionMatrix, true);
        Matrix4x4 vp   = proj * _reflectCamera.worldToCameraMatrix;
        Shader.SetGlobalMatrix(ReflectionVPID, vp);
    }

    private void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }
}
