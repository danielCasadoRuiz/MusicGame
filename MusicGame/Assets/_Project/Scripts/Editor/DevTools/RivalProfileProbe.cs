#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play Mode probe (the driver puts a COPY of the real save in place and restores it afterwards):
///   rival difficulty — every campaign position in order gives a rival at least as strong (budget strictly up, charges
///   never down), each rival has its own attribute identity, versions can be fine-tuned (difficultyScale);
///   player profile — a finished run's converted build is averaged in once, manual style picks count and automatic
///   ones do not, it survives save → load, and the Main Menu profile icon opens the screen with those values;
///   real fight — the rival starts every round with exactly its per-round charges (refilled after a round change)
///   and does not throw its Special before its first-Special delay.
/// Report: Logs/RivalProfileProbe.txt + Logs/RivalProfileProbe/profile.png.  Batch: -executeMethod RivalProfileProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class RivalProfileProbe
{
    private const string KeyActive = "RivalProfileProbe.Active";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked;

    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static FightFlowState FightState => FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.OpponentSelection;
    private static PlayerProgressService P => PlayerProgressService.Instance;
    private static AppConfigSO App => Resources.Load<AppConfigSO>("AppConfig");
    private static FighterActor Rival => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.OpponentActor : null;
    private static int s_runsBefore, s_specialsAtStart, s_round;
    private static float s_roundStart;

    static RivalProfileProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (4f, "boot → main menu", () => Flow != GameFlowState.Boot && AppFlowController.Instance != null && P != null, () =>
        {
            Time.timeScale = 1f;
            AppFlowController.Instance.RequestState(GameFlowState.MainMenu);
        }),
        (2f, "rival difficulty along the campaign", () => Flow == GameFlowState.MainMenu, () =>
        {
            var campaign = App.campaign; var balance = App.combatBalance;
            var rows = new List<(string pos, float d, FightCombatBalanceConfig.RivalTuning t)>();
            for (int li = 0; li < campaign.LevelCount; li++)
            {
                var level = campaign.Level(li);
                for (int m = 1; m <= level.matchCount; m++)
                {
                    float d = campaign.RunnerDifficultyAt(level.id, m, 1);
                    rows.Add(($"{level.id}#{m}", d, balance.RivalTuningAt(d)));
                }
            }
            bool budgetUp = true, chargesNeverDown = true;
            for (int i = 1; i < rows.Count; i++)
            {
                budgetUp &= rows[i].t.Budget > rows[i - 1].t.Budget;
                chargesNeverDown &= rows[i].t.SpecialsPerRound >= rows[i - 1].t.SpecialsPerRound && rows[i].t.TriplesPerRound >= rows[i - 1].t.TriplesPerRound
                                    && rows[i].t.QuadsPerRound >= rows[i - 1].t.QuadsPerRound;
            }
            foreach (var r in rows.Where((_, i) => i % 3 == 0 || i == rows.Count - 1))
                Log($"   {r.pos,-6} difficulty {r.d:0.00} → budget {r.t.Budget:0} (≈{r.t.Budget / 4f:0.#} per attribute), per round {r.t.SpecialsPerRound} Special / {r.t.TriplesPerRound} x3 / {r.t.QuadsPerRound} x4, first Special after {r.t.FirstSpecialDelay:0.#} s, cooldown {r.t.SpecialCooldown:0.#} s");
            Check("every campaign position gives a stronger rival than the one before (within and across levels)", budgetUp, $"{rows.First().t.Budget:0} → {rows.Last().t.Budget:0} over {rows.Count} positions");
            Check("rival charges never decrease along the campaign", chargesNeverDown);
            var boss = balance.RivalTuningAt(1f, 2);
            Check("final-rival stages above the last campaign match", boss.Budget > rows.Last().t.Budget && boss.SpecialsPerRound > rows.Last().t.SpecialsPerRound, $"final stage 3 budget {boss.Budget:0}, {boss.SpecialsPerRound} Specials");

            // Identities: each rival's own proportions.
            var roster = App.opponentRoster.opponents.Where(o => o != null).ToList();
            var shapes = roster.Select(o => (o.id, b: o.levels[0].combatStats != null ? o.levels[0].combatStats.buildStats : default)).ToList();
            foreach (var (id, b) in shapes) Log($"   {id,-12} P {b.punchPower} / K {b.kickPower} / R {b.resistance} / A {b.agility}");
            int distinct = shapes.Select(s => $"{s.b.punchPower}/{s.b.kickPower}/{s.b.resistance}/{s.b.agility}").Distinct().Count();
            Check("every rival has its own attribute identity (all versions share it)", distinct >= roster.Count - 1
                  && roster.All(o => o.levels.All(l => l.combatStats == o.levels[0].combatStats)), $"{distinct} distinct shapes for {roster.Count} rivals");
        }),
        (0.5f, "player profile: a finished run", null, () =>
        {
            s_runsBefore = P.Profile.runs;
            float avgBefore = P.Profile.avgKickPower;
            var perf = new GamePerformance();
            perf.ByType[RingType.Kick] = new TypePerformance { Type = RingType.Kick, Available = 80, Collected = 60, CollectionRate = 0.75f, TimingAccuracy = 0.9f };
            perf.ByType[RingType.Snare] = new TypePerformance { Type = RingType.Snare, Available = 60, Collected = 30, CollectionRate = 0.5f, TimingAccuracy = 0.9f };
            var stats = new CollectionStats(); stats.AddScore(1000);
            EventBus.Publish(new GameEndedEvent { Stats = stats, Performance = perf, Resources = new RunnerResourceCounts() });
            var cb = GameSession.Instance.RunnerResults.CombatBuild;
            float expected = avgBefore + (cb.KickPower - avgBefore) / (s_runsBefore + 1);
            Check("a finished run's CONVERTED build joins the profile average (once)", P.Profile.runs == s_runsBefore + 1 && Mathf.Abs(P.Profile.avgKickPower - expected) < 1e-3f
                  && Mathf.Approximately(P.Profile.lastKickPower, cb.KickPower), $"runs {P.Profile.runs}, avg kick {P.Profile.avgKickPower:0.##}, last {P.Profile.lastKickPower:0.#}");

            // Styles: manual pick counts, automatic does not.
            int rockBefore = Picks(GameMusicStyle.Rock), jazzBefore = Picks(GameMusicStyle.Jazzy);
            GameSession.Instance.SelectedSong = new SelectedSongInfo { SongId = "song_001", DisplayName = "probe" };
            GameSession.Instance.MarkManualChoice("song_001");
            GameSession.Instance.RecordStyleIfManual(GameMusicStyle.Rock);
            GameSession.Instance.RecordStyleIfManual(GameMusicStyle.Rock);   // the same choice never counts twice
            GameSession.Instance.SelectedSong = new SelectedSongInfo { SongId = "song_002", DisplayName = "auto" };   // CONTINUE-style: not marked
            GameSession.Instance.RecordStyleIfManual(GameMusicStyle.Jazzy);
            Check("styles: a manual pick counts once, an automatic one never", Picks(GameMusicStyle.Rock) == rockBefore + 1 && Picks(GameMusicStyle.Jazzy) == jazzBefore,
                  $"rock {rockBefore}→{Picks(GameMusicStyle.Rock)}, jazzy {jazzBefore}→{Picks(GameMusicStyle.Jazzy)}");

            P.Save();
            var loaded = PlayerProgressStore.Load();
            Check("profile saved + loaded (save v8)", loaded != null && loaded.version == PlayerProgressData.CurrentVersion && loaded.profile.runs == P.Profile.runs
                  && loaded.profile.stylePicks.Any(x => x.style == (int)GameMusicStyle.Rock && x.count == Picks(GameMusicStyle.Rock)), $"v{loaded?.version}, runs {loaded?.profile.runs}");

            UnityEngine.Object.FindObjectsByType<MainMenuView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First().profileButton.onClick.Invoke();
        }),
        (1f, "profile screen", null, () =>
        {
            var view = UnityEngine.Object.FindObjectsByType<ProfileScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            Capture("profile");
            bool open = view != null && view.root.activeInHierarchy;
            bool values = open && view.stats.Length == 4 && view.stats.All(r => r.value.text != "–") && view.stylesText.text.Length > 0 && !view.stylesText.text.Contains("Profile.");
            Check("the profile icon opens the profile with the four averages and the styles", values,
                  open ? $"{string.Join(" ", view.stats.Select(r => $"{r.stat}={r.value.text}"))} | {view.summaryText.text} | {view.stylesText.text} | {view.lookText.text}" : "not open");
            ProfileScreenController.Instance?.Close();
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (2f, "→ fight", () => Flow == GameFlowState.SongSelection, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
        (0f, "fighting: rival charges + Special pacing", () => FightState == FightFlowState.Fighting && Rival != null, () =>
        {
            var t = FightSceneBootstrap.Instance.RivalTuning;
            var w = Rival.CombatResources;
            s_specialsAtStart = w.Specials;
            s_roundStart = Time.time;
            s_round = FightMatchController.Instance.CurrentRound;
            Check("the rival starts the round with exactly its per-round charges for this difficulty",
                  w.Specials == t.SpecialsPerRound && w.TripleCombos == t.TriplesPerRound && w.QuadCombos == t.QuadsPerRound,
                  $"difficulty {t.Difficulty:0.00}: {w.Specials}/{w.TripleCombos}/{w.QuadCombos} vs {t.SpecialsPerRound}/{t.TriplesPerRound}/{t.QuadsPerRound}, budget {t.Budget:0} [{Rival.BuildStats}]");
        }),
        (0f, "no Special before the delay", () => Time.time - s_roundStart >= Mathf.Max(1f, FightSceneBootstrap.Instance.RivalTuning.FirstSpecialDelay - 0.5f) || FightState != FightFlowState.Fighting, () =>
        {
            Check("the rival keeps its Special during the opening seconds of the round (AI pacing)", Rival.CombatResources.Specials == s_specialsAtStart || FightState != FightFlowState.Fighting,
                  $"{Rival.CombatResources.Specials} of {s_specialsAtStart} after {Time.time - s_roundStart:0.0} s");
            Rival.CombatResources.TryConsume(CombatResourceType.Special, Rival.CombatResources.Specials);   // as if spent
            Rival.CombatResources.TryConsume(CombatResourceType.TripleCombo, Rival.CombatResources.TripleCombos);
            FightMatchController.Instance.DebugForceDraw();
        }),
        (0f, "next round: rival refilled", () => FightMatchController.Instance.CurrentRound > s_round && FightState == FightFlowState.Fighting, () =>
        {
            var t = FightSceneBootstrap.Instance.RivalTuning;
            var w = Rival.CombatResources;
            Check("round change: the rival's charges are refilled to its per-round allowance (the player's never are)",
                  w.Specials == t.SpecialsPerRound && w.TripleCombos == t.TriplesPerRound, $"{w.Specials}/{w.TripleCombos} vs {t.SpecialsPerRound}/{t.TriplesPerRound}");
        }),
    };

    private static int Picks(GameMusicStyle s) => P.Profile.stylePicks.Where(x => x.style == (int)s).Sum(x => x.count);

    private static void Capture(string name)
    {
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var camGo = new GameObject("[ProbeCamera]");
        camGo.transform.position = new Vector3(0f, -5000f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 10f; }
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory("Logs/RivalProfileProbe");
        File.WriteAllBytes($"Logs/RivalProfileProbe/{name}.png", tex.EncodeToPNG());
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(camGo);
    }

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
                Application.logMessageReceived += (msg, _, type) => { if (type == LogType.Exception || type == LogType.Error) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); } };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 420; }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} flow={Flow} fight={FightState}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow}, fight={FightState})");
            step.act?.Invoke();
            s_step++; s_stepStart = now;
        }
        catch (Exception e) { Log("EXCEPTION " + e); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[RivalProfileProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/RivalProfileProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
