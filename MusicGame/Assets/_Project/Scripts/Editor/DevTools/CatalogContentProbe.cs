#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Play Mode probe of the PUBLISHED catalog (P3) against REALLY BUILT remote content: Addressables
/// "Use Existing Build" + the LocalContentServer profile, served over HTTP by a local test server
/// (_AudioAnalysisWork scratch content_server.py) whose control file this probe writes to throttle or fail
/// downloads. Before Play it clears the bundle cache and the cached remote catalog (a clean install: nothing
/// downloaded, no analysis cache used). Boots Core.unity like the game and drives Song Selection:
///   index + listing from the remote catalog; first play (download → … → Ready → countdown) with stage order,
///   clock untouched before Ready, every consumer ready at the countdown, no analysis / model / worker;
///   the same song again (already downloaded: no Downloading stage); cancel during Downloading (slow
///   download), during ApplyingTheme (content adopted) and during the Runner scene preparation; two
///   consecutive selections (the first never publishes); tampered and absent profiles (IncompatibleContent);
///   a failing server (DownloadFailed); lease / bundle release when the selection moves on.
/// Separate times (download / load+validate+build / style+theme / scene+world / countdown) and sizes /
/// memory are reported. Restores the previous Addressables profile and play mode script when done.
/// It boots the real AppBootstrap (song selection writes the save): back the save up before running.
/// Batch: -executeMethod CatalogContentProbe.RunFromCommandLine -serverControl FILE -serverLog FILE [-catalogProbeOut FILE]
/// Exit code 0 = all checks passed, 1 = a check failed, 2 = timeout/exception.
/// </summary>
[InitializeOnLoad]
public static class CatalogContentProbe
{
    private const string KeyActive  = "CatalogContentProbe.Active";
    private const string KeyOut     = "CatalogContentProbe.Out";
    private const string KeyControl = "CatalogContentProbe.Control";
    private const string KeyLog     = "CatalogContentProbe.ServerLog";
    private const float  StepTimeout = 120f;

    private static readonly StringBuilder s_log = new();
    private static int    s_pass, s_fail, s_errors, s_expectedErrors;
    private static int    s_step = -1;
    private static double s_stepStart;
    private static bool   s_hooked;

    // Event bookkeeping.
    private struct Prep { public int Gen; public SongPreparationStage Stage; public float Progress; public long Bytes; public SongContentError Error; public double Time; public MusicClockState Clock; public bool ClockExists; }
    private static readonly List<Prep> s_prep = new();
    private static readonly List<int> s_profileGens = new();
    private static readonly List<(int gen, double time)> s_started = new();
    private static Action<SongPreparationEvent>  s_onPrep;
    private static Action<SongProfileReadyEvent> s_onProfile;
    private static Action<GameStartedEvent>      s_onStarted;

    // Carried between steps.
    private static readonly List<string> s_rowIds = new();
    private static int s_runs0, s_models0, s_workers0;
    private static int s_gen;
    private static string s_song;
    private static AudioClip s_prevClip;
    private static SongPreparationStage s_cancelAt;
    private static double s_cancelTime;
    private static bool s_cancelled;
    private static readonly List<string> s_startChecks = new();
    private static readonly StringBuilder s_times = new();

    static CatalogContentProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    // ── Setup (edit mode) ────────────────────────────────────────────────────────

    public static void RunFromCommandLine()
    {
        string outPath = ContentUpdateTools.Arg("-catalogProbeOut") ?? Path.GetFullPath("Logs/CatalogContentProbe.txt");
        string control = ContentUpdateTools.Arg("-serverControl");
        string serverLog = ContentUpdateTools.Arg("-serverLog");
        if (string.IsNullOrEmpty(control) || string.IsNullOrEmpty(serverLog)) { Debug.LogError("[CatalogProbe] -serverControl and -serverLog are required"); EditorApplication.Exit(2); return; }

        // Clean install: no downloaded bundle, no cached remote catalog.
        if (!BuiltContentPlayMode.Enter(cleanInstall: true, out string error)) { Debug.LogError("[CatalogProbe] " + error); EditorApplication.Exit(2); return; }
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        SessionState.SetString(KeyControl, control);
        SessionState.SetString(KeyLog, serverLog);
        Server(0, null);
        File.WriteAllText(serverLog, "");
        Log("setup: profile LocalContentServer, Use Existing Build; bundle cache + cached remote catalog cleared (clean install)");

        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Server(int kbps, string[] fail) =>
        File.WriteAllText(SessionState.GetString(KeyControl, ""),
            "{\"kbps\": " + kbps + ", \"fail\": [" + string.Join(",", (fail ?? Array.Empty<string>()).Select(f => "\"" + f + "\"")) + "]}");

    // ── Steps ────────────────────────────────────────────────────────────────────

    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static int Gen => GameSession.Instance != null ? GameSession.Instance.SelectionGeneration : -1;

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (8f, "boot", () => ContentDownloadManager.Instance != null && ContentDownloadManager.Instance.IsInitialized && Flow != GameFlowState.Boot, () =>
        {
            Subscribe();
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (0f, "listing from the remote index", () => SongCatalogService.IndexAvailable && Rows().Count >= SongCatalogService.Index.songs.Count, () =>
        {
            s_rowIds.Clear();
            // Rows show the published display name; map it back to the stable id through the index.
            s_rowIds.AddRange(Rows().Select(r => SongCatalogService.Index.songs.FirstOrDefault(e => e.displayName == Plain(r.label.text))?.songId));
            Check("published index loaded from the remote catalog", SongCatalogService.Index.songs.Count == 20, $"{SongCatalogService.Index.songs.Count} songs, revision {SongCatalogService.Index.contentRevision}");
            Check("Song Selection lists every published song with its stable id", s_rowIds.Count == 20 && s_rowIds.All(id => id != null), $"{s_rowIds.Count} rows");
            s_runs0 = SongAnalysisJob.RunsStarted;
            s_models0 = SentisMusicTagger.ModelLoadCount;
            s_workers0 = SentisMusicTagger.WorkerCreateCount;
            Log($"baseline: analyses {s_runs0}, model loads {s_models0}, workers {s_workers0}; Mono {Profiler.GetMonoUsedSizeLong() / 1048576.0:F1} MB, total allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB");
            Server(8192, null); // 8 MB/s: the download stage is visible and measurable
            Play(0);
        }),
        (0f, "A: clean first play (download)", () => Started(s_gen), () =>
        {
            CheckFullPreparation("A first play (not downloaded)", expectDownload: true);
            s_firstLayoutKey = BonusLayoutProvider.LastKey;
            Log($"   bonus layout: {BonusLayoutProvider.LastOutcome} {s_firstLayoutKey}");
        }),
        (2f, "A: playing", () => Clock != null && Clock.HasPlaybackStarted, () =>
        {
            var src = Source;
            Check("A: music started after the countdown, clock playing", src != null && src.isPlaying && Clock.State == MusicClockState.Playing, $"state={Clock.State}");
            s_prevClip = src != null ? src.clip : null;
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (1f, "back to selection", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () =>
        {
            Check("A: lease kept while A stays the selection", SongContentLease.LiveCount == 1, $"live leases {SongContentLease.LiveCount}");
            Play(0);
        }),
        (0f, "A: second play (already downloaded)", () => Started(s_gen), () =>
        {
            CheckFullPreparation("A again (downloaded)", expectDownload: false);
            Check("A again: the SAME combination loads its stored bonus layout (no regeneration, no attempt in the key)",
                  BonusLayoutProvider.LastOutcome == BonusLayoutOutcome.Loaded && BonusLayoutProvider.LastKey == s_firstLayoutKey,
                  $"{BonusLayoutProvider.LastOutcome} {BonusLayoutProvider.LastKey}");
        }),
        (1f, "A again → selection", null, () => AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection)),
        (1f, "B: cancel while downloading (slow server)", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () =>
        {
            Server(1024, null); // 1 MB/s
            ArmCancel(SongPreparationStage.Downloading);
            Play(1);
        }),
        (0f, "B: cancelled", () => s_cancelled && Flow == GameFlowState.SongSelection, () => { }),
        (2.5f, "B: nothing published after the cancel", null, () =>
        {
            CheckCancelled("B (Downloading)", expectLease: 0);
            Check("A's playback audio released when B became the selection (asset unloaded)", s_prevClip == null, $"clip {(s_prevClip == null ? "destroyed" : "still alive")}");
            Server(0, null);
            ArmCancel(SongPreparationStage.ApplyingTheme);
            Play(2);
        }),
        (0f, "C: cancelled at ApplyingTheme", () => s_cancelled && Flow == GameFlowState.SongSelection, () => { }),
        (2.5f, "C: nothing started", null, () =>
        {
            CheckCancelled("C (ApplyingTheme, content already adopted)", expectLease: 1);
            s_prevClip = GameSession.Instance.SelectedSong?.Clip;
            ArmCancel(SongPreparationStage.LoadingScene);
            Play(3);
        }),
        (0f, "D: cancelled while the Runner scene loads", () => s_cancelled && Flow == GameFlowState.SongSelection, () => { }),
        (3f, "D: nothing started", null, () =>
        {
            CheckCancelled("D (LoadingScene, Runner scene loading)", expectLease: 1);
            Check("C's playback audio released when D became the selection", s_prevClip == null);
            // Two consecutive selections: E is replaced right after Play by F.
            Play(4);
            s_song = s_rowIds[4];
        }),
        (0f, "E: superseded right away", () => SongPreparation.CurrentGeneration == s_gen && SongPreparation.Current != SongPreparationStage.None, () =>
        {
            s_cancelAt = SongPreparation.Current;
            s_cancelTime = Time.realtimeSinceStartupAsDouble;
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (0.2f, "F: select + play", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () =>
        {
            int superseded = s_gen;
            Play(5);
            s_supersededGen = superseded;
        }),
        (0f, "F: started", () => Started(s_gen), () =>
        {
            Check("E (superseded at " + s_cancelAt + ") published no profile and never started",
                  !s_profileGens.Contains(s_supersededGen) && !s_started.Any(x => x.gen == s_supersededGen),
                  $"E #{s_supersededGen}, F #{s_gen}");
            Check("E published no stage after it was superseded",
                  !s_prep.Any(p => p.Gen == s_supersededGen && p.Time > s_cancelTime + 1e-6 && p.Stage != SongPreparationStage.Cancelled), "");
            CheckFullPreparation("F after a superseded selection", expectDownload: true);
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (1f, "G: tampered profile revision", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () =>
        {
            SongCatalogService.TryGetEntry(s_rowIds[6], out var e);
            e.profileCrc ^= 1u;
            SongCatalogService.OverrideEntryForTests(e);
            s_failureWindow = true; // G, H, I fail on purpose: their error logs are expected
            Play(6);
        }),
        (0f, "G: failed", () => FailedFor(s_gen), () =>
        {
            CheckFailure("G tampered revision", SongContentError.IncompatibleContent);
            SongCatalogService.TryGetEntry(s_rowIds[7], out var e);
            e.profileAddress = SongContentKeys.ProfilePrefix + "__absent__";
            SongCatalogService.OverrideEntryForTests(e);
        }),
        (1f, "H: absent profile", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () => Play(7)),
        (0f, "H: failed", () => FailedFor(s_gen), () =>
        {
            CheckFailure("H absent profile", SongContentError.IncompatibleContent);
            Server(0, new[] { ".bundle" }); // every bundle request answers 503
        }),
        (1f, "I: server failing", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () => Play(8)),
        (0f, "I: failed", () => FailedFor(s_gen), () =>
        {
            CheckFailure("I download failure (HTTP 503, retries exhausted)", SongContentError.DownloadFailed);
            Server(0, null);
            s_failureWindow = false;
            SongCatalogService.ResetForTests(); // published entries back (overrides dropped)
        }),
        (1f, "I: retry once the server answers", () => Flow == GameFlowState.SongSelection && Rows().Count > 0, () => Play(8)),
        (0f, "I: started after the retry", () => Started(s_gen), () =>
        {
            CheckFullPreparation("I after a failed download (recovery = retry)", expectDownload: true);
            AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        }),
        (2f, "final release check", () => Flow == GameFlowState.SongSelection, () =>
        {
            var songBundles = AssetBundle.GetAllLoadedAssetBundles().Select(b => b.name).Where(n => n.Contains("remote_songs")).ToList();
            Check("loaded song bundles: only the current selection's audio (+ the index)", songBundles.Count <= 2 && SongContentLease.LiveCount <= 1,
                  $"{songBundles.Count} bundles [{string.Join(", ", songBundles)}], live leases {SongContentLease.LiveCount}");
            Check("no analysis, no model load, no Sentis worker during the whole catalog session",
                  SongAnalysisJob.RunsStarted == s_runs0 && SentisMusicTagger.ModelLoadCount == s_models0 && SentisMusicTagger.WorkerCreateCount == s_workers0,
                  $"analyses +{SongAnalysisJob.RunsStarted - s_runs0}, model loads +{SentisMusicTagger.ModelLoadCount - s_models0}, workers +{SentisMusicTagger.WorkerCreateCount - s_workers0}");
            Log($"memory at the end: Mono {Profiler.GetMonoUsedSizeLong() / 1048576.0:F1} MB, total allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB");
            Log("server requests:\n" + ServerSummary());
        }),
    };

    private static int s_supersededGen, s_genBefore;
    private static string s_firstLayoutKey;
    private static bool s_awaitGen;

    // The k-th SELECTABLE row (completed songs cannot be chosen in Song Selection).
    private static void Play(int k)
    {
        var rows = Rows();
        var selectable = Enumerable.Range(0, rows.Count).Where(i => rows[i].button.interactable && !rows[i].Played).ToList();
        if (k >= selectable.Count) throw new InvalidOperationException($"only {selectable.Count} selectable songs (needs {k + 1}) — run the probe with a fresh progress save");
        int row = selectable[k];
        s_song = s_rowIds[row];
        rows[row].button.onClick.Invoke();
        // The selection (and its generation) is recorded a frame later by SongSelectionService: the
        // generation is taken from the first preparation event of a newer selection.
        s_genBefore = Gen;
        s_gen = -1;
        s_awaitGen = true;
        Find<SongSelectionView>().playButton.onClick.Invoke();
        Log($"   play {s_song} '{rows[row].label.text}'");
    }

    private static void ArmCancel(SongPreparationStage stage)
    {
        s_cancelAt = stage;
        s_cancelled = false;
        s_cancelTime = double.MaxValue;
    }

    // ── Checks ───────────────────────────────────────────────────────────────────

    private static void CheckFullPreparation(string label, bool expectDownload)
    {
        var p = s_prep.Where(x => x.Gen == s_gen).ToList();
        double T(SongPreparationStage s) { var m = p.FirstOrDefault(x => x.Stage == s); return m.Stage == s ? m.Time : -1; }
        var order = new[]
        {
            SongPreparationStage.ResolvingSelection, SongPreparationStage.LoadingContent, SongPreparationStage.ValidatingProfile,
            SongPreparationStage.BuildingProfile, SongPreparationStage.PreparingAudio, SongPreparationStage.ContentReady,
            SongPreparationStage.ResolvingStyle, SongPreparationStage.ApplyingTheme, SongPreparationStage.LoadingScene,
            SongPreparationStage.PreparingWorld, SongPreparationStage.PreparingConsumers, SongPreparationStage.Ready,
        };
        var times = order.Select(T).ToArray();
        bool ordered = times.All(t => t >= 0) && times.Zip(times.Skip(1), (a, b) => a <= b).All(x => x);
        Check($"{label}: every barrier stage completed, in order", ordered,
              string.Join(" ", order.Select((s, i) => $"{s}{(times[i] < 0 ? "(missing)" : "")}")));
        var dl = p.Where(x => x.Stage == SongPreparationStage.Downloading).ToList();
        if (expectDownload)
        {
            bool monotonic = dl.Zip(dl.Skip(1), (a, b) => b.Progress >= a.Progress - 1e-4f).All(x => x);
            Check($"{label}: real download stage (bytes, monotonic progress)", dl.Count > 0 && dl[0].Bytes > 0 && monotonic,
                  dl.Count > 0 ? $"{dl[0].Bytes / 1048576.0:F2} MB, {dl.Count} progress reports, last {dl.Last().Progress:F2}" : "no Downloading stage");
        }
        else Check($"{label}: no download (content cached)", dl.Count == 0, $"{dl.Count} Downloading events");
        Check($"{label}: MusicClock never ran before Ready", p.Where(x => x.Stage != SongPreparationStage.Ready).All(x => !x.ClockExists || x.Clock == MusicClockState.Stopped),
              string.Join(",", p.Where(x => x.ClockExists && x.Clock != MusicClockState.Stopped).Select(x => $"{x.Stage}:{x.Clock}")));
        foreach (var c in s_startChecks) Check($"{label}: at the countdown — {c.Substring(2)}", c[0] == '+', "");
        s_startChecks.Clear();
        Check($"{label}: one profile publish for this selection", s_profileGens.Count(g => g == s_gen) >= 1 && s_profileGens.Count(g => g == s_gen) <= 2,
              $"{s_profileGens.Count(g => g == s_gen)} (content publish + Runner republish)");
        double t0 = T(SongPreparationStage.ResolvingSelection), started = s_started.First(x => x.gen == s_gen).time;
        double dlStart = dl.Count > 0 ? dl[0].Time : -1;
        string line = $"{label}: download {(dlStart >= 0 ? $"{(T(SongPreparationStage.LoadingContent) - dlStart) * 1000:F0} ms" : "—")}" +
                      $" | index+size {( (dlStart >= 0 ? dlStart : T(SongPreparationStage.LoadingContent)) - t0) * 1000:F0} ms" +
                      $" | load+validate+profile+audio {(T(SongPreparationStage.ContentReady) - T(SongPreparationStage.LoadingContent)) * 1000:F0} ms" +
                      $" (load {(T(SongPreparationStage.ValidatingProfile) - T(SongPreparationStage.LoadingContent)) * 1000:F0}, validate {(T(SongPreparationStage.BuildingProfile) - T(SongPreparationStage.ValidatingProfile)) * 1000:F0}, profile build {(T(SongPreparationStage.PreparingAudio) - T(SongPreparationStage.BuildingProfile)) * 1000:F0}, audio {(T(SongPreparationStage.ContentReady) - T(SongPreparationStage.PreparingAudio)) * 1000:F0})" +
                      $" | style+theme+reveal {(T(SongPreparationStage.LoadingScene) - T(SongPreparationStage.ContentReady)) * 1000:F0} ms" +
                      $" | scene {(T(SongPreparationStage.PreparingWorld) - T(SongPreparationStage.LoadingScene)) * 1000:F0} ms, world+consumers+audio {(T(SongPreparationStage.Ready) - T(SongPreparationStage.PreparingWorld)) * 1000:F0} ms" +
                      $" | Ready→countdown {(started - T(SongPreparationStage.Ready)) * 1000:F1} ms | total {(started - t0) * 1000:F0} ms";
        var clip = Source != null ? Source.clip : null;
        if (clip != null) line += $" | clip {clip.loadType}, runtime {Profiler.GetRuntimeMemorySizeLong(clip) / 1048576.0:F2} MB; Mono {Profiler.GetMonoUsedSizeLong() / 1048576.0:F1} MB, total allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB";
        s_times.AppendLine(line);
        Log("   TIMES " + line);
        Check($"{label}: no analysis / model / worker", SongAnalysisJob.RunsStarted == s_runs0 && SentisMusicTagger.ModelLoadCount == s_models0 && SentisMusicTagger.WorkerCreateCount == s_workers0, "");
    }

    private static void CheckCancelled(string label, int expectLease)
    {
        var after = s_prep.Where(x => x.Gen == s_gen && x.Time > s_cancelTime + 1e-6).Select(x => x.Stage).ToList();
        Check($"{label}: cancelled at {s_cancelAt} — no profile, no countdown",
              !s_started.Any(x => x.gen == s_gen) && (expectLease == 1 || !s_profileGens.Contains(s_gen)),
              $"profile publishes {s_profileGens.Count(g => g == s_gen)}, started {s_started.Count(x => x.gen == s_gen)}");
        Check($"{label}: no stage published after the cancel", after.All(s => s == SongPreparationStage.Cancelled || s == SongPreparationStage.Failed), string.Join(",", after));
        Check($"{label}: leases {expectLease} (content {(expectLease == 1 ? "adopted by the selection" : "never adopted")})", SongContentLease.LiveCount == expectLease, $"live {SongContentLease.LiveCount}");
    }

    private static void CheckFailure(string label, SongContentError expected)
    {
        var f = s_prep.LastOrDefault(x => x.Gen == s_gen && x.Stage == SongPreparationStage.Failed);
        Check($"{label}: typed failure {expected}, back to Song Selection, nothing started",
              f.Stage == SongPreparationStage.Failed && f.Error == expected && !s_started.Any(x => x.gen == s_gen) && !s_profileGens.Contains(s_gen),
              $"error {f.Error}, controller {SongAnalysisController.LastError}");
        Check($"{label}: no analysis started to cover it", SongAnalysisJob.RunsStarted == s_runs0 && SentisMusicTagger.ModelLoadCount == s_models0, "");
        Check($"{label}: nothing leased", SongContentLease.LiveCount == 0, $"live {SongContentLease.LiveCount}");
    }

    private static bool Started(int gen) => s_started.Any(x => x.gen == gen);
    private static bool FailedFor(int gen) => s_prep.Any(x => x.Gen == gen && x.Stage == SongPreparationStage.Failed) && Flow == GameFlowState.SongSelection;

    // ── Subscriptions ────────────────────────────────────────────────────────────

    private static MusicClock Clock => MusicClock.Instance;
    private static AudioSource Source => UnityEngine.Object.FindObjectsByType<AudioSystemBootstrapper>(FindObjectsSortMode.None).FirstOrDefault()?.AudioSource;

    private static void Subscribe()
    {
        s_onPrep = e =>
        {
            if (s_awaitGen && e.SelectionGeneration > s_genBefore) { s_gen = e.SelectionGeneration; s_awaitGen = false; Log($"   selection #{s_gen}"); }
            var c = Clock;
            s_prep.Add(new Prep { Gen = e.SelectionGeneration, Stage = e.Stage, Progress = e.Progress, Bytes = e.DownloadBytes, Error = e.Error,
                                  Time = Time.realtimeSinceStartupAsDouble, ClockExists = c != null, Clock = c != null ? c.State : MusicClockState.Stopped });
            if (e.SelectionGeneration == s_gen && e.Stage == s_cancelAt && !s_cancelled && s_cancelTime == double.MaxValue) s_cancelPending = true;
        };
        s_onProfile = e => s_profileGens.Add(e.SelectionGeneration);
        s_onStarted = e =>
        {
            int gen = Gen;
            if (s_awaitGen && gen > s_genBefore) { s_gen = gen; s_awaitGen = false; }
            s_started.Add((gen, Time.realtimeSinceStartupAsDouble));
            // Everything the first musical instant needs, checked AT the countdown (synchronously).
            var world = MusicWorldManager.Instance;
            var env = RunnerEnvironmentController.Instance;
            var src = Source;
            var themes = ThemeManager.Instance;
            s_startChecks.Add((SongPreparation.Current == SongPreparationStage.Ready && SongPreparation.CurrentGeneration == gen ? "+ " : "- ") + "barrier state Ready for this selection");
            s_startChecks.Add((world != null && world.IsReady ? "+ " : "- ") + "world path built");
            s_startChecks.Add((env == null || env.IsBuilt ? "+ " : "- ") + $"environment modules loaded ({env?.Summary})");
            s_startChecks.Add((src != null && src.clip != null && src.clip.loadState == AudioDataLoadState.Loaded && src.clip == GameSession.Instance.SelectedSong?.Clip ? "+ " : "- ") + "the selection's clip is on the AudioSource with its data Loaded");
            s_startChecks.Add((src != null && !src.isPlaying ? "+ " : "- ") + "music not playing yet (countdown first)");
            s_startChecks.Add((themes == null || themes.IsStyleTransitionSettled ? "+ " : "- ") + "theme transition settled");
            s_startChecks.Add((GameSession.Instance.Profile != null ? "+ " : "- ") + "session profile adopted");
        };
        EventBus.Subscribe(s_onPrep);
        EventBus.Subscribe(s_onProfile);
        EventBus.Subscribe(s_onStarted);
    }

    private static bool s_cancelPending, s_failureWindow;

    private static string Plain(string label) => System.Text.RegularExpressions.Regex.Replace(label ?? "", "<[^>]*>", "");

    private static List<SongRowView> Rows() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None).Where(r => r.isActiveAndEnabled)
            .OrderBy(r => r.transform.GetSiblingIndex()).ToList();

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static string ServerSummary()
    {
        try
        {
            var lines = File.ReadAllLines(SessionState.GetString(KeyLog, "")).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();
            long bytes = lines.Where(l => l.Length > 2 && l[1] == "200").Sum(l => long.Parse(l[2]));
            var sb = new StringBuilder($"   {lines.Count} requests, {bytes / 1048576.0:F2} MB served\n");
            foreach (var g in lines.GroupBy(l => l[1])) sb.AppendLine($"   status {g.Key}: {g.Count()}");
            foreach (var l in lines.Where(l => l[1] == "200").Take(60)) sb.AppendLine($"     {l[0]}  {long.Parse(l[2]) / 1048576.0:F2} MB  {l[3]} ms");
            return sb.ToString();
        }
        catch (Exception e) { return "   (server log unreadable: " + e.Message + ")"; }
    }

    // ── Driver ───────────────────────────────────────────────────────────────────

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!s_hooked)
            {
                s_hooked = true;
                Application.logMessageReceived += (msg, stack, type) =>
                {
                    if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                    {
                        if (s_failureWindow) { s_expectedErrors++; s_log.AppendLine($"  (expected) [{type}] {msg}"); return; }
                        s_errors++;
                        s_log.AppendLine($"  [{type}] {msg}");
                    }
                };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; }

            // Cancel requested by the preparation event of the armed stage: done here, outside the publish.
            if (s_cancelPending)
            {
                s_cancelPending = false;
                s_cancelled = true;
                s_cancelTime = Time.realtimeSinceStartupAsDouble;
                Log($"   cancel requested during {SongPreparation.Current} (armed {s_cancelAt}) of #{s_gen}");
                AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
            }

            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart > StepTimeout + step.wait) { Log($"TIMEOUT at step {s_step} ({step.name}) flow={Flow} prep={SongPreparation.Current}"); Finish(2); return; }
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow {Flow}, prep #{SongPreparation.CurrentGeneration} {SongPreparation.Current})");
            step.act?.Invoke();
            s_step++;
            s_stepStart = now;
        }
        catch (Exception ex)
        {
            Log("EXCEPTION " + ex);
            Finish(2);
        }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg)
    {
        s_log.AppendLine(msg);
        Debug.Log("[CatalogProbe] " + msg);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onPrep != null) EventBus.Unsubscribe(s_onPrep);
        if (s_onProfile != null) EventBus.Unsubscribe(s_onProfile);
        if (s_onStarted != null) EventBus.Unsubscribe(s_onStarted);
        Log("\nSEPARATE TIMES (editor play mode, local HTTP server):\n" + s_times);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} unexpected errors logged ({s_expectedErrors} expected ones inside the failure scenarios) (exit {code})");
        try { BuiltContentPlayMode.Restore(); }
        catch (Exception ex) { Log("could not restore Addressables settings: " + ex.Message); }
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/CatalogContentProbe.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[CatalogProbe] could not write report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
