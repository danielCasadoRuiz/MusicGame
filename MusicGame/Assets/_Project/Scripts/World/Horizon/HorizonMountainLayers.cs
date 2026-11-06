using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two flat, textured mountain LAYERS (far/near) for the Horizon World — each a single quad using
/// a hand-authored PNG silhouette WITH ALPHA that you assign in HorizonConfig (no procedural
/// mesh/noise generation of any kind). A layer with no texture assigned simply doesn't render.
///
/// OWNERSHIP: this is the ONLY system that writes to the mountain layer materials' shader
/// properties.
///
/// STATIC vs DYNAMIC: texture, opacity and the layer's base transform (scale/distance/vertical
/// offset) are pure art-direction/placement — applied once in ApplyStaticConfig() (Initialize, or
/// every frame only if HorizonConfig.devLiveConfigSync is on). The per-frame parallax POSITION
/// offset (tracks the Horizon Camera's own translation) and the tint color (macro-blended +
/// haze'd) are genuinely dynamic, so only those are recomputed every Tick.
///
/// MANUAL STEP: assign horizonMountainFarTexture / horizonMountainNearTexture in HorizonConfig's
/// Inspector — any silhouette PNG works as long as its ALPHA channel carries the shape (opaque =
/// mountain, transparent = sky showing through). Suggested source: a single artist-drawn ridge
/// silhouette, duplicated once for far (recolored blue/violet, lower contrast) and once for near
/// (recolored near-black, higher contrast) — or two different drawings entirely.
/// </summary>
public class HorizonMountainLayers : MonoBehaviour, IRunnerEnvironmentModule
{
    // ── Environment module (Runner Horizon slot — see RunnerEnvironmentController) ─────────────
    [Tooltip("Optional: this prefab's OWN HorizonConfig (mountain textures/scale/placement/tint). " +
             "Empty = the Runner's resolved HorizonConfig, i.e. exactly the mountains the Runner always had.")]
    [SerializeField] private HorizonConfig settingsOverride;

    public void InitializeModule(RunnerEnvironmentContext context) =>
        Initialize(settingsOverride != null ? settingsOverride : context.Config, context.ModuleRoot);

    public void TickModule(RunnerEnvironmentContext context) => Tick();

    // Runtime materials are owned here and released with the module (the layer GameObjects are
    // children of the module root and are destroyed with it).
    private void OnDestroy()
    {
        if (_farMaterial != null) Destroy(_farMaterial);
        if (_nearMaterial != null) Destroy(_nearMaterial);
        if (_farGO != null) Destroy(_farGO.GetComponent<MeshFilter>().sharedMesh);
        if (_nearGO != null) Destroy(_nearGO.GetComponent<MeshFilter>().sharedMesh);
    }

    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int TintID    = Shader.PropertyToID("_Tint");
    private static readonly int OpacityID = Shader.PropertyToID("_Opacity");

    private HorizonConfig _config;
    private Transform _root;
    private GameObject _farGO, _nearGO;
    private MeshRenderer _farRenderer, _nearRenderer;
    private Material _farMaterial, _nearMaterial;
    private Vector3 _farBaseLocalPos, _nearBaseLocalPos;

    public void Initialize(HorizonConfig config, Transform root)
    {
        _config = config;
        _root = root;
        if (!config.horizonMountainsEnabled) return;

        var shader = Shader.Find("MusicGame/HorizonMountainLayer") ?? Shader.Find("Universal Render Pipeline/Unlit");
        int layer = LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);

        (_farGO, _farRenderer, _farMaterial) = BuildLayer(root, layer, "HorizonMountains_Far", shader, config.horizonMountainMirrorEdges, config.horizonMountains360);
        (_nearGO, _nearRenderer, _nearMaterial) = BuildLayer(root, layer, "HorizonMountains_Near", shader, config.horizonMountainMirrorEdges, config.horizonMountains360);

        ApplyStaticConfig();
        ApplyDynamic(0f);
    }

    private static (GameObject, MeshRenderer, Material) BuildLayer(Transform root, int layer, string name, Shader shader, bool mirrorEdges, bool surround)
    {
        var go = new GameObject(name);
        go.layer = Mathf.Max(0, layer);
        go.transform.SetParent(root, false);

        var mesh = surround ? BuildCylinder() : BuildQuad(mirrorEdges);
        // The panorama importer supplies mirrored U wrapping for this optional extension.
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(shader) { name = name };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return (go, mr, mat);
    }

    private static Mesh BuildQuad(bool mirrorEdges)
    {
        var mesh = new Mesh { name = "HorizonMountainQuad" };
        float halfWidth = mirrorEdges ? 3.5f : 0.5f;
        float uvMin = mirrorEdges ? -3f : 0f;
        float uvMax = mirrorEdges ? 4f : 1f;
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -0.5f, 0f),
            new Vector3( halfWidth, -0.5f, 0f),
            new Vector3(-halfWidth,  0.5f, 0f),
            new Vector3( halfWidth,  0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(uvMin, 0), new Vector2(uvMax, 0), new Vector2(uvMin, 1), new Vector2(uvMax, 1) };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildCylinder()
    {
        const int segments=128;
        var vertices=new Vector3[(segments+1)*2];
        var uv=new Vector2[vertices.Length]; var triangles=new int[segments*6];
        for(int i=0;i<=segments;i++)
        {
            float t=(float)i/segments, angle=(t-.5f)*Mathf.PI*2;
            var radial=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
            vertices[i*2]=radial+Vector3.down*.5f;
            vertices[i*2+1]=radial+Vector3.up*.5f;
            // Four mirrored repetitions close with identical UV samples and derivative.
            uv[i*2]=new Vector2((t-.5f)*4+.5f,0); uv[i*2+1]=new Vector2(uv[i*2].x,1);
            if(i==segments) continue;
            int a=i*2,j=i*6;
            triangles[j]=a; triangles[j+1]=a+1; triangles[j+2]=a+2;
            triangles[j+3]=a+2; triangles[j+4]=a+1; triangles[j+5]=a+3;
        }
        var mesh=new Mesh{name="Horizon Mountains — closed 360 panorama"};
        mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=triangles;
        mesh.RecalculateBounds(); return mesh;
    }

    public void Tick()
    {
        if (_config == null || !_config.horizonMountainsEnabled || _farGO == null) return;

        if (_config.devLiveConfigSync) ApplyStaticConfig();

        float intensity = MusicEnvironmentController.Instance != null
            ? MusicEnvironmentController.Instance.SmoothedMacroIntensity
            : 0f;
        ApplyDynamic(intensity);
    }

    /// <summary>Parallax position offset + macro-blended/haze'd tint — the only two things that
    /// genuinely change frame to frame.</summary>
    private void ApplyDynamic(float intensity)
    {
        Vector3 delta = HorizonCameraController.Instance != null ? HorizonCameraController.Instance.CameraDelta : Vector3.zero;

        bool farVisible  = _config.horizonMountainFarTexture  != null;
        bool nearVisible = _config.horizonMountainNearTexture != null;

        if (farVisible)
        {
            _farGO.transform.localPosition = _farBaseLocalPos + delta * _config.horizonMountainFarParallax;
            ApplyTint(_farMaterial, _config.horizonMountainFarTint, _config.horizonMountainFarTintIntense,
                _config.horizonMountainFarBrightness, intensity, distance01: 1f);
        }
        if (nearVisible)
        {
            _nearGO.transform.localPosition = _nearBaseLocalPos + delta * _config.horizonMountainNearParallax;
            ApplyTint(_nearMaterial, _config.horizonMountainNearTint, _config.horizonMountainNearTintIntense,
                _config.horizonMountainNearBrightness, intensity, distance01: 0.35f);
        }
    }

    private void ApplyTint(Material mat, Color baseTint, Color intenseTint, float brightness, float macroIntensity, float distance01)
    {
        Color tint = Color.Lerp(baseTint, intenseTint, macroIntensity);
        // The style palette tints the haze a little, so the panorama follows the sky's colours.
        var palette = HorizonPaletteDriver.Instance;
        if (palette != null && palette.HasPalette)
        {
            Color glow = palette.Frame.glow;
            float max = Mathf.Max(0.001f, Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b)));
            tint *= Color.Lerp(Color.white, glow / max, 0.3f);
        }

        // Haze applied once here (CPU side) rather than per-pixel in the shader — a static
        // backdrop image doesn't need a real per-pixel gradient; blending the whole layer's tint
        // toward the haze color (using its own representative height/distance) reads the same and
        // keeps the shader trivial. Height passed as the layer's OWN vertical placement (relative
        // to the bar baseline), Distance01 as this layer's "how far away" (far=1, near=0.35).
        const float representativeHeight = 0f; // silhouettes sit right at/around the horizon line
        Color hazed = HorizonHaze.Apply(tint * brightness, representativeHeight, distance01, macroIntensity, _config);
        hazed = Color.Lerp(tint * brightness, hazed, _config.horizonMountainHazeAmount);
        hazed.a = baseTint.a;
        mat.SetColor(TintID, hazed);
        mat.SetVector("_HorizonCenter", _root.position);
        float azimuth=_config.horizonSunAzimuthDeg*Mathf.Deg2Rad;
        mat.SetVector("_SunHorizontal", new Vector4(Mathf.Sin(azimuth),0,Mathf.Cos(azimuth),0));
        mat.SetFloat("_ShadowBrightness", _config.horizonMountainShadowBrightness);
    }

    private void ConfigureWaterBlend(Material material)
    {
        // Water is transparent and does not write depth. Draw the distant mountain's mist
        // afterwards, while keeping depth testing so foreground bars still occlude it.
        material.renderQueue = _config.horizonMountainBlendIntoWater ? 3010 : 3000;
        material.SetFloat("_WaterBlend", _config.horizonMountainBlendIntoWater ? 1f : 0f);
        material.SetFloat("_WaterFadeStart", _config.horizonMountainWaterFadeStart);
        material.SetFloat("_WaterFadeEnd", Mathf.Max(_config.horizonMountainWaterFadeStart + 0.001f,
            _config.horizonMountainWaterFadeEnd));
    }

    /// <summary>Texture/opacity/transform placement — pure art-direction, applied once and only
    /// re-applied on demand (HorizonConfig.devLiveConfigSync).</summary>
    public void ApplyStaticConfig()
    {
        ConfigureWaterBlend(_farMaterial);
        ConfigureWaterBlend(_nearMaterial);

        bool farVisible  = _config.horizonMountainFarTexture  != null;
        bool nearVisible = _config.horizonMountainNearTexture != null;
        _farRenderer.enabled  = farVisible;
        _nearRenderer.enabled = nearVisible;

        // Anchored to Water Level, the single vertical reference point for the whole Horizon World
        // floor (see SpectrumBars3D's own doc) — mountains no longer read the old, separately-
        // tuned "bar vertical offset" field (removed).
        float ridgeBase = _config.horizonWaterLevel;

        if (farVisible)
        {
            _farMaterial.SetTexture(MainTexID, _config.horizonMountainFarTexture);
            _farMaterial.SetFloat(OpacityID, Mathf.Clamp01(_config.horizonMountainFarOpacity));

            float dist = _config.horizonArcRadius * _config.horizonMountainFarDistance;
            _farBaseLocalPos = new Vector3(0f, ridgeBase + _config.horizonMountainFarVerticalOffset, _config.horizonMountains360 ? 0f : dist);
            _farGO.transform.localPosition = _farBaseLocalPos;
            _farGO.transform.localRotation = Quaternion.identity;
            _farGO.transform.localScale = _config.horizonMountains360 ? new Vector3(dist, _config.horizonMountainFarScale.y, dist) : new Vector3(_config.horizonMountainFarScale.x, _config.horizonMountainFarScale.y, 1f);
        }

        if (nearVisible)
        {
            _nearMaterial.SetTexture(MainTexID, _config.horizonMountainNearTexture);
            _nearMaterial.SetFloat(OpacityID, Mathf.Clamp01(_config.horizonMountainNearOpacity));

            float dist = _config.horizonArcRadius * _config.horizonMountainNearDistance;
            _nearBaseLocalPos = new Vector3(0f, ridgeBase + _config.horizonMountainNearVerticalOffset, _config.horizonMountains360 ? 0f : dist);
            _nearGO.transform.localPosition = _nearBaseLocalPos;
            _nearGO.transform.localRotation = Quaternion.identity;
            _nearGO.transform.localScale = _config.horizonMountains360 ? new Vector3(dist, _config.horizonMountainNearScale.y, dist) : new Vector3(_config.horizonMountainNearScale.x, _config.horizonMountainNearScale.y, 1f);
        }
    }
}
