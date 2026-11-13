using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Applies the arena's AA pipeline only after combat begins, and restores it on unload.</summary>
[DefaultExecutionOrder(-100)]
public sealed class ArenaBakedPresentation : MonoBehaviour
{
    public UniversalRenderPipelineAsset pipeline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void RegisterProbeUpdates()
    {
        LightProbes.needsRetetrahedralization -= RebuildLoadedProbes;
        LightProbes.needsRetetrahedralization += RebuildLoadedProbes;
    }
    static void RebuildLoadedProbes()
    {
        if (LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
            LightProbes.TetrahedralizeAsync();
    }
    RenderPipelineAsset previous;
    bool owns;
    float next;
    void Update()
    {
        if (owns || !pipeline || Time.unscaledTime < next) return;
        next = Time.unscaledTime + .5f;
        if (!FindFirstObjectByType<FightCameraController>()) return;
        previous = QualitySettings.renderPipeline;
        QualitySettings.renderPipeline = pipeline;
        owns = true;
    }
    void OnDisable()
    {
        if (owns && QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previous;
        owns = false;
    }
}
