#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SongCarouselInstall
{
    static SongCarouselInstall(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        var request=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/SongCarouselInstall.request"));
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../song-carousel-install.txt"));
        try{SongSelectionSkinSetup.Install();SongHorizontalCatalogValidation.Verify();AssetDatabase.SaveAssets();File.WriteAllText(output,"PASS: horizontal catalogue configured; decade filters, grid, native skins and finite bounds validated.");}
        catch(Exception e){File.WriteAllText(output,"FAIL: "+e);Debug.LogException(e);}
    }
}
#endif
