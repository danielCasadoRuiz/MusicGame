#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// One-shot Editor tool: marks every audio clip under Assets/_Project/Audio/Music as Addressable,
/// in its OWN "Songs" group (never dumped into "Default Local Group" alongside everything else —
/// this is what makes it trivial to later flip JUST this group's Build/Load path to Remote / Unity
/// Cloud Content Delivery without reorganizing anything), tagged with the "Song" label.
///
/// SongSelectionController lists whatever this label currently resolves to at runtime — there is no
/// hand-authored catalog ScriptableObject to keep in sync any more (see AddressableSongSource's own
/// doc). Adding a new song is: drop the audio file in this folder, re-run this tool (or push it as
/// part of a future Addressables content release once Remote/CCD is wired up) — nothing else to
/// touch. Removing one (e.g. a copyright takedown) is the same in reverse.
///
/// Safe to re-run any time the Music folder's contents change — already-Addressable clips are moved
/// (not duplicated) if they end up in a different group, and their address is refreshed from the
/// FriendlyNames table below.
/// </summary>
public static class SongAddressablesSetup
{
    private const string GroupName    = "Songs";
    private const string Label        = "Song";
    private const string MusicFolder  = "Assets/_Project/Audio/Music";

    // File name (without extension) → friendly display name shown in Song Selection. Anything
    // found in the Music folder that ISN'T listed here still gets added (falls back to its own file
    // name) — this table only makes the display nicer, it's never a gate on what counts as a song.
    private static readonly (string fileName, string displayName)[] FriendlyNames =
    {
        ("Jamiroquai_CannedHeat",      "Jamiroquai - Canned Heat"),
        ("BillieJean",                 "Michael Jackson - Billie Jean"),
        ("Daft Punk_Around The World", "Daft Punk - Around the World"),
        ("Weeknd_BlindingLights",      "The Weeknd - Blinding Lights"),
    };

    [MenuItem("Tools/MusicGame/Setup Song Addressables")]
    public static void Setup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[SongAddressablesSetup] No AddressableAssetSettings found — open " +
                            "Window > Asset Management > Addressables > Groups once first to create it.");
            return;
        }

        if (!settings.GetLabels().Contains(Label))
            settings.AddLabel(Label);

        var group = settings.FindGroup(GroupName);
        if (group == null)
        {
            group = settings.CreateGroup(GroupName, false, false, true, null,
                typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        }

        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder });
        foreach (var guid in guids)
        {
            string path     = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
            entry.address = FriendlyNameFor(fileName);
            entry.SetLabel(Label, true, false, false);
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SongAddressablesSetup] Done — {guids.Length} audio clip(s) from {MusicFolder} are " +
                  $"now Addressable in the '{GroupName}' group with the '{Label}' label. Re-run any time " +
                  "a song is added to (or removed from) that folder.");
    }

    private static string FriendlyNameFor(string fileName)
    {
        foreach (var (name, display) in FriendlyNames)
            if (name == fileName) return display;
        return fileName;
    }
}
#endif
