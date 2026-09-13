using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two flat, textured mountain LAYERS (far/near) for the Horizon World — each a single quad using
/// a hand-authored PNG silhouette WITH ALPHA that you assign in GameplayConfig (no procedural
/// mesh/noise generation of any kind). A layer with no texture assigned simply doesn't render.
///
/// MANUAL STEP: assign horizonMountainFarTexture / horizonMountainNearTexture in GameplayConfig's
/// Inspector — any silhouette PNG works as long as its ALPHA channel carries the shape (opaque =
/// mountain, transparent = sky showing through). Suggested source: a single artist-drawn ridge
/// silhouette, duplicated once for far (recolored blue/violet, lower contrast) and once for near
/// (recolored near-black, higher contrast) — or two different drawings entirely.
///
/// Each layer is a plain quad (not curved to the bars' arc) — a flat backdrop card sitting behind
/// the bars, tinted/scaled/positioned entirely from GameplayConfig, with a small independent
/// parallax fraction of the Horizon Camera's own translation for a subtle sense of depth between
/// the two layers (see HorizonCameraController.CameraDelta).
/// </summary>
public class HorizonMountainLayers : MonoBehaviour
{
    private GameplayConfig _config;
    private Transform _root;
    private GameObject _farGO, _nearGO;
    private MeshRenderer _farRenderer, _nearRenderer;
    private Material _farMaterial, _nearMaterial;
    private Vector3 _farBaseLocalPos, _nearBaseLocalPos;

    public void Initialize(GameplayConfig config, Transform root)
    {
        _config = config;
        _root = root;
        if (!config.horizonMountainsEnabled) return;

        var shader = Shader.Find("MusicGame/HorizonMountainLayer") ?? Shader.Find("Universal Render Pipeline/Unlit");
        int layer = LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);

        (_farGO, _farRenderer, _farMaterial) = BuildLayer(root, layer, "HorizonMountains_Far", shader);
        (_nearGO, _nearRenderer, _nearMaterial) = BuildLayer(root, layer, "HorizonMountains_Near", shader);

        Rebuild();
    }

    private static (GameObject, MeshRenderer, Material) BuildLayer(Transform root, int layer, string name, Shader shader)
    {
        var go = new GameObject(name);
        go.layer = Mathf.Max(0, layer);
        go.transform.SetParent(root, false);

        var mesh = BuildQuad();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(shader) { name = name };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return (go, mr, mat);
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "HorizonMountainQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public void Tick()
    {
        if (_config == null || !_config.horizonMountainsEnabled || _farGO == null) return;

        Rebuild();

        Vector3 delta = HorizonCameraController.Instance != null ? HorizonCameraController.Instance.CameraDelta : Vector3.zero;
        _farGO.transform.localPosition  = _farBaseLocalPos  + delta * _config.horizonMountainFarParallax;
        _nearGO.transform.localPosition = _nearBaseLocalPos + delta * _config.horizonMountainNearParallax;
    }

    private void Rebuild()
    {
        bool farVisible  = _config.horizonMountainFarTexture  != null;
        bool nearVisible = _config.horizonMountainNearTexture != null;
        _farRenderer.enabled  = farVisible;
        _nearRenderer.enabled = nearVisible;

        float ridgeBase = _config.horizonBarVerticalOffset;

        if (farVisible)
        {
            ApplyLayer(_farMaterial, _config.horizonMountainFarTexture, _config.horizonMountainFarTint,
                _config.horizonMountainFarBrightness, _config.horizonMountainFarOpacity, distance01: 1f);

            float dist = _config.horizonArcRadius * _config.horizonMountainFarDistance;
            _farBaseLocalPos = new Vector3(0f, ridgeBase + _config.horizonMountainFarVerticalOffset, dist);
            _farGO.transform.localPosition = _farBaseLocalPos;
            _farGO.transform.localRotation = Quaternion.identity;
            _farGO.transform.localScale = new Vector3(_config.horizonMountainFarScale.x, _config.horizonMountainFarScale.y, 1f);
        }

        if (nearVisible)
        {
            ApplyLayer(_nearMaterial, _config.horizonMountainNearTexture, _config.horizonMountainNearTint,
                _config.horizonMountainNearBrightness, _config.horizonMountainNearOpacity, distance01: 0.35f);

            float dist = _config.horizonArcRadius * _config.horizonMountainNearDistance;
            _nearBaseLocalPos = new Vector3(0f, ridgeBase + _config.horizonMountainNearVerticalOffset, dist);
            _nearGO.transform.localPosition = _nearBaseLocalPos;
            _nearGO.transform.localRotation = Quaternion.identity;
            _nearGO.transform.localScale = new Vector3(_config.horizonMountainNearScale.x, _config.horizonMountainNearScale.y, 1f);
        }
    }

    private void ApplyLayer(Material mat, Texture2D tex, Color tint, float brightness, float opacity, float distance01)
    {
        mat.SetTexture("_MainTex", tex);

        // Haze applied once here (CPU side) rather than per-pixel in the shader — a static
        // backdrop image doesn't need a real per-pixel gradient; blending the whole layer's tint
        // toward the haze color (using its own representative height/distance) reads the same and
        // keeps the shader trivial. Height passed as the layer's OWN vertical placement (relative
        // to the bar baseline), Distance01 as this layer's "how far away" (far=1, near=0.35).
        float representativeHeight = 0f; // silhouettes sit right at/around the horizon line
        Color hazed = HorizonHaze.Apply(tint * brightness, representativeHeight, distance01, _config);
        hazed.a = tint.a;
        mat.SetColor("_Tint", hazed);
        mat.SetFloat("_Opacity", Mathf.Clamp01(opacity));
    }
}
