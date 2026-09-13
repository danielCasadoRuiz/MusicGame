using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flat water plane for the Horizon World — reuses CheapWater.shader (dark tint, two combined
/// tileable normal maps for a subtle microwave ripple, Fresnel rim + specular highlight, and
/// REFRACTION of whatever opaque geometry sits behind/below it via URP's _CameraOpaqueTexture —
/// see the shader's own doc). The mirrored reflection bars (SpectrumBars3D) sit just below this
/// plane as real opaque geometry; this shader's refraction is what makes them read as a wobbly,
/// distorted reflection instead of a perfect duplicate. No second camera/RenderTexture involved.
///
/// OWNERSHIP: this is the ONLY system that writes to the water material's shader properties.
///
/// STATIC vs DYNAMIC: base color/darkness, normal-map textures/tiling/scroll-speed, normal
/// strength, Fresnel/specular params, refraction strength and transform (position/scale) are all
/// pure art-direction or one-time setup — applied once in ApplyStaticConfig() (Initialize, or
/// every frame only if HorizonConfig.devLiveConfigSync is on). The horizon-tint COLOR alone is
/// genuinely dynamic (macro-reactive — see MusicEnvironmentController), so only that one
/// SetColor call happens every frame.
///
/// MANUAL STEP: assign two tileable water normal-map textures to HorizonConfig's
/// horizonWaterNormalMapA/B (Inspector) for real ripple detail — without them the shader falls
/// back to Unity's flat default normal (still fully functional/dark/glossy, just no micro-detail).
/// </summary>
public class HorizonWater : MonoBehaviour
{
    private HorizonConfig _config;
    private Material _material;
    private Transform _transform;

    private static readonly int BaseColorID  = Shader.PropertyToID("_BaseColor");
    private static readonly int NormalMapAID = Shader.PropertyToID("_NormalMapA");
    private static readonly int NormalMapBID = Shader.PropertyToID("_NormalMapB");
    private static readonly int TilingAID    = Shader.PropertyToID("_TilingA");
    private static readonly int TilingBID    = Shader.PropertyToID("_TilingB");
    private static readonly int ScrollAID    = Shader.PropertyToID("_ScrollA");
    private static readonly int ScrollBID    = Shader.PropertyToID("_ScrollB");
    private static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
    private static readonly int FresnelPowerID   = Shader.PropertyToID("_FresnelPower");
    private static readonly int SpecularPowerID     = Shader.PropertyToID("_SpecularPower");
    private static readonly int SpecularIntensityID = Shader.PropertyToID("_SpecularIntensity");
    private static readonly int HorizonTintID         = Shader.PropertyToID("_HorizonTint");
    private static readonly int HorizonTintStrengthID = Shader.PropertyToID("_HorizonTintStrength");
    private static readonly int RefractionStrengthID  = Shader.PropertyToID("_RefractionStrength");

    /// <summary>World-space Y of the actual rendered water plane — the single source of truth
    /// SpectrumBars3D mirrors its reflection bars across, so the two can never drift apart.</summary>
    public float WaterLevelWorldY { get; private set; }

    public void Initialize(HorizonConfig config, Transform root)
    {
        _config = config;

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

        ApplyStaticConfig();
        ApplyDynamic(0f);
    }

    public void Tick()
    {
        if (_config.devLiveConfigSync) ApplyStaticConfig();

        float intensity = MusicEnvironmentController.Instance != null
            ? MusicEnvironmentController.Instance.SmoothedMacroIntensity
            : 0f;
        ApplyDynamic(intensity);
    }

    /// <summary>The one genuinely-dynamic property: the horizon-tint color, macro-blended.</summary>
    private void ApplyDynamic(float intensity)
    {
        Color tint = Color.Lerp(_config.horizonWaterHorizonTint, _config.horizonWaterHorizonTintIntense, intensity);
        _material.SetColor(HorizonTintID, tint);
    }

    /// <summary>Transform (position/scale) + every static shader property — applied once at
    /// Initialize and only re-applied on demand (HorizonConfig.devLiveConfigSync).</summary>
    public void ApplyStaticConfig()
    {
        float waterSize = Mathf.Max(10f, _config.horizonArcRadius * 6f);
        WaterLevelWorldY = _transform.parent.position.y + _config.horizonWaterLevel - 0.02f;
        _transform.localPosition = new Vector3(0f, _config.horizonWaterLevel - 0.02f, 0f);
        _transform.localScale    = new Vector3(waterSize, waterSize, 1f);

        float d = Mathf.Clamp01(_config.horizonWaterDarkness);
        Color waterColor = Color.Lerp(new Color(0.05f, 0.10f, 0.16f, 0.92f), new Color(0.005f, 0.01f, 0.03f, 0.95f), d);
        _material.SetColor(BaseColorID, waterColor);

        if (_config.horizonWaterNormalMapA != null) _material.SetTexture(NormalMapAID, _config.horizonWaterNormalMapA);
        if (_config.horizonWaterNormalMapB != null) _material.SetTexture(NormalMapBID, _config.horizonWaterNormalMapB);
        _material.SetFloat(TilingAID, _config.horizonWaterTilingA);
        _material.SetVector(ScrollAID, new Vector4(_config.horizonWaterScrollA.x, _config.horizonWaterScrollA.y, 0f, 0f));
        _material.SetFloat(TilingBID, _config.horizonWaterTilingB);
        _material.SetVector(ScrollBID, new Vector4(_config.horizonWaterScrollB.x, _config.horizonWaterScrollB.y, 0f, 0f));
        _material.SetFloat(NormalStrengthID, _config.horizonWaterNormalStrength);
        _material.SetFloat(FresnelPowerID, _config.horizonWaterFresnelPower);
        _material.SetFloat(SpecularPowerID, _config.horizonWaterSpecularPower);
        _material.SetFloat(SpecularIntensityID, _config.horizonWaterSpecularIntensity);
        _material.SetFloat(HorizonTintStrengthID, _config.horizonWaterHorizonTintStrength);
        _material.SetFloat(RefractionStrengthID, _config.horizonWaterRefractionStrength);
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
