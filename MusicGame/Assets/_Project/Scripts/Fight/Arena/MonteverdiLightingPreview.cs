using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Self-contained preview for the Monteverdi lighting experiment. It does not change project assets.
[DefaultExecutionOrder(-10000)]
public sealed class MonteverdiLightingPreview : MonoBehaviour
{
    public UniversalRenderPipelineAsset pipeline;
    public ArenaEnvironmentSettings environment;
    public Light sun;
    RenderPipelineAsset previous;
    ArenaEnvironmentSettings.Snapshot snapshot;
    bool applied;
    readonly Dictionary<Texture2D, (FilterMode filter, int aniso)> filters = new();

    void OnEnable()
    {
        if (!Application.isPlaying || applied) return;
        previous = QualitySettings.renderPipeline;
        QualitySettings.renderPipeline = pipeline;
        snapshot = environment.Apply(sun, GetComponent<Camera>());
        if (LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
            LightProbes.Tetrahedralize();
        foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null) continue;
            foreach (var property in material.GetTexturePropertyNames())
            {
                if (material.GetTexture(property) is not Texture2D texture || filters.ContainsKey(texture)) continue;
                filters[texture] = (texture.filterMode, texture.anisoLevel);
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 8;
            }
        }
        applied = true;
    }

    void OnDisable()
    {
        if (!applied) return;
        foreach (var entry in filters)
            if (entry.Key != null) { entry.Key.filterMode = entry.Value.filter; entry.Key.anisoLevel = entry.Value.aniso; }
        filters.Clear();
        ArenaEnvironmentSettings.Restore(snapshot);
        QualitySettings.renderPipeline = previous;
        applied = false;
    }
}
