#if UNITY_EDITOR
using System.IO;
using UnityEditor;
[InitializeOnLoad]
public static class SettingsOpenEditorInstall
{
    const string Request="../settings-open-install.request";
    static SettingsOpenEditorInstall(){EditorApplication.delayCall+=Run;}
    static void Run()
    {
        if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var command=File.ReadAllText(Request).Trim();File.Delete(Request);
        if(command=="test"){EditorApplication.delayCall+=SettingsOpenPlayProbe.Begin;return;}
        try{SettingsDSetup.Run();File.WriteAllText("../settings-open-install.result","PASS");EditorApplication.delayCall+=SettingsOpenPlayProbe.Begin;}
        catch(System.Exception e){File.WriteAllText("../settings-open-install.result",e.ToString());UnityEngine.Debug.LogException(e);}
    }
}
#endif
