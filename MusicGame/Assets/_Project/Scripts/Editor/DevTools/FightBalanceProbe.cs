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
/// Play Mode probe of the four-attribute build in a REAL fight (Main Menu → Song Selection → Fight; the driver puts a
/// COPY of the real save in place and restores it afterwards). Every player build comes from COLLECTED BONUS COUNTS
/// through the real converter (MusicCombatBuildConverter + FightStatsConfig); counts are realistic for the songs' real
/// offer (BonusDensityReport). Against the same rival at the same level the probe measures in the running fight: the
/// damage of a real landed normal punch / normal kick (HitLandedEvent), the damage the player takes from the rival's
/// same punch, the real walking speed (FighterMovement.ForwardSpeed while holding forward), the hit stun and the real
/// push-back distance a punch / kick causes. Then the same build against the rival at level 1 and 4.
/// Persistence: build + charges survive a round change and Fight Again (spent charges never restored); a new run
/// discards the build and charges and keeps the lives. HUD = exactly the four attributes combat uses.
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
    private static FightStatsConfig Stats => App.fightStats;
    private static FightMoveSetSO MoveSet => App.fightFlow.defaultPlayerCombatProfile.moveSet;
    private static float BaseSpeed => App.arena != null ? App.arena.baseMovementSpeed : 4f;
    private static bool Fighting => FightState == FightFlowState.Fighting && Player != null;
    private static bool Idle(FighterActor a) => MC(a) != null && MC(a).CurrentMove == null && MC(a).QueuedMove == null;

    // ── Cases: collected bonus counts in one run (Kick, Snare, HiHat, Beat, Onset, Impact) → the real converter.
    // An average song offers ≈ Kick 80, Snare 65, HiHat 12, Onset 30 (Beat / Impact 0 today) in its played minute.
    private struct Case { public string name; public int k, s, h, b, o, i; public int rivalLevel; }
    private static readonly Case[] Cases =
    {
        new() { name = "Mínima (0 bonus)",         rivalLevel = 1 },
        new() { name = "Baixa (~25 %)",            k = 20, s = 16, h = 3,  o = 7,  rivalLevel = 1 },
        new() { name = "Intermèdia (~50 %)",       k = 40, s = 32, h = 6,  o = 15, rivalLevel = 1 },
        new() { name = "Alta (~80 %)",             k = 64, s = 52, h = 10, o = 24, rivalLevel = 1 },
        new() { name = "Especialista puny",        k = 10, s = 90, h = 6,  o = 15, rivalLevel = 1 },
        new() { name = "Especialista patada",      k = 95, s = 10, h = 6,  o = 15, rivalLevel = 1 },
        new() { name = "Agilitat baixa",           k = 40, s = 32, h = 0,  o = 0,  rivalLevel = 1 },
        new() { name = "Agilitat alta",            k = 40, s = 32, h = 30, o = 0,  rivalLevel = 1 },
        new() { name = "Resistència baixa",        k = 40, s = 32, h = 6,  o = 0,  rivalLevel = 1 },
        new() { name = "Resistència alta (Beat)",  k = 40, s = 32, h = 6,  b = 60, o = 30, rivalLevel = 1 },
        new() { name = "Amb Impact (×6)",          k = 40, s = 32, h = 6,  o = 15, i = 6, rivalLevel = 1 },
        new() { name = "Intermèdia vs rival nv.1", k = 40, s = 32, h = 6,  o = 15, rivalLevel = 1 },
        new() { name = "Intermèdia vs rival nv.4", k = 40, s = 32, h = 6,  o = 15, rivalLevel = 4 },
    };

    private struct Row
    {
        public string name;
        public FighterBuildStats build;
        public float punch, kick, taken, speed, punchStun, kickStun, punchPush, kickPush, rivalSpeed, startup;
        public bool punchLanded, kickLanded;
    }
    private static readonly List<Row> s_rows = new();
    private static Row s_cur;
    private static float s_lastDamage = -1f, s_lastStun;
    private static FighterActor s_lastAttacker;
    private static Vector3 s_rivalBefore;
    private static readonly List<string> s_mismatch = new();

    static FightBalanceProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static FighterBuildStats BuildFor(Case c)
    {
        var perf = new GamePerformance();
        void Add(RingType t, int n) { if (n > 0) perf.ByType[t] = new TypePerformance { Type = t, Available = n * 2, Collected = n, CollectionRate = 0.5f, TimingAccuracy = 0.9f }; }
        Add(RingType.Kick, c.k); Add(RingType.Snare, c.s); Add(RingType.HiHat, c.h); Add(RingType.Beat, c.b); Add(RingType.Onset, c.o); Add(RingType.Impact, c.i);
        return FighterBuildStats.FromCombatBuild(MusicCombatBuildConverter.Build(MusicalPerformanceBuilder.Build(perf, Stats.performanceTimingInfluence), Stats));
    }

    private static List<(float wait, string name, Func<bool> until, Action act)> s_steps;

    private static List<(float, string, Func<bool>, Action)> BuildSteps()
    {
        var steps = new List<(float, string, Func<bool>, Action)>
        {
            (4f, "boot → song selection", () => Flow != GameFlowState.Boot && AppFlowController.Instance != null,
                () => { Time.timeScale = 1f; AppFlowController.Instance.RequestState(GameFlowState.SongSelection); }),
            (2f, "→ fight", () => Flow == GameFlowState.SongSelection, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
            (1f, "fighting", () => Fighting, () =>
            {
                FreezeRival();
                EventBus.Subscribe<HitLandedEvent>(e => { s_lastAttacker = e.Attacker; s_lastDamage = e.Result.FinalDamage; s_lastStun = e.Result.FinalHitStun; });
                EventBus.Subscribe<HitEvadedEvent>(e => { if (e.Defender == Player) s_evaded = true; });
            }),
        };
        for (int idx = 0; idx < Cases.Length; idx++)
        {
            int n = idx;
            steps.Add((0.3f, $"case {Cases[n].name}", () => Fighting && Idle(Player) && Idle(Rival), () => SetCase(n)));
            steps.Add((0.2f, "  punch", null, () =>
            {
                Strike(Player, MoveSet.normalPunch);
                s_cur.startup = MoveSet.normalPunch.startupDuration / MC(Player).AttackSpeed;   // real seconds to the hit
            }));
            steps.Add((1.0f, "  punch measured", null, () => Measure(MoveSet.normalPunch, ref s_cur.punch, ref s_cur.punchStun, ref s_cur.punchPush, out s_cur.punchLanded)));
            steps.Add((0.3f, "  kick", () => Idle(Player) && Idle(Rival), () => { Place(1.15f); Strike(Player, MoveSet.normalKick); }));
            steps.Add((1.0f, "  kick measured", null, () => Measure(MoveSet.normalKick, ref s_cur.kick, ref s_cur.kickStun, ref s_cur.kickPush, out s_cur.kickLanded)));
            steps.Add((0.3f, "  rival punch", () => Idle(Player) && Idle(Rival), () => { Place(1.15f); Strike(Rival, MoveSet.normalPunch); }));
            steps.Add((1.0f, "  rival punch measured", null, () => { if (!Landed(Rival, out s_cur.taken)) s_cur.taken = Resolve(Rival, Player, MoveSet.normalPunch).FinalDamage; }));
            // The horizontal axis walks towards / away from the rival (facing-relative); pick the sign that walks forward.
            steps.Add((0.3f, "  walk", () => Idle(Player) && Idle(Rival), () => { Place(7f); FightTouchInputState.Horizontal = 1f; }));
            steps.Add((0.3f, "  walk direction", null, () => { if (Player.Movement.ForwardSpeed < 0f) FightTouchInputState.Horizontal = -1f; s_forwardSign = FightTouchInputState.Horizontal; }));
            steps.Add((1.0f, "  walk measured", null, () =>
            {
                FightTouchInputState.Horizontal = 0f;
                s_cur.speed = Player.Movement.ForwardSpeed;
                s_cur.rivalSpeed = BaseSpeed * Rival.Movement.SpeedMultiplier;
                s_rows.Add(s_cur);
                if (n == 2) CheckHud();
            }));
        }
        // ── The Special: always 30 % of the defender's max health, unblockable, dodgeable — both directions ──
        steps.Add((0.3f, "special: rival → player (no guard)", () => Fighting && Idle(Player) && Idle(Rival), () => { SetCase(3); RivalSpecial(); }));
        steps.Add((1.4f, "  measured", null, () => s_specialHit = Lost(Player)));
        steps.Add((0.3f, "special: rival → player guarding", () => Idle(Player) && Idle(Rival), () => { SetCase(3); RivalSpecial(); FightTouchInputState.Horizontal = -s_forwardSign; }));
        steps.Add((0.35f, "  guard up before the impact?", null, () => s_guardSeen = Player.Guard != null && Player.Guard.State != FighterGuardState.None));
        steps.Add((1.05f, "  measured", null, () => { FightTouchInputState.Horizontal = 0f; s_specialGuarded = Lost(Player); }));
        steps.Add((0.3f, "special: rival → player dodging", () => Idle(Player) && Idle(Rival), () => { SetCase(3); s_evaded = false; RivalSpecial(); }));
        steps.Add((0.3f, "  dodge", null, () => MC(Player).TryExecuteMove(DodgeMove)));
        steps.Add((1.2f, "  measured", null, () => s_specialDodged = Lost(Player)));
        steps.Add((0.3f, "special: player → rival", () => Idle(Player) && Idle(Rival), () =>
        {
            SetCase(0);   // the weakest build: the Special must not care
            Player.CombatResources.DebugGrant(0, 0, 1);
            Place(3f); s_healthBefore = Rival.Health.CurrentHealth;
            MC(Player).TryExecuteSignature();
        }));
        steps.Add((1.4f, "  measured", null, () =>
        {
            float lost = s_healthBefore - Rival.Health.CurrentHealth;
            float max = Rival.Health.MaxHealth;
            Check("Special rival → player: exactly 30 % of the player's max health", Mathf.Abs(s_specialHit - 0.3f * Player.Health.MaxHealth) < 0.01f, $"{s_specialHit:0.0} of {Player.Health.MaxHealth}");
            Check("Special vs guard: guard does NOT stop it (same 30 %)", s_guardSeen && Mathf.Abs(s_specialGuarded - 0.3f * Player.Health.MaxHealth) < 0.01f,
                  $"{s_specialGuarded:0.0}, guard state seen {s_guardSeen}");
            Check("Special vs dodge: dodging avoids it entirely", s_evaded && s_specialDodged < 0.01f, $"lost {s_specialDodged:0.0}, evaded {s_evaded}");
            Check("Special player → rival (weakest build): exactly 30 % of the rival's max health", Mathf.Abs(lost - 0.3f * max) < 0.01f, $"{lost:0.0} of {max}");
        }));
        steps.Add((0f, "report", null, Report));   // the table first: the persistence steps below end rounds / the match
        // ── Persistence ──
        steps.Add((0.5f, "the run's build + charges", () => Fighting && Idle(Player), () =>
        {
            var results = GameSession.Instance.RunnerResults;
            s_build = results?.CombatBuild != null ? FighterBuildStats.FromCombatBuild(results.CombatBuild) : BuildFor(Cases[2]);
            Player.SetBuildStats(s_build);
            var w = Player.CombatResources;
            w.DebugGrant(2, 1, 2);
            w.TryConsume(CombatResourceType.Special, 1);   // spent before the round change: must stay spent
            s_wallet = Wallet();
            s_round = FightMatchController.Instance.CurrentRound;
            FightMatchController.Instance.DebugForceDraw();   // a drawn round: the match goes on to the next one
        }));
        steps.Add((0f, "next round", () => FightMatchController.Instance.CurrentRound > s_round && Fighting, () =>
        {
            Check("round change: build untouched (no reset / overwrite)", Same(Player.BuildStats, s_build), $"{Player.BuildStats}");
            Check("round change: charges kept, the spent one not restored", Wallet() == s_wallet, $"{Wallet()}");
            PlayerProgressService.Instance.AddLife(1, "probe");   // Fight Again needs a life (the save copy is restored afterwards)
            s_lives = PlayerProgressService.Instance.ExtraLives;
            FightMatchController.Instance.DebugForceMatchResult(false);
        }));
        steps.Add((2.5f, "match lost → Fight Again", () => FightState == FightFlowState.MatchLost, () =>
        {
            var view = UnityEngine.Object.FindObjectsByType<MatchResultView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            view?.fightAgainButton.onClick.Invoke();
        }));
        steps.Add((0.5f, "next fight of the same run", () => Fighting && FightMatchController.Instance.CurrentRound == 1, () =>
        {
            FreezeRival();
            Check("Fight Again: the same run's build in the next fight", Same(Player.BuildStats, s_build), $"{Player.BuildStats}");
            Check("Fight Again: charges carried over exactly (spent stay spent, left-overs kept)", Wallet() == s_wallet, $"{Wallet()} vs {s_wallet}");
            Check("Fight Again used one life (the life rule is unchanged)", PlayerProgressService.Instance.ExtraLives == s_lives - 1, $"{PlayerProgressService.Instance.ExtraLives}");
            // A NEW RUN — GameSession's real GameStartedEvent handler: build + charges discarded, lives kept.
            int lives = PlayerProgressService.Instance.ExtraLives;
            var handler = typeof(GameSession).GetField("_onGameStarted", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(GameSession.Instance) as Action<GameStartedEvent>;
            handler?.Invoke(default);
            var run = GameSession.Instance.Run;
            Check("new run: previous build discarded, charges back to 0, lives kept",
                  handler != null && GameSession.Instance.RunnerResults == null && run.Wallet.Specials == 0 && run.Wallet.TripleCombos == 0 && run.Wallet.QuadCombos == 0
                  && PlayerProgressService.Instance.ExtraLives == lives, $"results {(GameSession.Instance.RunnerResults == null ? "discarded" : "kept")}, lives {lives}");
        }));
        return steps;
    }

    private static FighterBuildStats s_build;
    private static float s_forwardSign = 1f, s_healthBefore, s_specialHit, s_specialGuarded, s_specialDodged;
    private static bool s_evaded, s_guardSeen;
    private static FightMoveDefinition DodgeMove => AssetDatabase.LoadAssetAtPath<FightMoveDefinition>("Assets/_Project/Configs/Fight/Combat/Moves/Move_Dodge.asset");

    private static void RivalSpecial()
    {
        Place(3f);
        s_healthBefore = Player.Health.CurrentHealth;
        Rival.CombatResources.DebugGrant(0, 0, 1);
        MC(Rival).TryExecuteSignature();
    }

    private static float Lost(FighterActor a) => s_healthBefore - a.Health.CurrentHealth;
    private static (int, int, int) s_wallet;
    private static int s_round, s_lives;
    private static (int, int, int) Wallet() { var w = Player.CombatResources; return (w.Specials, w.TripleCombos, w.QuadCombos); }

    private static void FreezeRival() { foreach (var ai in Rival.GetComponentsInChildren<FighterAI>(true)) ai.enabled = false; }

    private static void SetCase(int idx)
    {
        var c = Cases[idx];
        var build = BuildFor(c);
        Player.SetBuildStats(build);
        var profile = FighterBuildStats.Even(80f);   // the shared default profile every rival level uses (even proportions)
        // Rival "level" 1 / 4 = the first / last campaign difficulty (0.15 / 0.85): its build size comes from the difficulty.
        float difficulty = c.rivalLevel >= 4 ? 0.85f : 0.15f;
        Rival.SetBuildStats(Balance.RivalBuild(profile, Balance.RivalTuningAt(difficulty).Budget));
        Heal(Rival); Heal(Player);
        FightMatchController.Instance.DebugSetRoundTimeRemaining(120f);   // no round ends by time while measuring
        Place(1.15f);
        s_cur = new Row { name = c.name, build = build };
    }

    private static void Measure(FightMoveDefinition move, ref float damage, ref float stun, ref float push, out bool landed)
    {
        var formula = Resolve(Player, Rival, move);
        landed = Landed(Player, out damage);
        if (!landed) { damage = formula.FinalDamage; stun = formula.FinalHitStun; push = -1f; return; }
        if (!Mathf.Approximately(damage, formula.FinalDamage)) s_mismatch.Add($"{s_cur.name} {move.debugName}: landed {damage:0.00} vs {formula.FinalDamage:0.00}");
        stun = s_lastStun;
        var after = Rival.transform.position;
        push = Vector3.Distance(new Vector3(after.x, 0f, after.z), new Vector3(s_rivalBefore.x, 0f, s_rivalBefore.z));
    }

    private static void Heal(FighterActor a) => a.Health.GetType().GetProperty("CurrentHealth").SetValue(a.Health, a.Health.MaxHealth);

    private static void Place(float distance)
    {
        var p = Player.transform.position;
        var fwd = Player.ForwardXZ.sqrMagnitude > 0.01f ? Player.ForwardXZ.normalized : Vector3.right;
        Rival.transform.position = new Vector3(p.x + fwd.x * distance, Rival.transform.position.y, p.z + fwd.z * distance);
    }

    private static void Strike(FighterActor a, FightMoveDefinition move)
    {
        s_lastDamage = -1f; s_lastAttacker = null;
        s_rivalBefore = Rival.transform.position;
        MC(a).TryExecuteMove(move);
    }

    private static bool Landed(FighterActor attacker, out float damage)
    {
        damage = s_lastAttacker == attacker ? s_lastDamage : 0f;
        return s_lastAttacker == attacker && s_lastDamage >= 0f;
    }

    private static FightHitResult Resolve(FighterActor a, FighterActor d, FightMoveDefinition m) =>
        FightHitResolver.Resolve(a, d, m, m.hits[0], Balance, false);

    private static bool Same(FighterBuildStats x, FighterBuildStats y) =>
        Mathf.Approximately(x.punchPower, y.punchPower) && Mathf.Approximately(x.kickPower, y.kickPower) && Mathf.Approximately(x.agility, y.agility)
        && Mathf.Approximately(x.resistance, y.resistance);

    private static void CheckHud()
    {
        var panel = UnityEngine.Object.FindFirstObjectByType<FightHudView>()?.buildPanel;
        if (panel == null) { Check("HUD rows = the build combat uses", false, "no panel"); return; }
        var b = Player.BuildStats;
        var wrong = panel.stats.Where(r => r.value.text != Mathf.RoundToInt(Value(b, r.stat)).ToString()).Select(r => $"{r.stat} shows {r.value.text}").ToList();
        Check("HUD: exactly the four attributes, showing the build combat uses",
              panel.stats.Length == 4 && panel.stats.Select(r => r.stat).SequenceEqual(MusicCombatBuildConverter.Stats) && wrong.Count == 0,
              $"{string.Join(",", panel.stats.Select(r => r.stat))} {string.Join(", ", wrong)}");
    }

    private static float Value(FighterBuildStats b, FightStatId s) => s switch
    {
        FightStatId.PunchPower => b.punchPower, FightStatId.KickPower => b.kickPower, FightStatId.Agility => b.agility,
        FightStatId.Resistance => b.resistance, _ => 0f,
    };

    private static string Push(float v) => v < 0f ? "–" : v.ToString("0.00");

    private static void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("| Cas | Puny/Pat./Res./Agi. | Puny | Patada | Puny rebut | Caminar m/s | Arrencada puny s | Atur. puny s | Atur. patada s | Empenta puny m | Empenta patada m | Rival m/s |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in s_rows)
            sb.AppendLine($"| {r.name} | {r.build.punchPower:0.#}/{r.build.kickPower:0.#}/{r.build.resistance:0.#}/{r.build.agility:0.#} | {r.punch:0.0}{(r.punchLanded ? "" : "*")} | " +
                          $"{r.kick:0.0}{(r.kickLanded ? "" : "*")} | {r.taken:0.0} | {r.speed:0.00} | {r.startup:0.000} | {r.punchStun:0.00} | {r.kickStun:0.00} | {Push(r.punchPush)} | {Push(r.kickPush)} | {r.rivalSpeed:0.00} |");
        sb.AppendLine("(* = the hit did not land in the probe: value from the same resolver formula)");
        Log(sb.ToString());

        Row R(string n) => s_rows.First(x => x.name == n);
        var min = R("Mínima (0 bonus)"); var low = R("Baixa (~25 %)"); var mid = R("Intermèdia (~50 %)"); var high = R("Alta (~80 %)");
        Check("real landed hits = the balance formula on the HUD build (nothing fixed or overridden in between)",
              s_rows.Count(x => x.punchLanded) >= s_rows.Count / 2 && s_mismatch.Count == 0, $"{s_rows.Count(x => x.punchLanded)}/{s_rows.Count} punches landed; {string.Join("; ", s_mismatch)}");
        Check("no bonuses: very weak but functional (punch and kick > 0, still walks)", min.punch > 0f && min.kick > 0f && min.punch < 3.5f && min.speed > 1.5f,
              $"punch {min.punch:0.0}, kick {min.kick:0.0}, walk {min.speed:0.00}");
        Check("minimum < low < middle < high on punch, kick and walking speed",
              min.punch < low.punch && low.punch < mid.punch && mid.punch < high.punch && min.kick < low.kick && low.kick < mid.kick && mid.kick < high.kick
              && min.speed < low.speed && low.speed < mid.speed && mid.speed < high.speed);
        Check("high vs minimum: clear advantage (punch ≥ 3×, walk ≥ 1.3×)", high.punch >= 3f * min.punch && high.speed >= 1.3f * min.speed,
              $"punch {min.punch:0.0}→{high.punch:0.0}, walk {min.speed:0.00}→{high.speed:0.00}");
        var pp = R("Especialista puny"); var kk = R("Especialista patada");
        Check("punch specialist: punches far stronger than the kick specialist's, and the reverse for kicks",
              pp.punch > 1.8f * kk.punch && kk.kick > 1.8f * pp.kick, $"punch {kk.punch:0.0} vs {pp.punch:0.0}, kick {pp.kick:0.0} vs {kk.kick:0.0}");
        var slow = R("Agilitat baixa"); var fast = R("Agilitat alta");
        Check("Agility: walking speed clearly apart, damage unchanged", fast.speed >= 1.4f * slow.speed && Mathf.Approximately(fast.punch, slow.punch),
              $"walk {slow.speed:0.00}→{fast.speed:0.00}");
        Check("Agility: the same punch comes out clearly sooner (whole move faster)", fast.startup <= 0.75f * slow.startup && high.startup < min.startup,
              $"start-up {slow.startup:0.000}→{fast.startup:0.000} s");
        Check("fastest build stays controllable (≤ 1.35× base walk)", s_rows.Max(x => x.speed) <= BaseSpeed * 1.35f + 0.05f, $"max {s_rows.Max(x => x.speed):0.00} m/s");
        var rl = R("Resistència baixa"); var rh = R("Resistència alta (Beat)");
        Check("Resistance: the same rival punch hurts clearly less with high Resistance", rh.taken <= 0.7f * rl.taken, $"{rl.taken:0.0} → {rh.taken:0.0}");
        var imp = R("Amb Impact (×6)");
        Check("Impact: strengthens punch AND kick, nothing else", imp.punch > mid.punch && imp.kick > mid.kick && Mathf.Approximately(imp.taken, mid.taken) && Mathf.Abs(imp.speed - mid.speed) < 0.05f,
              $"punch {mid.punch:0.0}→{imp.punch:0.0}, kick {mid.kick:0.0}→{imp.kick:0.0}");
        Check("hit reaction: a stronger attack stuns at least as long, always within the bounds",
              high.punchStun >= min.punchStun && high.kickStun >= min.kickStun
              && s_rows.All(x => x.punchStun <= MoveSet.normalPunch.hits[0].baseHitStun * Balance.maxReactionScale + 1e-3f
                                 && x.kickStun <= MoveSet.normalKick.hits[0].baseHitStun * Balance.maxReactionScale + 1e-3f),
              $"punch stun {min.punchStun:0.00}→{high.punchStun:0.00}, kick stun {min.kickStun:0.00}→{high.kickStun:0.00}");
        var pushed = s_rows.Where(x => x.punchPush >= 0f && x.kickPush >= 0f).ToList();
        Check("push-back: stronger attacks push at least as far; the kick keeps out-pushing the punch (its own character)",
              (min.punchPush < 0f || high.punchPush < 0f || high.punchPush >= min.punchPush - 0.02f) && pushed.All(x => x.kickPush >= x.punchPush - 0.05f),
              $"punch {Push(min.punchPush)}→{Push(high.punchPush)} m, kick {Push(min.kickPush)}→{Push(high.kickPush)} m (base knockback punch {MoveSet.normalPunch.hits[0].baseKnockback}, kick {MoveSet.normalKick.hits[0].baseKnockback})");
        var r1 = R("Intermèdia vs rival nv.1"); var r4 = R("Intermèdia vs rival nv.4");
        Check("rival level 4 vs 1, same player build: rival hits harder, moves faster, takes less", r4.taken > 1.4f * r1.taken && r4.rivalSpeed > r1.rivalSpeed && r4.punch < r1.punch,
              $"taken {r1.taken:0.0}→{r4.taken:0.0}, rival walk {r1.rivalSpeed:0.00}→{r4.rivalSpeed:0.00}, player punch {r1.punch:0.0}→{r4.punch:0.0}");
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
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 720; }
            if (Fighting && Rival != null && s_step < s_steps.Count - 4)   // nobody gets KO'd mid-measure
                foreach (var a in new[] { Player, Rival })
                    if (a.Health.CurrentHealth < a.Health.MaxHealth * 0.5f) Heal(a);
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
