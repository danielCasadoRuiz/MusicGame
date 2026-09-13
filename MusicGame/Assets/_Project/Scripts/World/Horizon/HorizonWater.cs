using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flat water plane for the Horizon World — reuses CheapWater.shader (dark tint, two combined
/// tileable normal maps for a subtle microwave ripple, Fresnel rim + specular highlight, and an
/// optional sampled reflection of the bars via HorizonBarsReflectionCamera's RenderTexture; see
/// the shader's own doc). Built once; per-frame work is just pushing the (rarely-changing) config
/// values into the shared material, cheap enough to do unconditionally.
///
/// MANUAL STEP: assign two tileable water normal-map textures to GameplayConfig's
/// horizonWaterNormalMapA/B (Inspector) for real ripple detail — without them the shader falls
/// back to Unity's flat default normal (still fully functional/dark/glossy, just no micro-detail).
/// </summary>
public class HorizonWater : MonoBehaviour
{
    private GameplayConfig _config;
    private Material _material;
    private Transform _transform;
    private HorizonBarsReflectionCamera _reflection;

    private static readonly int NormalMapAID = Shader.PropertyToID("_NormalMapA");
    private static readonly int NormalMapBID = Shader.PropertyToID("_NormalMapB");
    private static readonly int ScrollAID    = Shader.PropertyToID("_ScrollA");
    private static readonly int ScrollBID    = Shader.PropertyToID("_ScrollB");
    private static readonly int ReflectionTexID       = Shader.PropertyToID("_ReflectionTex");
    private static readonly int ReflectionEnabledID   = Shader.PropertyToID("_ReflectionEnabled");
    private static readonly int ReflectionOpacityID   = Shader.PropertyToID("_ReflectionOpacity");
    private static readonly int ReflectionEmissionID  = Shader.PropertyToID("_ReflectionEmission");
    private static readonly int ReflectionDistortionID = Shader.PropertyToID("_ReflectionDistortion");

    public void Initialize(GameplayConfig config, Transform root, HorizonBarsReflectionCamera reflection)
    {
        _config = config;
        _reflection = reflection;

        int layer  = LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);
        var shader = Shader.Find("MusicGame/CheapWater") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _material  = new Material(shader) { name = "Horizon_Water", renderQueue = 3000 };

        var go = new GameObject("HorizonWater");
        go.layer = Mathf.Max(0, layer);
        go.transform.SetParent(root, false);

        var mesh = BuildQuad();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        _transform = go.transform;
        _transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies flat (double-sided material, facing doesn't matter)

        ApplyMaterialValues();
    }

    public void Tick()
    {
        float waterSize = Mathf.Max(10f, _config.horizonArcRadius * 6f);
        _transform.localPosition = new Vector3(0f, _config.horizonWaterLevel - 0.02f, 0f);
        _transform.localScale    = new Vector3(waterSize, waterSize, 1f);

        ApplyMaterialValues();
    }

    private void ApplyMaterialValues()
    {
        float d = Mathf.Clamp01(_config.horizonWaterDarkness);
        Color waterColor = Color.Lerp(new Color(0.05f, 0.10f, 0.16f, 0.92f), new Color(0.005f, 0.01f, 0.03f, 0.95f), d);
        _material.SetColor("_BaseColor", waterColor);

        if (_config.horizonWaterNormalMapA != null) _material.SetTexture(NormalMapAID, _config.horizonWaterNormalMapA);
        if (_config.horizonWaterNormalMapB != null) _material.SetTexture(NormalMapBID, _config.horizonWaterNormalMapB);
        _material.SetFloat("_TilingA", _config.horizonWaterTilingA);
        _material.SetVector(ScrollAID, new Vector4(_config.horizonWaterScrollA.x, _config.horizonWaterScrollA.y, 0f, 0f));
        _material.SetFloat("_TilingB", _config.horizonWaterTilingB);
        _material.SetVector(ScrollBID, new Vector4(_config.horizonWaterScrollB.x, _config.horizonWaterScrollB.y, 0f, 0f));
        _material.SetFloat("_NormalStrength", _config.horizonWaterNormalStrength);
        _material.SetFloat("_FresnelPower", _config.horizonWaterFresnelPower);
        _material.SetFloat("_SpecularPower", _config.horizonWaterSpecularPower);
        _material.SetFloat("_SpecularIntensity", _config.horizonWaterSpecularIntensity);
        _material.SetColor("_HorizonTint", _config.horizonWaterHorizonTint);
        _material.SetFloat("_HorizonTintStrength", _config.horizonWaterHorizonTintStrength);

        bool reflectionActive = _config.horizonReflectionEnabled && _reflection != null && _reflection.IsActive;
        _material.SetFloat(ReflectionEnabledID, reflectionActive ? 1f : 0f);
        if (reflectionActive)
        {
            _material.SetTexture(ReflectionTexID, _reflection.ReflectionTexture);
            _material.SetFloat(ReflectionOpacityID, _config.horizonReflectionOpacity);
            _material.SetFloat(ReflectionEmissionID, _config.horizonReflectionEmission);
            _material.SetFloat(ReflectionDistortionID, _config.horizonReflectionDistortion);
        }
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "Horizon_WaterQuad" };
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
        mesh.RecalculateTangents(); // needed for CheapWater.shader's tangent-space normal mapping
        mesh.RecalculateBounds();
        return mesh;
    }
}
