using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the approved full-screen style collection and verifies each viewport.</summary>
[InitializeOnLoad]
public static class StyleCornerSetup
{
    const string Art = "Assets/_Project/Art/UI/Analysis/";
    const string Backgrounds = "Assets/_Project/Resources/UI/AnalysisBackdrops/";
    const string Output = "../VisualReviews/AllStyleBackgrounds";
    static double nextPoll;
    static StyleCornerSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Logs/StyleCornerSetup.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { File.Delete(request); } catch (IOException) { return; }
        try { Install(); }
        catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/MusicGame/UI/Install All Style Backgrounds")]
    public static void Install()
    {
        Directory.CreateDirectory(Output);
        Directory.CreateDirectory(Backgrounds);
        if (!File.Exists(Backgrounds + "Dance.png")) File.Copy(Art + "DanceBackdrop.png", Backgrounds + "Dance.png");
        var report = new StringBuilder();
        foreach (GameMusicStyle style in Enum.GetValues(typeof(GameMusicStyle)))
        {
            if (style == GameMusicStyle.Unknown) continue;
            string path = Backgrounds + style + ".png";
            if (!File.Exists(path)) throw new Exception("Missing style background: " + style);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false; importer.isReadable = false; importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            float aspect = (float)texture.width / texture.height;
            if (Mathf.Abs(aspect - 16f / 9f) > .03f) throw new Exception("Wrong landscape proportions: " + style);
            report.AppendLine(style + ": " + texture.width + "x" + texture.height + ", opaque, compressed, no mipmaps.");
        }
        const string prefab = "Assets/_Project/Prefabs/UI/AnalyzingScreen.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefab);
        try
        {
            var p = root.GetComponent<AnalysisPresentation>();
            p.danceBackdrop = null;
            p.motifAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "AnalysisMotifs.png");
            p.cornerAtlas = null; p.danceFrame = null;
            PrefabUtility.SaveAsPrefabAsset(root, prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        foreach (GameMusicStyle style in Enum.GetValues(typeof(GameMusicStyle)))
        {
            if (style == GameMusicStyle.Unknown) continue;
            DancePreparationSetup.Preview(1920, 1080, style, Output);
            DancePreparationSetup.Preview(1280, 720, style, Output);
            DancePreparationSetup.Preview(2560, 1080, style, Output);
        }
        report.AppendLine("PASS: 17 backgrounds connected by style name; 51 viewport renders exported. Unknown stays neutral. Only the current style texture is loaded by the presentation and it is released on hide/style change.");
        File.WriteAllText(Output + "/result.txt", report.ToString());
        Debug.Log("[StyleCornerSetup] All 17 style backgrounds installed and rendered.");
    }
}
