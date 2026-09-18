#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// One-shot Editor tool (Tools > MusicGame > Setup Music Style Placeholders): creates a minimal,
/// real MusicStyleVisualSO + UIStyleSO pair for every MusicStyleId that doesn't already have a
/// MusicStyleRegistry entry, registers it, and marks the new MusicStyleVisualSO Addressable (via
/// the real Addressables API — the same effect as checking "Addressable" in the Inspector, so there
/// is no hand-authored YAML to get subtly wrong) — so every real style is immediately selectable
/// from the Theme Debugger and visibly distinct.
///
/// Deliberately color-only: World/Track/Player/Collectibles/VFX are left null on every generated
/// asset, which ThemeResolver already treats as "fall back to BaseTheme" correctly (see
/// VisualOverrideLayer's own doc) — a placeholder doesn't need real geometry to prove the Theme
/// system actually swaps per style; it needs to be visibly, unambiguously different, which a
/// distinct UI accent/background color already achieves.
///
/// Safe to re-run: any style that already has a MusicStyleRegistry entry (Funk, hand-authored in an
/// earlier phase, with its own hand-picked color) is left completely untouched — same "run once,
/// then hand-tune the asset" contract as LocalizationSetup/UIPrefabBuilder.
/// </summary>
public static class MusicStyleContentSetup
{
    private const string RegistryPath = "Assets/_Project/Configs/Theme/MusicStyleRegistry.asset";
    private const string StylesFolder = "Assets/_Project/Configs/Theme/Styles";

    // Every real style (Unknown excluded — a song that isn't classified has no visual of its own,
    // it just falls back to whatever the random Frontend Visual/BaseTheme already provides).
    private static readonly MusicStyleId[] Styles =
    {
        MusicStyleId.Rock, MusicStyleId.Pop, MusicStyleId.Electronic, MusicStyleId.House,
        MusicStyleId.Techno, MusicStyleId.Jazz, MusicStyleId.Metal, MusicStyleId.Soul,
        MusicStyleId.Funk, MusicStyleId.Disco, MusicStyleId.HipHop, MusicStyleId.RnB,
        MusicStyleId.Folk, MusicStyleId.Country, MusicStyleId.Punk, MusicStyleId.Blues,
        MusicStyleId.Ambient, MusicStyleId.Acoustic, MusicStyleId.Experimental,
    };

    [MenuItem("Tools/MusicGame/Setup Music Style Placeholders")]
    public static void Setup()
    {
        var registry = AssetDatabase.LoadAssetAtPath<MusicStyleRegistrySO>(RegistryPath);
        if (registry == null)
        {
            Debug.LogError($"[MusicStyleContentSetup] '{RegistryPath}' not found — aborting.");
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group    = settings.DefaultGroup;

        var entries = new List<MusicStyleRegistrySO.Entry>(registry.entries);
        int created = 0;

        for (int i = 0; i < Styles.Length; i++)
        {
            var style = Styles[i];
            if (HasEntry(registry, style)) continue; // e.g. Funk — hand-authored earlier, leave it alone

            float hue        = i / (float)Styles.Length;
            Color accent     = Color.HSVToRGB(hue, 0.75f, 0.95f);
            Color secondary  = Color.HSVToRGB(hue, 0.15f, 0.75f);
            Color background = Color.HSVToRGB(hue, 0.55f, 0.08f);

            string folder = $"{StylesFolder}/{style}";
            EnsureFolder(folder);

            var ui = ScriptableObject.CreateInstance<UIStyleSO>();
            ui.primaryColor    = Color.white;
            ui.secondaryColor  = secondary;
            ui.accentColor     = accent;
            ui.backgroundColor = background;
            AssetDatabase.CreateAsset(ui, $"{folder}/UIStyle_{style}.asset");

            var visual = ScriptableObject.CreateInstance<MusicStyleVisualSO>();
            visual.style = style;
            visual.ui    = ui;
            AssetDatabase.CreateAsset(visual, $"{folder}/{style}Visual.asset");

            string guid  = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(visual));
            var    entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = $"{style}Visual";

            entries.Add(new MusicStyleRegistrySO.Entry
            {
                style       = style,
                displayName = style.ToString(),
                visual      = new AssetReferenceT<MusicStyleVisualSO>(guid),
            });

            created++;
        }

        registry.entries = entries.ToArray();
        EditorUtility.SetDirty(registry);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MusicStyleContentSetup] Created {created} new Music Style placeholder(s) " +
                  $"(marked Addressable in '{group.Name}'); {Styles.Length - created} already had a " +
                  "registry entry and were left untouched.");
    }

    private static bool HasEntry(MusicStyleRegistrySO registry, MusicStyleId style)
    {
        foreach (var e in registry.entries)
            if (e.style == style) return true;
        return false;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf   = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
        AssetDatabase.Refresh();
    }
}
#endif
