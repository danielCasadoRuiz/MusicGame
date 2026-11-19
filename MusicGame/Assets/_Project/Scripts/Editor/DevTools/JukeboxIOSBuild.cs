#if UNITY_IOS && UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class JukeboxIOSBuild
{
    [PostProcessBuild(100)]
    public static void Configure(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject(); project.ReadFromFile(projectPath);
        string framework = project.GetUnityFrameworkTargetGuid();
        foreach (var name in new[] { "AVFoundation.framework", "MediaPlayer.framework", "UniformTypeIdentifiers.framework" })
            project.AddFrameworkToProject(framework, name, name == "UniformTypeIdentifiers.framework");
        string file = project.FindFileGuidByProjectPath("Libraries/Plugins/iOS/TempoFuryMusic.mm");
        if (!string.IsNullOrEmpty(file)) project.SetCompileFlagsForFile(framework, file, new List<string> { "-fobjc-arc" });
        project.WriteToFile(projectPath);
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
        PlistElementArray modes = plist.root.values.TryGetValue("UIBackgroundModes", out var element) ? element.AsArray() : plist.root.CreateArray("UIBackgroundModes");
        if (!modes.values.Exists(e => e.AsString() == "audio")) modes.AddString("audio");
        plist.WriteToFile(plistPath);
    }
}
#endif
