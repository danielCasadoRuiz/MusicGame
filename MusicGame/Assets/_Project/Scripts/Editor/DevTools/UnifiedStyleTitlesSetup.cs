#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TMPro;
[InitializeOnLoad]
public static class UnifiedStyleTitlesSetup
{
    const string Root="Assets/_Project/Prefabs/UI/";
    static readonly string[] SongScreens={"CountdownScreen","RoundIntro","RoundEnd","FinishBanner","EndScreen","FightHud","MatchResult","VersusScreen","OpponentSelection","LiveHud","PauseMenu"};
    static readonly string[] MenuScreens={"MainMenu","SongSelection","Collection/RivalCollectionScreen","Progression/ProgressionScreen","ProfileScreen","Jukebox","IntroScreen"};
    static UnifiedStyleTitlesSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="../unified-style-titles.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);try{Install();File.WriteAllText("../unified-style-titles.result","PASS");}catch(Exception e){File.WriteAllText("../unified-style-titles.result",e.ToString());Debug.LogException(e);}
    }
    public static void Install()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        foreach(string screen in SongScreens.Concat(MenuScreens).Concat(new[]{"AnalyzingScreen","HelpScreen"}))
        {
            string path=Root+screen+".prefab";if(!File.Exists(path))continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var scope=root.GetComponent<UIStyleScope>();if(scope==null)scope=root.AddComponent<UIStyleScope>();
                scope.domain=SongScreens.Contains(screen)?UIStyleScope.Domain.Song:MenuScreens.Contains(screen)?UIStyleScope.Domain.Menu:UIStyleScope.Domain.Automatic;
                if(root.TryGetComponent<CountdownScreenView>(out var countdown))EnsureTitle(countdown.numberText,lib,TitleTier.Display);
                if(root.TryGetComponent<RoundIntroView>(out var round))EnsureTitle(round.text,lib,TitleTier.Display);
                if(root.TryGetComponent<RoundEndView>(out var endRound))EnsureTitle(endRound.text,lib,TitleTier.Display);
                if(root.TryGetComponent<FinishBannerView>(out var finish))EnsureTitle(finish.text,lib,TitleTier.Display);
                if(root.TryGetComponent<VersusScreenView>(out var versus))EnsureTitle(versus.vsText,lib,TitleTier.Display);
                if(root.TryGetComponent<MatchResultView>(out var result)){EnsureTitle(result.titleText,lib,TitleTier.ScreenTitle);EnsureTitle(result.perfectBanner,lib,TitleTier.Section);}
                if(root.TryGetComponent<JukeboxView>(out var jukebox))
                {
                    var ink=jukebox.archedTitle.GetComponent<StyleTitleInk>();if(ink==null)ink=jukebox.archedTitle.gameObject.AddComponent<StyleTitleInk>();ink.library=lib;ink.inverseOutline=true;
                }
                foreach(var title in root.GetComponentsInChildren<TitleText>(true))title.Apply();
                var settings=root.GetComponentInChildren<SettingsSkinPanel>(true);if(settings!=null)settings.Apply();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();UnifiedStyleTitlesReview.Run();
    }
    static void EnsureTitle(TMP_Text text,StyleSkinLibrarySO lib,TitleTier tier)
    {
        if(text==null)return;var title=text.GetComponent<TitleText>();if(title==null)title=text.gameObject.AddComponent<TitleText>();title.library=lib;title.tier=tier;title.Apply();
    }
}
#endif
