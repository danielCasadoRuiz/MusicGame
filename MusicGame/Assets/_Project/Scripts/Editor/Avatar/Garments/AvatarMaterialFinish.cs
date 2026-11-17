using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Authored matte finishes, applied after generated PBR maps so reimports retain the intended look.</summary>
[InitializeOnLoad]
public static class AvatarMaterialFinish
{
    static double nextPoll;
    static AvatarMaterialFinish() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Logs/MaterialFinish/apply.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { ApplyAll(); }
        catch (Exception e) { File.WriteAllText("Logs/MaterialFinish/failure.txt", e.ToString()); Debug.LogException(e); }
    }
    public static bool IsHair(string name)
    {
        name = name.ToLowerInvariant();
        return name.Contains("_hair") || name.Contains("_wig") || name.Contains("_beard");
    }

    public static bool IsMatteClothing(string name) => name.StartsWith("Vivaldi_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Mozart_", StringComparison.OrdinalIgnoreCase);

    public static void Apply(Material material, string piece)
    {
        if (IsHair(piece))
        {
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .06f);
            material.SetFloat("_Glossiness", .06f);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.SetFloat("_ClearCoatMask", 0);
            EditorUtility.SetDirty(material);
        }
        else if (IsMatteClothing(piece))
        {
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Glossiness", .08f);
            material.SetFloat("_ClearCoatMask", 0);
            EditorUtility.SetDirty(material);
        }
        else if (piece.Equals("Dance_jacket", StringComparison.OrdinalIgnoreCase) || piece.Equals("Dance_trousers", StringComparison.OrdinalIgnoreCase))
        {
            var original = material.GetTexture("_MetallicGlossMap");
            string source = AssetDatabase.GetAssetPath(original).Replace("_Matte.png", ".png");
            string albedo = AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"));
            if (!File.Exists(source) || !File.Exists(albedo)) throw new InvalidOperationException("Missing Dance PBR source maps: " + material.name);
            string target = source.Replace(".png", "_Matte.png");
            if (!File.Exists(target) || File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(source) || File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(albedo))
            {
                var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                var color = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    packed.LoadImage(File.ReadAllBytes(source));
                    color.LoadImage(File.ReadAllBytes(albedo));
                    if (packed.width != color.width || packed.height != color.height) throw new InvalidOperationException("Dance map sizes differ: " + material.name);
                    var pixels = packed.GetPixels32();
                    var colors = color.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var c = colors[i];
                        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b)) / 255f;
                        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                        float white = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .85f, min)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .25f, max - min)));
                        pixels[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(pixels[i].a * Mathf.Lerp(.3f, 1, white)));
                    }
                    packed.SetPixels32(pixels);
                    packed.Apply();
                    File.WriteAllBytes(target, packed.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(packed); UnityEngine.Object.DestroyImmediate(color); }
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(source);
            if (importer.sRGBTexture || importer.maxTextureSize != sourceImporter.maxTextureSize)
            {
                importer.sRGBTexture = false;
                importer.maxTextureSize = sourceImporter.maxTextureSize;
                importer.SaveAndReimport();
            }
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(target));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .20f); // Fabric <= .06; neutral white plastic <= .20, respecting original roughness.
            material.SetFloat("_ClearCoatMask", 0);
            EditorUtility.SetDirty(material);
        }
    }

    [MenuItem("Tools/MusicGame/Avatars/Surface/Apply Matte Hair And Clothing")]
    public static void ApplyAll()
    {
        var report = new StringBuilder();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Art/Opponents", "Assets/_Project/Art/Player" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) continue;
                string piece = path.Split('/').Reverse().Skip(3).First();
                if (!IsHair(piece) && !IsMatteClothing(piece) && piece != "Dance_jacket" && piece != "Dance_trousers") continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Apply(material, piece);
                report.AppendLine("PASS matte finish: " + path);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/MaterialFinish");
            File.WriteAllText("Logs/MaterialFinish/materials.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        catch (Exception e) { Debug.LogException(e); throw; }
    }

    public static void RunFromCommandLine()
    {
        try { ApplyAll(); EditorApplication.delayCall += AvatarWearableDeliveryCheck.RunFromCommandLine; }
        catch (Exception) { if (Application.isBatchMode) EditorApplication.Exit(1); }
    }
}
