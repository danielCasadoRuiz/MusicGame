using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// The player's LANGUAGE POLICY — "Automatic" or one specific language — on top of Unity Localization
/// (which still owns locales, tables and SelectedLocale; nothing here duplicates translation logic).
///
///   Stored:   PlayerPrefs "MusicGame.Language" = "auto" or a locale code ("ca", "en"), never a UI index.
///             Missing (fresh install) or invalid (unknown code, garbage) → Automatic.
///   Automatic: the device language (CultureInfo.CurrentUICulture, then Application.systemLanguage),
///             matched exactly ("ca-ES" == "ca-ES") or by base language ("ca-ES" → "ca", "en-GB" → "en");
///             no match → English; no English → the first available locale.
///   Manual:   wins over the device and persists across runs. Resolving Automatic to a concrete locale
///             NEVER turns it into a manual choice (only SetMode writes the preference).
///
/// Applied at startup by MusicGameStartupLocaleSelector (first in LocalizationSettings' startup selector
/// list), i.e. before the first localized screen; at runtime by SetMode (the Settings selector), which
/// switches LocalizationSettings.SelectedLocale immediately (Loc refreshes visible texts).
///
/// Testing: "-deviceLanguage xx-YY" on the command line overrides the device language seen by Automatic.
/// </summary>
public static class LanguagePreference
{
    public const string Auto = "auto";
    public const string PrefKey = "MusicGame.Language";
    public const string FallbackCode = "en";

    /// <summary>Raised after SetMode applied a new mode.</summary>
    public static event Action ModeChanged;

    /// <summary>"auto" or a locale code — always a usable value (invalid stored values read as "auto").</summary>
    public static string Mode
    {
        get
        {
            string stored = PlayerPrefs.GetString(PrefKey, Auto);
            if (string.IsNullOrWhiteSpace(stored) || stored == Auto) return Auto;
            var locales = AvailableCodes();
            // Before Localization is up we cannot validate: trust the code, ResolveCode re-checks it.
            if (locales.Count > 0 && !locales.Contains(stored)) return Auto;
            return stored;
        }
    }

    /// <summary>Sets and persists the mode, then applies it right away.</summary>
    public static void SetMode(string mode)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? Auto : mode;
        var codes = AvailableCodes();
        if (mode != Auto && codes.Count > 0 && !codes.Contains(mode)) mode = Auto;
        PlayerPrefs.SetString(PrefKey, mode);
        PlayerPrefs.Save();
        Apply();
        ModeChanged?.Invoke();
    }

    /// <summary>Re-applies the stored mode (e.g. Automatic after the device language changed).</summary>
    public static void Apply()
    {
        if (!LocalizationSettings.HasSettings) return;
        var locales = LocalizationSettings.AvailableLocales;
        var locale = ResolveLocale(locales, Mode);
        if (locale != null && LocalizationSettings.SelectedLocale != locale) LocalizationSettings.SelectedLocale = locale;
    }

    public static Locale ResolveLocale(ILocalesProvider locales, string mode)
    {
        if (locales == null || locales.Locales == null || locales.Locales.Count == 0) return null;
        var codes = new List<string>();
        foreach (var l in locales.Locales) if (l != null) codes.Add(l.Identifier.Code);
        string code = ResolveCode(codes, mode, DeviceLanguageCodes());
        foreach (var l in locales.Locales) if (l != null && l.Identifier.Code == code) return l;
        return locales.Locales[0];
    }

    /// <summary>Pure policy (also used by the tests): the locale code for `mode` among `available`,
    /// given the device's preferred language codes (most specific first).</summary>
    public static string ResolveCode(IList<string> available, string mode, IEnumerable<string> deviceCodes)
    {
        if (available == null || available.Count == 0) return null;
        if (!string.IsNullOrEmpty(mode) && mode != Auto && available.Contains(mode)) return mode;

        foreach (var device in deviceCodes)
        {
            if (string.IsNullOrEmpty(device)) continue;
            string d = device.Replace('_', '-');
            foreach (var a in available) if (string.Equals(a, d, StringComparison.OrdinalIgnoreCase)) return a;
            string baseLang = BaseLanguage(d);
            foreach (var a in available) if (string.Equals(BaseLanguage(a), baseLang, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return available.Contains(FallbackCode) ? FallbackCode : available[0];
    }

    /// <summary>The device's language codes, most specific first ("ca-ES", then "ca").</summary>
    public static IEnumerable<string> DeviceLanguageCodes()
    {
        string forced = CommandLineArg("-deviceLanguage");
        if (!string.IsNullOrEmpty(forced)) { yield return forced; yield break; }

        string ui = null;
        try { ui = CultureInfo.CurrentUICulture?.Name; } catch { /* invariant on some platforms */ }
        if (!string.IsNullOrEmpty(ui)) yield return ui;

        string system = null;
        try
        {
            var lang = Application.systemLanguage;
            if (lang != SystemLanguage.Unknown) system = new LocaleIdentifier(lang).Code;
        }
        catch { /* unmapped SystemLanguage */ }
        if (!string.IsNullOrEmpty(system)) yield return system;
    }

    /// <summary>Display name of a language option: localized "Automatic", or the language's own name
    /// with a capital ("Català", "English") — proper names, never translated.</summary>
    public static string DisplayName(string mode)
    {
        if (mode == Auto) return Loc.Get("Settings.LanguageAuto");
        try
        {
            string native = new CultureInfo(mode).NativeName;
            int paren = native.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) native = native.Substring(0, paren);
            return native.Length > 0 ? char.ToUpper(native[0], new CultureInfo(mode)) + native.Substring(1) : mode;
        }
        catch { return mode; }
    }

    public static List<string> AvailableCodes()
    {
        var codes = new List<string>();
        try
        {
            if (!LocalizationSettings.HasSettings || !LocalizationSettings.InitializationOperation.IsDone) return codes;
            foreach (var l in LocalizationSettings.AvailableLocales.Locales) if (l != null) codes.Add(l.Identifier.Code);
        }
        catch { /* not initialized */ }
        return codes;
    }

    private static string BaseLanguage(string code)
    {
        int dash = code.IndexOf('-');
        return dash > 0 ? code.Substring(0, dash) : code;
    }

    private static string CommandLineArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
}

/// <summary>
/// Unity Localization startup selector applying LanguagePreference before the first localized screen.
/// First in the project LocalizationSettings' startup selector list (Tools > MusicGame > Localization >
/// Setup Language Settings); a SpecificLocaleSelector(English) follows as a last resort.
/// </summary>
[Serializable]
public class MusicGameStartupLocaleSelector : IStartupLocaleSelector
{
    public Locale GetStartupLocale(ILocalesProvider availableLocales)
    {
        // Validate the stored mode against the locales Localization just discovered.
        string stored = PlayerPrefs.GetString(LanguagePreference.PrefKey, LanguagePreference.Auto);
        return LanguagePreference.ResolveLocale(availableLocales, stored);
    }
}
