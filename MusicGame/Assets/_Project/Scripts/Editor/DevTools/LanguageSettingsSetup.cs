#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// Tools > MusicGame > Localization > Setup Language Settings — gives the project its OWN
/// LocalizationSettings asset (until now the package's runtime default was silently used) configured
/// for the MusicGame language policy (LanguagePreference):
///
///   startup     MusicGameStartupLocaleSelector (Automatic/manual preference) → SpecificLocaleSelector(en)
///               — no PlayerPref/system selectors: Unity never persists a resolved locale on its own,
///               so "Automatic" keeps its meaning.
///   fallback    TRANSLATION fallback (not the startup language): ca → en via the ca Locale's
///               FallbackLocale metadata, with StringDatabase.UseFallback on.
///   preload     PreloadSelectedLocale (only tables explicitly marked Preload — none of the composer
///               dialogue tables, which keep loading per composer on demand).
///   UIText      adds the keys this feature needs (Fight.Skip, Settings.Language, Settings.LanguageAuto)
///               only where missing — existing texts are never overwritten.
///
/// Idempotent. Batch: -executeMethod LanguageSettingsSetup.SetupFromCommandLine
/// </summary>
public static class LanguageSettingsSetup
{
    public const string SettingsPath = "Assets/_Project/Localization/LocalizationSettings.asset";

    public static readonly (string key, string en, string ca)[] NewKeys =
    {
        ("Fight.Skip",            "SKIP",      "SALTA"),
        ("Settings.Language",     "Language",  "Idioma"),
        ("Settings.LanguageAuto", "Automatic", "Automàtic"),
    };

    [MenuItem("Tools/MusicGame/Localization/Setup Language Settings")]
    public static void SetupMenu() => Debug.Log(Setup(out _));

    public static void SetupFromCommandLine()
    {
        string report = Setup(out bool ok);
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/LanguageSettingsSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static string Setup(out bool ok)
    {
        var sb = new StringBuilder("[LanguageSettingsSetup]\n");
        ok = true;

        var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null || !AssetDatabase.Contains(settings))
        {
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, SettingsPath);
                sb.AppendLine($"  new   {SettingsPath}");
            }
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            sb.AppendLine("  ok    registered as the active Localization Settings");
        }
        else sb.AppendLine($"  ok    active settings: {AssetDatabase.GetAssetPath(settings)}");

        var selectors = settings.GetStartupLocaleSelectors();
        selectors.Clear();
        selectors.Add(new MusicGameStartupLocaleSelector());
        selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
        sb.AppendLine("  ok    startup selectors: MusicGameStartupLocaleSelector → SpecificLocaleSelector(en)");

        settings.GetStringDatabase().UseFallback = true;
        settings.GetAssetDatabase().UseFallback = true;
        var preload = typeof(LocalizationSettings).GetField("m_PreloadBehavior", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        preload?.SetValue(settings, PreloadBehavior.PreloadSelectedLocale);
        EditorUtility.SetDirty(settings);

        var locales = LocalizationEditorSettings.GetLocales();
        var en = locales.FirstOrDefault(l => l.Identifier.Code == "en");
        var ca = locales.FirstOrDefault(l => l.Identifier.Code == "ca");
        if (en == null || ca == null) { ok = false; sb.AppendLine("  FAIL  ca/en Locale assets missing"); }
        else
        {
            var fallback = ca.Metadata.GetMetadata<FallbackLocale>();
            if (fallback == null) { ca.Metadata.AddMetadata(new FallbackLocale(en)); EditorUtility.SetDirty(ca); }
            else if (fallback.Locale != en) { fallback.Locale = en; EditorUtility.SetDirty(ca); }
            sb.AppendLine("  ok    translation fallback ca → en (Locale metadata + UseFallback)");
        }

        var ui = LocalizationEditorSettings.GetStringTableCollection("UIText");
        if (ui == null) { ok = false; sb.AppendLine("  FAIL  UIText missing"); }
        else
        {
            int added = 0;
            foreach (var (key, enText, caText) in NewKeys)
            {
                foreach (var table in ui.StringTables)
                {
                    if (table.GetEntry(key) != null && !string.IsNullOrEmpty(table.GetEntry(key).Value)) continue;
                    table.AddEntry(key, table.LocaleIdentifier.Code == "ca" ? caText : enText);
                    EditorUtility.SetDirty(table);
                    added++;
                }
            }
            EditorUtility.SetDirty(ui.SharedData);
            sb.AppendLine($"  ok    UIText: {added} new texts ({ui.SharedData.Entries.Count} keys)");
        }

        AssetDatabase.SaveAssets();
        sb.AppendLine(ok ? "  RESULT PASS" : "  RESULT FAIL");
        return sb.ToString();
    }
}
#endif
