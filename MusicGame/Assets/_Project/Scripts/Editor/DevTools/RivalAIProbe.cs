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
/// Play Mode probe of the rival's fighting skill in a REAL fight (the driver puts a COPY of the real save in place and
/// restores it afterwards). The "player" spams Punch from point-blank range (the strategy that used to win) against the
/// live AI at the first campaign difficulty (0.15) and at the last one (0.85), the same rival build both times, and
/// counts what happened: player hits that landed vs were guarded, rival hits on the player, and every Special the
/// rival threw with the player's state at that instant (an opening or not).
/// Report: Logs/RivalAIProbe.txt.  Batch: -executeMethod RivalAIProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class RivalAIProbe
{
    private const string KeyActive = "RivalAIProbe.Active";
    private const float SpamSeconds = 22f;
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked;

    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static FightFlowState FightState => FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.OpponentSelection;
    private static FighterActor Player => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.PlayerActor : null;
    private static FighterActor Rival => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.OpponentActor : null;
    private static FightCombatBalanceConfig Balance => Resources.Load<AppConfigSO>("AppConfig").combatBalance;
    private static bool Fighting => FightState == FightFlowState.Fighting && Player != null;

    private class Tally { public int landed, guarded, rivalHits, specials, specialsOnOpening; public readonly List<string> specialNotes = new(); }
    private static readonly Tally s_easy = new(), s_hard = new();
    private static Tally s_cur;
    private static float s_spamUntil, s_nextPress;

    static RivalAIProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (4f, "boot → song selection", () => Flow != GameFlowState.Boot && AppFlowController.Instance != null,
            () => { Time.timeScale = 1f; AppFlowController.Instance.RequestState(GameFlowState.SongSelection); }),
        (2f, "→ fight", () => Flow == GameFlowState.SongSelection, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
        (1f, "fighting", () => Fighting, () =>
        {
            EventBus.Subscribe<HitLandedEvent>(e =>
            {
                if (s_cur == null) return;
                if (e.Attacker == Player) s_cur.landed++; else if (e.Attacker == Rival) s_cur.rivalHits++;
            });
            EventBus.Subscribe<HitBlockedEvent>(e => { if (s_cur != null && e.Attacker == Player) s_cur.guarded++; });
            EventBus.Subscribe<SignatureExecutedEvent>(e =>
            {
                if (s_cur == null || e.Fighter != Rival || !e.Enhanced) return;
                var mc = Player.MoveController;
                bool opening = (mc != null && mc.CurrentPhase == FighterMoveState.Recovery) || (Player.HitReaction != null && (Player.HitReaction.IsInHitStun || Player.HitReaction.IsInBlockStun))
                               || Player.Posture == FighterPosture.Airborne || Vector3.Distance(Player.transform.position, Rival.transform.position) >= 2f;
                s_cur.specials++;
                if (opening) s_cur.specialsOnOpening++;
                s_cur.specialNotes.Add($"{(opening ? "opening" : "no opening")} (player {mc?.CurrentPhase}, dist {Vector3.Distance(Player.transform.position, Rival.transform.position):0.0})");
            });
        }),
        (0.5f, "EASY rival (difficulty 0.15): punch spam", () => Fighting, () => Begin(0.15f, s_easy)),
        (0f, "  spam done", () => !Spamming(), () => End(s_easy, "easy")),
        (1f, "HARD rival (difficulty 0.85): punch spam", () => Fighting, () => Begin(0.85f, s_hard)),
        (0f, "  spam done", () => !Spamming(), () => End(s_hard, "hard")),
        (0f, "report", null, () =>
        {
            float Ratio(Tally t) => t.landed + t.guarded > 0 ? t.guarded / (float)(t.landed + t.guarded) : 0f;
            Check("hard rival stops a punch spam: guards at least 40 % of the punches", Ratio(s_hard) >= 0.4f, $"{s_hard.guarded} guarded / {s_hard.landed} landed");
            Check("the easy rival guards clearly less than the hard one", Ratio(s_easy) + 0.15f <= Ratio(s_hard), $"easy {Ratio(s_easy):P0} vs hard {Ratio(s_hard):P0}");
            Check("the hard rival hits back (punishes) more than the easy one", s_hard.rivalHits > s_easy.rivalHits, $"rival hits easy {s_easy.rivalHits}, hard {s_hard.rivalHits}");
            Check("Specials are thrown on openings, not on a timer", s_hard.specials + s_easy.specials == 0 ||
                  (s_hard.specialsOnOpening + s_easy.specialsOnOpening) >= 0.75f * (s_hard.specials + s_easy.specials),
                  $"easy: {string.Join("; ", s_easy.specialNotes)} | hard: {string.Join("; ", s_hard.specialNotes)}");
        }),
    };

    private static bool Spamming()
    {
        if (Time.time >= s_spamUntil || !Fighting) { FightTouchInputState.Horizontal = 0f; return false; }
        // Keep the fight going (nobody KO'd) and stay point-blank: the strategy that used to win.
        foreach (var a in new[] { Player, Rival })
            if (a.Health.CurrentHealth < a.Health.MaxHealth * 0.5f) a.Health.GetType().GetProperty("CurrentHealth").SetValue(a.Health, a.Health.MaxHealth);
        float dist = Vector3.Distance(Player.transform.position, Rival.transform.position);
        FightTouchInputState.Horizontal = dist > 1.3f ? (Player.FacingRight ? 1f : -1f) : 0f;
        if (Time.time >= s_nextPress) { FightTouchInputState.PunchRequested = true; s_nextPress = Time.time + 0.18f; }
        return true;
    }

    private static void Begin(float difficulty, Tally tally)
    {
        var tuning = Balance.RivalTuningAt(difficulty);
        Rival.SetBuildStats(Balance.RivalBuild(FighterBuildStats.Even(80f), tuning.Budget));
        Rival.AI.SetProfile(Balance.RivalAIProfile(difficulty, null));
        Rival.AI.SetSpecialPacing(tuning.FirstSpecialDelay, tuning.SpecialCooldown);
        var w = Rival.CombatResources;
        w.TryConsume(CombatResourceType.Special, w.Specials);
        w.Add(CombatResourceType.Special, 2);
        FightMatchController.Instance.DebugSetRoundTimeRemaining(120f);
        s_cur = tally;
        s_spamUntil = Time.time + SpamSeconds;
        s_nextPress = Time.time;
    }

    private static void End(Tally t, string label)
    {
        s_cur = null;
        Log($"   {label}: player punches landed {t.landed}, guarded {t.guarded}; rival hits on the player {t.rivalHits}; rival Specials {t.specials} ({t.specialsOnOpening} on an opening)");
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
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 300; }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} flow={Flow} fight={FightState}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (fight={FightState})");
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

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[RivalAIProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        FightTouchInputState.Horizontal = 0f;
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/RivalAIProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
