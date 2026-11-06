using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>One character-caster shadow map for the ring; static decor keeps its baked lighting.</summary>
public sealed class ArenaCharacterGrounding : MonoBehaviour
{
    public const uint ReceiverMask = 4;
    public ArenaCombatLook look;
    public Light shadowKey;
    public UniversalRenderPipelineAsset midPipeline;
    public Material lowContactMaterial;
    readonly Dictionary<Renderer,uint> receivers = new();
    readonly Dictionary<Renderer,ShadowCastingMode> casting = new();
    readonly Dictionary<Light,LightShadows> suppressed = new();
    readonly Dictionary<Transform,GameObject> contacts = new();
    readonly List<Transform> roots = new();
    RenderPipelineAsset previousPipeline;
    bool active, ownsPipeline;
    float nextScan, floorY;
    static Mesh contactMesh;
    public bool UsingContactFallback { get; private set; }
    public int ActiveContactCount { get { int n=0;foreach(var g in contacts.Values)if(g&&g.activeSelf)n++;return n; } }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + .5f;
            var preview = FindFirstObjectByType<CombatLookPreviewActors>();
            bool previewReady = preview && preview.Ready;
            if (!FindFirstObjectByType<FightCameraController>() && !previewReady) return;
            if (!active) Activate();
            foreach (var a in FindObjectsByType<FighterActor>(FindObjectsSortMode.None)) Register(a.transform);
            if (previewReady) { Register(preview.Player.Root); Register(preview.Rival.Root); }
            RefreshMode();
        }
        foreach (var pair in contacts)
        {
            if (!pair.Key || !pair.Value) continue;
            pair.Value.SetActive(UsingContactFallback);
            pair.Value.transform.position = new Vector3(pair.Key.position.x, floorY + .012f, pair.Key.position.z);
        }
    }

    public void Activate()
    {
        if (active) return;
        var pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (pipeline && pipeline.additionalLightsRenderingMode == LightRenderingMode.PerPixel && !pipeline.supportsAdditionalLightShadows && midPipeline)
        {
            previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = midPipeline;
            ownsPipeline = true;
            // These shadows were already disabled by the original Mid pipeline.
            foreach (var l in look.transform.parent.GetComponentsInChildren<Light>(true))
                if (l != shadowKey && l.type != LightType.Directional && l.shadows != LightShadows.None)
                { suppressed[l] = l.shadows; l.shadows = LightShadows.None; }
        }
        foreach (var r in look.transform.parent.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r.GetComponentInParent<FighterActor>()) continue;
            receivers[r] = r.renderingLayerMask;
            r.renderingLayerMask |= ReceiverMask;
            if (r.name == "Floor") floorY = r.bounds.max.y;
        }
        active = true;
        RefreshMode();
    }

    public void Register(Transform root)
    {
        if (!root) return;
        look.RegisterFighterRoot(root); // One owner restores the shared character rendering bit.
        if (roots.Contains(root)) return;
        roots.Add(root);
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            casting[r] = r.shadowCastingMode;
            if (r.shadowCastingMode == ShadowCastingMode.Off) r.shadowCastingMode = ShadowCastingMode.On;
        }
        if (lowContactMaterial)
        {
            if (!contactMesh)
            {
                contactMesh = new Mesh { name = "CombatContactQuad" };
                contactMesh.vertices = new[] { new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f) };
                contactMesh.uv = new[] { Vector2.zero,Vector2.up,Vector2.one,Vector2.right };
                contactMesh.triangles = new[] { 0,1,2,0,2,3 };
                contactMesh.RecalculateNormals();
            }
            var go = new GameObject("LowQualityContactShadow");
            go.transform.SetParent(transform,false);
            go.transform.localScale = new Vector3(.85f,1,.65f);
            go.AddComponent<MeshFilter>().sharedMesh = contactMesh;
            var r = go.AddComponent<MeshRenderer>();r.sharedMaterial = lowContactMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;r.receiveShadows = false;
            go.SetActive(false);contacts[root] = go;
        }
    }

    void RefreshMode()
    {
        var p = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        bool full = p && p.supportsAdditionalLightShadows && p.additionalLightsRenderingMode == LightRenderingMode.PerPixel;
        UsingContactFallback = !full;
        if (shadowKey) shadowKey.enabled = full && active;
    }
    void OnEnable() { if (shadowKey) shadowKey.enabled = false; nextScan = 0; }
    void OnDisable()
    {
        if (shadowKey) shadowKey.enabled = false;
        foreach (var p in receivers) if (p.Key) p.Key.renderingLayerMask = (p.Key.renderingLayerMask & ~ReceiverMask) | (p.Value & ReceiverMask);
        foreach (var p in casting) if (p.Key) p.Key.shadowCastingMode = p.Value;
        foreach (var p in suppressed) if (p.Key) p.Key.shadows = p.Value;
        foreach (var p in contacts) if (p.Value) { if(Application.isPlaying)Destroy(p.Value);else DestroyImmediate(p.Value); }
        if (ownsPipeline && QualitySettings.renderPipeline == midPipeline) QualitySettings.renderPipeline = previousPipeline;
        receivers.Clear();casting.Clear();suppressed.Clear();contacts.Clear();roots.Clear();active = false;ownsPipeline = false;
    }
}
