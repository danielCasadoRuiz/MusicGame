using System.Collections.Generic;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Composes the body's skin visibility mask for one set of covered regions: start black, then take the
/// per-pixel MAXIMUM with every region mask (white wins). A region without a mask (or with an unusable one)
/// leaves its skin visible while the other masks still apply; no applicable mask at all → null (callers bind
/// a black texture, i.e. the whole body stays visible).
///
/// The result is a linear R8 texture (no mipmaps, Clamp, bilinear), uploaded and then made non-readable, so
/// it costs width×height bytes of GPU memory and no CPU copy. Identical region sets share ONE texture across
/// avatars (reference-counted by key); the last Release destroys it.
///
/// The max-combine runs as a Burst job over the masks' raw pixel data (no per-pixel managed calls, no
/// GetPixels arrays): a 1024² mask set composes in about a millisecond.
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

    /// <summary>Live composed masks — diagnostics/tests (0 once every avatar released its mask).</summary>
    public static int CachedCount => s_cache.Count;

    /// <summary>Cache key for `regions` on `catalog` (regions must already be sorted/deduplicated).</summary>
    public static string KeyFor(GarmentRegionCatalogSO catalog, SortedSet<string> regions)
    {
        s_key.Clear();
        s_key.Append(catalog != null ? catalog.GetInstanceID() : 0).Append(':');
        foreach (var region in regions) s_key.Append(region).Append('|');
        return s_key.ToString();
    }

    /// <summary>The shared mask for `regions` (composed on first use), or null when no region has a usable
    /// mask. Every non-null Acquire must be paired with Release(key).</summary>
    public static Texture2D Acquire(GarmentRegionCatalogSO catalog, SortedSet<string> regions, string key)
    {
        if (s_cache.TryGetValue(key, out var entry))
        {
            entry.References++;
            return entry.Texture;
        }
        var texture = Compose(catalog, regions);
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

    private static Texture2D Compose(GarmentRegionCatalogSO catalog, SortedSet<string> regions)
    {
        if (catalog == null || regions.Count == 0) return null;

        Texture2D result = null;
        NativeArray<byte> target = default;
        JobHandle chain = default;
        int width = 0, height = 0;
        var names = new StringBuilder();

        foreach (var region in regions)
        {
            if (!catalog.IsKnownRegion(region))
            {
                GarmentDiagnostics.Warn($"bodyCoverage region '{region}' is not in catalog '{catalog.name}' — that skin stays visible.");
                continue;
            }
            var mask = catalog.GetMask(region);
            if (mask == null)
            {
                GarmentDiagnostics.Warn($"No body mask for region '{region}' in catalog '{catalog.name}' — that skin stays visible.");
                continue;
            }
            if (!TryGetLayout(mask, out int stride, out int channel, out string problem))
            {
                GarmentDiagnostics.Error($"Body mask '{mask.name}' (region '{region}') {problem} — that skin stays visible.");
                continue;
            }
            if (result == null)
            {
                width = mask.width; height = mask.height;
                result = new Texture2D(width, height, TextureFormat.R8, mipChain: false, linear: true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0,
                };
                target = result.GetRawTextureData<byte>();
                var fill = new FillJob { Destination = target, Value = 0 }.Schedule(target.Length, 16384);
                chain = fill;
            }
            else if (mask.width != width || mask.height != height)
            {
                GarmentDiagnostics.Error($"Body mask '{mask.name}' (region '{region}') is {mask.width}x{mask.height}, the others are {width}x{height} — that skin stays visible.");
                continue;
            }

            var source = mask.GetPixelData<byte>(0);
            chain = new MaxCombineJob { Source = source, Stride = stride, Channel = channel, Destination = target }
                .Schedule(target.Length, 16384, chain);
            if (names.Length > 0) names.Append('+');
            names.Append(region);
        }

        if (result == null) return null;
        chain.Complete();
        result.name = "SkinVisibility[" + names + "]";
        result.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return result;
    }

    /// <summary>Byte stride/offset of the red (or only) channel; false for formats that cannot be read raw.</summary>
    private static bool TryGetLayout(Texture2D mask, out int stride, out int channel, out string problem)
    {
        stride = 1; channel = 0; problem = null;
        if (!mask.isReadable) { problem = "is not CPU-readable (re-import it with GarmentCutsImporter)"; return false; }
        if (mask.mipmapCount > 1) { problem = "has mipmaps (re-import it with GarmentCutsImporter)"; return false; }
        switch (mask.format)
        {
            case TextureFormat.R8:
            case TextureFormat.Alpha8: return true;
            case TextureFormat.RGB24: stride = 3; return true;
            case TextureFormat.RGBA32: stride = 4; return true;
            case TextureFormat.ARGB32: stride = 4; channel = 1; return true;
            case TextureFormat.BGRA32: stride = 4; channel = 2; return true;
            default: problem = $"has format {mask.format} (expected R8)"; return false;
        }
    }

    [BurstCompile]
    private struct FillJob : IJobParallelFor
    {
        public NativeArray<byte> Destination;
        public byte Value;
        public void Execute(int index) => Destination[index] = Value;
    }

    [BurstCompile]
    private struct MaxCombineJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int Stride;
        public int Channel;
        public NativeArray<byte> Destination;

        public void Execute(int index)
        {
            byte value = Source[index * Stride + Channel];
            if (value > Destination[index]) Destination[index] = value;
        }
    }
}
