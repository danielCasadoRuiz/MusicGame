#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

/// <summary>
/// One-shot Editor tool: creates (or updates) the "UIText" String Table Collection — every
/// user-facing string in the game (AnalyzingScreenController's tips, GameplayHUD's live/end-screen
/// text, PauseController's menu) reads from this ONE table via Loc.Get, so this is the single
/// place new text gets added. Populated with English + Catalan entries.
///
/// Also creates the English + Catalan Locale assets if the project doesn't have them yet — same
/// CreateInstance&lt;Locale&gt;/AssetDatabase.CreateAsset/LocalizationEditorSettings.AddLocale
/// sequence Unity's own Locale Generator window uses internally, just for these two specific
/// languages instead of an arbitrary picked set. Any OTHER locale already in the project (or
/// added later via the Localization window) is left alone and simply gets English text.
///
/// Safe to re-run: existing entries for the same key are overwritten with the text below (so hand
/// edits made directly in the table asset afterward are NOT preserved by re-running this — same
/// "run once, then hand-tune the asset" contract as UIPrefabBuilder). Existing Locales are never
/// duplicated or touched.
/// </summary>
public static class LocalizationSetup
{
    private const string TableName    = "UIText";
    private const string AssetFolder  = "Assets/_Project/Localization";
    private const string LocaleFolder = "Assets/_Project/Localization/Locales";

    // key → (English, Catalan). Any OTHER project locale (if added later) falls back to the
    // English text below rather than being left blank.
    private static readonly (string key, string en, string ca)[] Entries =
    {
        ("Analyzing.Title", "Analyzing song...",                 "Analitzant la cançó..."),
        ("Analyzing.Tip01", "Extracting musical style...",        "Extraient l'estil musical..."),
        ("Analyzing.Tip02", "Measuring the flow...",              "Mesurant el flow..."),
        ("Analyzing.Tip03", "Counting beats per minute...",       "Comptant els beats per minut..."),
        ("Analyzing.Tip04", "Untangling the rhythm...",           "Desxifrant el ritme..."),
        ("Analyzing.Tip05", "Listening for the kick drum...",     "Escoltant el bombo..."),
        ("Analyzing.Tip06", "Mapping the hi-hats...",             "Mapejant els hi-hats..."),
        ("Analyzing.Tip07", "Detecting the drops...",             "Detectant els drops..."),
        ("Analyzing.Tip08", "Reading the song's mood...",         "Llegint l'ànim de la cançó..."),
        ("Analyzing.Tip09", "Calibrating the jump physics...",    "Calibrant la física dels salts..."),
        ("Analyzing.Tip10", "Tuning into the frequencies...",     "Sintonitzant les freqüències..."),
        ("Analyzing.Tip11", "Teaching the track to run...",       "Ensenyant el circuit a córrer..."),
        ("Analyzing.Tip12", "Syncing the beat to the world...",   "Sincronitzant el ritme amb el món..."),

        // ── Pause menu ──────────────────────────────────────────────────────────
        ("Pause.PauseButton", "Pause",           "Pausa"),
        ("Pause.Title",       "PAUSED",          "PAUSA"),
        ("Pause.Resume",      "RESUME",          "REPRÈN"),
        ("Pause.RestartSong", "RESTART SONG",    "REINICIA LA CANÇÓ"),
        ("Pause.ThirdPerson", "Third Person",    "Tercera Persona"),
        ("Pause.FirstPerson", "First Person",    "Primera Persona"),

        // ── Live HUD — top-bar column headers, reused by EndScreen's performance rows ──────────
        ("HUD.Kick",     "KICK",   "KICK"),
        ("HUD.Snare",    "SNARE",  "SNARE"),
        ("HUD.HiHat",    "HI-HAT", "HI-HAT"),
        ("HUD.Beat",     "BEAT",   "BEAT"),
        ("HUD.Onset",    "ONSET",  "ONSET"),
        ("HUD.Peak",     "PEAK",   "PEAK"),
        ("HUD.Impact",   "IMPACT", "IMPACT"),
        ("HUD.Score",    "SCORE",  "PUNTS"),
        ("HUD.Total",    "TOTAL",  "TOTAL"),
        ("HUD.TagStyle", "STYLE",  "ESTIL"),
        ("HUD.TagVibe",  "VIBE",   "VIBE"),
        ("HUD.TagOther", "OTHER",  "ALTRES"),

        // ── End screen ───────────────────────────────────────────────────────────
        ("EndScreen.Title",          "─── RESULTS ───",   "─── RESULTATS ───"),
        ("EndScreen.Restart",        "RESTART",           "REINICIA"),
        ("EndScreen.Continue",       "CONTINUE",          "CONTINUA"),
        ("EndScreen.RatingPoor",     "POOR",              "POBRE"),
        ("EndScreen.RatingWeak",     "WEAK",              "FLUIX"),
        ("EndScreen.RatingDecent",   "DECENT",            "DECENT"),
        ("EndScreen.RatingGreat",    "GREAT",             "MOLT BO"),
        ("EndScreen.RatingInsane",   "INSANE",            "BRUTAL"),
        ("EndScreen.ScoreSummary",   "score {0} / {1}  (raw {2}%)",       "puntuació {0} / {1}  (en brut {2}%)"),
        ("EndScreen.Falls",          "Falls: {0}",                        "Caigudes: {0}"),
        ("EndScreen.NoFallBonus",    "NO FALL BONUS +{0}%",               "BONUS SENSE CAURE +{0}%"),
        ("EndScreen.SessionSingular","Session: {0} run · {1}% avg",       "Sessió: {0} partida · {1}% mitjana"),
        ("EndScreen.SessionPlural",  "Session: {0} runs · {1}% avg",      "Sessió: {0} partides · {1}% mitjana"),
    };

    [MenuItem("Tools/MusicGame/Setup Localization")]
    public static void Setup()
    {
        // Standard, stable Addressables bootstrap — safe to call even if Addressables settings
        // already exist (no-op then). Avoids a "no Addressable settings" error the first time
        // CreateStringTableCollection touches the Addressables system below.
        AddressableAssetSettingsDefaultObject.GetSettings(true);

        EnsureFolder(AssetFolder);
        EnsureFolder(LocaleFolder);

        Locale english = EnsureLocale(SystemLanguage.English, "en");
        Locale catalan  = EnsureLocale(SystemLanguage.Catalan, "ca");

        var locales = new List<Locale>(LocalizationEditorSettings.GetLocales());

        var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
        if (collection == null)
            collection = LocalizationEditorSettings.CreateStringTableCollection(TableName, AssetFolder, locales);

        foreach (var locale in locales)
        {
            // Self-healing: a previous broken/partial run (or a collection made before every
            // Locale existed) can leave the collection missing a table for some locale — add it
            // instead of silently skipping that locale forever.
            var table = collection.GetTable(locale.Identifier) as StringTable;
            if (table == null)
                table = collection.AddNewTable(locale.Identifier) as StringTable;
            if (table == null)
            {
                Debug.LogError($"[LocalizationSetup] Could not create/find a StringTable for locale '{locale.Identifier}' — skipping it.");
                continue;
            }

            bool useCatalan = catalan != null && locale.Identifier.Equals(catalan.Identifier);
            foreach (var e in Entries)
                table.AddEntry(e.key, useCatalan ? e.ca : e.en);

            EditorUtility.SetDirty(table);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LocalizationSetup] '{TableName}' populated with {Entries.Length} entries across {locales.Count} locale(s).");
    }

    // Returns the existing project Locale matching `code` if there is one; otherwise creates it
    // (CreateInstance<Locale> + AssetDatabase.CreateAsset + LocalizationEditorSettings.AddLocale —
    // the exact same sequence Unity's own Locale Generator window uses) and returns the new one.
    private static Locale EnsureLocale(SystemLanguage language, string code)
    {
        foreach (var l in LocalizationEditorSettings.GetLocales())
            if (l.Identifier.Code == code) return l;

        var locale = Locale.CreateLocale(language);

        // A plain concatenated path, not AssetDatabase.GenerateUniqueAssetPath — that call can
        // return "" (and CreateAsset then silently writes a malformed, extension-less file) if
        // the folder isn't in the AssetDatabase's index yet, which is exactly what happened right
        // after EnsureFolder created it in this same script run without a Refresh in between.
        AssetDatabase.Refresh();
        string assetPath = $"{LocaleFolder}/{code}.asset";
        if (AssetDatabase.LoadAssetAtPath<Locale>(assetPath) != null)
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError($"[LocalizationSetup] Could not compute a valid asset path under '{LocaleFolder}' for locale '{code}' " +
                           "— the folder may not exist. This locale will still work for THIS run, but won't be saved as an asset.");
            return locale;
        }

        AssetDatabase.CreateAsset(locale, assetPath);
        LocalizationEditorSettings.AddLocale(locale);
        Debug.Log($"[LocalizationSetup] Created Locale '{locale.name}' ({code}) at {assetPath}.");
        return locale;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
        AssetDatabase.Refresh(); // make the new folder immediately visible to AssetDatabase APIs
    }
}
#endif
