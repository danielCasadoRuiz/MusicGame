#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEngine;

/// <summary>
/// Probes that must run against REALLY BUILT Addressables content (not the AssetDatabase): switches to the
/// LocalContentServer profile + "Use Existing Build" for the next Play, optionally as a clean install
/// (bundle cache + cached remote catalog cleared), and restores the previous profile / play mode script
/// afterwards. The content must have been built with the LocalContentServer profile and be served at
/// http://localhost:8765 (content_server.py of the P3 work folder).
/// </summary>
public static class BuiltContentPlayMode
{
    private const string KeyActive  = "BuiltContentPlayMode.Active";
    private const string KeyProfile = "BuiltContentPlayMode.PrevProfile";
    private const string KeyBuilder = "BuiltContentPlayMode.PrevBuilder";
    public const string ProfileName = "LocalContentServer";

    public static bool Enter(bool cleanInstall, out string error)
    {
        error = null;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string profileId = settings.profileSettings.GetProfileId(ProfileName);
        int builder = settings.DataBuilders.FindIndex(b => b is BuildScriptPackedPlayMode);
        if (string.IsNullOrEmpty(profileId) || builder < 0) { error = $"{ProfileName} profile / packed play mode builder missing"; return false; }
        if (!SessionState.GetBool(KeyActive, false))
        {
            SessionState.SetString(KeyProfile, settings.activeProfileId);
            SessionState.SetInt(KeyBuilder, settings.ActivePlayModeDataBuilderIndex);
            SessionState.SetBool(KeyActive, true);
        }
        settings.activeProfileId = profileId;
        settings.ActivePlayModeDataBuilderIndex = builder;
        AssetDatabase.SaveAssets();
        if (cleanInstall)
        {
            Caching.ClearCache();
            string catalogCache = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
            if (Directory.Exists(catalogCache)) Directory.Delete(catalogCache, true);
        }
        return true;
    }

    public static void Restore()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        settings.activeProfileId = SessionState.GetString(KeyProfile, settings.activeProfileId);
        settings.ActivePlayModeDataBuilderIndex = SessionState.GetInt(KeyBuilder, settings.ActivePlayModeDataBuilderIndex);
        SessionState.SetBool(KeyActive, false);
        AssetDatabase.SaveAssets();
    }
}
#endif
