using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Single entry point for every user-facing string in the game — every UI script calls THIS
/// instead of hardcoding text, so translating the game is a "Tools > MusicGame > Setup
/// Localization" + table-editing job, never a code change.
///
/// Wraps Unity's Localization package (com.unity.localization), reading from the "UIText" String
/// Table Collection. Synchronous on purpose: every lookup here is a tiny LOCAL string-table entry
/// (no remote/Addressable download), so blocking briefly is far simpler than threading an async
/// callback through every Button/Text setup call in GameplayHUD/PauseController.
///
/// If the table/locales haven't been set up yet, Get() returns the raw KEY instead of throwing —
/// visibly "unfinished" (e.g. a button reading "Pause.Resume") rather than silently broken, and it
/// upgrades to real text the moment Tools > MusicGame > Setup Localization has been run.
/// </summary>
public static class Loc
{
    public const string Table = "UIText";

    public static string Get(string key)
    {
        try
        {
            var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(Table, key);
            string result = op.WaitForCompletion();
            if (!string.IsNullOrEmpty(result)) return result;
        }
        catch
        {
            // Localization not configured yet — Tools > MusicGame > Setup Localization.
        }
        return key;
    }

    /// <summary>
    /// Smart-string form — the table entry uses {0}, {1}.. placeholders (e.g. "Falls: {0}"),
    /// positioned however each language needs. Pass args already formatted to plain strings
    /// (ToString("F0") etc. done by the caller) rather than raw numbers, so no format-specifier
    /// behavior depends on Smart Format's own number formatting rules.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        try
        {
            var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(Table, key, args);
            string result = op.WaitForCompletion();
            if (!string.IsNullOrEmpty(result)) return result;
        }
        catch
        {
            // Localization not configured yet — Tools > MusicGame > Setup Localization.
        }
        return key;
    }
}
