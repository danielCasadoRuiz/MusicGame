#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Focused Play Mode probe (real flow, boots Core.unity) of the first hazards version and of Song Selection's BACK:
///   OPENINGS — each obstacle kind built for real; capsule queries against its real colliders: what must pass
///     passes (crouched / tucked / beside / corridor), what must not doesn't (standing / jump over a high part).
///   BLOCKING — a tall block straight ahead: the player is physically stopped (never inside it) while the music
///     goes on, the song offset consumes the existing hold-back margin, then ONE Trapped penalty → respawn →
///     protection. A dodge while blocked frees the player with no burst back (gradual recovery, no penalty).
///   CHASER — asleep → warning → lets the player pass → chases continuously (bounded step per frame), frozen
///     while paused, ONE ChaserContact penalty on contact. Two penalties in the same frame → one.
///   NAVIGATION — song end → Fight → forced win → Match Result → SELECT SONG → BACK returns to the SAME result
///     (no roulette, no new match end, save file byte-identical); from the Main Menu BACK goes to the Main Menu.
/// The generated hazards are cleared first (the probe spawns its own). It writes the save (the forced win):
/// back it up before running. Batch: -executeMethod HazardNavigationProbe.RunFromCommandLine [-hazardProbeOut FILE]
/// Exit 0 = all passed, 1 = a check failed, 2 = timeout/exception.
/// </summary>
[InitializeOnLoad]
public static class HazardNavigationProbe
{
    private const string KeyActive = "HazardNavigationProbe.Active";
    private const string KeyOut    = "HazardNavigationProbe.Out";

    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors;
    private static int s_step = -1;
    private static double s_stepStart, s_hardDeadline;
    private static bool s_hooked;

    private static MusicClock Clock => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();
    private static PlayerController Player => UnityEngine.Object.FindFirstObjectByType<PlayerController>();
    private static RunnerHazardSystem Hazards => RunnerHazardSystem.Instance;
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static readonly FieldInfo LateralField = typeof(PlayerController).GetField("_lateralOffset", BindingFlags.Instance | BindingFlags.NonPublic);

    // Event bookkeeping.
    private static readonly List<PlayerFellEvent> s_fell = new();
    private static readonly List<RunnerPenaltyEvent> s_penalties = new();
    private static int s_respawns, s_matchEnded, s_opponentSelections;
    private static Action<PlayerFellEvent> s_onFell;
    private static Action<RunnerPenaltyEvent> s_onPenalty;
    private static Action<PlayerRespawnedEvent> s_onRespawn;
    private static Action<MatchEndedEvent> s_onMatchEnded;
    private static Action<FightFlowStateChangedEvent> s_onFightFlow;

    // Per-test state.
    private static RunnerObstacle s_block;
    private static int s_fellBefore, s_respawnsBefore;
    private static bool s_track;
    private static float s_minOffset, s_maxRecoverStep, s_prevOffset, s_maxPenetration, s_songAtBlock;
    private static Vector3 s_prevChaser;
    private static float s_maxChaserStep, s_chaserStepLimit;
    private static string s_maxChaserState = "";
    private static readonly List<RunnerChaser.State> s_chaserStates = new();
    private static string s_saveBefore;
    private static int s_matchEndedBefore, s_selectionsBefore;

    static HazardNavigationProbe()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        string outPath = Path.GetFullPath("Logs/HazardNavigationProbe.txt");
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-hazardProbeOut") outPath = args[i + 1];
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outPath);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot → song selection", null, () => { Subscribe(); AppFlowController.Instance.RequestState(GameFlowState.SongSelection); }),
        (4f,  "select first playable", null, () => FirstPlayableRow().button.onClick.Invoke()),
        (1f,  "play", null, () => Find<SongSelectionView>().playButton.onClick.Invoke()),
        (0f,  "wait for playback", () => Clock != null && Manager != null && Manager.IsRunning && Clock.HasPlaybackStarted && Hazards != null, () =>
        {
            Check("hazards generated for the run", Hazards.Placements.Count > 0, $"{Hazards.Placements.Count} placements");
            Hazards.ClearGeneratedForTest();
        }),

        // ── Openings ──
        (0.5f, "openings (real colliders)", null, CheckOpenings),

        // ── Blocking until the existing back limit → one Trapped penalty ──
        (0.5f, "tall block straight ahead", null, () =>
        {
            Hazards.ClearGeneratedForTest();
            s_block = Hazards.SpawnTestObstacle(RunnerObstacleKind.HighBlock, 7f, Player.LateralOffset);
            s_fellBefore = s_fell.Count; s_respawnsBefore = s_respawns;
            s_minOffset = 0f; s_maxPenetration = float.MinValue; s_songAtBlock = -1f; s_track = true;
        }),
        (0f, "wait for the trap penalty", () => s_fell.Count > s_fellBefore, () =>
        {
            s_track = false;
            var maxBack = Manager.Config.core.maxHoldBack;
            Check("blocked: never inside the block", s_maxPenetration <= 0.06f, $"worst penetration {s_maxPenetration:F3} m");
            Check("blocked: the margin consumed down to the existing back limit", s_minOffset <= -maxBack + 0.01f, $"min offset {s_minOffset:F2} (limit −{maxBack:F2})");
            Check("blocked: the music kept going", s_songAtBlock >= 0f && Clock.SongTime > s_songAtBlock + 0.3f, $"song {s_songAtBlock:F2} → {Clock.SongTime:F2}");
            Check("ONE Trapped penalty", s_fell.Count == s_fellBefore + 1 && s_fell.Last().Cause == RunnerPenaltyCause.Trapped
                  && s_penalties.Count(p => p.Cause == RunnerPenaltyCause.Trapped) == 1, $"falls +{s_fell.Count - s_fellBefore}, cause {s_fell.Last().Cause}");
        }),
        (0f, "wait for the respawn", () => s_respawns > s_respawnsBefore, () =>
        {
            Check("respawn → protected", Hazards.IsProtected);
            Check("respawned clear of the block", s_block == null || !s_block.gameObject.activeInHierarchy || BlockLocalZ(s_block) > s_block.depth * 0.5f + 0.4f, $"z={(s_block != null ? BlockLocalZ(s_block) : 0f):F2}");
            s_fellBefore = s_fell.Count;
        }),
        (2.5f, "no repeated penalty after the respawn", null, () =>
        {
            Check("no second penalty", s_fell.Count == s_fellBefore, $"+{s_fell.Count - s_fellBefore}");
            Hazards.ClearGeneratedForTest();
        }),

        // ── Dodge while blocked → free, gradual recovery, no penalty ──
        (0.5f, "block again, dodge it", null, () =>
        {
            s_block = Hazards.SpawnTestObstacle(RunnerObstacleKind.HighBlock, 7f, Player.LateralOffset);
            s_fellBefore = s_fell.Count;
        }),
        (0f, "wait until blocked", () => Player.IsBlocked, () =>
        {
            float bw = s_block.BlockWidth(s_block.TrackWidth) * 0.5f;
            float side = s_block.Lateral <= 0f ? 1f : -1f;
            LateralField.SetValue(Player, s_block.Lateral + side * (bw + 1.0f)); // the free side
            s_prevOffset = Player.SongOffset; s_maxRecoverStep = 0f; s_track = true;
        }),
        (3f, "dodged", null, () =>
        {
            s_track = false;
            var c = Manager.Config.core;
            Check("dodge frees the player: no penalty", s_fell.Count == s_fellBefore);
            Check("no burst back: recovery ≤ holdBackRecover", s_maxRecoverStep <= c.holdBackRecover + 0.5f, $"max {s_maxRecoverStep:F2} m/s (limit {c.holdBackRecover:F1})");
            Check("back near the reference", Player.SongOffset > -0.3f, $"offset {Player.SongOffset:F2}");
            LateralField.SetValue(Player, 0f);
            Hazards.ClearGeneratedForTest();
        }),

        // ── Chaser ──
        (0.5f, "chaser ahead", null, () =>
        {
            s_chaserStates.Clear(); s_maxChaserStep = 0f; s_fellBefore = s_fell.Count; s_respawnsBefore = s_respawns;
            var ch = Hazards.SpawnTestChaser(14f, 1);
            s_prevChaser = ch.transform.position;
            var cfg = Manager.Config.hazards;
            s_chaserStepLimit = Manager.Config.core.playerSpeed + cfg.hardChaserCatchUpSpeed + cfg.chaserLateralSpeed + 6f;
            s_track = true;
        }),
        (0f, "wait for the chase", () => Hazards.Chaser != null && Hazards.Chaser.Current == RunnerChaser.State.Chasing, () => Find<PauseController>().Pause()),
        (1f, "paused chase", null, () =>
        {
            Check("pause freezes the chaser", (Hazards.Chaser.transform.position - s_prevChaser).magnitude < 1e-4f);
            Find<PauseController>().Resume();
        }),
        (0f, "wait for the contact penalty", () => s_fell.Count > s_fellBefore || Hazards.Chaser.Current == RunnerChaser.State.Inactive, () =>
        {
            s_track = false;
            var seq = string.Join("→", s_chaserStates);
            Check("asleep → warning → waits → chases", seq.StartsWith("Asleep→Warning→WaitingPass→Chasing"), seq);
            Check("continuous chase (no teleport)", s_maxChaserStep <= s_chaserStepLimit, $"max {s_maxChaserStep:F1} m/s (bound {s_chaserStepLimit:F1}) in {s_maxChaserState}");
            Check("ONE ChaserContact penalty", s_fell.Count == s_fellBefore + 1 && s_fell.Last().Cause == RunnerPenaltyCause.ChaserContact, $"falls +{s_fell.Count - s_fellBefore}");
        }),
        (0f, "wait for the respawn", () => s_respawns > s_respawnsBefore, null),
        (2.5f, "two causes in the same frame → one penalty", null, () =>
        {
            s_fellBefore = s_fell.Count;
            bool a = Hazards.Penalize(RunnerPenaltyCause.Trapped), b = Hazards.Penalize(RunnerPenaltyCause.ChaserContact);
            Check("one penalty for simultaneous causes", a && !b, $"first={a} second={b}");
            s_respawnsBefore = s_respawns;
        }),
        (0f, "wait for the respawn", () => s_respawns > s_respawnsBefore, () =>
        {
            Check("exactly one fall event for it", s_fell.Count == s_fellBefore + 1);
            var e = s_penalties.LastOrDefault();
            Check("penalty event carries cause + position + real loss", e.Cause == RunnerPenaltyCause.Trapped && e.Position != Vector3.zero && e.ScoreLost >= 0 && e.PickupsLost >= 0,
                  $"cause {e.Cause} pos {e.Position} lost {e.ScoreLost} pts / {e.PickupsLost} pickups");
        }),

        // ── Navigation: end the song → Fight → forced win → result ──
        (1f, "seek near the song end", null, () =>
        {
            Hazards.ClearGeneratedForTest();
            float clip = Manager.SongPlayEnd - 1.5f;
            Clock.StartPlayback(clip, MusicTimeDiscontinuityReason.Seek);
            Manager.ResyncCursors(Clock.SongTime);
        }),
        (0f, "wait for GameEnded → Fight", () => Flow == GameFlowState.Fight || FindEndContinue(), null),
        (0f, "wait for the fight", () => FightFlowController.Instance != null && (FightFlowController.Instance.CurrentState == FightFlowState.RoundIntro
                                         || FightFlowController.Instance.CurrentState == FightFlowState.Fighting), () => FightMatchController.Instance.DebugForceMatchResult(true)),
        (0f, "wait for the match result", () => FightFlowController.Instance.CurrentState == FightFlowState.MatchWon, null),
        (1.5f, "SELECT SONG", null, () =>
        {
            s_saveBefore = File.ReadAllText(PlayerProgressStore.FilePath);
            s_matchEndedBefore = s_matchEnded;
            Find<MatchResultView>().selectSongButton.onClick.Invoke();
        }),
        (0f, "in Song Selection", () => Flow == GameFlowState.SongSelection, null),
        (2f, "BACK", null, () => { s_selectionsBefore = s_opponentSelections; Find<SongSelectionView>().backButton.onClick.Invoke(); }),
        (0f, "back in Fight", () => Flow == GameFlowState.Fight, null),
        (3f, "the same result", null, () =>
        {
            var view = Find<MatchResultView>();
            Check("BACK returns to the match result", FightFlowController.Instance.CurrentState == FightFlowState.MatchWon && view.root.activeInHierarchy,
                  $"fight state {FightFlowController.Instance.CurrentState}, result visible {view.root.activeInHierarchy}");
            Check("result restored with its actions", view.selectSongButton.gameObject.activeInHierarchy && view.continueButton.gameObject.activeInHierarchy);
            Check("nothing re-run: no roulette, no new match end", s_opponentSelections == s_selectionsBefore && s_matchEnded == s_matchEndedBefore,
                  $"roulettes +{s_opponentSelections - s_selectionsBefore}, match ends +{s_matchEnded - s_matchEndedBefore}");
            Check("no reward / campaign change (save byte-identical)", File.ReadAllText(PlayerProgressStore.FilePath) == s_saveBefore);
            AppFlowController.Instance.RequestState(GameFlowState.MainMenu);
        }),
        (3f, "song selection from the Main Menu", null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (2f, "BACK", null, () => Find<SongSelectionView>().backButton.onClick.Invoke()),
        (1f, "main menu", null, () => Check("from the Main Menu, BACK goes to the Main Menu", Flow == GameFlowState.MainMenu, $"flow {Flow}")),
    };

    // ── Openings: capsule queries against the real colliders ───────────────────

    private static void CheckOpenings()
    {
        var c = Manager.Config.core;
        float stand = PlayerController.CapsuleHeight, crouch = c.crouchHeight;
        float apex = c.jumpForce * c.jumpForce / (2f * -c.gravity), lift = (stand - crouch) * (1f - c.airTuckHeadDrop);
        float ahead = 30f;
        foreach (RunnerObstacleKind kind in Enum.GetValues(typeof(RunnerObstacleKind)))
        {
            Hazards.ClearGeneratedForTest();
            var o = Hazards.SpawnTestObstacle(kind, ahead, 0f);
            Physics.SyncTransforms();
            if (o == null) { Check($"{kind}: built", false); continue; }
            var r = o.OpeningRect; float cx = r.x + r.width * 0.5f;
            switch (kind)
            {
                case RunnerObstacleKind.LowBlock:
                    Expect(o, kind, "standing into it", cx, 0.05f, stand, false);
                    Expect(o, kind, "jumping over it (feet 0.9 m)", cx, 0.9f, stand, true);
                    Expect(o, kind, "beside it", r.xMax + 0.7f, 0.05f, stand, true);
                    break;
                case RunnerObstacleKind.HighBlock:
                    Expect(o, kind, "over it at the jump apex", cx, apex, stand, false);
                    Expect(o, kind, "beside it", r.xMax + 0.7f, 0.05f, stand, true);
                    break;
                case RunnerObstacleKind.Portal:
                    Expect(o, kind, "crouched under the beam", cx, 0.05f, crouch, true);
                    Expect(o, kind, "standing", cx, 0.05f, stand, false);
                    Expect(o, kind, "jumping over the beam", cx, apex, stand, false);
                    break;
                case RunnerObstacleKind.WindowWall:
                    Expect(o, kind, "tucked through the window (jump + crouch)", cx, apex - 0.35f + lift, crouch, true);
                    Expect(o, kind, "tucked at the very apex", cx, apex + lift, crouch, true);
                    Expect(o, kind, "standing jump at the apex", cx, apex, stand, false);
                    Expect(o, kind, "on the ground", cx, 0.05f, stand, false);
                    break;
                case RunnerObstacleKind.CrawlWall:
                    Expect(o, kind, "crouched through the opening", cx, 0.05f, crouch, true);
                    Expect(o, kind, "standing", cx, 0.05f, stand, false);
                    Expect(o, kind, "crouched beside the opening", r.xMax + 0.6f, 0.05f, crouch, false);
                    break;
                case RunnerObstacleKind.UShape:
                    Expect(o, kind, "standing in the corridor", cx, 0.05f, stand, true);
                    Expect(o, kind, "into a side block", r.xMax + 0.6f, 0.05f, stand, false);
                    break;
            }
        }
        Hazards.ClearGeneratedForTest();
    }

    // Capsule of radius 0.45 (minus a 2 cm skin) with its feet at (x, feetY) in the obstacle's local frame, z = 0.
    private static void Expect(RunnerObstacle o, RunnerObstacleKind kind, string what, float x, float feetY, float height, bool shouldFit)
    {
        const float r = 0.43f;
        Vector3 a = o.transform.TransformPoint(new Vector3(x, feetY + r, 0f));
        Vector3 b = o.transform.TransformPoint(new Vector3(x, feetY + height - r, 0f));
        bool hit = false;
        foreach (var col in Physics.OverlapCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore))
            if (col.transform.IsChildOf(o.transform)) { hit = true; break; } // only the obstacle's own parts
        Check($"{kind}: {what} → {(shouldFit ? "passes" : "blocked")}", hit != shouldFit, $"x={x:F2} feet={feetY:F2} h={height:F2}");
    }

    private static float BlockLocalZ(RunnerObstacle o) => -o.transform.InverseTransformPoint(Player.transform.position).z;

    // ── Driver ─────────────────────────────────────────────────────────────────

    private static void TrackFrame()
    {
        var p = Player;
        if (p == null || Clock == null) return;
        if (s_block != null && s_block.gameObject.activeInHierarchy && !p.IsFalling)
        {
            // How far the capsule's front reaches past the block's front face (local −z face), only within its width.
            Vector3 local = s_block.transform.InverseTransformPoint(p.transform.position);
            float bw = s_block.BlockWidth(s_block.TrackWidth) * 0.5f;
            if (Mathf.Abs(local.x - s_block.Lateral) < bw)
                s_maxPenetration = Mathf.Max(s_maxPenetration, local.z + 0.45f - (-s_block.depth * 0.5f));
            if (p.IsBlocked && s_songAtBlock < 0f) s_songAtBlock = Clock.SongTime;
        }
        s_minOffset = Mathf.Min(s_minOffset, p.SongOffset);
        if (!p.IsBlocked && Time.deltaTime > 0f) s_maxRecoverStep = Mathf.Max(s_maxRecoverStep, (p.SongOffset - s_prevOffset) / Time.deltaTime);
        s_prevOffset = p.SongOffset;
        var ch = Hazards != null ? Hazards.Chaser : null;
        if (ch != null && ch.Current != RunnerChaser.State.Inactive)
        {
            if (s_chaserStates.Count == 0 || s_chaserStates[^1] != ch.Current) s_chaserStates.Add(ch.Current);
            bool activated = ch.Current == RunnerChaser.State.Warning || ch.Current == RunnerChaser.State.WaitingPass || ch.Current == RunnerChaser.State.Chasing;
            if (activated && s_chaserStates.Count > 1 && Time.deltaTime > 0f)
            {
                float v = (ch.transform.position - s_prevChaser).magnitude / Time.deltaTime;
                if (v > s_maxChaserStep) { s_maxChaserStep = v; s_maxChaserState = $"{ch.Current} Δ{ch.transform.position - s_prevChaser}"; }
            }
            s_prevChaser = ch.transform.position;
        }
    }

    // The Runner end screen's real CONTINUE button, once the run ended and the screen is up.
    private static bool s_continued;
    private static bool FindEndContinue()
    {
        if (s_continued || Manager == null || Manager.IsRunning) return false;
        var end = Find<EndScreenView>();
        if (end == null || !end.continueButton.gameObject.activeInHierarchy) return false;
        s_continued = true;
        end.continueButton.onClick.Invoke(); // the real end-screen CONTINUE (resumes time, banks the run)
        return false;
    }

    private static void Subscribe()
    {
        s_onFell = e => s_fell.Add(e);
        s_onPenalty = e => s_penalties.Add(e);
        s_onRespawn = e => { if (e.CheckpointIndex == -1) s_respawns++; };
        s_onMatchEnded = _ => s_matchEnded++;
        s_onFightFlow = e => { if (e.Current == FightFlowState.OpponentSelection) s_opponentSelections++; };
        EventBus.Subscribe(s_onFell); EventBus.Subscribe(s_onPenalty); EventBus.Subscribe(s_onRespawn);
        EventBus.Subscribe(s_onMatchEnded); EventBus.Subscribe(s_onFightFlow);
    }

    private static SongRowView FirstPlayableRow() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None).OrderBy(r => r.transform.GetSiblingIndex())
            .First(r => r.button.interactable && !r.Played);

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

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
                    if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); }
                };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_hardDeadline = now + 420; }
            if (now > s_hardDeadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "end")}) flow={Flow}"); Finish(2); return; }
            if (s_track) TrackFrame();
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow}, t={(Clock != null ? Clock.SongTime : 0f):F2}, offset={(Player != null ? Player.SongOffset : 0f):F2})");
            step.act?.Invoke();
            s_step++;
            s_stepStart = now;
        }
        catch (Exception ex) { Log("EXCEPTION " + ex); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string msg) { s_log.AppendLine(msg); Debug.Log("[HazardProbe] " + msg); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        if (s_onFell != null) { EventBus.Unsubscribe(s_onFell); EventBus.Unsubscribe(s_onPenalty); EventBus.Unsubscribe(s_onRespawn); EventBus.Unsubscribe(s_onMatchEnded); EventBus.Unsubscribe(s_onFightFlow); }
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors logged (exit {code})");
        try
        {
            string outPath = SessionState.GetString(KeyOut, Path.GetFullPath("Logs/HazardNavigationProbe.txt"));
            File.WriteAllText(outPath, s_log.ToString());
        }
        catch (Exception ex) { Debug.LogError("[HazardProbe] report: " + ex.Message); }
        EditorApplication.Exit(code);
    }
}
#endif
