#if UNITY_EDITOR
using System.IO;
using UnityEditor;
[InitializeOnLoad]
public static class SongSelectionPolishInstallRequest
{
    static SongSelectionPolishInstallRequest(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string path="../song-selection-polish.request";
        if(!File.Exists(path)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(path);
        try{SongSelectionPolishSetup.Install();File.WriteAllText("../song-selection-polish.result","PASS");}
        catch(System.Exception e){File.WriteAllText("../song-selection-polish.result",e.ToString());UnityEngine.Debug.LogException(e);}
    }
}
#endif
