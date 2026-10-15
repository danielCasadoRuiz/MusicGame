#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// End-to-end proof that a published build receives dialogue changes WITHOUT being rebuilt
/// (driven by an outside script that also serves ServerData over HTTP and runs the player):
///
///   BuildProbePlayerFromCommandLine   release content (profile LocalContentServer, state archived in the
///                                     build folder) + a small Windows player whose only scene runs
///                                     ComposerDialogueUpdateProbe
///   ApplyTestUpdateFromCommandLine    edits bach Intro.001 and adds bach Intro.011 (ca + en), then
///                                     builds a content update against that archived state
///
/// The edit is a TEST: the driver restores the project's localization/Addressables files afterwards.
/// </summary>
public static class ComposerDialogueTestBuild
{
    public const string ScenePath = "Assets/_Project/Scenes/Tests/DialogueUpdateProbe.unity";
    public const string DefaultBuildDir = "Builds/DialogueUpdateProbe";
    public const string ModifiedKey = "Composer.bach.Dialogue.Intro.001";
    public const string AddedKey    = "Composer.bach.Dialogue.Intro.011";

    public static void BuildProbePlayerFromCommandLine()
    {
        string dir = ContentUpdateTools.Arg("-probeBuildDir") ?? DefaultBuildDir;
        string archive = Path.Combine(dir, "ContentState");
        var sb = new StringBuilder();
        EnsureProbeScene();

        string report = ContentUpdateTools.WithProfile(ComposerDialogueContentSetup.LocalServerProfile, () =>
        {
            var inner = new StringBuilder(ContentUpdateTools.BuildReleaseContent(null, archive, out bool ok));
            if (!ok) return inner.AppendLine("FAIL release content").ToString();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(dir, "Player", "DialogueUpdateProbe.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var build = BuildPipeline.BuildPlayer(options);
            inner.AppendLine(build.summary.result == BuildResult.Succeeded
                ? $"  ok    player built: {options.locationPathName} ({build.summary.totalTime.TotalSeconds:0}s)"
                : $"  FAIL  player build: {build.summary.result} ({build.summary.totalErrors} errors)");
            return inner.ToString();
        });
        sb.Append(report);
        Finish(sb.ToString());
    }

    public static void ApplyTestUpdateFromCommandLine()
    {
        string dir = ContentUpdateTools.Arg("-probeBuildDir") ?? DefaultBuildDir;
        string archive = Path.Combine(dir, "ContentState");
        var sb = new StringBuilder("[DialogueTestUpdate]\n");

        var collection = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName("bach"));
        var ca = collection.GetTable("ca") as StringTable;
        var en = collection.GetTable("en") as StringTable;
        ca.AddEntry(ModifiedKey, "[Actualització remota] " + ca.GetEntry(ModifiedKey).Value);
        en.AddEntry(ModifiedKey, "[Remote update] " + en.GetEntry(ModifiedKey).Value);
        if (collection.SharedData.GetEntry(AddedKey) == null) collection.SharedData.AddKey(AddedKey);
        ca.AddEntry(AddedKey, "Variant nova de prova: una fuga a tres veus i tu encara busques la primera!");
        en.AddEntry(AddedKey, "Brand-new test variant: a three-voice fugue and you are still looking for the first!");
        EditorUtility.SetDirty(ca); EditorUtility.SetDirty(en); EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        sb.AppendLine($"  edited {ModifiedKey}, added {AddedKey}");

        sb.Append(ContentUpdateTools.WithProfile(ComposerDialogueContentSetup.LocalServerProfile,
            () => ContentUpdateTools.BuildContentUpdate(null, archive, out _)));
        Finish(sb.ToString());
    }

    public static void EnsureProbeScene()
    {
        if (File.Exists(ScenePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ComposerDialogueUpdateProbe").AddComponent<ComposerDialogueUpdateProbe>();
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void Finish(string report)
    {
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.AppendAllText("Logs/DialogueUpdateProbe.txt", report + "\n", Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }
}
#endif
