using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports Art/Opponents/Versus/&lt;Composer&gt;_versus.png as Sprites and assigns each one to the
/// composer's OpponentLevelConfig.portrait — every tier + defaultConfig (one image per composer for
/// now; per-tier portraits stay possible because the field is per tier). The same portrait feeds
/// the selection grid and the Versus screen. Idempotent.
/// Menu: Tools > MusicGame > Fight > Setup Opponent Portraits. Batch: OpponentPortraitSetup.SetupFromCommandLine.
/// </summary>
public static class OpponentPortraitSetup
{
    private const string Folder = "Assets/_Project/Art/Opponents/Versus";

    [MenuItem("Tools/MusicGame/Fight/Setup Opponent Portraits")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/OpponentPortraitSetup.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[OpponentPortraitSetup]\n");
        var roster = Resources.Load<AppConfigSO>("AppConfig")?.opponentRoster;
        if (roster == null) return sb.AppendLine("  ERROR  AppConfig.opponentRoster missing").ToString();

        foreach (var opponent in roster.opponents.Where(o => o != null))
        {
            string path = $"{Folder}/{opponent.displayName}_versus.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { sb.AppendLine($"  ERROR  {opponent.displayName}: no image at {path}"); continue; }
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) { sb.AppendLine($"  ERROR  {opponent.displayName}: {path} did not import as a Sprite"); continue; }

            int count = 0;
            foreach (var level in opponent.levels.Append(opponent.defaultConfig).Where(l => l != null))
            {
                level.portrait = sprite;
                count++;
            }
            EditorUtility.SetDirty(opponent);
            sb.AppendLine($"  PASS  {opponent.displayName,-11} ← {Path.GetFileName(path)} ({sprite.rect.width:0}×{sprite.rect.height:0}) on {count} config(s)");
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
