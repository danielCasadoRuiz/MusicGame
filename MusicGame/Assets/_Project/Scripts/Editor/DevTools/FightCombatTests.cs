using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Focused play-mode checks of the combat architecture in the REAL Fight scene (cases A–I), plus
/// frame captures of the key transitions (Logs/Combat_*.png) for visual review.
/// Batch: -executeMethod FightCombatTests.RunFromCommandLine   (no -quit; exits itself)
/// Setup mirrors MakeHumanBodyTests' Fight check: a standalone debug scene (no app boot), a
/// GameSession with a real RunnerResults (built from a GameEndedEvent), Mozart Tier 1 selected.
/// </summary>
[InitializeOnLoad]
public static class FightCombatTests
{
    private const string PendingKey = "FightCombatTests.Pending";
    private const string ResultFile = "Logs/FightCombatTests.txt";

    static FightCombatTests()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            _ = Run();
        };
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static readonly StringBuilder Log = new();
    private static int _fails;
    private static void Check(string name, bool ok, string detail = "")
    {
        Log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static async Task WaitSeconds(float s)
    {
        float end = Time.realtimeSinceStartup + s;
        while (Time.realtimeSinceStartup < end) await Task.Yield();
    }

    private static async Task Run()
    {
        Log.Clear(); _fails = 0;
        try { await Body(); }
        catch (System.Exception e) { Check("Exception", false, e.ToString()); }
        string report = (_fails == 0 ? "[FightCombatTests] ALL PASSED\n" : $"[FightCombatTests] {_fails} FAILED\n") + Log;
        File.WriteAllText(ResultFile, report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    private static async Task Body()
    {
        // Disable the debug scene's own avatars so they don't clutter the capture.
        foreach (var p in Object.FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        var session = new GameObject("TestGameSession").AddComponent<GameSession>();
        session.Configure(appConfig.fightStats);
        session.Configure(appConfig.progression);

        // A real finalized Runner result (musical performance → CombatBuild), Specials = 0.
        var perf = new GamePerformance();
        foreach (var (t, a, c) in new[] { (RingType.Kick, 40, 34), (RingType.Snare, 30, 18), (RingType.Beat, 60, 50), (RingType.Onset, 25, 20), (RingType.Impact, 8, 7) })
            perf.ByType[t] = new TypePerformance { Type = t, Available = a, Collected = c, CollectionRate = (float)c / a, TimingAccuracy = 0.9f };
        var stats = new CollectionStats(); stats.AddScore(3000);
        EventBus.Publish(new GameEndedEvent { Stats = stats, Performance = perf, Resources = new RunnerResourceCounts { TripleCombos = 0, QuadCombos = 0, Lives = 2, Specials = 0 } });
        var results = session.RunnerResults;
        Check("setup: GameSession finalized RunnerResults with a CombatBuild", results?.CombatBuild != null && results.CombatBuild.Budget > 0f);

        var roster = appConfig.opponentRoster;
        var mozart = roster.opponents.First(o => o != null && o.id == "mozart");
        var tier1 = mozart.GetConfigForTier(1, out int resolved);
        session.SelectedOpponent = mozart;
        session.SelectedOpponentLevelConfig = tier1;
        session.SelectedOpponentTier = resolved;

        // H — tier profile resolution (+ independence: pointing Tier 2 elsewhere affects only Tier 2).
        var mozartProfile = tier1.combatProfile;
        bool tiersOk = mozartProfile != null;
        for (int t = 1; t <= 4; t++) tiersOk &= mozart.GetConfigForTier(t).combatProfile == mozartProfile;
        var tier2 = mozart.GetConfigForTier(2);
        var original = tier2.combatProfile;
        tier2.combatProfile = appConfig.fightFlow.defaultOpponentCombatProfile;
        bool independent = mozart.GetConfigForTier(2).combatProfile == appConfig.fightFlow.defaultOpponentCombatProfile && mozart.GetConfigForTier(1).combatProfile == mozartProfile;
        tier2.combatProfile = original; // in-memory only, restored
        Check($"H: Mozart tiers 1-4 resolve CombatProfile '{(mozartProfile != null ? mozartProfile.name : "null")}'", tiersOk);
        Check("H: a tier can point at a different profile without affecting the others", independent);

        // I — the real Fight scene.
        var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Fight.unity", new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone) await Task.Yield();
        FighterActor player = null, opponent = null;
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            player   = GameObject.Find("FighterPlayer")?.GetComponent<FighterActor>();
            opponent = GameObject.Find("FighterOpponent")?.GetComponent<FighterActor>();
            if (player != null && opponent != null && player.AnimationDriver is AnimatorFighterAnimationDriver && opponent.AnimationDriver is AnimatorFighterAnimationDriver) break;
            await Task.Yield();
        }
        Check("I: Fight spawned both fighters with a combat profile", player?.CombatProfile != null && opponent?.CombatProfile != null,
              $"player {player?.CombatProfile}, opponent {opponent?.CombatProfile}");
        Check("I: both avatars bound to the shared combat controller", player?.AnimationDriver is AnimatorFighterAnimationDriver && opponent?.AnimationDriver is AnimatorFighterAnimationDriver);
        if (player == null || opponent == null || !(player.AnimationDriver is AnimatorFighterAnimationDriver pd) || !(opponent.AnimationDriver is AnimatorFighterAnimationDriver od)) return;
        Check($"I: opponent profile is Mozart Tier 1's ({opponent.CombatProfile.name})", opponent.CombatProfile == mozartProfile);

        // Deterministic: no AI, flow = Fighting.
        if (opponent.AI != null) opponent.AI.enabled = false;
        EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.Countdown, Current = FightFlowState.Fighting });
        _player = player; _opponent = opponent;
        Reposition();
        await WaitSeconds(0.5f);

        // A — override isolation.
        Check("A: each fighter has its OWN AnimatorOverrideController on the SAME base controller",
              pd.Override != od.Override && pd.Override.runtimeAnimatorController == od.Override.runtimeAnimatorController);
        var po = new List<KeyValuePair<AnimationClip, AnimationClip>>(); pd.Override.GetOverrides(po);
        var oo = new List<KeyValuePair<AnimationClip, AnimationClip>>(); od.Override.GetOverrides(oo);
        var key = po.First(k => k.Key.name.EndsWith("LightAttack")).Key;
        var opponentBefore = od.Override[key];
        pd.Override[key] = player.CombatProfile.animationSet.Get(CombatRole.Kick);
        Check("A: changing the player's LightAttack clip leaves the opponent's untouched", od.Override[key] == opponentBefore && pd.Override[key] != opponentBefore);
        pd.Override[key] = opponentBefore;

        // G — Runner data.
        var expected = FighterBuildStats.FromCombatBuild(results.CombatBuild);
        Check($"G: player build = RunnerResults.CombatBuild [{player.BuildStats}]",
              Mathf.Approximately(player.BuildStats.punchPower, expected.punchPower) && Mathf.Approximately(player.BuildStats.kickPower, expected.kickPower) &&
              Mathf.Approximately(player.BuildStats.agility, expected.agility) && Mathf.Approximately(player.BuildStats.resistance, expected.resistance) &&
              Mathf.Approximately(player.BuildStats.impactPower, expected.impactPower) && player.BuildStats.Total > 0f);
        Check($"G: player resources from RunnerResults.Resources [{player.CombatResources}]", player.CombatResources.Specials == 0 && player.CombatResources.Lives == 2);

        var mc = player.MoveController;
        var captures = new List<Task>();

        Reposition();
        // B — phases (Idle → Startup → Active → Recovery → Idle), captured.
        var seen = new List<FighterMoveState>();
        var capture = Capture("Combat_1_Light", 1.1f, 12);
        bool started = mc.TryExecuteRole(CombatRole.LightAttack);
        float until = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < until)
        {
            if (seen.Count == 0 || seen[seen.Count - 1] != mc.CurrentPhase) seen.Add(mc.CurrentPhase);
            if (seen.Count > 1 && mc.CurrentPhase == FighterMoveState.Idle) break;
            await Task.Yield();
        }
        await capture;
        Check($"B: LightAttack phases {string.Join(" → ", seen)}", started &&
              seen.SequenceEqual(new[] { FighterMoveState.Startup, FighterMoveState.Active, FighterMoveState.Recovery, FighterMoveState.Idle }));
        Check($"B: the LightAttack hit the opponent (opponent reaction {opponent.HitReaction.State}, health {opponent.Health.CurrentHealth:0})",
              opponent.Health.CurrentHealth < opponent.Health.MaxHealth);
        await WaitSeconds(0.8f);

        Reposition();
        await Sequence(player, CombatRole.HeavyAttack, "Combat_2_Heavy", 1.4f);
        Reposition();
        await Sequence(player, CombatRole.Kick, "Combat_3_Kick", 1.3f);

        Reposition();
        // Block: opponent attacks into the player's Block move → blocked.
        int blocked = 0; System.Action<HitBlockedEvent> onBlock = e => { if (e.Defender == player) blocked++; };
        EventBus.Subscribe(onBlock);
        var blockCap = Capture("Combat_4_Block", 1.2f, 12);
        mc.TryExecuteRole(CombatRole.Block);
        await WaitSeconds(0.15f);
        opponent.MoveController.TryExecuteRole(CombatRole.LightAttack);
        await blockCap;
        EventBus.Unsubscribe(onBlock);
        Check($"Block: the opponent's attack was blocked by the Block move ({blocked})", blocked > 0);
        await WaitSeconds(0.6f);

        Reposition();
        // Dodge: opponent attacks inside the player's invulnerability window → evaded.
        int evaded = 0; System.Action<HitEvadedEvent> onEvade = e => { if (e.Defender == player) evaded++; };
        EventBus.Subscribe(onEvade);
        var dodgeCap = Capture("Combat_5_Dodge", 1.0f, 10);
        mc.TryExecuteRole(CombatRole.Dodge);
        opponent.MoveController.TryExecuteRole(CombatRole.LightAttack);
        await dodgeCap;
        EventBus.Unsubscribe(onEvade);
        Check($"Dodge: the attack inside the invulnerability window was evaded ({evaded})", evaded > 0);
        await WaitSeconds(0.8f);

        Reposition();
        // Hit reaction on the player (opponent attacks).
        var hitCap = Capture("Combat_6_HitReaction", 1.1f, 11);
        float hpBefore = player.Health.CurrentHealth;
        opponent.MoveController.TryExecuteRole(CombatRole.HeavyAttack);
        await hitCap;
        Check($"Hit: player took damage from Mozart ({hpBefore:0} → {player.Health.CurrentHealth:0})", player.Health.CurrentHealth < hpBefore);
        await WaitSeconds(0.8f);

        // C / D — Special rejection and consumption.
        var special = player.CombatProfile.GetMoveById("special_test");
        bool rejected = !mc.TryExecuteMove(special);
        Check($"C: Special with 0 Specials is rejected ({mc.LastRejection}), resource stays 0", rejected && player.CombatResources.Specials == 0);
        player.CombatResources.DebugGrant(0, 0, 1);
        Reposition();
        var specialCap = Capture("Combat_7_Special", 1.8f, 16);
        bool accepted = mc.TryExecuteMove(special);
        Check($"D: Special with 1 Special executes and consumes it (now {player.CombatResources.Specials})", accepted && player.CombatResources.Specials == 0 && mc.CurrentMove == special);
        await specialCap;
        await WaitSeconds(1.0f);

        // E — Triple / Quad.
        var triple = player.CombatProfile.GetMoveById("triple_technique_test");
        var quad   = player.CombatProfile.GetMoveById("quad_technique_test");
        bool tripleRejected = !mc.TryExecuteMove(triple);
        player.CombatResources.DebugGrant(1, 1, 0);
        bool tripleOk = mc.TryExecuteMove(triple) && player.CombatResources.TripleCombos == 0 && player.CombatResources.QuadCombos == 1;
        await WaitSeconds(1.2f);
        Reposition();
        bool quadOk = mc.TryExecuteMove(quad) && player.CombatResources.QuadCombos == 0;
        Check($"E: Triple test rejected at 0, consumes exactly 1 TripleCombo; Quad test consumes 1 QuadCombo", tripleRejected && tripleOk && quadOk);
        await WaitSeconds(3.5f); // quad knocks Mozart down → let him get up

        Reposition();
        // F — knockdown interrupts a normal attack → Downed → GetUp → idle.
        mc.TryExecuteRole(CombatRole.HeavyAttack);
        await WaitSeconds(0.05f);
        var kdCap = Capture("Combat_8_Knockdown_GetUp", 4.0f, 20);
        player.HitReaction.ApplyKnockdown(0.5f);
        bool interrupted = mc.CurrentMove == null && player.HitReaction.State == FighterReactionState.Knockdown && player.IsInvulnerable;
        bool rejectedWhileDown = !mc.TryExecuteRole(CombatRole.LightAttack);
        var states = new List<FighterReactionState>();
        until = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < until)
        {
            if (states.Count == 0 || states[states.Count - 1] != player.HitReaction.State) states.Add(player.HitReaction.State);
            if (player.HitReaction.State == FighterReactionState.None) break;
            await Task.Yield();
        }
        await kdCap;
        Check($"F: knockdown interrupts the attack, blocks new moves, flows {string.Join(" → ", states)}",
              interrupted && rejectedWhileDown && states.SequenceEqual(new[] { FighterReactionState.Knockdown, FighterReactionState.Downed, FighterReactionState.GetUp, FighterReactionState.None }));
        await WaitSeconds(0.5f);
        Check("F: after GetUp the fighter can act again", mc.TryExecuteRole(CombatRole.LightAttack));
        await WaitSeconds(1.0f);

        // Taunt + Victory / Defeat pose.
        Reposition();
        await Sequence(player, CombatRole.Taunt, "Combat_9_Taunt", 2.0f);
        var endCap = Capture("Combat_10_Victory", 2.0f, 10);
        EventBus.Publish(new MatchEndedEvent { Winner = FighterSide.Player });
        await endCap;
        Check("Match end: moves locked, winner shows Victory", mc.IsMatchLocked && !mc.TryExecuteRole(CombatRole.LightAttack) && pd.CurrentRole == CombatRole.Victory);
    }

    private static FighterActor _player, _opponent;
    private static readonly System.Text.StringBuilder _trace = new();

    private static string TraceFighter(FighterActor a)
    {
        if (a == null) return "-";
        var anim = a.GetComponentInChildren<Animator>();
        var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        var drv = a.AnimationDriver as AnimatorFighterAnimationDriver;
        return $"{a.Side} {drv?.CurrentRole} pos {a.transform.position:F2} hips {(hips != null ? hips.position.ToString("F2") : "-")} root {(anim != null ? anim.transform.position.ToString("F2") : "-")}";
    }

    /// <summary>Knockback pushes the fighters apart — put them back in reach (1.1 m) before each action.</summary>
    private static void Reposition()
    {
        if (_player == null || _opponent == null) return;
        _player.transform.position   = new Vector3(-0.55f, _player.transform.position.y, 0f);
        _opponent.transform.position = new Vector3(0.55f, _opponent.transform.position.y, 0f);
    }

    private static async Task Sequence(FighterActor actor, CombatRole role, string file, float seconds)
    {
        var cap = Capture(file, seconds, Mathf.RoundToInt(seconds * 10f));
        bool ok = actor.MoveController.TryExecuteRole(role);
        if (!ok) Check($"{role} executes", false, actor.MoveController.LastRejection);
        await cap;
        await WaitSeconds(0.6f);
    }

    /// <summary>Side-view frames of both fighters every seconds/frames, as one horizontal strip.</summary>
    private static async Task Capture(string file, float seconds, int frames)
    {
        var camGO = new GameObject("CaptureCam");
        var cam = camGO.AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, 1.05f, -3.6f);
        cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));
        cam.fieldOfView = 34f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.enabled = false;
        const int w = 400, h = 460;
        var strip = new Texture2D(w * frames, h, TextureFormat.RGB24, false);
        var rt = new RenderTexture(w, h, 24);
        float start = Time.realtimeSinceStartup;
        for (int i = 0; i < frames; i++)
        {
            while (Time.realtimeSinceStartup < start + seconds * i / frames) await Task.Yield();
            if (_player != null && _opponent != null)
            {
                // Follow both fighters (knockback separates them): centre on the midpoint, back off with distance.
                float mid = (_player.transform.position.x + _opponent.transform.position.x) * 0.5f;
                float sep = Mathf.Abs(_player.transform.position.x - _opponent.transform.position.x);
                cam.transform.position = new Vector3(mid, 1.05f, -Mathf.Max(3.6f, sep * 1.9f + 1.2f));
                cam.transform.LookAt(new Vector3(mid, 0.95f, 0f));
            }
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            strip.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
            RenderTexture.active = null;
            _trace.AppendLine($"{file}[{i}] {TraceFighter(_player)} | {TraceFighter(_opponent)}");
        }
        strip.Apply();
        File.WriteAllBytes($"Logs/{file}.png", strip.EncodeToPNG());
        File.WriteAllText("Logs/FightCombatTrace.txt", _trace.ToString());
        cam.targetTexture = null;
        Object.Destroy(rt); Object.Destroy(strip); Object.Destroy(camGO);
    }
}
