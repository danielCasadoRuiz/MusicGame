using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Progression — one-shot production roster setup + a quick validation.
///
/// Setup (safe to re-run; never touches existing composer tier entries):
///   - counts the real playable song catalog (Addressables entries labelled "Song") and seeds
///     ceil(songs / songsPerTier) content tiers per composer;
///   - creates ProgressionConfig.asset and wires it into Resources/AppConfig;
///   - creates the 12 composer OpponentDefinitions (Configs/Fight/Opponents/), every tier pointing at
///     the same provisional content (MakeHuman test recipe, AI profile Easy/Medium/Hard, default stats);
///   - makes OpponentRoster.asset the 12 composers, and moves the old placeholder opponents to
///     Configs/Fight/Debug/ (GUIDs kept, so every reference survives) + OpponentRoster_Debug.asset.
/// </summary>
public static class OpponentRosterSetup
{
    public const string OpponentsFolder = "Assets/_Project/Configs/Fight/Opponents";
    public const string DebugFolder     = "Assets/_Project/Configs/Fight/Debug";
    public const string RosterPath      = "Assets/_Project/Configs/Fight/OpponentRoster.asset";
    public const string DebugRosterPath = DebugFolder + "/OpponentRoster_Debug.asset";
    public const string ProgressionPath = "Assets/_Project/Configs/Progression/ProgressionConfig.asset";
    private const string AppConfigPath  = "Assets/_Project/Resources/AppConfig.asset";

    public static readonly (string id, string name)[] Composers =
    {
        ("bach", "Bach"), ("beethoven", "Beethoven"), ("brahms", "Brahms"), ("chopin", "Chopin"),
        ("handel", "Handel"), ("haydn", "Haydn"), ("monteverdi", "Monteverdi"), ("mozart", "Mozart"),
        ("pachelbel", "Pachelbel"), ("tchaikovsky", "Tchaikovsky"), ("vivaldi", "Vivaldi"), ("wagner", "Wagner"),
    };

    private static readonly string[] PlaceholderOpponents = { "Ash", "Bolt", "Jax", "Kira", "Nova", "Nyx", "Rex", "Vex" };

    [MenuItem("Tools/MusicGame/Progression/Setup Composer Roster")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        File.WriteAllText("Logs/OpponentRosterSetup.txt", Setup() + "\n\n" + Validate(out bool ok));
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static int CountCatalogSongs()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return 0;
        int n = 0;
        foreach (var group in settings.groups)
            if (group != null)
                foreach (var entry in group.entries)
                    if (entry.labels.Contains("Song")) n++;
        return n;
    }

    public static string Setup()
    {
        var report = new StringBuilder("[OpponentRosterSetup]\n");
        EnsureFolder(OpponentsFolder);
        EnsureFolder(DebugFolder);
        EnsureFolder(Path.GetDirectoryName(ProgressionPath).Replace('\\', '/'));

        // Progression config + seeded tier count from the REAL catalog.
        var progression = AssetDatabase.LoadAssetAtPath<ProgressionConfigSO>(ProgressionPath);
        if (progression == null)
        {
            progression = ScriptableObject.CreateInstance<ProgressionConfigSO>();
            AssetDatabase.CreateAsset(progression, ProgressionPath);
        }
        int songs = CountCatalogSongs();
        int tiers = Mathf.Max(1, Mathf.CeilToInt(songs / (float)progression.songsPerTier));
        progression.seededContentTiers = tiers;
        EditorUtility.SetDirty(progression);
        report.AppendLine($"  song catalog: {songs} songs (Addressables label 'Song'), songsPerTier {progression.songsPerTier} -> {tiers} content tiers");

        var appConfig = AssetDatabase.LoadAssetAtPath<AppConfigSO>(AppConfigPath);
        if (appConfig != null && appConfig.progression != progression)
        {
            appConfig.progression = progression;
            EditorUtility.SetDirty(appConfig);
            report.AppendLine("  AppConfig.progression assigned");
        }

        // Move placeholder opponents to Debug (GUIDs preserved).
        var placeholders = new List<OpponentDefinition>();
        foreach (var p in PlaceholderOpponents)
        {
            string from = $"Assets/_Project/Configs/Fight/Opponent_{p}.asset";
            string to   = $"{DebugFolder}/Opponent_{p}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(from) != null)
            {
                string err = AssetDatabase.MoveAsset(from, to);
                report.AppendLine(string.IsNullOrEmpty(err) ? $"  moved placeholder {p} -> Debug/" : $"  FAILED to move {p}: {err}");
            }
            var def = AssetDatabase.LoadAssetAtPath<OpponentDefinition>(to);
            if (def != null) placeholders.Add(def);
        }
        var debugRoster = LoadOrCreate<OpponentRosterSO>(DebugRosterPath);
        debugRoster.opponents = placeholders.ToArray();
        EditorUtility.SetDirty(debugRoster);

        // Provisional shared content (taken from the existing placeholder Rex setup — nothing duplicated).
        var rex = placeholders.FirstOrDefault(o => o.id == "rex");
        var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>("Assets/_Project/Avatar/MakeHuman/Content/Recipes/Avatar_MakeHuman_TestMale.asset")
                     ?? rex?.levels.FirstOrDefault()?.avatarRecipe;
        var stats  = AssetDatabase.LoadAssetAtPath<FighterStatsProfileSO>("Assets/_Project/Configs/Fight/FighterStatsProfile_Default.asset");
        var ai = new[]
        {
            AssetDatabase.LoadAssetAtPath<AIDifficultyProfile>("Assets/_Project/Configs/Fight/AIDifficultyProfile_Easy.asset"),
            AssetDatabase.LoadAssetAtPath<AIDifficultyProfile>("Assets/_Project/Configs/Fight/AIDifficultyProfile_Medium.asset"),
            AssetDatabase.LoadAssetAtPath<AIDifficultyProfile>("Assets/_Project/Configs/Fight/AIDifficultyProfile_Hard.asset"),
        };

        var composers = new List<OpponentDefinition>();
        foreach (var (id, displayName) in Composers)
        {
            string path = $"{OpponentsFolder}/Opponent_{displayName}.asset";
            var def = LoadOrCreate<OpponentDefinition>(path);
            def.id = id;
            def.displayName = displayName;
            var levels = new List<OpponentLevelConfig>(def.levels ?? System.Array.Empty<OpponentLevelConfig>());
            for (int tier = 1; tier <= tiers; tier++)
            {
                if (levels.Any(l => l != null && l.level == tier)) continue; // never overwrite authored tiers
                levels.Add(new OpponentLevelConfig
                {
                    level = tier,
                    avatarRecipe = recipe,
                    difficultyProfile = ai[Mathf.Min(tier, ai.Length) - 1],
                    combatStats = stats,
                });
            }
            def.levels = levels.OrderBy(l => l.level).ToArray();
            def.defaultConfig = new OpponentLevelConfig { level = 0, avatarRecipe = recipe, difficultyProfile = ai[0], combatStats = stats };
            EditorUtility.SetDirty(def);
            composers.Add(def);
        }

        var roster = AssetDatabase.LoadAssetAtPath<OpponentRosterSO>(RosterPath);
        roster.opponents = composers.ToArray();
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        report.AppendLine($"  production roster: {string.Join(", ", composers.Select(c => c.displayName))} ({tiers} tiers each)");
        report.AppendLine($"  debug roster: {DebugRosterPath} ({placeholders.Count} placeholder opponents)");
        return report.ToString();
    }

    // ── Validation (cases A–H; I = persistence is structural: GameSession is DontDestroyOnLoad) ──

    [MenuItem("Tools/MusicGame/Progression/Validate Roster And Selection")]
    public static void ValidateMenu() => Debug.Log(Validate(out _));

    public static string Validate(out bool ok)
    {
        var sb = new StringBuilder("[OpponentRosterSetup] validation\n");
        int fails = 0;
        void Check(string name, bool pass, string detail = "")
        {
            sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}{(pass ? "" : " — " + detail)}");
            if (!pass) fails++;
        }

        var roster = AssetDatabase.LoadAssetAtPath<OpponentRosterSO>(RosterPath);
        var progression = AssetDatabase.LoadAssetAtPath<ProgressionConfigSO>(ProgressionPath);
        var ids = roster != null ? roster.opponents.Where(o => o != null).Select(o => o.id).ToList() : new List<string>();
        Check($"A: 12 production opponents with unique ids ({ids.Count})", ids.Count == 12 && ids.Distinct().Count() == 12 &&
              Composers.All(c => ids.Contains(c.id)));

        var p = ScriptableObject.CreateInstance<ProgressionConfigSO>();
        p.songsPerTier = 5;
        Check("B: 0/4/5/9/10 completed -> tier 1/1/2/2/3",
              p.TierForCompletedSongs(0) == 1 && p.TierForCompletedSongs(4) == 1 && p.TierForCompletedSongs(5) == 2 &&
              p.TierForCompletedSongs(9) == 2 && p.TierForCompletedSongs(10) == 3);
        var prog = new GameProgression(new GameProgressionState { completedSongs = 7 }, p);
        var snap = prog.Snapshot;
        Check($"B: snapshot at 7 songs = tier 2, 2/5, progress 0.4 ({snap.CurrentTier}, {snap.CompletedSongsInCurrentTier}/{snap.SongsRequiredForNextTier}, {snap.Progress01:0.00})",
              snap.CurrentTier == 2 && snap.CompletedSongsInCurrentTier == 2 && snap.SongsRequiredForNextTier == 5 && Mathf.Approximately(snap.Progress01, 0.4f));
        Object.DestroyImmediate(p);

        if (roster != null && ids.Count > 0)
        {
            int cooldown = progression != null ? progression.recentOpponentCooldown : 2;
            var bag = new OpponentShuffleBag();
            var rng = new System.Random(1234);
            var picks = new List<string>();
            for (int i = 0; i < 12 * 50; i++) picks.Add(bag.Next(roster.opponents, cooldown, rng).id);

            bool noConsecutive = true, cooldownOk = true;
            for (int i = 1; i < picks.Count; i++)
            {
                if (picks[i] == picks[i - 1]) noConsecutive = false;
                for (int k = 1; k <= cooldown && i - k >= 0; k++) if (picks[i] == picks[i - k]) cooldownOk = false;
            }
            Check("C: 600 picks never repeat the same opponent consecutively", noConsecutive);

            // Bags: 12 consecutive picks per bag. The cooldown may pull an opponent to later in its
            // bag, never out of it, so each bag of 12 still contains every composer exactly once.
            bool bagsComplete = true;
            for (int b = 0; b + 12 <= picks.Count; b += 12)
                if (picks.Skip(b).Take(12).Distinct().Count() != 12) bagsComplete = false;
            Check("D: every bag of 12 contains all 12 composers exactly once", bagsComplete);

            bool boundaryOk = true;
            for (int b = 12; b < picks.Count; b += 12) if (picks[b] == picks[b - 1]) boundaryOk = false;
            Check("E: last of a bag is never first of the next", boundaryOk);
            Check($"F: recent cooldown {cooldown} respected over 600 picks", cooldownOk);

            var mozart = roster.opponents.FirstOrDefault(o => o != null && o.id == "mozart");
            if (mozart != null)
            {
                int maxTier = mozart.levels.Max(l => l.level);
                bool tiersOk = true;
                for (int t = 1; t <= maxTier; t++)
                {
                    var cfg = mozart.GetConfigForTier(t, out int resolved);
                    tiersOk &= cfg != null && cfg.level == t && resolved == t;
                }
                Check($"G: Mozart tier 1..{maxTier} each resolve to their own config", tiersOk);
                var above = mozart.GetConfigForTier(maxTier + 2, out int resolvedAbove);
                Check($"H: tier {maxTier + 2} (no content) resolves to highest configured tier {resolvedAbove}", above != null && resolvedAbove == maxTier);
            }
            else Check("G/H: Mozart exists", false, "no mozart in roster");
        }
        sb.AppendLine("  INFO  I: progression + bag live in GameSession ([App Bootstrap], DontDestroyOnLoad) — scene loads never recreate them.");
        ok = fails == 0;
        sb.Insert(0, ok ? "ALL PASSED\n" : $"{fails} FAILED\n");
        return sb.ToString();
    }

    // ── Debug forcing (Play mode) — production progression/bag untouched ──
    [MenuItem("Tools/MusicGame/Progression/Debug Force Mozart Tier 1")] public static void ForceMozart1() => Force("mozart", 1);
    [MenuItem("Tools/MusicGame/Progression/Debug Force Mozart Tier 2")] public static void ForceMozart2() => Force("mozart", 2);
    [MenuItem("Tools/MusicGame/Progression/Debug Force Mozart Tier 3")] public static void ForceMozart3() => Force("mozart", 3);
    [MenuItem("Tools/MusicGame/Progression/Debug Clear Force")] public static void ClearForce() => Force(null, 0);

    private static void Force(string id, int tier)
    {
        if (!Application.isPlaying || GameSession.Instance == null) { Debug.LogWarning("[OpponentRosterSetup] Enter Play mode first (GameSession lives at runtime)."); return; }
        GameSession.Instance.DebugForce(id, tier);
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
