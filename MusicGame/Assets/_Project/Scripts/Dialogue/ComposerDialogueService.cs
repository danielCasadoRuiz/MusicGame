using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>One resolved composer line: WHICH variant (Key) was picked, and its text in LocaleCode.</summary>
public readonly struct ComposerDialogueLine
{
    public readonly string ComposerId;
    public readonly ComposerDialogueCategory Category;
    public readonly string Key;
    public readonly string Text;
    public readonly string LocaleCode;

    public ComposerDialogueLine(string composerId, ComposerDialogueCategory category, string key, string text, string localeCode)
    {
        ComposerId = composerId; Category = category; Key = key; Text = text; LocaleCode = localeCode;
    }

    public bool IsValid => !string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(Text);
}

/// <summary>
/// CONTENT ACCESS for the composer dialogues — async, never WaitForCompletion (a table may be a
/// remote content-update bundle that has to download first). Everything goes through Unity
/// Localization's own StringDatabase, which already owns the table handles, shares one load between
/// concurrent requests for the same table, and drops every table itself when the language changes.
/// This class only adds what the game needs on top:
///
///   Preload(id)                    the definitive rival is known → start loading its table, release others
///   PickLineAsync(id, category)    discover variants in the loaded table, pick one (ComposerDialogueSelector)
///   ResolveTextAsync(id, key)      the SAME key again (language changed mid-presentation)
///   Release(id) / ReleaseAll()     give the tables back to the StringDatabase (memory only — the
///                                  Addressables download cache on disk is never touched here)
///
/// Variant policy (see GetVariantKeys): a variant is a key that parses strictly as
/// Composer.{id}.Dialogue.{Category}.{index} for THIS composer and has non-empty text in the table's
/// language. Empty text = retired in that language. If the active language has no valid variant for
/// the category, the locale's fallback (or English) is used and a warning is logged; if nothing is
/// found at all the line is invalid and callers must carry on without dialogue.
/// </summary>
public static class ComposerDialogueService
{
    private static readonly HashSet<string> s_Held = new();            // composer ids we requested tables for
    private static readonly Dictionary<string, int> s_InFlight = new(); // composer id → loads in progress
    private static readonly HashSet<string> s_ReleaseWhenIdle = new();
    private static readonly HashSet<string> s_WarnedKeys = new();

    /// <summary>True while any dialogue table load is running — content updates wait for this.</summary>
    public static bool IsBusy
    {
        get { foreach (var kv in s_InFlight) if (kv.Value > 0) return true; return false; }
    }

    public static int HeldTableCount => s_Held.Count;

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    /// <summary>The definitive rival is known: start loading its table for the active language and
    /// release every other composer's tables. Safe to call repeatedly / without awaiting.</summary>
    public static void Preload(string composerId)
    {
        if (string.IsNullOrEmpty(composerId)) return;
        ReleaseAllExcept(composerId);
        _ = PreloadAsync(composerId);
    }

    private static async Task PreloadAsync(string composerId)
    {
        var locale = await ActiveLocaleAsync();
        if (locale != null) await LoadTableAsync(composerId, locale);
    }

    public static void ReleaseAllExcept(string composerId)
    {
        foreach (var id in new List<string>(s_Held))
            if (id != composerId) Release(id);
    }

    public static void ReleaseAll()
    {
        foreach (var id in new List<string>(s_Held)) Release(id);
    }

    /// <summary>Releases this composer's tables (every language) once no load for it is running.</summary>
    public static void Release(string composerId)
    {
        if (string.IsNullOrEmpty(composerId)) return;
        if (s_InFlight.TryGetValue(composerId, out int n) && n > 0) { s_ReleaseWhenIdle.Add(composerId); return; }
        ReleaseNow(composerId);
    }

    private static void ReleaseNow(string composerId)
    {
        s_Held.Remove(composerId);
        s_ReleaseWhenIdle.Remove(composerId);
        if (!LocalizationSettings.HasSettings || !LocalizationSettings.InitializationOperation.IsDone) return;
        var locales = LocalizationSettings.AvailableLocales?.Locales;
        if (locales == null) return;
        foreach (var locale in locales)
            LocalizationSettings.StringDatabase.ReleaseTable(ComposerDialogueKeys.TableName(composerId), locale);
    }

    // ── Loading ──────────────────────────────────────────────────────────────────

    public static async Task<Locale> ActiveLocaleAsync()
    {
        if (!LocalizationSettings.HasSettings) return null;
        var init = LocalizationSettings.InitializationOperation;
        if (!init.IsDone) await init.Task;
        var selected = LocalizationSettings.SelectedLocaleAsync;
        if (!selected.IsDone) await selected.Task;
        return selected.Result;
    }

    /// <summary>The composer's table in `locale` (null = failed; the error is logged).</summary>
    public static async Task<StringTable> LoadTableAsync(string composerId, Locale locale)
    {
        if (string.IsNullOrEmpty(composerId) || locale == null) return null;
        string tableName = ComposerDialogueKeys.TableName(composerId);

        s_ReleaseWhenIdle.Remove(composerId);
        s_Held.Add(composerId);
        s_InFlight[composerId] = (s_InFlight.TryGetValue(composerId, out int n) ? n : 0) + 1;
        try
        {
            var handle = LocalizationSettings.StringDatabase.GetTableAsync(tableName, locale);
            if (!handle.IsDone) await handle.Task;
            if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Debug.LogError($"[ComposerDialogue] Could not load table '{tableName}' ({locale.Identifier.Code}): " +
                               $"{(handle.IsValid() ? handle.OperationException?.Message : "invalid handle")}. " +
                               "The game continues without this dialogue.");
                return null;
            }
            return handle.Result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ComposerDialogue] Loading '{tableName}' ({locale.Identifier.Code}) threw: {e.Message}");
            return null;
        }
        finally
        {
            s_InFlight[composerId]--;
            if (s_InFlight[composerId] <= 0 && s_ReleaseWhenIdle.Contains(composerId)) ReleaseNow(composerId);
        }
    }

    // ── Variants ─────────────────────────────────────────────────────────────────

    /// <summary>Valid variant keys of (composer, category) in `table`, sorted by index. Gaps, any
    /// count and 4+ digit indices are fine; malformed keys under this composer's prefix are logged once.</summary>
    public static List<string> GetVariantKeys(StringTable table, string composerId, ComposerDialogueCategory category, int version = 0)
    {
        var found = new List<(int index, string key)>();
        var forVersion = new List<(int index, string key)>();
        if (table == null || table.SharedData == null) return new List<string>();

        string prefix = ComposerDialogueKeys.KeyPrefix(composerId);
        foreach (var shared in table.SharedData.Entries)
        {
            if (!ComposerDialogueKeys.TryParse(shared.Key, out var id, out var cat, out int index, out int lineVersion) || id != composerId)
            {
                if (shared.Key != null && shared.Key.StartsWith(prefix) && s_WarnedKeys.Add(shared.Key))
                    Debug.LogWarning($"[ComposerDialogue] Ignoring malformed dialogue key '{shared.Key}' in {table.TableCollectionName} " +
                                     "(expected Composer.{id}.Dialogue.{Intro|Defeat|Victory}.{index:000}).");
                continue;
            }
            if (cat != category) continue;
            var entry = table.GetEntry(shared.Id);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Value)) continue; // retired / untranslated in this language
            if (lineVersion == 0) found.Add((index, shared.Key));
            else if (lineVersion == version) forVersion.Add((index, shared.Key));
        }
        // A version with its own lines (final rival levels) uses only those; otherwise the generic ones.
        if (forVersion.Count > 0) found = forVersion;
        found.Sort((a, b) => a.index.CompareTo(b.index));
        var keys = new List<string>(found.Count);
        foreach (var f in found) keys.Add(f.key);
        return keys;
    }

    /// <summary>Picks a fresh line for (composer, category) in the active language (falls back to the
    /// fallback language when the active one has none). Invalid line = nothing to show.</summary>
    public static async Task<ComposerDialogueLine> PickLineAsync(string composerId, ComposerDialogueCategory category,
                                                                 CancellationToken cancellation = default, int version = 0)
    {
        var locale = await ActiveLocaleAsync();
        if (locale == null || cancellation.IsCancellationRequested) return default;

        var table = await LoadTableAsync(composerId, locale);
        if (cancellation.IsCancellationRequested) return default;
        var keys = GetVariantKeys(table, composerId, category, version);

        if (keys.Count == 0)
        {
            var fallback = FallbackLocale(locale);
            if (fallback != null)
            {
                var fallbackTable = await LoadTableAsync(composerId, fallback);
                if (cancellation.IsCancellationRequested) return default;
                keys = GetVariantKeys(fallbackTable, composerId, category, version);
                if (keys.Count > 0)
                {
                    Debug.LogWarning($"[ComposerDialogue] No {category} lines for '{composerId}' in '{locale.Identifier.Code}' — using '{fallback.Identifier.Code}'.");
                    table = fallbackTable;
                    locale = fallback;
                }
            }
        }

        if (keys.Count == 0)
        {
            Debug.LogError($"[ComposerDialogue] No valid {category} lines for composer '{composerId}' " +
                           $"(table {ComposerDialogueKeys.TableName(composerId)}). The game continues without dialogue.");
            return default;
        }

        string key = ComposerDialogueSelector.Pick(keys, composerId, category);
        return new ComposerDialogueLine(composerId, category, key, TextOf(table, key), locale.Identifier.Code);
    }

    /// <summary>The text of an ALREADY chosen key in the active language (fallback language if empty
    /// there). Invalid line = the key no longer resolves anywhere.</summary>
    public static async Task<ComposerDialogueLine> ResolveTextAsync(string composerId, ComposerDialogueCategory category, string key,
                                                                    CancellationToken cancellation = default)
    {
        var locale = await ActiveLocaleAsync();
        if (locale == null || cancellation.IsCancellationRequested) return default;
        var text = TextOf(await LoadTableAsync(composerId, locale), key);
        if (cancellation.IsCancellationRequested) return default;
        if (string.IsNullOrWhiteSpace(text))
        {
            var fallback = FallbackLocale(locale);
            if (fallback != null)
            {
                text = TextOf(await LoadTableAsync(composerId, fallback), key);
                if (cancellation.IsCancellationRequested) return default;
                if (!string.IsNullOrWhiteSpace(text)) locale = fallback;
            }
        }
        return string.IsNullOrWhiteSpace(text) ? default
            : new ComposerDialogueLine(composerId, category, key, text, locale.Identifier.Code);
    }

    private static string TextOf(StringTable table, string key)
    {
        var entry = table != null ? table.GetEntry(key) : null;
        if (entry == null || string.IsNullOrWhiteSpace(entry.Value)) return null;
        return entry.GetLocalizedString();
    }

    /// <summary>The locale's own Fallback metadata, else English, else null (never the locale itself).</summary>
    public static Locale FallbackLocale(Locale locale)
    {
        var meta = locale != null ? locale.Metadata.GetMetadata<FallbackLocale>() : null;
        if (meta?.Locale != null && meta.Locale != locale) return meta.Locale;
        var english = LocalizationSettings.AvailableLocales?.GetLocale("en");
        return english != null && english != locale ? english : null;
    }
}
