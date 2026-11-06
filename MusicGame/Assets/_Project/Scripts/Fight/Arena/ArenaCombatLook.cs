using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Scoped presentation for an arena: restores camera and character masks on unload.</summary>
public sealed class ArenaCombatLook : MonoBehaviour
{
    public Volume volume;
    public Light[] characterLights = System.Array.Empty<Light>();
    public const uint CharacterMask = 2;
    readonly Dictionary<Renderer, uint> masks = new();
    readonly Dictionary<Camera, (bool post, bool hdr)> cameras = new();
    float nextScan;

    public void RegisterFighterRoot(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || masks.ContainsKey(r)) continue;
            masks[r] = r.renderingLayerMask;
            r.renderingLayerMask |= CharacterMask;
        }
    }

    public void AttachCamera(Camera camera)
    {
        if (!camera || cameras.ContainsKey(camera)) return;
        var data = camera.GetUniversalAdditionalCameraData();
        cameras[camera] = (data.renderPostProcessing, camera.allowHDR);
        data.renderPostProcessing = true;
        camera.allowHDR = true;
        SetTreatment(true);
    }

    void Update()
    {
        if (!Application.isPlaying || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + .5f;
        foreach (var actor in FindObjectsByType<FighterActor>(FindObjectsSortMode.None)) RegisterFighterRoot(actor.transform);
        foreach (var controller in FindObjectsByType<FightCameraController>(FindObjectsSortMode.None)) AttachCamera(controller.GetComponent<Camera>());
    }

    public void SetTreatment(bool active)
    {
        if (volume) volume.enabled = active;
        bool supported = QualitySettings.renderPipeline is UniversalRenderPipelineAsset p && p.useRenderingLayers && p.additionalLightsRenderingMode != LightRenderingMode.Disabled;
        foreach (var light in characterLights) if (light) light.enabled = active && supported;
    }

    // An additively preloaded arena must not grade the Runner camera before Fight starts.
    void OnEnable() { SetTreatment(false); nextScan = 0; }
    void OnDisable()
    {
        SetTreatment(false);
        foreach (var pair in masks) if (pair.Key) pair.Key.renderingLayerMask = (pair.Key.renderingLayerMask & ~CharacterMask) | (pair.Value & CharacterMask);
        foreach (var pair in cameras) if (pair.Key) { pair.Key.GetUniversalAdditionalCameraData().renderPostProcessing = pair.Value.post; pair.Key.allowHDR = pair.Value.hdr; }
        masks.Clear(); cameras.Clear();
    }
}
