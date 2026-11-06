using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shrinks texture SOURCE files to the size Unity already imports them at: a PNG larger than the largest
/// maxTextureSize of any of its platforms is rewritten at that size (aspect kept). What the game loads does not
/// change; the project and the repository stop carrying pixels no build ever uses. Pipeline assets keep their
/// full-resolution originals in AssetsMaterial/3D_pipeline/Output/&lt;asset&gt;/Textures/&lt;Q&gt;/.
///
/// Downscaling is a chain of 2× box reductions (each blit averages 2×2 texels, like mip generation) plus a final
/// bilinear step for sizes that are not a power-of-two fraction. Raw values are kept (linear read/write, no colour
/// conversion); RGB stays RGB, alpha stays alpha.
///
/// Run on demand (menu / batch) and at the end of the garment / bonus / hair setups, after they configure the
/// importers: a texture is only shrunk once its final max size is set.
/// Menu: Tools > MusicGame > Art > Shrink Texture Sources To Import Size.
/// Batch: -executeMethod TextureSourceShrinker.RunFromCommandLine [-shrinkRoot Assets/_Project/Art]
/// </summary>
public static class TextureSourceShrinker
{
    public const string DefaultRoot = "Assets/_Project/Art";

    [MenuItem("Tools/MusicGame/Art/Shrink Texture Sources To Import Size")]
    public static void RunMenu() => Debug.Log(Run(DefaultRoot));

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-shrinkRoot");
        string report = Run(i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultRoot);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/TextureSourceShrinker.txt", report);
        Debug.Log(report);
        if (Application.isBatchMode) EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    /// <summary>Shrinks every oversized PNG under `root` (an Assets/ folder). Returns a report.</summary>
    public static string Run(string root) => Shrink(AssetDatabase.FindAssets("t:Texture2D", new[] { root })
                                                                .Select(AssetDatabase.GUIDToAssetPath));

    /// <summary>Shrinks the given texture assets when they are larger than their import size.</summary>
    public static string Shrink(IEnumerable<string> assetPaths)
    {
        var log = new StringBuilder("[TextureSourceShrinker]\n");
        int shrunk = 0, checkedCount = 0;
        long before = 0, after = 0;
        var changed = new List<string>();
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string path in assetPaths.Distinct())
            {
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                checkedCount++;
                try
                {
                    string full = Path.GetFullPath(path);
                    var (w, h, alpha) = PngSize(full);
                    int max = ImportMax(importer);
                    if (w <= max && h <= max) continue;
                    float scale = (float)max / Math.Max(w, h);
                    int tw = Math.Max(1, Mathf.RoundToInt(w * scale)), th = Math.Max(1, Mathf.RoundToInt(h * scale));
                    long size = new FileInfo(full).Length;
                    byte[] png = Downscale(File.ReadAllBytes(full), tw, th, alpha);
                    File.WriteAllBytes(full, png);
                    before += size;
                    after += png.Length;
                    shrunk++;
                    changed.Add(path);
                }
                catch (Exception e) { log.AppendLine($"  FAIL  {path}: {e.Message}"); }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var path in changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        finally { AssetDatabase.StopAssetEditing(); }
        log.AppendLine($"  {checkedCount} textures checked, {shrunk} shrunk to their import size: " +
                       $"{before / 1048576.0:N0} MB -> {after / 1048576.0:N0} MB");
        return log.ToString();
    }

    /// <summary>The largest size any platform imports this texture at (default + overridden platforms).</summary>
    public static int ImportMax(TextureImporter importer)
    {
        int max = importer.maxTextureSize;
        foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL", "Server" })
        {
            var s = importer.GetPlatformTextureSettings(platform);
            if (s.overridden) max = Math.Max(max, s.maxTextureSize);
        }
        return max;
    }

    private static (int w, int h, bool alpha) PngSize(string path)
    {
        using var f = File.OpenRead(path);
        var b = new byte[26];
        if (f.Read(b, 0, 26) != 26 || b[0] != 0x89 || b[1] != 0x50) throw new InvalidDataException("not a PNG");
        int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return (Be(16), Be(20), b[25] == 4 || b[25] == 6 || HasTransparencyChunk(path));   // colour type with alpha (or tRNS)
    }

    /// <summary>A tRNS chunk before the image data (palette / colour-key transparency).</summary>
    private static bool HasTransparencyChunk(string path)
    {
        using var f = new BinaryReader(File.OpenRead(path));
        f.BaseStream.Position = 8;
        while (f.BaseStream.Position + 8 <= f.BaseStream.Length)
        {
            var lb = f.ReadBytes(4);
            int len = (lb[0] << 24) | (lb[1] << 16) | (lb[2] << 8) | lb[3];
            string type = Encoding.ASCII.GetString(f.ReadBytes(4));
            if (type == "tRNS") return true;
            if (type == "IDAT" || type == "IEND") return false;
            f.BaseStream.Position += len + 4;
        }
        return false;
    }

    private static byte[] Downscale(byte[] source, int tw, int th, bool alpha)
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: true);
        var rts = new List<RenderTexture>();
        var previous = RenderTexture.active;
        try
        {
            if (!src.LoadImage(source, markNonReadable: false)) throw new InvalidDataException("cannot decode");
            src.filterMode = FilterMode.Bilinear;
            src.wrapMode = TextureWrapMode.Clamp;
            Texture current = src;
            int w = src.width, h = src.height;
            while (w / 2 >= tw && h / 2 >= th)
            {
                w /= 2; h /= 2;
                current = Blit(current, w, h, rts);
            }
            if (w != tw || h != th) current = Blit(current, tw, th, rts);
            RenderTexture.active = (RenderTexture)current;
            var dst = new Texture2D(tw, th, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, linear: true);
            dst.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            dst.Apply();
            byte[] png = dst.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(dst);
            return png;
        }
        finally
        {
            RenderTexture.active = previous;
            foreach (var rt in rts) RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(src);
        }
    }

    private static RenderTexture Blit(Texture source, int w, int h, List<RenderTexture> rts)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        Graphics.Blit(source, rt);
        rts.Add(rt);
        return rt;
    }
}
