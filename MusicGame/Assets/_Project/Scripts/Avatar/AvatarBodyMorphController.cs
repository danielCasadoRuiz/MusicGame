using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies ONE BodyMorphValues to every registered SkinnedMeshRenderer (the body AND any compatible
/// garment — task's own explicit "Els mateixos BodyMorphValues s'apliquen al body i a les peces
/// compatibles" requirement), driving each renderer's blendshape weights.
///
/// Blendshape INDICES are resolved by NAME (MorphChannel.ToString(), see that enum's own doc) exactly
/// ONCE per renderer, at RegisterRenderer time, and cached — task's own explicit "no hardcodegis
/// blendshape indexes... busca els índexs una vegada i cacheja'ls" requirement. A declared channel
/// whose blendshape name doesn't exist on that particular mesh is simply skipped (index -1, recorded
/// as "not present") — never a crash, never a silent runtime search on every Apply call.
///
/// Only ever touches a renderer's OWN cloned mesh (see AvatarFactory's own doc on why AvatarFactory
/// clones sharedMesh before handing a renderer to this class) — this class itself has no opinion about
/// sharing; it just sets blend shape weights on whatever mesh the renderer currently has.
/// </summary>
public class AvatarBodyMorphController
{
    private class RegisteredRenderer
    {
        public SkinnedMeshRenderer Renderer;
        public Dictionary<MorphChannel, int> ChannelToBlendShapeIndex;
    }

    private readonly List<RegisteredRenderer> _renderers = new();
    private readonly List<MorphWeight> _scratch = new();

    /// <summary>Caches blendshape indices for `channels` on `renderer` — call once per renderer right
    /// after it's assigned a cloned (never shared) mesh. `itemLabel` is purely for warning text.</summary>
    public void RegisterRenderer(SkinnedMeshRenderer renderer, IReadOnlyList<MorphChannel> channels, string itemLabel)
    {
        if (renderer == null || renderer.sharedMesh == null) return;

        var map = new Dictionary<MorphChannel, int>();
        var mesh = renderer.sharedMesh;

        foreach (var channel in channels)
        {
            int index = mesh.GetBlendShapeIndex(channel.ToString());
            if (index < 0)
            {
                Debug.LogWarning($"[AvatarBodyMorphController] '{itemLabel}' declares morph channel " +
                                  $"'{channel}' but its mesh '{mesh.name}' has no matching blendshape " +
                                  $"(expected name '{channel}') — that channel will simply never move on this renderer.");
                continue;
            }
            map[channel] = index;
        }

        _renderers.Add(new RegisteredRenderer { Renderer = renderer, ChannelToBlendShapeIndex = map });
    }

    /// <summary>Drives every registered renderer's cached blendshape indices from `values` — renderers
    /// with no matching channel for a given MorphWeight are silently skipped (see RegisterRenderer's
    /// own doc on why that's already resolved, not re-checked per Apply call).</summary>
    public void Apply(BodyMorphValues values)
    {
        values.GetMorphWeights(_scratch);

        foreach (var registered in _renderers)
        {
            if (registered.Renderer == null) continue;

            foreach (var morphWeight in _scratch)
            {
                if (!registered.ChannelToBlendShapeIndex.TryGetValue(morphWeight.Channel, out int index)) continue;
                registered.Renderer.SetBlendShapeWeight(index, Mathf.Clamp01(morphWeight.Weight01) * 100f);
            }
        }
    }
}
