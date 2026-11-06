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
/// PLAY-MODE contact table (2026-11-05): the edit-mode FightReachProbe says every strike reaches, the playtest
/// says kicks never land. This fires every grounded strike of the move set, BOTH directions (player → rival,
/// rival → player), at a range of root distances, in the real Fight scene (Animator crossfades, step-in, the
/// defender's live idle) and reports landed / blocked / whiff + the closest limb gap and the real press→Active
/// and press→Idle times. Part 2: the real AI (enabled) against a passive player for a while — attempts vs
/// contacts per move. Batch: -executeMethod FightStrikeContactProbe.RunFromCommandLine (no -quit; exits itself)
/// → Logs/FightStrikeContactProbe.txt. Writes into the save like every fight probe: run through the
/// save-restoring driver.
/// </summary>
[InitializeOnLoad]
public static class FightStrikeContactProbe
{
    private const string PendingKey = "FightStrikeContactProbe.Pending";
    private const string ResultFile = "Logs/FightStrikeContactProbe.txt";

    static FightStrikeContactProbe()
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
        EditorSceneManager.OpenScene(CanonicalAvatarSetup.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static readonly StringBuilder Log = new();
    private static int _fails;
    private static void Check(string name, bool ok, string detail = "")
    {
        Log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
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
        string report = $"[FightStrikeContactProbe] {(_fails == 0 ? "ALL PASSED" : _fails + " FAILED")}\n" + Log;
        File.WriteAllText(ResultFile, report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    private static FighterActor _player, _opponent;

    private static async Task Body()
    {
        foreach (var p in Object.FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        var session = new GameObject("TestGameSession").AddComponent<GameSession>();
        session.Configure(appConfig.fightStats);
        session.Configure(appConfig.progression);
        var perf = new GamePerformance();
        foreach (var (t, a, c) in new[] { (RingType.Kick, 40, 34), (RingType.Snare, 30, 18), (RingType.Beat, 60, 50), (RingType.Onset, 25, 20), (RingType.Impact, 8, 7) })
            perf.ByType[t] = new TypePerformance { Type = t, Available = a, Collected = c, CollectionRate = (float)c / a, TimingAccuracy = 0.9f };
        var stats = new CollectionStats(); stats.AddScore(3000);
        EventBus.Publish(new GameEndedEvent { Stats = stats, Performance = perf, Resources = new RunnerResourceCounts { TripleCombos = 0, QuadCombos = 0, Lives = 2, Specials = 0 } });
        var mozart = appConfig.opponentRoster.opponents.First(o => o != null && o.id == "mozart");
        var tier = mozart.GetConfigForTier(3, out int resolved);
        session.SelectedOpponent = mozart; session.SelectedOpponentLevelConfig = tier; session.SelectedOpponentTier = resolved;

        var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Fight.unity", new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone) await Task.Yield();
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            _player   = GameObject.Find("FighterPlayer")?.GetComponent<FighterActor>();
            _opponent = GameObject.Find("FighterOpponent")?.GetComponent<FighterActor>();
            if (_player != null && _opponent != null && _player.Body != null && _opponent.Body != null) break;
            await Task.Yield();
        }
        if (_player?.Body == null || _opponent?.Body == null) { Check("setup: both fighters with a real body", false); return; }
        bool aiWas = _opponent.AI != null && _opponent.AI.enabled;
        if (_opponent.AI != null) _opponent.AI.enabled = false;
        EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.Countdown, Current = FightFlowState.Fighting });
        await WaitSeconds(0.6f);

        var set = _player.MoveController.MoveSet;
        var moves = new (string, FightMoveDefinition)[]
        {
            ("P",        set.normalPunch), ("Up+P", set.highNormalPunch), ("Down+P", set.lowNormalPunch),
            ("K",        set.normalKick),  ("Up+K", set.highNormalKick),  ("Down+K", set.lowNormalKick),
            ("Back+K",   set.backNormalKick),
            // Axes / distance / context techniques (2026-11-05)
            ("SideP",    set.punchMidSide),  ("Up+SideP", set.punchHighSide), ("Dn+SideP", set.punchLowSide),
            ("CtrK",     set.kickMidCenter), ("Up+CtrK",  set.kickHighCenter), ("Dn+CtrK", set.kickLowCenter),
            ("PushK",    set.pushKickMid),   ("Uppercut", set.closeUppercut),  ("Knee",    set.closeKnee),
            ("SpinMid",  set.spinKickMid),   ("Sweep",    set.spinSweep),
        };
        float[] distances = { 0.6f, 0.75f, 0.9f, 1.1f, 1.3f, 1.5f };

        await Diagnose(moves);

        int landed = 0, blocked = 0;
        // The side that really struck: the contact point lies on the attacker's requested side (its own right / left).
        int sideOk = 0, sideAll = 0;
        System.Action<HitLandedEvent> onLand = e =>
        {
            landed++;
            var mc = e.Attacker != null ? e.Attacker.MoveController : null;
            if (mc == null || mc.DebugForceStrikeSide == 0 || !e.Result.HasContact || e.Move == null) return;
            // Only the SIDE techniques (hooks, circular kicks): a straight centre strike lands on the midline by design.
            var r = e.Move.role;
            if (r != CombatRole.PunchHighSide && r != CombatRole.PunchMidSide && r != CombatRole.PunchLowSide && r != CombatRole.Kick &&
                r != CombatRole.HighKick && r != CombatRole.LowKick) return;
            float lateral = Vector3.Dot(e.Result.ContactPoint - e.Attacker.transform.position, e.Attacker.SideXZ);
            sideAll++;
            if (Mathf.Sign(lateral) == mc.DebugForceStrikeSide || Mathf.Abs(lateral) < 0.06f) sideOk++;
        };
        System.Action<HitBlockedEvent> onBlock = e => blocked++;
        EventBus.Subscribe(onLand); EventBus.Subscribe(onBlock);

        foreach (var (attacker, defender, label) in new[] { (_player, _opponent, "PLAYER → rival"), (_opponent, _player, "RIVAL → player") })
        {
            Log.AppendLine($"== {label}  (root distance → result, closest limb gap; L = landed, B = blocked, - = whiff)");
            foreach (var (name, move) in moves)
            {
                if (move == null) { Log.AppendLine($"  {name,-7} (no move)"); continue; }
                var row = new StringBuilder($"  {name,-7} {move.debugName,-24} reach {move.contactReach:0.00}  ");
                int hitsAtClose = 0, triesAtClose = 0;
                float pressToActive = 0f, pressToIdle = 0f;
                foreach (float d in distances)
                {
                    string cell = "";
                    for (int rep = 0; rep < 4; rep++) // (base + alt clip) × (right, left: mirrored)
                    {
                        attacker.MoveController.DebugForceStrikeSide = rep < 2 ? 1 : -1;
                        await Place(attacker, defender, d);
                        landed = 0; blocked = 0;
                        float t0 = Time.time, tActive = -1f, tIdle = -1f;
                        float distAtActive = -1f;
                        if (!attacker.MoveController.TryExecuteMove(move)) { cell += "x"; continue; }
                        float until = Time.realtimeSinceStartup + 3f;
                        while (Time.realtimeSinceStartup < until)
                        {
                            var ph = attacker.MoveController.CurrentPhase;
                            if (tActive < 0f && ph == FighterMoveState.Active) { tActive = Time.time - t0; distAtActive = attacker.DistanceToOpponent; }
                            if (tActive >= 0f && ph == FighterMoveState.Idle) { tIdle = Time.time - t0; break; }
                            await Task.Yield();
                        }
                        await WaitSeconds(0.15f);
                        float gap = attacker.Attack.LastContactGap;
                        string res = landed > 0 ? "L" : blocked > 0 ? "B" : "-";
                        cell += res + (float.IsInfinity(gap) ? "" : $"{gap:+0.00;-0.00}");
                        if (rep < 3) cell += "/";
                        if (d <= move.contactReach - 0.15f) { triesAtClose++; if (landed > 0) hitsAtClose++; }
                        if (d == distances[0] && rep == 0) { pressToActive = tActive; pressToIdle = tIdle; }
                        if (d == 1.1f && rep == 0) cell += $"@{distAtActive:0.00}";
                    }
                    row.Append($"| {d:0.00}: {cell,-24}");
                }
                row.Append($"| press→Active {pressToActive:0.00}s  press→Idle {pressToIdle:0.00}s");
                Log.AppendLine(row.ToString());
                Check($"{label} {name} ({move.debugName}) lands inside its reach", triesAtClose == 0 || hitsAtClose * 4 >= triesAtClose * 3,
                      $"{hitsAtClose}/{triesAtClose} at distances ≤ reach−0.15");
            }
        }
        // Part 1b — air strikes: jump (the real StartJump), then the air punch / kick at a few delays.
        var startJump = typeof(FighterMovement).GetMethod("StartJump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Log.AppendLine("== AIR strikes, player → rival (delay after take-off: result; max root height)");
        foreach (var (name, move) in new[] { ("Air P", set.airNormalPunch), ("Air K", set.airNormalKick) })
        {
            if (move == null || startJump == null) continue;
            var row = new StringBuilder($"  {name,-6} {move.debugName,-14} startup {move.startupDuration:0.00} active {move.activeDuration:0.00} ");
            int ok = 0, tries = 0;
            foreach (float d in new[] { 0.8f, 1.0f, 1.2f })
                foreach (float delay in new[] { 0.0f, 0.15f, 0.3f })
                {
                    await Place(_player, _opponent, d);
                    landed = 0; blocked = 0;
                    float y0 = _player.transform.position.y, maxH = 0f;
                    startJump.Invoke(_player.Movement, null);
                    float t0 = Time.time;
                    while (Time.time - t0 < delay) { maxH = Mathf.Max(maxH, _player.transform.position.y - y0); await Task.Yield(); }
                    bool started = _player.MoveController.TryExecuteMove(move);
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < until && (_player.Posture == FighterPosture.Airborne || _player.MoveController.CurrentPhase != FighterMoveState.Idle))
                    { maxH = Mathf.Max(maxH, _player.transform.position.y - y0); await Task.Yield(); }
                    await WaitSeconds(0.1f);
                    tries++; if (landed > 0) ok++;
                    float gap = _player.Attack.LastContactGap;
                    row.Append($"| {d:0.0}m+{delay:0.00}s: {(started ? (landed > 0 ? "L" : blocked > 0 ? "B" : "-") : "x(" + _player.MoveController.LastRejection + ")")}{(float.IsInfinity(gap) ? "" : $"{gap:+0.00;-0.00}")} h{maxH:0.00} ");
                }
            Log.AppendLine(row.ToString());
            Check($"air {name} lands most of the time at 0.8–1.2 m", ok * 3 >= tries * 2, $"{ok}/{tries}");
        }
        _player.MoveController.DebugForceStrikeSide = _opponent.MoveController.DebugForceStrikeSide = 0;
        Check("left / right strikes land with the requested side (contact on that side of the attacker)", sideAll > 0 && sideOk * 10 >= sideAll * 8,
              $"{sideOk}/{sideAll}");

        // Part 1c — FIRST-PERSON SIDE GUARD: the rival's right hand arrives on the player's LEFT; a left guard stops it,
        // a right guard does not; a third-person guard (no side) stops both.
        Log.AppendLine("== side guard (rival mid punch, right hand → player's left)");
        var guard = _player.Guard;
        var balance = Resources.Load<AppConfigSO>("AppConfig").combatBalance;
        float reactKeep = balance.guardSideReactTime;
        try
        {
            // (guard side, strike sides in a row, react time override, expected result of the LAST strike, label)
            foreach (var (gSide, sides, react, expectBlock, label) in new[]
            {
                (-1, new[] { 1, 1 },  -1f,  true,  "first person, left guard: blocks a right-hand punch"),
                ( 1, new[] { 1, 1 },  -1f,  false, "first person, right guard: a right-hand punch gets through"),
                ( 0, new[] { -1, 1 }, 0.08f, true,  "third person: a fast defender's guard turns to the new side in time"),
                ( 0, new[] { -1, 1 }, 0.6f,  false, "third person: a guard slower than the strike's startup is too late"),
            })
            {
                if (react > 0f) balance.guardSideReactTime = react;
                bool last = false; string why = "";
                await Place(_opponent, _player, 0.9f);
                guard.DebugForcedState = FighterGuardState.StandingGuard; guard.DebugForcedSide = gSide;
                await WaitSeconds(0.35f);
                foreach (int strikeSide in sides)
                {
                    await Place(_opponent, _player, 0.9f); // the previous hit's pushback must not leave it out of reach (the guard stays held)
                    landed = 0; blocked = 0;
                    _opponent.MoveController.DebugForceStrikeSide = strikeSide;
                    _opponent.MoveController.TryExecuteMove(set.normalPunch);
                    await WaitSeconds(0.9f);
                    last = expectBlock ? blocked > 0 : landed > 0;
                    why = $"landed {landed} blocked {blocked}; {guard.LastSideInfo}";
                }
                guard.DebugForcedState = FighterGuardState.None; guard.DebugForcedSide = 0;
                balance.guardSideReactTime = reactKeep;
                Check($"side guard: {label}", last, why);
            }
        }
        finally { balance.guardSideReactTime = reactKeep; }

        // Part 1d — a strike from a JUMP falls onto the defender: only the high guard stops it.
        var jump = typeof(FighterMovement).GetMethod("StartJump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var (gState, expectBlock) in new[] { (FighterGuardState.StandingGuard, false), (FighterGuardState.HighGuard, true) })
        {
            int ok = 0, n = 0; string why = "";
            for (int rep = 0; rep < 3; rep++)
            {
                await Place(_opponent, _player, 0.9f);
                guard.DebugForcedState = gState; guard.DebugForcedSide = 0;
                await WaitSeconds(0.4f);
                landed = 0; blocked = 0;
                jump.Invoke(_opponent.Movement, null);
                await WaitSeconds(0.12f);
                _opponent.MoveController.TryExecuteMove(set.airNormalKick);
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until && (_opponent.Posture == FighterPosture.Airborne || _opponent.MoveController.CurrentPhase != FighterMoveState.Idle)) await Task.Yield();
                await WaitSeconds(0.1f);
                if (landed + blocked > 0) { n++; if (expectBlock ? blocked > 0 : landed > 0) ok++; }
                why = guard.LastSideInfo;
                guard.DebugForcedState = FighterGuardState.None;
            }
            Check($"air kick from a jump vs {gState}: {(expectBlock ? "blocked" : "gets through")}", n > 0 && ok == n, $"{ok}/{n} contacts; {why}");
        }
        _opponent.MoveController.DebugForceStrikeSide = 0;
        EventBus.Unsubscribe(onLand); EventBus.Unsubscribe(onBlock);

        // Part 2 — the real AI against a passive player.
        if (_opponent.AI != null)
        {
            _opponent.AI.enabled = true;
            // The AI was disabled when Fighting was published: it only learns the flow state from the event.
            EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.Countdown, Current = FightFlowState.Fighting });
            var attempts = new Dictionary<string, int>(); var hits = new Dictionary<string, int>(); var blocks = new Dictionary<string, int>();
            System.Action<FightMovePhaseChangedEvent> onPhase = e =>
            {
                if (e.Source != _opponent.MoveController || e.Current != FighterMoveState.Active || e.Move == null) return;
                attempts[e.Move.debugName] = attempts.GetValueOrDefault(e.Move.debugName) + 1;
            };
            System.Action<HitLandedEvent> onL = e => { if (e.Attacker == _opponent) hits[e.Move.debugName] = hits.GetValueOrDefault(e.Move.debugName) + 1; };
            System.Action<HitBlockedEvent> onB = e => { if (e.Attacker == _opponent) blocks[e.Move.debugName] = blocks.GetValueOrDefault(e.Move.debugName) + 1; };
            // The rival's Special use: give it 2 and trace its intentions / Signature requests.
            var wallet = _opponent.CombatResources;
            if (wallet != null) { wallet.TryConsume(CombatResourceType.Special, wallet.Specials); wallet.Add(CombatResourceType.Special, 2); }
            int sigEvents = 0; var intentions = new Dictionary<string, int>(); var sigInfos = new HashSet<string>();
            System.Action<SignatureExecutedEvent> onSig = e => { if (e.Fighter == _opponent) sigEvents++; };
            EventBus.Subscribe(onSig);
            FightAIIntention lastIntent = (FightAIIntention)(-1);
            EventBus.Subscribe(onPhase); EventBus.Subscribe(onL); EventBus.Subscribe(onB);
            await Place(_opponent, _player, 2.2f);
            float end = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < end)
            {
                var it = _opponent.AI.CurrentIntention;
                if (it != lastIntent) { lastIntent = it; intentions[it.ToString()] = intentions.GetValueOrDefault(it.ToString()) + 1; }
                var si = _opponent.MoveController.LastSignatureInfo; if (!string.IsNullOrEmpty(si)) sigInfos.Add(si);
                if (_player.Health.IsKO || _player.Health.CurrentHealth < _player.Health.MaxHealth * 0.3f) _player.Health.ResetForRound();
                if (_player.HitReaction.WaitingForGetUp) _player.HitReaction.RequestGetUp();
                await Task.Yield();
            }
            EventBus.Unsubscribe(onPhase); EventBus.Unsubscribe(onL); EventBus.Unsubscribe(onB); EventBus.Unsubscribe(onSig);
            Log.AppendLine("== AI rival vs passive player, 40 s (Active windows → landed / blocked)");
            Log.AppendLine($"  intentions: {string.Join(", ", intentions.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value))}");
            Log.AppendLine($"  Specials given 2 → Signature events {sigEvents}, left {wallet?.Specials}; signature requests: {string.Join(" | ", sigInfos)}; last rejection '{_opponent.MoveController.LastRejection}'");
            Check("AI throws a Special when it has one (40 s, passive player)", sigEvents >= 1, $"{sigEvents}");
            int kickTries = 0, kickHits = 0;
            foreach (var kv in attempts.OrderByDescending(k => k.Value))
            {
                int h = hits.GetValueOrDefault(kv.Key), b = blocks.GetValueOrDefault(kv.Key);
                Log.AppendLine($"  {kv.Key,-30} {kv.Value,3} → {h} landed, {b} blocked");
                if (kv.Key.ToLowerInvariant().Contains("kick")) { kickTries += kv.Value; kickHits += h; }
            }
            Check("AI kicks land on a passive player", kickTries == 0 || kickHits * 2 >= kickTries, $"{kickHits}/{kickTries}");
            _opponent.AI.enabled = aiWas;
        }
    }

    // Part 0 — WHERE the striking limb really goes in play (edit-mode FightReachProbe disagrees): for each move at 1.4 m
    // (out of reach, so nothing interrupts it), the farthest the limb tip gets along the line to the defender, its
    // lateral offset / height then, the hips' own forward offset, the facing angles and the clip actually playing.
    private static async Task Diagnose((string, FightMoveDefinition)[] moves)
    {
        Log.AppendLine("== DIAG limb path in play (attacker = player, roots 1.4 m apart; fwd/lat measured from the attacker ROOT along the line to the defender)");
        var anim = _player.BoundAnimator;
        if (anim == null) { Log.AppendLine("  no bound animator"); return; }
        Transform Bone(HumanBodyBones b) => anim.GetBoneTransform(b);
        var hips = Bone(HumanBodyBones.Hips);
        await Place(_player, _opponent, 1.4f);
        {
            Vector3 dir = _opponent.transform.position - _player.transform.position; dir.y = 0f; dir.Normalize();
            Vector3 hipFwd = hips.forward; hipFwd.y = 0f;
            Log.AppendLine($"  idle: actor.ForwardXZ∠line {Vector3.SignedAngle(dir, _player.ForwardXZ, Vector3.up):+0;-0}°, " +
                           $"actor.transform.fwd∠ {Vector3.SignedAngle(dir, Flat(_player.transform.forward), Vector3.up):+0;-0}°, " +
                           $"animator.fwd∠ {Vector3.SignedAngle(dir, Flat(anim.transform.forward), Vector3.up):+0;-0}°, hips.fwd∠ {Vector3.SignedAngle(dir, hipFwd, Vector3.up):+0;-0}°, " +
                           $"animator offset {Vector3.Dot(anim.transform.position - _player.transform.position, dir):+0.00;-0.00} fwd, " +
                           $"hips fwd {Vector3.Dot(hips.position - _player.transform.position, dir):+0.00;-0.00}, rival hips at {Vector3.Dot(_opponent.Body.HipsPosition - _player.transform.position, dir):0.00}, " +
                           $"applyRootMotion {anim.applyRootMotion}, humanScale {anim.humanScale:0.00}");
        }
        foreach (var (name, move) in moves)
        {
            if (move == null) continue;
            var limb = FighterBody.LimbFor(move);
            var tips = limb == FighterBody.Limb.Feet
                ? new[] { Bone(HumanBodyBones.RightToes) ?? Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.LeftToes) ?? Bone(HumanBodyBones.LeftFoot) }
                : new[] { Bone(HumanBodyBones.RightHand), Bone(HumanBodyBones.LeftHand) };
            for (int rep = 0; rep < 2; rep++)
            {
                await Place(_player, _opponent, 1.4f);
                Vector3 root0 = _player.transform.position;
                Vector3 dir = _opponent.transform.position - root0; dir.y = 0f; dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                float stance = 0f; foreach (var t in tips) stance = Mathf.Max(stance, Vector3.Dot(t.position - root0, dir));
                if (!_player.MoveController.TryExecuteMove(move)) { Log.AppendLine($"  {name} rejected"); continue; }
                float t0 = Time.time, best = float.MinValue, bestT = 0f, bestLat = 0f, bestH = 0f, bestHips = 0f, activeBest = float.MinValue, rootMove = 0f;
                string clipAtActive = "?";
                float until = Time.realtimeSinceStartup + 3f;
                bool sawActive = false;
                while (Time.realtimeSinceStartup < until)
                {
                    var ph = _player.MoveController.CurrentPhase;
                    if (ph == FighterMoveState.Active && !sawActive)
                    {
                        sawActive = true;
                        var ci = anim.GetCurrentAnimatorClipInfo(0); var ni = anim.GetNextAnimatorClipInfo(0);
                        clipAtActive = string.Join("+", ci.Select(c => $"{c.clip.name.Replace("__Strike_InPlace", "")}:{c.weight:0.0}")) +
                                       (ni.Length > 0 ? " → " + string.Join("+", ni.Select(c => $"{c.clip.name.Replace("__Strike_InPlace", "")}:{c.weight:0.0}")) : "") +
                                       $" nt{anim.GetCurrentAnimatorStateInfo(0).normalizedTime:0.00} spd{anim.GetCurrentAnimatorStateInfo(0).speed * anim.GetCurrentAnimatorStateInfo(0).speedMultiplier:0.00}";
                    }
                    foreach (var t in tips)
                    {
                        Vector3 rel = t.position - root0;
                        float f = Vector3.Dot(rel, dir);
                        if (f > best) { best = f; bestT = Time.time - t0; bestLat = Vector3.Dot(rel, side); bestH = t.position.y - (root0.y - 1f); bestHips = Vector3.Dot(hips.position - root0, dir); }
                        if (ph == FighterMoveState.Active) activeBest = Mathf.Max(activeBest, f);
                    }
                    rootMove = Mathf.Max(rootMove, Mathf.Abs(Vector3.Dot(_player.transform.position - root0, dir)));
                    if (sawActive && ph == FighterMoveState.Idle) break;
                    await Task.Yield();
                }
                Log.AppendLine($"  {name,-7} rep{rep} stance tip {stance:0.00} | peak tip {best:0.00} @{bestT:0.00}s (lat {bestLat:+0.00;-0.00}, h {bestH:0.00}, hips {bestHips:+0.00;-0.00}) | " +
                               $"best during Active {activeBest:0.00} (Active {move.startupDuration:0.00}-{move.startupDuration + move.activeDuration:0.00}) | root moved {rootMove:0.00} | {clipAtActive}");
            }
        }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

    // Both fighters standing, facing each other along X, `d` apart (roots), healthy and idle.
    private static async Task Place(FighterActor attacker, FighterActor defender, float d)
    {
        await WaitSeconds(0.05f);
        for (int i = 0; i < 200 && (attacker.MoveController.CurrentPhase != FighterMoveState.Idle || defender.MoveController.CurrentPhase != FighterMoveState.Idle ||
                                   defender.HitReaction.State != FighterReactionState.None || attacker.HitReaction.State != FighterReactionState.None); i++)
        {
            if (defender.HitReaction.WaitingForGetUp) defender.HitReaction.RequestGetUp();
            if (attacker.HitReaction.WaitingForGetUp) attacker.HitReaction.RequestGetUp();
            await WaitSeconds(0.05f);
        }
        attacker.transform.position = new Vector3(-d * 0.5f, attacker.transform.position.y, 0f);
        defender.transform.position = new Vector3(d * 0.5f, defender.transform.position.y, 0f);
        if (defender.Health.CurrentHealth < defender.Health.MaxHealth * 0.5f) defender.Health.ResetForRound();
        if (attacker.Health.CurrentHealth < attacker.Health.MaxHealth * 0.5f) attacker.Health.ResetForRound();
        await WaitSeconds(0.45f);
        attacker.transform.position = new Vector3(-d * 0.5f, attacker.transform.position.y, 0f);
        defender.transform.position = new Vector3(d * 0.5f, defender.transform.position.y, 0f);
        await Task.Yield();
    }
}
