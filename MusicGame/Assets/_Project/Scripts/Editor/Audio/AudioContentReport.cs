#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measured report of the game's audio content: per voice × language the clips, their spoken duration, the SOURCE
/// files (production masters outside the project, game WAVs inside it) and the IMPORTED size Unity actually stores and
/// packs into the bundles (AudioImporter compressed size — the bundle adds a small header per asset; bundles are only
/// measured if ServerData exists). Plus the announcers, the vocalisations, the other sfx, formats (rate / channels /
/// encoding) and the production installation (models, venv). Nothing is estimated silently: unknown values are
/// reported as such. Batch: -executeMethod AudioContentReport.RunFromCommandLine → Logs/AudioContentReport.txt
/// </summary>
public static class AudioContentReport
{
    public static void RunFromCommandLine()
    {
        string r;
        try { r = Build(); } catch (Exception e) { r = e.ToString(); }
        File.WriteAllText(Path.Combine(VoiceProduction.ProjectRoot, "Logs", "AudioContentReport.txt"), r);
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/MusicGame/Audio/Audio Content Report")]
    public static void Run() => Debug.Log(Build());

    private static readonly PropertyInfo CompSize = typeof(AudioImporter).GetProperty("compSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static long Imported(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        if (imp == null || CompSize == null) return -1;
        try { return Convert.ToInt64(CompSize.GetValue(imp)); } catch { return -1; }
    }

    private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static string MB(long b) => b < 0 ? "unknown" : $"{b / 1048576.0:0.00} MB";

    private static string Fmt(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (imp == null || clip == null) return "?";
        var s = imp.defaultSampleSettings;
        return $"{clip.frequency} Hz, {(imp.forceToMono ? 1 : clip.channels)} ch, {s.compressionFormat} q{s.quality:0.00}, {s.loadType}";
    }

    public static string Build()
    {
        var sb = new StringBuilder();
        var m = VoiceProduction.LoadManifest();
        var inv = VoiceProduction.Inventory(m);
        sb.AppendLine("== Dialogue voices (per voice × language) ==");
        sb.AppendLine("voice / lang       | lines (table) | clips ok | pending | failed | spoken  | masters (24-bit) | game WAV (16-bit) | imported (in bundle)");
        long tMaster = 0, tGame = 0, tImp = 0; int tClips = 0;
        string anyFormat = null;
        foreach (var g in inv.Where(l => !l.IsAnnouncer).GroupBy(l => (l.Voice, l.Language)).OrderBy(g => g.Key.Voice).ThenBy(g => g.Key.Language))
        {
            var ok = g.Where(l => l.State == VoiceProduction.LineState.UpToDate).ToList();
            int pending = g.Count(VoiceProduction.IsPending), failed = g.Count(l => l.State == VoiceProduction.LineState.Failed);
            long master = ok.Sum(l => FileSize(l.MasterPath)), game = ok.Sum(l => FileSize(Path.Combine(VoiceProduction.ProjectRoot, l.GamePath)));
            long imp = ok.Sum(l => Math.Max(0, Imported(l.GamePath)));
            float spoken = ok.Sum(l => l.Done != null ? l.Done.duration : 0f);
            bool noEngine = g.All(l => l.State == VoiceProduction.LineState.NoEngine);
            sb.AppendLine($"{g.Key.Voice,-12}/{g.Key.Language,-4} | {g.Count(),13} | {ok.Count,8} | {pending,7} | {failed,6} | {spoken,6:0}s | {MB(master),16} | {MB(game),17} | {(noEngine ? "subtitles only (no engine)" : MB(imp))}");
            tMaster += master; tGame += game; tImp += imp; tClips += ok.Count;
            if (anyFormat == null && ok.Count > 0) anyFormat = Fmt(ok[0].GamePath);
        }
        sb.AppendLine($"TOTAL dialogue clips {tClips}: masters {MB(tMaster)}, game WAV {MB(tGame)}, imported {MB(tImp)}");
        sb.AppendLine($"dialogue import format: {anyFormat}");

        var lib = AssetDatabase.LoadAssetAtPath<GameSfxLibrary>(GameAudioSetup.LibraryPath);
        sb.AppendLine("\n== Announcers (shared, local, always English) ==");
        foreach (var a in lib?.announcers ?? new AnnouncerSet[0])
        {
            var paths = a.All().Where(c => c != null).Select(AssetDatabase.GetAssetPath).ToList();
            sb.AppendLine($"{a.voiceId}: {paths.Count}/6 clips, game WAV {MB(paths.Sum(p => FileSize(Path.Combine(VoiceProduction.ProjectRoot, p))))}, imported {MB(paths.Sum(p => Math.Max(0, Imported(p))))}, {(paths.Count > 0 ? Fmt(paths[0]) : "")}");
        }

        sb.AppendLine("\n== Vocalisations (recorded CC0, shared by all languages, local) ==");
        var vocal = lib == null ? new List<string>() : new[] { lib.playerEfforts, lib.defaultComposerEfforts }.Concat(lib.composerEfforts.Select(c => c.efforts))
            .Where(s => s != null).SelectMany(s => s.All()).Where(c => c != null).SelectMany(c => c.clips).Where(c => c != null)
            .Select(AssetDatabase.GetAssetPath).Distinct().ToList();
        sb.AppendLine($"{vocal.Count} distinct takes, source {MB(vocal.Sum(p => FileSize(Path.Combine(VoiceProduction.ProjectRoot, p))))}, imported {MB(vocal.Sum(p => Math.Max(0, Imported(p))))}, {(vocal.Count > 0 ? Fmt(vocal[0]) : "")}");

        sb.AppendLine("\n== Other sound effects (local) ==");
        var sfx = lib == null ? new List<string>() : new[] { lib.hitLight, lib.hitHeavy, lib.hitBlocked, lib.specialLaunch, lib.knockOut, lib.chaserWake, lib.chaserActivate, lib.chaserContact, lib.trapped }
            .Where(c => c != null).SelectMany(c => c.clips).Where(c => c != null).Select(AssetDatabase.GetAssetPath).Distinct().ToList();
        sb.AppendLine($"{sfx.Count} clips, source {MB(sfx.Sum(p => FileSize(Path.Combine(VoiceProduction.ProjectRoot, p))))}, imported {MB(sfx.Sum(p => Math.Max(0, Imported(p))))}, {(sfx.Count > 0 ? Fmt(sfx[0]) : "")}");

        sb.AppendLine("\n== Built bundles (ServerData) ==");
        string server = Path.Combine(VoiceProduction.ProjectRoot, "ServerData");
        var bundles = Directory.Exists(server) ? Directory.GetFiles(server, "*.bundle", SearchOption.AllDirectories).Where(f => Path.GetFileName(f).StartsWith("voice_", StringComparison.OrdinalIgnoreCase)).ToArray() : new string[0];
        sb.AppendLine(bundles.Length == 0 ? "not built yet — the imported sizes above are what each bundle will contain (+ a small header per asset)"
                                          : string.Join("\n", bundles.Select(b => $"{Path.GetFileName(b)}: {MB(new FileInfo(b).Length)}")));

        sb.AppendLine("\n== Production only (never shipped) ==");
        long Dir(string d) { try { return Directory.Exists(d) ? Directory.GetFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0; } catch { return -1; } }
        string tool = VoiceProduction.ToolRoot;
        foreach (var d in Directory.Exists(Path.Combine(tool, "models")) ? Directory.GetDirectories(Path.Combine(tool, "models")) : new string[0])
            sb.AppendLine($"model {Path.GetFileName(d)}: {MB(Dir(d))}");
        sb.AppendLine($"Python env (.venv: PyTorch CUDA, qwen-tts, deps): {MB(Dir(Path.Combine(tool, ".venv")))}");
        sb.AppendLine($"voice masters + references + manifest ({VoiceProduction.MastersRoot}): {MB(Dir(VoiceProduction.MastersRoot))}");
        sb.AppendLine($"generator work files (jobs / logs): {MB(Dir(Path.Combine(tool, "work")))}");
        return sb.ToString();
    }
}
#endif
