#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Compatibility entry point; the common generator owns all arena identities and quality rules.
public static class ArenaSceneSetup
{
    public const string Master="Assets/_Project/Art/Arenas/Monteverdi/Monteverdi_Arena_01.prefab";
    public const string Folder="Assets/_Project/Art/Arenas/Monteverdi/Scenes";
    public static readonly string[] Qualities={"Low","Mid","HighMid","High","Ultra"};
    [MenuItem("Tools/MusicGame/Content/Arenas/Generate Monteverdi Quality Scenes")]
    public static void Build()=>ComposerSceneryAssembly.BuildOneForReview("Monteverdi");
    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try {Build();File.WriteAllText("Logs/ArenaSceneSetup.txt","PASS common distance-quality generator; Monteverdi arena identities 01–03 retained.");EditorApplication.Exit(0);}
        catch(Exception e){File.WriteAllText("Logs/ArenaSceneSetup.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
#endif
