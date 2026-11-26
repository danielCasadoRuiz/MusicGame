#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class SettingsAlignedSetup
{
    static SettingsAlignedSetup(){EditorApplication.update+=Poll;}
    public static void Run()=>Poll();
    static void Poll()
    {
        const string request="../settings-aligned.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        bool hadMode=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string oldMode=AppearancePreference.Mode;
        var launch=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.NonPublic|BindingFlags.Static);var oldStyle=launch.GetValue(null);
        try
        {
            const string path="Assets/_Project/Prefabs/UI/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try{SettingsPauseSetup.Configure(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            LocalizationSetup.AddMissingEntries(new[]{"Settings.Skin"});AssetDatabase.SaveAssets();SettingsOpenValidation.Verify();
            TitleScreensReview.OutputOverride="../VisualReviews/SettingsAligned";Directory.CreateDirectory(TitleScreensReview.OutputOverride);
            foreach(var style in new[]{GameMusicStyle.Groove,GameMusicStyle.Dance,GameMusicStyle.Acoustic})
            {
                launch.SetValue(null,(GameMusicStyle?)style);TitleScreensReview.ReviewStyle=style;TitleScreensReview.OutputOverride="../VisualReviews/SettingsAligned/"+style;Directory.CreateDirectory(TitleScreensReview.OutputOverride);
                foreach(string mode in new[]{AppearancePreference.Light,AppearancePreference.Dark}){AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);TitleScreensReview.CaptureScreen("Settings",mode);}
            }
            File.WriteAllText("../settings-aligned.result","PASS: settings layout installed; existing behaviour checks passed; six native captures rendered.");
        }
        catch(Exception e){File.WriteAllText("../settings-aligned.result",e.ToString());Debug.LogException(e);}
        finally{launch.SetValue(null,oldStyle);AppearancePreference.SetMode(oldMode);AppearancePreference.Tick(10);if(!hadMode)PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);TitleScreensReview.OutputOverride=null;TitleScreensReview.ReviewStyle=GameMusicStyle.Dance;}
    }
}
#endif
