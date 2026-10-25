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
/// Play Mode comparison of the runner build's REAL effect in a fight (Main Menu → Song Selection → Fight; the driver
/// puts a COPY of the real save in place and restores it afterwards). For each player build, against the same rival at
/// the same level: the damage of a real landed normal punch and normal kick (HitLandedEvent — the combat pipeline),
/// the real walking speed (FighterMovement.ForwardSpeed while holding forward) and the attack start-up (Agility).
/// Then the same player build against the rival at level 1 and level 4. The "before" columns re-evaluate the
/// previous curves (copied below) with the same formula, so the two can be compared side by side.
/// Also: the HUD rows show the build combat uses; the build survives a round change.
/// Report + table: Logs/FightBalanceProbe.txt.  Batch: -executeMethod FightBalanceProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class FightBalanceProbe
{
    private const string KeyActive = "FightBalanceProbe.Active";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked;

    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static FightFlowState FightState => FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.OpponentSelection;
    private static FighterActor Player => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.PlayerActor : null;
    private static FighterActor Rival => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.OpponentActor : null;
    private static FighterMoveController MC(FighterActor a) => a != null ? a.GetComponentInChildren<FighterMoveController>() : null;
    private static AppConfigSO App => Resources.Load<AppConfigSO>("AppConfig");
    private static FightCombatBalanceConfig Balance => App.combatBalance;
    private static FightMoveSetSO MoveSet => App.fightFlow.defaultPlayerCombatProfile.moveSet;
    private static bool Fighting => FightState == FightFlowState.Fighting && Player != null;
    private static bool Idle(FighterActor a) => MC(a) != null && MC(a).CurrentMove == null && MC(a).QueuedMove == null;

    // ── Cases ────────────────────────────────────────────────────────────────
    private struct Case { public string name; public FighterBuildStats build; public int rivalLevel; }
    private static FighterBuildStats B(float p, float k, float a, float r, float i) =>
        new() { punchPower = p, kickPower = k, agility = a, resistance = r, impactPower = i };

    private static readonly Case[] Cases =
    {
        new() { name = "Nul·la (0 bonus)",        build = B(0, 0, 0, 0, 0),     rivalLevel = 1 },
        new() { name = "Baixa (total 20)",        build = B(4, 4, 4, 4, 4),     rivalLevel = 1 },
        new() { name = "Mitjana (total 50)",      build = B(10, 10, 10, 10, 10), rivalLevel = 1 },
        new() { name = "Alta (total 90)",         build = B(18, 18, 18, 18, 18), rivalLevel = 1 },
        new() { name = "Molt Kick, poc puny",     build = B(4, 30, 8, 8, 6),    rivalLevel = 1 },
        new() { name = "Molt puny, poc Kick",     build = B(30, 4, 8, 8, 6),    rivalLevel = 1 },
        new() { name = "Velocitat baixa",         build = B(10, 10, 2, 10, 10), rivalLevel = 1 },
        new() { name = "Velocitat alta",          build = B(10, 10, 30, 10, 10), rivalLevel = 1 },
        new() { name = "Mitjana vs rival nv.1",   build = B(10, 10, 10, 10, 10), rivalLevel = 1 },
        new() { name = "Mitjana vs rival nv.4",   build = B(10, 10, 10, 10, 10), rivalLevel = 4 },
    };

    private struct Row
    {
        public string name;
        public float punch, kick, speed, startup, rivalPunch, rivalSpeed;
        public float oldPunch, oldKick, oldSpeed, oldStartup, oldRivalPunch;
        public bool punchLanded, kickLanded;
    }
    private static readonly List<Row> s_rows = new();
    private static Row s_cur;
    private static int s_case;
    private static float s_lastDamage = -1f;
    private static FighterActor s_lastAttacker;

    static FightBalanceProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static List<(float wait, string name, Func<bool> until, Action act)> s_steps;

    private static List<(float, string, Func<bool>, Action)> BuildSteps()
    {
        var steps = new List<(float, string, Func<bool>, Action)>
        {
            // Past the intro (or straight out of it after a few seconds: its timed sequence is not what is measured here).
            (4f, "boot → song selection", () => Flow != GameFlowState.Boot && AppFlowController.Instance != null,
                () => { Time.timeScale = 1f; AppFlowController.Instance.RequestState(GameFlowState.SongSelection); }),
            (2f, "→ fight", () => Flow == GameFlowState.SongSelection, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
            (1f, "fighting", () => Fighting, () =>
            {
                foreach (var ai in Rival.GetComponentsInChildren<FighterAI>(true)) ai.enabled = false;
                EventBus.Subscribe<HitLandedEvent>(e => { s_lastAttacker = e.Attacker; s_lastDamage = e.Result.FinalDamage; });
            }),
        };
        for (int i = 0; i < Cases.Length; i++)
        {
            int idx = i;
            steps.Add((0.3f, $"case {Cases[idx].name}: set builds", () => Fighting && Idle(Player) && Idle(Rival), () => SetCase(idx)));
            steps.Add((0.3f, "  punch", null, () => Strike(Player, MoveSet.normalPunch)));
            steps.Add((0.9f, "  punch measured", null, () =>
            {
                float formula = Resolve(Player, Rival, MoveSet.normalPunch);
                s_cur.punchLanded = Landed(Player, out s_cur.punch);
                if (!s_cur.punchLanded) s_cur.punch = formula;
                else if (!Mathf.Approximately(s_cur.punch, formula)) s_mismatch.Add($"{s_cur.name}: landed {s_cur.punch:0.00} vs {formula:0.00}");
            }));
            steps.Add((0.2f, "  kick", () => Idle(Player), () => { Place(1.15f); Strike(Player, MoveSet.normalKick); }));
            steps.Add((0.9f, "  kick measured", null, () => { s_cur.kickLanded = Landed(Player, out s_cur.kick); if (!s_cur.kickLanded) s_cur.kick = Resolve(Player, Rival, MoveSet.normalKick); }));
            steps.Add((0.2f, "  rival punch", () => Idle(Player) && Idle(Rival), () => { Place(1.15f); Strike(Rival, MoveSet.normalPunch); }));
            steps.Add((0.9f, "  rival punch measured", null, () => { if (!Landed(Rival, out s_cur.rivalPunch)) s_cur.rivalPunch = Resolve(Rival, Player, MoveSet.normalPunch); }));
            // The horizontal axis walks towards / away from the rival (facing-relative); pick the sign that walks forward.
            steps.Add((0.2f, "  walk", () => Idle(Player), () => { Place(7f); FightTouchInputState.Horizontal = 1f; }));
            steps.Add((0.3f, "  walk direction", null, () => { if (Player.Movement.ForwardSpeed < 0f) FightTouchInputState.Horizontal = -1f; }));
            steps.Add((1.0f, "  walk measured", null, () =>
            {
                FightTouchInputState.Horizontal = 0f;
                s_cur.speed = Player.Movement.ForwardSpeed;
                FightTouchInputState.Vertical = 0f;
                s_cur.rivalSpeed = BaseSpeed * Rival.Movement.SpeedMultiplier;
                s_rows.Add(s_cur);
                if (idx == 2) CheckHud();
            }));
        }
        steps.Add((0.3f, "round change keeps the build", () => Fighting && Idle(Player), () =>
        {
            SetCase(3);
            s_build = Player.BuildStats; s_round = FightMatchController.Instance.CurrentRound;
            FightMatchController.Instance.DebugSetRoundTimeRemaining(0.05f);
        }));
        steps.Add((0f, "next round", () => FightMatchController.Instance.CurrentRound > s_round && Fighting, () =>
            Check("build untouched by the round change (no reset / overwrite)", Same(Player.BuildStats, s_build), $"{Player.BuildStats}")));
        steps.Add((0f, "report", null, Report));
        return steps;
    }

    private static FighterBuildStats s_build;
    private static readonly List<string> s_mismatch = new();
    private static int s_round;
    private static float BaseSpeed => App.arena != null ? App.arena.baseMovementSpeed : 4f;

    private static void SetCase(int idx)
    {
        var c = Cases[idx];
        s_case = idx;
        Player.SetBuildStats(c.build);
        var profile = FighterBuildStats.Even(80f);   // the shared FighterStatsProfile_Default every rival level uses (16 each)
        Rival.SetBuildStats(Balance.RivalBuild(profile, c.rivalLevel));
        Rival.Health.GetType().GetProperty("CurrentHealth").SetValue(Rival.Health, Rival.Health.MaxHealth);
        Player.Health.GetType().GetProperty("CurrentHealth").SetValue(Player.Health, Player.Health.MaxHealth);
        Place(1.15f);
        s_cur = new Row { name = c.name };
        var rivalOld = FighterBuildStats.Even(80f);   // before: every rival level used the same shared 16-each profile
        s_cur.oldKick = OldDamage(c.build.kickPower) * OldTaken(rivalOld.resistance) * Base(MoveSet.normalKick);
        s_cur.oldPunch = OldDamage(c.build.punchPower) * OldTaken(rivalOld.resistance) * Base(MoveSet.normalPunch);
        s_cur.oldRivalPunch = OldDamage(rivalOld.punchPower) * OldTaken(c.build.resistance) * Base(MoveSet.normalPunch);
        s_cur.oldSpeed = BaseSpeed;                                       // before: movement ignored every stat
        s_cur.oldStartup = MoveSet.normalPunch.startupDuration * Mathf.Clamp(OldTiming(c.build.agility), 0.7f, 1.15f);
        s_cur.startup = MoveSet.normalPunch.startupDuration * Balance.ComputeModifiers(c.build).TimingScale;
    }

    private static float Base(FightMoveDefinition m) => m.hits != null && m.hits.Length > 0 ? m.hits[0].baseDamage : 0f;

    // The previous curves (FightCombatBalanceConfig before the 2026-10-25 rebalance), for the "before" columns.
    private static readonly AnimationCurve s_oldDamage = new(new Keyframe(0f, 0.85f), new Keyframe(16f, 1.00f), new Keyframe(40f, 1.25f));
    private static readonly AnimationCurve s_oldTaken = new(new Keyframe(0f, 1.10f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.85f));
    private static readonly AnimationCurve s_oldTiming = new(new Keyframe(0f, 1.08f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.88f));
    private static float OldDamage(float v) => s_oldDamage.Evaluate(v);
    private static float OldTaken(float v) => s_oldTaken.Evaluate(v);
    private static float OldTiming(float v) => s_oldTiming.Evaluate(v);

    // Rival right in front of the player (or far, for the walk), both facing each other.
    private static void Place(float distance)
    {
        var p = Player.transform.position;
        var fwd = Player.ForwardXZ.sqrMagnitude > 0.01f ? Player.ForwardXZ.normalized : Vector3.right;
        Rival.transform.position = new Vector3(p.x + fwd.x * distance, Rival.transform.position.y, p.z + fwd.z * distance);
    }

    private static void Strike(FighterActor a, FightMoveDefinition move)
    {
        s_lastDamage = -1f; s_lastAttacker = null;
        MC(a).TryExecuteMove(move);
    }

    private static bool Landed(FighterActor attacker, out float damage)
    {
        damage = s_lastAttacker == attacker ? s_lastDamage : 0f;
        return s_lastAttacker == attacker && s_lastDamage >= 0f;
    }

    private static float Resolve(FighterActor a, FighterActor d, FightMoveDefinition m) =>
        FightHitResolver.Resolve(a, d, m, m.hits[0], Balance, false).FinalDamage;

    private static bool Same(FighterBuildStats x, FighterBuildStats y) =>
        Mathf.Approximately(x.punchPower, y.punchPower) && Mathf.Approximately(x.kickPower, y.kickPower) && Mathf.Approximately(x.agility, y.agility)
        && Mathf.Approximately(x.resistance, y.resistance) && Mathf.Approximately(x.impactPower, y.impactPower);

    private static void CheckHud()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<FightHudView>()?.buildPanel;
        if (panel == null) { Check("HUD rows = the build combat uses", false, "no panel"); return; }
        var b = Player.BuildStats;
        var wrong = panel.stats.Where(r => r.value.text != Mathf.RoundToInt(Value(b, r.stat)).ToString()).Select(r => $"{r.stat} shows {r.value.text}").ToList();
        Check("HUD rows show the exact build combat uses", wrong.Count == 0, string.Join(", ", wrong));
    }

    private static float Value(FighterBuildStats b, FightStatId s) => s switch
    {
        FightStatId.PunchPower => b.punchPower, FightStatId.KickPower => b.kickPower, FightStatId.Agility => b.agility,
        FightStatId.Resistance => b.resistance, FightStatId.ImpactPower => b.impactPower, _ => 0f,
    };

    private static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("| Cas | Puny abans → ara | Puntada abans → ara | Cops de puny per KO (ara) | Puny del rival rebut abans → ara | Caminar m/s abans → ara | Arrencada puny s abans → ara | Rival m/s |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in s_rows)
            sb.AppendLine($"| {r.name} | {r.oldPunch:0.0} → {r.punch:0.0}{(r.punchLanded ? "" : "*")} | {r.oldKick:0.0} → {r.kick:0.0}{(r.kickLanded ? "" : "*")} | " +
                          $"{(r.punch > 0 ? Mathf.CeilToInt(100f / r.punch) : 0)} | {r.oldRivalPunch:0.0} → {r.rivalPunch:0.0} | {r.oldSpeed:0.00} → {r.speed:0.00} | " +
                          $"{r.oldStartup:0.000} → {r.startup:0.000} | {r.rivalSpeed:0.00} |");
        sb.AppendLine("(* = no hit landed in the probe: value from the same resolver formula)");
        Log(sb.ToString());

        Row R(string n) => s_rows.First(x => x.name == n);
        var nul = R("Nul·la (0 bonus)"); var low = R("Baixa (total 20)"); var mid = R("Mitjana (total 50)"); var high = R("Alta (total 90)");
        Check("real landed punches = the balance formula on the HUD build (nothing fixed or overridden in between)",
              s_rows.Any(x => x.punchLanded) && s_mismatch.Count == 0, $"{s_rows.Count(x => x.punchLanded)}/{s_rows.Count} punches landed; {string.Join("; ", s_mismatch)}");
        Check("no bonuses: very weak but functional (punch > 0, still walks)", nul.punch > 0f && nul.punch < 3f && nul.speed > 1.5f, $"punch {nul.punch:0.0}, walk {nul.speed:0.00}");
        Check("high vs low build: clear advantage (punch ≥ 2.2×, walk ≥ 1.3×)", high.punch >= 2.2f * low.punch && high.speed >= 1.3f * low.speed, $"{low.punch:0.0}→{high.punch:0.0}, {low.speed:0.00}→{high.speed:0.00}");
        Check("low < mid < high on punch, kick and speed", low.punch < mid.punch && mid.punch < high.punch && low.kick < mid.kick && mid.kick < high.kick && low.speed < mid.speed && mid.speed < high.speed);
        var kk = R("Molt Kick, poc puny"); var pp = R("Molt puny, poc Kick");
        Check("kick specialist: strong kicks, weak punches; punch specialist the reverse", kk.kick > 2f * pp.kick && pp.punch > 2f * kk.punch, $"kick {kk.kick:0.0} vs {pp.kick:0.0}, punch {kk.punch:0.0} vs {pp.punch:0.0}");
        var slow = R("Velocitat baixa"); var fast = R("Velocitat alta");
        Check("Agility: movement speed and attack start-up clearly apart; damage unchanged", fast.speed >= 1.6f * slow.speed && fast.startup < slow.startup && Mathf.Approximately(fast.punch, slow.punch),
              $"walk {slow.speed:0.00}→{fast.speed:0.00}, start-up {slow.startup:0.000}→{fast.startup:0.000}");
        Check("fastest build stays controllable (≤ 1.35× base walk)", s_rows.Max(x => x.speed) <= BaseSpeed * 1.35f + 0.05f, $"max {s_rows.Max(x => x.speed):0.00} m/s");
        var r1 = R("Mitjana vs rival nv.1"); var r4 = R("Mitjana vs rival nv.4");
        Check("rival level 4 vs 1, same player build: rival hits harder, moves faster, takes less", r4.rivalPunch > 1.5f * r1.rivalPunch && r4.rivalSpeed > r1.rivalSpeed && r4.punch < r1.punch,
              $"rival punch {r1.rivalPunch:0.0}→{r4.rivalPunch:0.0}, rival walk {r1.rivalSpeed:0.00}→{r4.rivalSpeed:0.00}, player punch {r1.punch:0.0}→{r4.punch:0.0}");
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
            s_steps ??= BuildSteps().Select(x => (x.Item1, x.Item2, x.Item3, x.Item4)).ToList();
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 300; }
            if (Fighting && Rival != null) // nobody gets KO'd mid-measure
                foreach (var a in new[] { Player, Rival })
                    if (a.Health.CurrentHealth < a.Health.MaxHealth * 0.5f) a.Health.GetType().GetProperty("CurrentHealth").SetValue(a.Health, a.Health.MaxHealth);
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} flow={Flow} fight={FightState}"); Finish(2); return; }
            if (s_step >= s_steps.Count) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = s_steps[s_step];
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

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[BalanceProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        FightTouchInputState.Vertical = 0f;
        FightTouchInputState.Horizontal = 0f;
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/FightBalanceProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
