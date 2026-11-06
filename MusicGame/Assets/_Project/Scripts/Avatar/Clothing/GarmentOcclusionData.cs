using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// The occlusion masks of ONE garment variant (one quality prefab), decoded from its `_occlusion.bytes`
/// TextAsset (written by GarmentMaskImporter from the 3D pipeline's Rigged/Masks/*.gmask + per-vertex anchors):
///
///   cover  res×res bytes in the canonical body's UV space (row 0 = v 0): 0 = this garment is not over that
///          skin, else 1 + mm above the skin + 30 (CodeOffset). Used to hide LOWER garments' vertices.
///   skin   res×res bytes: 255 = the skin under this garment is hidden, 0 = visible. Max-combined into the body mask.
///   anchors, per renderer and per vertex of its imported mesh: body-UV texel (x, y) of the nearest skin point and
///          the vertex's height above that skin in the same code (255 = too far from the body, never hidden).
///
/// A vertex of garment G is hidden by an equipped garment O (O.layer > G.layer and O hides G's category) when O
/// covers G's anchor texel and O is at most O.occlusionGap above that vertex: everything of G that sits under O,
/// including what pokes through it. A triangle is dropped when its three vertices are hidden.
///
/// Decoded once per asset and shared by every avatar wearing it (reference-counted, like the topology cache).
/// </summary>
public sealed class GarmentOcclusionData
{
    public const byte CodeOffset = 31;   // code of a surface exactly on the skin (1 + 30 mm behind)
    public const byte FarCode = 255;
    private static readonly byte[] Magic = { (byte)'G', (byte)'O', (byte)'C', (byte)'C' };
    public const int Version = 1;

    private static readonly Dictionary<TextAsset, GarmentOcclusionData> s_cache = new();

    public TextAsset Source { get; }
    public int Resolution { get; }
    public byte[] Cover { get; }
    public byte[] Skin { get; }
    /// <summary>Per renderer layout index: texel index (y * res + x) and height code of every vertex.</summary>
    public int[][] AnchorTexel { get; }
    public byte[][] AnchorCode { get; }
    private int _references;

    public static int CachedCount => s_cache.Count;

    private GarmentOcclusionData(TextAsset source, int res, byte[] cover, byte[] skin, int[][] texels, byte[][] codes)
    {
        Source = source; Resolution = res; Cover = cover; Skin = skin; AnchorTexel = texels; AnchorCode = codes;
    }

    public static GarmentOcclusionData Acquire(TextAsset source)
    {
        if (source == null) return null;
        if (!s_cache.TryGetValue(source, out var data))
        {
            data = Decode(source.bytes, source);
            if (data == null) return null;
            s_cache.Add(source, data);
        }
        data._references++;
        return data;
    }

    public void Release()
    {
        if (_references <= 0) return;
        if (--_references == 0) s_cache.Remove(Source);
    }

    public int VertexCount(int renderer) => renderer < AnchorCode.Length ? AnchorCode[renderer].Length : 0;

    // ── Format (shared with the importer) ─────────────────────────────────────────
    //   "GOCC" u16 version, u16 resolution, u16 rendererCount, i32 vertexCount × rendererCount,
    //   raw deflate of: cover[res²], skin[res²], then per renderer u16 x[n], u16 y[n], u8 code[n].

    public static byte[] Encode(int res, byte[] cover, byte[] skin, IReadOnlyList<(ushort[] x, ushort[] y, byte[] code)> anchors)
    {
        using var output = new MemoryStream();
        using (var w = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write((ushort)Version);
            w.Write((ushort)res);
            w.Write((ushort)anchors.Count);
            foreach (var a in anchors) w.Write(a.code.Length);
        }
        using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(deflate))
        {
            w.Write(cover);
            w.Write(skin);
            foreach (var a in anchors)
            {
                foreach (var v in a.x) w.Write(v);
                foreach (var v in a.y) w.Write(v);
                w.Write(a.code);
            }
        }
        return output.ToArray();
    }

    public static GarmentOcclusionData Decode(byte[] bytes, TextAsset source = null)
    {
        string name = source != null ? source.name : "occlusion data";
        try
        {
            using var input = new MemoryStream(bytes);
            using var r = new BinaryReader(input);
            var magic = r.ReadBytes(4);
            for (int i = 0; i < 4; i++) if (magic[i] != Magic[i]) throw new InvalidDataException("not a GOCC file");
            int version = r.ReadUInt16();
            if (version != Version) throw new InvalidDataException($"version {version} (expected {Version})");
            int res = r.ReadUInt16();
            int renderers = r.ReadUInt16();
            var counts = new int[renderers];
            for (int i = 0; i < renderers; i++) counts[i] = r.ReadInt32();

            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var d = new BinaryReader(deflate);
            var cover = ReadExactly(d, res * res);
            var skin = ReadExactly(d, res * res);
            var texels = new int[renderers][];
            var codes = new byte[renderers][];
            for (int i = 0; i < renderers; i++)
            {
                int n = counts[i];
                var xs = ReadExactly(d, n * 2);
                var ys = ReadExactly(d, n * 2);
                codes[i] = ReadExactly(d, n);
                var t = texels[i] = new int[n];
                for (int v = 0; v < n; v++)
                {
                    int x = xs[2 * v] | (xs[2 * v + 1] << 8);
                    int y = ys[2 * v] | (ys[2 * v + 1] << 8);
                    t[v] = Mathf.Min(y, res - 1) * res + Mathf.Min(x, res - 1);
                }
            }
            return new GarmentOcclusionData(source, res, cover, skin, texels, codes);
        }
        catch (Exception e)
        {
            GarmentDiagnostics.Error($"'{name}' could not be decoded ({e.Message}) — the garment hides nothing.");
            return null;
        }
    }

    private static byte[] ReadExactly(BinaryReader r, int count)
    {
        var b = r.ReadBytes(count);
        if (b.Length != count) throw new EndOfStreamException($"truncated ({b.Length}/{count} bytes)");
        return b;
    }
}
