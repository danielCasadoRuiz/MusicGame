using System.Collections.Generic;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Composes the body's skin visibility mask for one set of equipped garments: start black, then take the
/// per-pixel MAXIMUM with every garment's skin map (GarmentOcclusionData.Skin, 255 = skin hidden). No skin map
/// → null (callers bind a black texture, i.e. the whole body stays visible).
///
/// The result is a linear R8 texture (no mipmaps, Clamp, bilinear) at the largest map resolution, uploaded and
/// then made non-readable, so it costs width×height bytes of GPU memory and no CPU copy. Identical garment sets
/// share ONE texture across avatars (reference-counted by key); the last Release destroys it.
///
/// The max-combine runs as a Burst job over the raw map data: a 512² set composes well under a millisecond.
/// </summary>
public static class BodyVisibilityMask
{
    private sealed class Entry
    {
        public Texture2D Texture;
        public int References;
    }

    private static readonly Dictionary<string, Entry> s_cache = new();
    private static readonly StringBuilder s_key = new();
    private static readonly List<int> s_ids = new();

    /// <summary>Live composed masks — diagnostics/tests (0 once every avatar released its mask).</summary>
    public static int CachedCount => s_cache.Count;

    /// <summary>Cache key for the skin maps of a set of garments (any order); null when there are none.</summary>
    public static string KeyFor(IReadOnlyList<GarmentOcclusionData> skinMaps)
    {
        if (skinMaps == null || skinMaps.Count == 0) return null;
        s_ids.Clear();
        foreach (var map in skinMaps) s_ids.Add(map.Source != null ? map.Source.GetInstanceID() : map.GetHashCode());
        s_ids.Sort();
        s_key.Clear();
        foreach (int id in s_ids) s_key.Append(id).Append('|');
        return s_key.ToString();
    }

    /// <summary>The shared mask for `skinMaps` (composed on first use), or null when there is none. Every
    /// non-null Acquire must be paired with Release(key).</summary>
    public static Texture2D Acquire(IReadOnlyList<GarmentOcclusionData> skinMaps, string key)
    {
        if (key == null) return null;
        if (s_cache.TryGetValue(key, out var entry))
        {
            entry.References++;
            return entry.Texture;
        }
        var texture = Compose(skinMaps);
        if (texture == null) return null;
        s_cache.Add(key, new Entry { Texture = texture, References = 1 });
        return texture;
    }

    public static void Release(string key)
    {
        if (key == null || !s_cache.TryGetValue(key, out var entry)) return;
        if (--entry.References > 0) return;
        s_cache.Remove(key);
        if (entry.Texture == null) return;
        if (Application.isPlaying) Object.Destroy(entry.Texture);
        else Object.DestroyImmediate(entry.Texture);
    }

    private static Texture2D Compose(IReadOnlyList<GarmentOcclusionData> skinMaps)
    {
        if (skinMaps == null || skinMaps.Count == 0) return null;
        int size = 0;
        foreach (var map in skinMaps) size = Mathf.Max(size, map.Resolution);
        if (size == 0) return null;

        var result = new Texture2D(size, size, TextureFormat.R8, mipChain: false, linear: true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
        };
        var target = result.GetRawTextureData<byte>();
        JobHandle chain = new FillJob { Destination = target, Value = 0 }.Schedule(target.Length, 16384);
        var names = new StringBuilder();
        var sources = new List<NativeArray<byte>>();
        foreach (var map in skinMaps)
        {
            var source = new NativeArray<byte>(map.Skin, Allocator.TempJob);
            sources.Add(source);
            chain = new MaxScaledJob { Source = source, SourceSize = map.Resolution, DestinationSize = size, Destination = target }
                .Schedule(target.Length, 16384, chain);
            if (names.Length > 0) names.Append('+');
            names.Append(map.Source != null ? map.Source.name : "mask");
        }
        chain.Complete();
        foreach (var s in sources) s.Dispose();
        result.name = "SkinVisibility[" + names + "]";
        result.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return result;
    }

    [BurstCompile]
    private struct FillJob : IJobParallelFor
    {
        public NativeArray<byte> Destination;
        public byte Value;
        public void Execute(int index) => Destination[index] = Value;
    }

    /// <summary>Max-combines a square map of another resolution (nearest texel), rows bottom-up like the texture.</summary>
    [BurstCompile]
    private struct MaxScaledJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int SourceSize;
        public int DestinationSize;
        public NativeArray<byte> Destination;

        public void Execute(int index)
        {
            int x = index % DestinationSize, y = index / DestinationSize;
            int sx = x * SourceSize / DestinationSize, sy = y * SourceSize / DestinationSize;
            byte value = Source[sy * SourceSize + sx];
            if (value > Destination[index]) Destination[index] = value;
        }
    }
}
