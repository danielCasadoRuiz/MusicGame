#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-step setup of the help / tip / conversion UI (idempotent — nothing existing is overwritten):
///   1. Placeholder icons: a white square on a transparent background, ONE PNG per bonus type (Bonus_&lt;BonusKind&gt;)
///      and per build stat (Stat_&lt;FightStatId&gt;) in Art/UI/Icons/Placeholders, imported as single sprites.
///      Existing files are kept (final art dropped in with the same name simply replaces the placeholder).
///   2. Resources/UI/HelpUIConfig.asset: the ONE place those sprites are referenced (empty slots only are filled)
///      plus the gameplay config the tints and rules come from.
///   3. The Help.* UIText keys (only the missing ones).
///   4. Builds the missing UI prefabs and patches FightHud / PauseMenu / LiveHud (UIScreenPrefabBuilder).
/// Batch: -executeMethod HelpUISetup.RunFromCommandLine   (Logs/HelpUISetup.txt)
/// </summary>
public static class HelpUISetup
{
    public const string PlaceholderFolder = "Assets/_Project/Art/UI/Icons/Placeholders";
    public const string ConfigPath = "Assets/_Project/Resources/UI/HelpUIConfig.asset";

    [MenuItem("Tools/MusicGame/UI/Help Placeholders + Prefabs")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string report = Apply();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/HelpUISetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        var log = new StringBuilder();
        EnsureFolder(PlaceholderFolder);
        EnsureFolder("Assets/_Project/Resources/UI");

        var cfg = AssetDatabase.LoadAssetAtPath<HelpUIConfigSO>(ConfigPath);
        if (cfg == null)
        {
            cfg = ScriptableObject.CreateInstance<HelpUIConfigSO>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            log.AppendLine($"  new   {ConfigPath}");
        }

        var bonusIcons = cfg.bonusIcons.ToList();
        foreach (var kind in BonusKinds.All)
        {
            var sprite = EnsurePlaceholder($"Bonus_{kind}", log);
            int i = bonusIcons.FindIndex(b => b.kind == kind);
            if (i < 0) bonusIcons.Add(new HelpUIConfigSO.BonusIcon { kind = kind, sprite = sprite });
            else if (bonusIcons[i].sprite == null) bonusIcons[i] = new HelpUIConfigSO.BonusIcon { kind = kind, sprite = sprite };
        }
        cfg.bonusIcons = bonusIcons.ToArray();

        // Placeholder tints of the five destinations (the bars): editable in the asset.
        var statColors = new[]
        {
            (FightStatId.PunchPower, new Color(0.95f, 0.62f, 0.25f)), (FightStatId.KickPower, new Color(0.92f, 0.3f, 0.25f)),
            (FightStatId.Agility, new Color(0.3f, 0.78f, 1f)), (FightStatId.Resistance, new Color(0.5f, 0.85f, 0.4f)),
            (FightStatId.ImpactPower, new Color(1f, 0.82f, 0.3f)),
        };
        var statIcons = cfg.statIcons.ToList();
        foreach (var (stat, color) in statColors)
        {
            var sprite = EnsurePlaceholder($"Stat_{stat}", log);
            int i = statIcons.FindIndex(s => s.stat == stat);
            if (i < 0) statIcons.Add(new HelpUIConfigSO.StatIcon { stat = stat, sprite = sprite, color = color });
            else if (statIcons[i].sprite == null) statIcons[i] = new HelpUIConfigSO.StatIcon { stat = stat, sprite = sprite, color = statIcons[i].color };
        }
        cfg.statIcons = statIcons.ToArray();

        if (cfg.gameplay == null)
        {
            var guid = AssetDatabase.FindAssets("t:MusicRunnerGameplayConfig").FirstOrDefault();
            cfg.gameplay = guid != null ? AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(AssetDatabase.GUIDToAssetPath(guid)) : null;
            log.AppendLine(cfg.gameplay != null ? $"  set   gameplay config {AssetDatabase.GetAssetPath(cfg.gameplay)}" : "  FAIL  no MusicRunnerGameplayConfig asset");
        }
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();

        int added = LocalizationSetup.AddMissingEntries(LocalizationSetup.Entries.Where(e => e.key.StartsWith("Help.")).Select(e => e.key));
        log.AppendLine($"  loc   {added} Help.* entr(ies) added");

        log.Append(UIScreenPrefabBuilder.Build(rebuild: false));

        // Verification of the central references (what the controllers read).
        foreach (var kind in BonusKinds.All)
            if (cfg.Icon(kind) == null) log.AppendLine($"  FAIL  HelpUIConfig has no icon for {kind}");
        foreach (var stat in BonusHelpCatalog.BuildStats)
            if (cfg.StatSprite(stat) == null) log.AppendLine($"  FAIL  HelpUIConfig has no icon for {stat}");
        log.AppendLine("  done");
        return log.ToString();
    }

    // 64×64: an opaque white 48×48 square centred on a transparent background (tintable, clearly a placeholder).
    private static Sprite EnsurePlaceholder(string name, StringBuilder log)
    {
        string path = $"{PlaceholderFolder}/{name}.png";
        if (!File.Exists(path))
        {
            const int size = 64, inset = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = x >= inset && x < size - inset && y >= inset && y < size - inset
                        ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"  new   {path}");
        }
        return UIComponentPrefabBuilder.EnsureSingleSprite(path, log);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
