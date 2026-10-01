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
/// GENERIC PROPAGATION: every registered renderer is probed for ALL canonical channel names
/// (Gender, MaleSlim/Heavy/Muscle, FemaleSlim/Heavy/Muscle — MorphChannel's names, never renamed),
/// not only the ones its item declares — so any garment/hair exported with matching shape keys
/// follows the body automatically; a renderer missing a shape simply never moves that channel.
/// A renderer registered AFTER values were applied (a wearable attached later) receives the
/// current values immediately — no rebuild needed.
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
    private static readonly MorphChannel[] CanonicalChannels = (MorphChannel[])System.Enum.GetValues(typeof(MorphChannel));
    private bool _hasValues;
    private BodyMorphValues _current;

    /// <summary>Last values applied (what a newly registered renderer is brought up to).</summary>
    public BodyMorphValues CurrentValues => _current;

    /// <summary>True when `renderer`'s mesh exposes at least one canonical morph blendshape.</summary>
    public static bool HasAnyCanonicalBlendShape(SkinnedMeshRenderer renderer)
    {
        var mesh = renderer != null ? renderer.sharedMesh : null;
        if (mesh == null || mesh.blendShapeCount == 0) return false;
        foreach (var channel in CanonicalChannels)
            if (mesh.GetBlendShapeIndex(channel.ToString()) >= 0) return true;
        return false;
    }

    /// <summary>Caches blendshape indices on `renderer` for every canonical channel its mesh exposes —
    /// call once per renderer right after it's assigned a cloned (never shared) mesh. `declaredChannels`
    /// (optional, e.g. a wearable variant's supportedMorphChannels) only adds a warning when a DECLARED
    /// channel is missing; undeclared missing channels are ignored silently. If values were already
    /// applied, the renderer is brought up to them immediately. Returns the number of channels found.</summary>
    public int RegisterRenderer(SkinnedMeshRenderer renderer, IReadOnlyList<MorphChannel> declaredChannels, string itemLabel)
    {
        if (renderer == null || renderer.sharedMesh == null) return 0;
        _renderers.RemoveAll(r => r.Renderer == renderer); // re-registration replaces, never duplicates

        var map = new Dictionary<MorphChannel, int>();
        var mesh = renderer.sharedMesh;

        foreach (var channel in CanonicalChannels)
        {
            int index = mesh.GetBlendShapeIndex(channel.ToString());
            if (index >= 0) { map[channel] = index; continue; }
            if (declaredChannels != null && Contains(declaredChannels, channel))
                Debug.LogWarning($"[AvatarBodyMorphController] '{itemLabel}' declares morph channel " +
                                  $"'{channel}' but its mesh '{mesh.name}' has no matching blendshape " +
                                  $"(expected name '{channel}') — that channel will simply never move on this renderer.");
        }

        var registered = new RegisteredRenderer { Renderer = renderer, ChannelToBlendShapeIndex = map };
        _renderers.Add(registered);
        if (_hasValues) ApplyTo(registered);
        return map.Count;
    }

    public int RegisterRenderer(SkinnedMeshRenderer renderer, string itemLabel) => RegisterRenderer(renderer, null, itemLabel);

    private static bool Contains(IReadOnlyList<MorphChannel> list, MorphChannel channel)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == channel) return true;
        return false;
    }

    /// <summary>Drives every registered renderer's cached blendshape indices from `values` — renderers
    /// with no matching channel for a given MorphWeight are silently skipped (see RegisterRenderer's
    /// own doc on why that's already resolved, not re-checked per Apply call).</summary>
    public void Apply(BodyMorphValues values)
    {
        _current = values;
        _hasValues = true;
        values.GetMorphWeights(_scratch);
        foreach (var registered in _renderers) ApplyWeights(registered);
    }

    private void ApplyTo(RegisteredRenderer registered)
    {
        _current.GetMorphWeights(_scratch);
        ApplyWeights(registered);
    }

    // The SAME weights for every renderer (body, hair, garments) — computed once per Apply.
    private void ApplyWeights(RegisteredRenderer registered)
    {
        if (registered.Renderer == null) return;
        foreach (var morphWeight in _scratch)
        {
            if (!registered.ChannelToBlendShapeIndex.TryGetValue(morphWeight.Channel, out int index)) continue;
            registered.Renderer.SetBlendShapeWeight(index, Mathf.Clamp01(morphWeight.Weight01) * 100f);
        }
    }
}
