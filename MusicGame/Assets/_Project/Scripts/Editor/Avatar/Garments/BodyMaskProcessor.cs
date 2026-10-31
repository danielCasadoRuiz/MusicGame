using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Editor-side processing of the producer's body masks against the Unity body mesh:
///
///   * UV islands: the body's UV0 triangles rasterised at mask resolution (texel centres) — which texels are
///     skin at all. Everything else is empty UV background.
///   * Orientation / compatibility: the black (hidden) texels of a mask must lie on skin texels. A mask whose
///     black texels mostly fall in background but would fit when flipped vertically is flipped; one that
///     fits neither does not belong to this body's UV layout.
///   * Seam padding: bilinear sampling near a UV island's border mixes in background texels (white = visible),
///     which would leave a visible hairline of skin along the seams of a covered region. Each background
///     texel within `radius` takes the value of its NEAREST skin texel (texture padding). Skin texels are
///     never changed, so exposed skin at necklines/cuffs/waistbands is not eroded. Because the nearest-texel
///     map depends only on the islands, padding each mask is identical to padding their combination.
/// </summary>
public static class BodyMaskProcessor
{
    /// <summary>Texels whose centre lies inside any UV0 triangle of `mesh` (all submeshes), row-major from v=0.</summary>
    public static bool[] RasterizeUvIslands(Mesh mesh, int resolution)
    {
        var inside = new bool[resolution * resolution];
        var uvs = new List<Vector2>();
        mesh.GetUVs(0, uvs);
        var indices = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            indices.Clear();
            mesh.GetIndices(indices, s);
            for (int t = 0; t + 2 < indices.Count; t += 3)
            {
                Vector2 a = uvs[indices[t]] * resolution, b = uvs[indices[t + 1]] * resolution, c = uvs[indices[t + 2]] * resolution;
                float det = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(det) < 1e-12f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                int y1 = Mathf.Min(resolution - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float u = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / det;
                    float v = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / det;
                    if (u >= -1e-5f && v >= -1e-5f && u + v <= 1f + 1e-5f) inside[y * resolution + x] = true;
                }
            }
        }
        return inside;
    }

    /// <summary>For every background texel within `radius` (8-neighbour steps) of skin, the index of the skin
    /// texel it inherits from; -1 elsewhere (skin texels map to themselves).</summary>
    public static int[] NearestSkinOwners(bool[] inside, int resolution, int radius)
    {
        var owner = new int[inside.Length];
        var frontier = new List<int>();
        for (int i = 0; i < inside.Length; i++)
        {
            owner[i] = inside[i] ? i : -1;
            if (inside[i]) frontier.Add(i);
        }
        var next = new List<int>();
        for (int step = 0; step < radius && frontier.Count > 0; step++)
        {
            next.Clear();
            foreach (int p in frontier)
            {
                int px = p % resolution, py = p / resolution;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = px + dx, y = py + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= resolution || y >= resolution) continue;
                    int q = y * resolution + x;
                    if (owner[q] >= 0) continue;
                    owner[q] = owner[p];
                    next.Add(q);
                }
            }
            (frontier, next) = (next, frontier);
        }
        return owner;
    }

    /// <summary>Fraction of a mask's white texels that lie on skin, as delivered and flipped vertically.</summary>
    public static (float asIs, float flipped, int hidden) HiddenOnSkin(byte[] values, bool[] inside, int resolution)
    {
        int hidden = 0, onSkin = 0, onSkinFlipped = 0;
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            if (values[y * resolution + x] < 128) continue;
            hidden++;
            if (inside[y * resolution + x]) onSkin++;
            if (inside[(resolution - 1 - y) * resolution + x]) onSkinFlipped++;
        }
        return hidden == 0 ? (1f, 1f, 0) : (onSkin / (float)hidden, onSkinFlipped / (float)hidden, hidden);
    }

    public static void FlipVertically(byte[] values, int resolution)
    {
        for (int y = 0; y < resolution / 2; y++)
        for (int x = 0; x < resolution; x++)
        {
            int a = y * resolution + x, b = (resolution - 1 - y) * resolution + x;
            (values[a], values[b]) = (values[b], values[a]);
        }
    }

    /// <summary>Applies the seam padding (see class doc). Returns how many background texels changed.</summary>
    public static int Pad(byte[] values, int[] owner, bool[] inside)
    {
        int changed = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (inside[i] || owner[i] < 0) continue;
            byte v = values[owner[i]];
            if (values[i] != v) { values[i] = v; changed++; }
        }
        return changed;
    }

    /// <summary>Red channel of a PNG (row 0 = bottom, like Unity textures and UV v=0).</summary>
    public static byte[] ReadRed(string pngPath, out int width, out int height)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(pngPath))) throw new IOException($"'{pngPath}' is not a readable PNG.");
            width = texture.width; height = texture.height;
            var pixels = texture.GetPixels32();
            var red = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) red[i] = pixels[i].r;
            return red;
        }
        finally { Object.DestroyImmediate(texture); }
    }

    /// <summary>Writes a grey PNG (r = g = b = value); imported as single-channel R8 by the importer.</summary>
    public static void WriteGrey(string pngPath, byte[] values, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
        try
        {
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++) pixels[i] = new Color32(values[i], values[i], values[i], 255);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
            File.WriteAllBytes(pngPath, texture.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(texture); }
    }
}
