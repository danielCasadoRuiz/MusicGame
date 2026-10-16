using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>A localization KEY used as an argument: resolved in the current language every time the
/// owning text is (re)built — e.g. Loc.Set(text, "Analyzing.StyleDetected", Loc.Key("GameStyle.Pop")).</summary>
public readonly struct LocKey
{
    public readonly string Key;
    public LocKey(string key) { Key = key; }
    public override string ToString() => Loc.Get(Key);
}

/// <summary>
/// Single entry point for every user-facing string in the game — every UI script calls THIS
/// instead of hardcoding text, so translating the game is a "Tools > MusicGame > Setup
/// Localization" + table-editing job, never a code change.
///
/// Wraps Unity's Localization package (com.unity.localization), reading from the "UIText" String
/// Table Collection. Get is synchronous on purpose: UIText is a tiny LOCAL table.
///
/// LIVE LANGUAGE CHANGE: Set / Track / Bind assign a text AND remember how it was built (key + its
/// current arguments, or a builder for composite lines). When the language changes, the new UIText
/// table is loaded asynchronously first, then every remembered text that still shows what Loc last put
/// there is rebuilt in the new language — no screen rebuild, no per-frame work, and a text another
/// code path has since overwritten is left alone (and forgotten). Only the latest of several quick
/// changes applies. Refreshed fires afterwards for screens with their own dynamic text.
///
/// If the table/locales haven't been set up yet, Get() returns the raw KEY instead of throwing.
/// </summary>
public static class Loc
{
    public const string Table = "UIText";

    /// <summary>Raised after a language change has been applied to every tracked text.</summary>
    public static event Action Refreshed;

    private sealed class Entry { public Func<string> Build; public string Last; }
    private static readonly Dictionary<TMP_Text, Entry> s_Tracked = new();
    private static bool s_Hooked;
    private static int s_ChangeToken;

    public static LocKey Key(string key) => new LocKey(key);

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
    /// Smart-string form — the table entry uses {0}, {1}.. placeholders, positioned however each
    /// language needs. Pass args already formatted to plain strings, or LocKey for an argument that is
    /// itself a localized text.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        try
        {
            var resolved = Resolve(args);
            var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(Table, key, resolved);
            string result = op.WaitForCompletion();
            if (!string.IsNullOrEmpty(result)) return result;
        }
        catch
        {
            // Localization not configured yet — Tools > MusicGame > Setup Localization.
        }
        return key;
    }

    // ── Live-refreshing assignment ───────────────────────────────────────────────

    /// <summary>text = Get(key, args), kept up to date when the language changes.</summary>
    public static void Set(TMP_Text text, string key, params object[] args)
    {
        var captured = args != null && args.Length > 0 ? (object[])args.Clone() : null;
        Bind(text, captured == null ? () => Get(key) : () => Get(key, captured));
    }

    /// <summary>Registers a text that was JUST assigned Get(key, args) (e.g. by UIFactory) — returns it.</summary>
    public static T Track<T>(T text, string key, params object[] args) where T : TMP_Text
    {
        if (text == null) return null;
        var captured = args != null && args.Length > 0 ? (object[])args.Clone() : null;
        Remember(text, captured == null ? () => Get(key) : () => Get(key, captured));
        return text;
    }

    /// <summary>text = build(), rebuilt with the same builder when the language changes (composite lines).</summary>
    public static void Bind(TMP_Text text, Func<string> build)
    {
        if (text == null || build == null) return;
        text.text = build();
        Remember(text, build);
    }

    /// <summary>Stop refreshing this text (its content is no longer localized UI text).</summary>
    public static void Forget(TMP_Text text) { if (text != null) s_Tracked.Remove(text); }

    private static void Remember(TMP_Text text, Func<string> build)
    {
        HookLocaleChanges();
        s_Tracked[text] = new Entry { Build = build, Last = text.text };
    }

    private static object[] Resolve(object[] args)
    {
        if (args == null) return null;
        object[] copy = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is LocKey k)
            {
                copy ??= (object[])args.Clone();
                copy[i] = Get(k.Key);
            }
        }
        return copy ?? args;
    }

    private static void HookLocaleChanges()
    {
        if (s_Hooked) return;
        try
        {
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            s_Hooked = true;
        }
        catch { /* Localization not available (edit mode / tests) */ }
    }

    private static void OnLocaleChanged(Locale locale) => _ = RefreshAsync(locale);

    private static async Task RefreshAsync(Locale locale)
    {
        int token = ++s_ChangeToken;
        try
        {
            // Load the new language's UIText without blocking the main thread; Get() is then instant.
            var handle = LocalizationSettings.StringDatabase.GetTableAsync(Table, locale);
            if (!handle.IsDone) await handle.Task;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Loc] Loading {Table} for '{locale?.Identifier.Code}' failed: {e.Message}");
        }
        if (token != s_ChangeToken) return; // a newer change won
        RefreshNow();
    }

    /// <summary>Rebuilds every tracked text in the current language (normally automatic).</summary>
    public static void RefreshNow()
    {
        var dead = new List<TMP_Text>();
        foreach (var kv in s_Tracked)
        {
            var text = kv.Key;
            if (text == null) { dead.Add(text); continue; }
            if (text.text != kv.Value.Last) { dead.Add(text); continue; } // overwritten elsewhere since
            string value;
            try { value = kv.Value.Build(); }
            catch (Exception e) { Debug.LogWarning($"[Loc] Refresh of '{text.name}' failed: {e.Message}"); continue; }
            text.text = value;
            kv.Value.Last = value;
        }
        foreach (var d in dead) s_Tracked.Remove(d);
        try { Refreshed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
