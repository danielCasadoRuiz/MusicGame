#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// Tools > MusicGame > Campaign > Setup Campaign Content (idempotent):
///   • Configs/Campaign/CampaignConfig.asset, EvaluationConfig.asset, FinalBossConfig.asset → AppConfig
///   • Opponent_MusicBeing.asset — the final rival as an ordinary OpponentDefinition (NOT in the roster)
///     with three versions. PLACEHOLDER content: each version is a copy of an existing Wagner version
///     (avatar, portrait, stats, AI, combat profile, songs: Lv2/Lv3/Lv4 → stages 1/2/3, rising difficulty)
///     until the real monster / cards / songs / arenas are assigned (the seal works with whatever is assigned).
///   • UIText keys of the campaign UI (only missing ones; existing texts never overwritten).
/// Batch: -executeMethod CampaignContentSetup.SetupFromCommandLine
/// </summary>
public static class CampaignContentSetup
{
    private const string Folder = "Assets/_Project/Configs/Campaign";
    private const string BossOpponentPath = Folder + "/Opponent_MusicBeing.asset";

    [MenuItem("Tools/MusicGame/Campaign/Setup Campaign Content")]
    public static void SetupMenu() => Debug.Log(Setup(out _));

    public static void SetupFromCommandLine()
    {
        string report = Setup(out bool ok);
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CampaignContentSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static string Setup(out bool ok)
    {
        var sb = new StringBuilder("[CampaignContentSetup]\n");
        ok = true;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Configs", "Campaign");

        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app == null) { ok = false; return sb.AppendLine("  FAIL  AppConfig not found").ToString(); }

        var campaign = LoadOrCreate<CampaignConfigSO>(Folder + "/CampaignConfig.asset", sb);
        var evaluation = LoadOrCreate<EvaluationConfigSO>(Folder + "/EvaluationConfig.asset", sb);
        var boss = LoadOrCreate<FinalBossConfigSO>(Folder + "/FinalBossConfig.asset", sb);

        // Final rival definition (development content from Wagner Lv2/3/4).
        var bossOpponent = AssetDatabase.LoadAssetAtPath<OpponentDefinition>(BossOpponentPath);
        if (bossOpponent == null)
        {
            var source = app.opponentRoster != null ? app.opponentRoster.opponents.FirstOrDefault(o => o != null && o.id == "wagner") : null;
            bossOpponent = ScriptableObject.CreateInstance<OpponentDefinition>();
            bossOpponent.id = "music_being";
            bossOpponent.displayName = "Ésser de la música";
            bossOpponent.heightMeters = 2.0f;
            bossOpponent.heightIsEstimate = true;
            bossOpponent.biographyKey = "Boss.Bio";
            var levels = new OpponentLevelConfig[3];
            for (int i = 0; i < 3; i++)
            {
                var from = source != null ? CampaignRivalPicker.Version(source, i + 2) : null;
                var copy = new OpponentLevelConfig();
                if (from != null) EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), copy);
                copy.level = i + 1;
                levels[i] = copy;
            }
            bossOpponent.levels = levels;
            AssetDatabase.CreateAsset(bossOpponent, BossOpponentPath);
            sb.AppendLine($"  new   {BossOpponentPath} (development content copied from {(source != null ? "Wagner Lv2/3/4" : "nothing — no Wagner found")})");
        }
        if (app.opponentRoster != null && app.opponentRoster.opponents.Contains(bossOpponent)) { ok = false; sb.AppendLine("  FAIL  the final rival must NOT be in the normal roster"); }

        if (boss.opponent == null) boss.opponent = bossOpponent;
        EditorUtility.SetDirty(boss);
        if (campaign.finalBoss == null) { campaign.finalBoss = boss; EditorUtility.SetDirty(campaign); }
        if (app.campaign != campaign || app.evaluation != evaluation)
        {
            app.campaign = campaign;
            app.evaluation = evaluation;
            EditorUtility.SetDirty(app);
            sb.AppendLine("  ok    AppConfig.campaign / AppConfig.evaluation assigned");
        }

        // Report: levels, required cards, boss readiness.
        sb.AppendLine($"  ok    campaign: {campaign.LevelCount} levels, {campaign.TotalMatches} matches/lap: " +
                      string.Join(", ", campaign.levels.Select(l => $"{l.id}={l.matchCount}×v{l.rivalLevel}/{l.runnerDifficulty}")));
        var required = CampaignModel.RequiredCards(app.opponentRoster.opponents, campaign);
        sb.AppendLine($"  ok    required normal cards: {required.Count}");
        foreach (var l in campaign.levels)
        {
            int composers = app.opponentRoster.opponents.Count(o => o != null && CampaignRivalPicker.HasVersion(o, l.rivalLevel));
            sb.AppendLine($"        {l.id}: {composers} composers have version {l.rivalLevel} → {Mathf.CeilToInt(composers / (float)l.matchCount)} lap(s) of guaranteed new cards with missing-first selection");
        }
        string bossIssues = boss.Validate();
        sb.AppendLine(string.IsNullOrEmpty(bossIssues) ? "  ok    final rival ready" : "  info  final rival: " + bossIssues.Replace("\n", "; "));

        sb.AppendLine(AddLocalization(out bool locOk));
        ok &= locOk;
        AssetDatabase.SaveAssets();
        sb.AppendLine(ok ? "  RESULT PASS" : "  RESULT FAIL");
        return sb.ToString();
    }

    private static T LoadOrCreate<T>(string path, StringBuilder sb) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        sb.AppendLine($"  new   {path}");
        return asset;
    }

    // ── Localization ─────────────────────────────────────────────────────────

    public static readonly (string key, string en, string ca)[] Keys =
    {
        ("MainMenu.Progression", "PROGRESSION", "PROGRESSIÓ"),
        ("Progression.Title", "PROGRESSION", "PROGRESSIÓ"),
        ("Progression.Unavailable", "Campaign not configured.", "Campanya no configurada."),
        ("Progression.LapFinished", "lap completed", "volta completada"),
        ("Progression.Summary", "Lap {0}  ·  {1}  ·  Music cycle {2}", "Volta {0}  ·  {1}  ·  Cicle musical {2}"),
        ("Progression.Requirements", "Mastered matches {0}/{1}  ·  Master cards {2}/{3}  ·  Final challenge: {4}", "Partides dominades {0}/{1}  ·  Cromos dels mestres {2}/{3}  ·  Repte final: {4}"),
        ("Progression.MasteredOnly", "Mastered matches {0}/{1}", "Partides dominades {0}/{1}"),
        ("Progression.BossLocked", "locked", "bloquejat"),
        ("Progression.BossAvailable", "available", "disponible"),
        ("Progression.BossDefeated", "completed", "superat"),
        ("Progression.Challenge", "CHALLENGE THE BEING OF MUSIC ({0}/{1})", "DESAFIA L'ÉSSER DE LA MÚSICA ({0}/{1})"),
        ("Progression.LevelLine", "This lap {0}/{1}  ·  {2} mastered", "Aquesta volta {0}/{1}  ·  {2} dominades"),
        ("Progression.Runner", "Race", "Cursa"),
        ("Progression.Combat", "Fight", "Combat"),
        ("Progression.Total", "Total", "Total"),
        ("Progression.ColMatch", "MATCH", "PARTIDA"),
        ("Progression.ColRunner", "RACE · song of the record", "CURSA · cançó del rècord"),
        ("Progression.ColCombat", "FIGHT · rival of the record", "COMBAT · rival del rècord"),
        ("Progression.Match", "MATCH {0}", "PARTIDA {0}"),
        ("Progression.Mastered", "Mastered", "Dominada"),
        ("Progression.Completed", "Completed", "Completada"),
        ("Progression.Unrated", "Completed · not rated", "Completada · sense qualificar"),
        ("Progression.BossStage", "Final fight {0}", "Combat final {0}"),
        ("Progression.UnratedHint", "Completed before ratings existed: play this position again in a new lap to score it.", "Completada abans que existissin les qualificacions: torna a jugar aquesta posició en una nova volta per puntuar-la."),
        ("Progression.PendingHint", "Not played yet.", "Encara no s'ha jugat."),
        ("Progression.Lap", "lap {0}", "volta {0}"),
        ("Progression.LastRunner", "Latest race", "Darrera cursa"),
        ("Progression.LastCombat", "Latest fight", "Darrer combat"),
        ("Progression.Lost", "(lost)", "(derrota)"),
        ("Progression.HintMastered", "Mastered: 5-star race and 5-star fight.", "Dominada: cursa i combat a 5 estrelles."),
        ("Progression.HintRunnerMissing", "Race: no rating yet.", "Cursa: encara sense qualificació."),
        ("Progression.HintRunner", "Race: reach {0}% of the maximum (every pickup, perfect timing, no falls) for 5 stars.", "Cursa: arriba al {0}% del màxim (totes les recollides, temps perfecte i sense caigudes) per a 5 estrelles."),
        ("Progression.HintCombatMissing", "Fight: win it to record a result.", "Combat: guanya'l per registrar-ne el resultat."),
        ("Progression.HintCombat", "Fight: win without taking any damage (perfect) for 5 stars.", "Combat: guanya sense rebre cap dany (perfect) per a 5 estrelles."),
        ("Progression.HintNewLap", "This position comes back in every new lap.", "Aquesta posició torna a cada nova volta."),
        ("Campaign.Position", "Level {0} · match {1}", "Nivell {0} · partida {1}"),
        ("Campaign.Level", "LEVEL {0}", "NIVELL {0}"),
        ("Campaign.CompletedTitle", "You completed every level!", "Has completat tots els nivells!"),
        ("Campaign.CompletedBody", "To challenge the Being of Music, master every match with five stars and complete the collection of the great masters.\nStart a new lap to improve your records and find the rivals you are missing.",
                                   "Per desafiar l'ésser de la música, domina totes les partides amb cinc estrelles i completa la col·lecció dels grans mestres.\nComença una nova volta per millorar les teves marques i trobar els rivals que et falten."),
        ("Campaign.NewLap", "NEW LAP", "NOVA VOLTA"),
        ("Campaign.Later", "LATER", "MÉS TARD"),
        ("Campaign.Ok", "OK", "D'ACORD"),
        ("Campaign.Challenge", "CHALLENGE", "DESAFIA"),
        ("Campaign.LapDoneTitle", "Lap {0} completed!", "Volta {0} completada!"),
        ("Campaign.LapDoneBody", "Start a new lap from level 1: your records and cards are kept.", "Comença una nova volta des del nivell 1: conserves els rècords i els cromos."),
        ("Campaign.BossUnlockedTitle", "The Being of Music awaits", "L'ésser de la música t'espera"),
        ("Campaign.BossUnlockedBody", "Every match mastered and every master defeated. Three fights stand between you and the very idea of music.", "Totes les partides dominades i tots els mestres derrotats. Tres combats et separen del concepte mateix de la música."),
        ("Campaign.FinaleTitle", "You defeated the Being of Music!", "Has derrotat l'ésser de la música!"),
        ("Campaign.FinaleBody", "The final challenge is complete. Challenge it again whenever you like to improve your records.", "Has superat el repte final. El pots tornar a desafiar quan vulguis per millorar els teus rècords."),
        ("Campaign.SealName", "Beyond music", "Més enllà de la música"),
        ("Campaign.SealBody", "Five stars in all three final fights: you have surpassed every great master and the very concept of music.", "Cinc estrelles als tres combats finals: has superat tots els grans mestres i fins i tot el concepte de la música."),
        ("Boss.Name", "Being of Music", "Ésser de la música"),
        ("Rivals.SummaryBoss", "{0} / {1} master cards  ·  Being of Music {2} / {3}", "{0} / {1} cromos dels mestres  ·  Ésser de la música {2} / {3}"),
        ("Eval.Pending", "pending", "pendent"),
        ("Eval.ThisCombat", "THIS FIGHT", "AQUEST COMBAT"),
        ("Eval.BossStage", "FINAL FIGHT {0}", "COMBAT FINAL {0}"),
        ("Eval.Points", "Points {0}", "Punts {0}"),
        ("Eval.Perfect", "PERFECT!", "PERFECT!"),
        ("Eval.LossInformative", "Defeat: informative result, it does not count as a record.", "Derrota: resultat informatiu, no compta com a rècord."),
        ("Eval.BestOfStage", "Best of this fight", "Millor d'aquest combat"),
        ("Eval.NewRecord", "NEW RECORD!", "NOU RÈCORD!"),
        ("Eval.BossNext", "Next: final fight {0}", "Següent: combat final {0}"),
        ("Eval.BossDefeated", "Being of Music defeated!", "Ésser de la música superat!"),
        ("Eval.ThisRunner", "Race of this match: {0}", "Cursa d'aquesta partida: {0}"),
        ("Eval.ThisAttempt", "This attempt (race + fight)", "Aquest intent (cursa + combat)"),
        ("Eval.PositionBests", "BEST RESULTS · {0}", "MILLORS RESULTATS · {0}"),
        ("Eval.BestRunner", "Best race", "Millor cursa"),
        ("Eval.BestCombat", "Best fight", "Millor combat"),
        ("Eval.Total", "Match rating", "Nota de la partida"),
        ("Eval.JustMastered", "MATCH MASTERED!", "PARTIDA DOMINADA!"),
        ("Eval.LevelCompleted", "Level completed!", "Nivell completat!"),
        ("Eval.LapCompleted", "Campaign completed!", "Campanya completada!"),
        ("Eval.ThisRunnerTitle", "RACE RATING", "QUALIFICACIÓ DE LA CURSA"),
        ("Eval.NextFight", "The fight is next.", "Ara ve el combat."),
        ("Eval.NotCampaign", "Free run: not part of the campaign.", "Cursa lliure: no forma part de la campanya."),
        ("Eval.Collected", "Pickups {0}", "Recollides {0}"),
        ("Eval.Timing", "Timing {0}%", "Precisió de temps {0}%"),
        ("Eval.Falls", "Falls {0}", "Caigudes {0}"),
        ("Eval.HealthKept", "Health kept {0}%", "Vida conservada {0}%"),
        ("Eval.Accuracy", "Accuracy {0}%", "Encert {0}%"),
        ("Eval.Hits", "Hits {0}", "Cops {0}"),
    };

    private static string AddLocalization(out bool ok)
    {
        ok = true;
        var ui = LocalizationEditorSettings.GetStringTableCollection("UIText");
        if (ui == null) { ok = false; return "  FAIL  UIText missing"; }
        int added = 0;
        foreach (var (key, en, ca) in Keys)
            foreach (var table in ui.StringTables)
            {
                var entry = table.GetEntry(key);
                // Existing texts are kept, except old campaign texts that used the ★ glyph (missing from the UI font).
                if (entry != null && !string.IsNullOrEmpty(entry.Value) && !entry.Value.Contains("★")) continue;
                table.AddEntry(key, table.LocaleIdentifier.Code == "ca" ? ca : en);
                EditorUtility.SetDirty(table);
                added++;
            }
        EditorUtility.SetDirty(ui.SharedData);
        return $"  ok    UIText: {added} new campaign texts ({ui.SharedData.Entries.Count} keys)";
    }
}
#endif
