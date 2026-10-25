#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Adds the Music / Effects / Voices volume sliders to the existing (hand-edited) MainMenu prefab's settings
/// panel — the same rows FrontendScreenPrefabs builds for a fresh prefab — and their UIText keys. Idempotent.
/// Batch: -executeMethod AudioSettingsUISetup.RunFromCommandLine</summary>
public static class AudioSettingsUISetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/MainMenu.prefab";

    [MenuItem("Tools/MusicGame/UI/Add Channel Volume Sliders To Settings")]
    public static void Run() => Debug.Log(Apply());

    public static void RunFromCommandLine()
    {
        string r = Apply();
        System.IO.File.WriteAllText("Logs/AudioSettingsUISetup.txt", r);
        EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Apply()
    {
        int keys = LocalizationSetup.AddMissingEntries(new[] { "Settings.MusicVolume", "Settings.SfxVolume", "Settings.VoiceVolume" });
        var go = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = go.GetComponent<MainMenuView>() ?? go.GetComponentInChildren<MainMenuView>(true);
            if (view == null || view.settingsPanel == null) return "FAIL no MainMenuView / settings panel";
            if (view.musicVolumeSlider != null) return $"kept (sliders already there); {keys} text key(s) added";
            FrontendScreenPrefabs.AddChannelVolumeRows((RectTransform)view.settingsPanel.transform, view);
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return $"added Music / Effects / Voices sliders to {PrefabPath}; {keys} text key(s) added";
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }
}
#endif
