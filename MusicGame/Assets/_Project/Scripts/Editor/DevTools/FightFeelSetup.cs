using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Combat > Apply Fight Feel (2026-11-04) — the data half of the "dynamic, Tekken-like"
/// fight pass (the code half: jump button, double/triple press, real-body contact, guard speed, ground hits,
/// Special wind-up, hit feedback). Idempotent; run AFTER CombatContentSetup (which it does not replace):
///   - normals hit while WALKING (movementLocked off, reduced speed) and chain from their Recovery
///     (cancelWindow ≈ Recovery), lunges only close the gap (FighterMovement caps them at contact);
///   - realistic heights: punch belly / chest / face, kick ankle / hip / face — each its own role
///     (LowPunch / HighPunch / LowKick / HighKick: own clips when the set has them, else the generic one);
///   - Back + Kick = spinning heel kick, running jump + Kick = flying kick (guard break, knockdown);
///   - the Special is telegraphed: ~0.9 s visible wind-up (SpecialCharge role + FightHitFeedback aura);
///   - snappier jump, tackle no longer knocks down, shorter knockdown flow;
///   - the 'bb' string (K K = a different kick) is removed: two kicks are two kicks now;
///   - new controller states for the new roles (CombatContentSetup.EnsureRoleStates).
/// Batch: -executeMethod FightFeelSetup.RunFromCommandLine  (Logs/FightFeelSetup.txt)
/// </summary>
public static class FightFeelSetup
{
    private const string FightDir  = "Assets/_Project/Configs/Fight";
    private const string MovesDir  = FightDir + "/Combat/Moves";
    private const string MoveSet   = FightDir + "/FightMoveSet.asset";
    private const string ComboSet  = FightDir + "/FightComboSet.asset";
    private const string Arena     = FightDir + "/FightArenaConfig.asset";
    private const string Balance   = FightDir + "/FightCombatBalanceConfig.asset";

    [MenuItem("Tools/MusicGame/Combat/Apply Fight Feel")]
    public static void Menu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report;
        try { report = Run(); }
        catch (System.Exception e) { report = "FAIL " + e; }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/FightFeelSetup.txt", report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var log = new StringBuilder("[FightFeelSetup]\n");
        log.Append(CombatContentSetup.EnsureRoleStates());

        var set = AssetDatabase.LoadAssetAtPath<FightMoveSetSO>(MoveSet);
        if (set == null) return log.AppendLine("FAIL no move set").ToString();

        // ── Normals: walk while striking, chain from Recovery, real heights ─────────────────────
        // Offsets are from the fighter's centre (root ≈ 1 m above the floor): face +0.62, chest +0.35,
        // belly / hips ≈ 0, knee −0.5, ankle −0.85.
        Normal(set.normalPunch,     CombatRole.LightAttack, AttackHeight.Mid,  new Vector3(0.7f, 0.35f, 0f), new Vector3(0.55f, 0.45f, 0.5f), 0.45f, log);
        Normal(set.highNormalPunch, CombatRole.HighPunch,   AttackHeight.High, new Vector3(0.7f, 0.62f, 0f), new Vector3(0.5f, 0.4f, 0.5f), 0.45f, log);
        Normal(set.lowNormalPunch,  CombatRole.LowPunch,    AttackHeight.Low,  new Vector3(0.7f, 0.0f, 0f),  new Vector3(0.55f, 0.45f, 0.5f), 0.4f, log);
        // 2026-11-05 "els cops de puny baixos no fan gaire": 6 → 8 (= the mid punch; it is the safer, slower-to-block option).
        if (set.lowNormalPunch != null && set.lowNormalPunch.hits != null && set.lowNormalPunch.hits.Length > 0 && set.lowNormalPunch.hits[0] != null)
            set.lowNormalPunch.hits[0].baseDamage = Mathf.Max(8f, set.lowNormalPunch.hits[0].baseDamage);
        // Air punch: the chest cross went over the defender's head from the jump (1/9 in play); the body jab angles down.
        if (set.airNormalPunch != null)
        {
            var ap = set.airNormalPunch;
            ap.role = CombatRole.LowPunch; ap.animationState = CombatRole.LowPunch.ToString();
            // 3/9 in play (misses by 0.12–0.35 m): it kept no forward momentum and its hit window lasted 0.10 s, so a
            // jump-then-punch mostly struck air. Like the air kick: carry forward and a longer window (still the faster one).
            // In the air the move keeps airCarry (not forwardCarry) and the grounded step-in is off: a straight-up jump
            // left the fist 0.15–0.35 m short at 1.0–1.2 m. The diving punch drives in (QueueLunge caps it at contact);
            // FighterPoseOverlay tips the torso so the fist comes down onto the opponent.
            ap.lungeDistance = 0.4f;
            ap.forwardCarry = 0.45f; ap.startupDuration = 0.14f; ap.activeDuration = 0.18f; ap.recoveryDuration = 0.14f;
            EditorUtility.SetDirty(ap);
        }
        Normal(set.normalKick,      CombatRole.Kick,        AttackHeight.Mid,  new Vector3(0.85f, 0.05f, 0f), new Vector3(0.65f, 0.55f, 0.6f), 0.3f, log);
        Normal(set.highNormalKick,  CombatRole.HighKick,    AttackHeight.High, new Vector3(0.85f, 0.6f, 0f), new Vector3(0.65f, 0.45f, 0.6f), 0.25f, log);
        Normal(set.lowNormalKick,   CombatRole.LowKick,     AttackHeight.Low,  new Vector3(0.85f, -0.8f, 0f), new Vector3(0.7f, 0.4f, 0.6f), 0.3f, log);
        var downB = set.GetByMoveId("move_down_b");   // Down + Kick (the combo that usually catches it): the ankle kick
        Normal(downB, CombatRole.LowKick, AttackHeight.Low, new Vector3(0.85f, -0.8f, 0f), new Vector3(0.7f, 0.4f, 0.6f), 0.3f, log);

        // Running / combo lunges: shorter (and FighterMovement now caps any lunge at contact distance).
        Lunge(set.GetByMoveId("move_forward_a"), 0.6f, log);
        Lunge(set.GetByMoveId("move_aaa_b"), 0.35f, log);
        Lunge(set.GetByMoveId("move_bbab"), 0.4f, log);
        Lunge(set.runNormalPunch, 0.4f, log);

        // ── New moves ────────────────────────────────────────────────────────────────────────
        var heel = Create("Move_SpinHeelKick", "move_spin_heel_kick", "Spinning Heel Kick", CombatRole.SpinHeelKick);
        heel.scalingStat = FighterBuildStat.KickPower;
        heel.startupDuration = 0.24f; heel.activeDuration = 0.14f; heel.recoveryDuration = 0.42f; heel.cancelWindow = 0f;
        heel.movementLocked = true; heel.movementMultiplier = 0f; heel.forwardCarry = 0.25f; heel.lungeDistance = 0.25f;
        heel.allowedPostures = new[] { FighterPosture.Standing };
        heel.strikingLimb = FightStrikingLimb.Feet; // CMU spinning kick: the heel / foot is what lands
        heel.hits = new[] { new FightHitDefinition { localOffset = new Vector3(0.9f, 0.55f, 0f), size = new Vector3(0.75f, 0.5f, 0.7f),
                                                     baseDamage = 15f, baseHitStun = 0.42f, baseKnockback = 1.7f, attackHeight = AttackHeight.High } };
        EditorUtility.SetDirty(heel);

        var fly = Create("Move_FlyingKick", "move_flying_kick", "Flying Kick", CombatRole.FlyingKick);
        fly.scalingStat = FighterBuildStat.KickPower;
        fly.startupDuration = 0.06f; fly.activeDuration = 0.26f; fly.recoveryDuration = 0.3f; fly.cancelWindow = 0f;
        fly.movementLocked = true; fly.movementMultiplier = 0f; fly.airCarry = 1f; fly.forwardCarry = 0.6f;
        fly.allowedPostures = new[] { FighterPosture.Airborne };
        fly.knockdownOnHit = true; fly.guardBreak = true;
        fly.hits = new[] { new FightHitDefinition { localOffset = new Vector3(0.9f, 0.25f, 0f), size = new Vector3(0.85f, 0.9f, 0.7f),
                                                    baseDamage = 18f, baseHitStun = 0.5f, baseKnockback = 2.4f, attackHeight = AttackHeight.Mid } };
        EditorUtility.SetDirty(fly);

        set.backNormalKick = heel;
        set.flyingKick = fly;
        var moves = set.moves.ToList();
        foreach (var m in new[] { heel, fly }) if (!moves.Contains(m)) moves.Add(m);
        set.moves = moves.ToArray();
        EditorUtility.SetDirty(set);
        log.AppendLine("  moves: Back+Kick = spinning heel kick, running jump + Kick = flying kick (guard break, knockdown)");

        // ── Axes / distance / context techniques (user design 2026-11-05) ─────────────────────────
        // Each one starts from its closest move (durations, hits, movement), re-derived every run; FightReachProbe
        // -alignActive then puts the Active window on ITS clip's contact.
        set.punchHighSide  = Derive(set.highNormalPunch, "Move_PunchHighSide",  "move_punch_high_side",  "Hook (High)",       CombatRole.PunchHighSide);
        set.punchMidSide   = Derive(set.normalPunch,     "Move_PunchMidSide",   "move_punch_mid_side",   "Hook (Mid)",        CombatRole.PunchMidSide);
        set.punchLowSide   = Derive(set.lowNormalPunch,  "Move_PunchLowSide",   "move_punch_low_side",   "Hook (Low)",        CombatRole.PunchLowSide);
        set.kickHighCenter = Derive(set.highNormalKick,  "Move_KickHighCenter", "move_kick_high_center", "Side Kick (High)",  CombatRole.KickHighCenter);
        set.kickMidCenter  = Derive(set.normalKick,      "Move_KickMidCenter",  "move_kick_mid_center",  "Front Kick (Mid)",  CombatRole.KickMidCenter);
        set.kickLowCenter  = Derive(set.lowNormalKick,   "Move_KickLowCenter",  "move_kick_low_center",  "Front Kick (Low)",  CombatRole.KickLowCenter);
        set.pushKickMid    = Hit(Derive(set.normalKick,     "Move_PushKickMid",  "move_push_kick_mid",  "Push Kick (Mid)",  CombatRole.KickMidCenter),  AttackHeight.Mid,  1f,   2.4f, false);
        set.pushKickHigh   = Hit(Derive(set.highNormalKick, "Move_PushKickHigh", "move_push_kick_high", "Push Kick (High)", CombatRole.KickHighCenter), AttackHeight.High, 1f,   2.0f, false);
        set.closeUppercut  = Hit(Derive(set.highNormalPunch, "Move_CloseUppercut", "move_close_uppercut", "Uppercut",     CombatRole.Uppercut),       AttackHeight.High, 1.25f, 1.2f, false);
        set.closeKnee      = Hit(Derive(set.normalKick,      "Move_CloseKnee",     "move_close_knee",     "Knee",         CombatRole.Knee),           AttackHeight.Mid,  1.2f,  1f,   false);
        set.spinKickMid    = Hit(Derive(heel,                "Move_SpinKickMid",   "move_spin_kick_mid",  "Spinning Kick (Mid)", CombatRole.SpinHeelKick), AttackHeight.Mid, 0.9f, 1f, false);
        set.spinSweep      = Hit(Derive(heel,                "Move_SpinSweep",     "move_spin_sweep",     "Spinning Sweep", CombatRole.SpinSweep),       AttackHeight.Low,  0.7f,  0.8f, false);
        if (set.spinSweep != null) { set.spinSweep.knockdownOnHit = true; EditorUtility.SetDirty(set.spinSweep); } // a sweep takes the legs
        set.airSpinHeel    = Hit(Derive(set.airNormalKick, "Move_AirSpinHeel", "move_air_spin_heel", "Jumping Spin Heel", CombatRole.AirSpinHeel), AttackHeight.Mid, 1.2f, 1.3f, false);
        // The axe kick has no clip of its own yet (Mixamo has none): the air kick's, falling onto the target. LOW so it
        // also reaches a downed rival; from a jump only the high guard stops it (FighterGuard.WouldBlock fromAbove).
        set.axeKick        = Hit(Derive(set.airNormalKick, "Move_AxeKick",     "move_axe_kick",      "Axe Kick",          set.airNormalKick != null ? set.airNormalKick.role : CombatRole.Kick),     AttackHeight.Low, 1.25f, 1f, false);
        set.counterStrike  = Hit(Derive(set.highNormalPunch, "Move_Counter",  "move_counter",       "Counter",           CombatRole.HighPunch),   AttackHeight.High, 1.1f, 1f, true);
        if (set.counterStrike != null)
        {
            // Faster than any normal: the block already did the reading (total kept — the clip still fits).
            float total = set.counterStrike.startupDuration + set.counterStrike.activeDuration + set.counterStrike.recoveryDuration;
            set.counterStrike.startupDuration = Mathf.Max(0.08f, set.counterStrike.startupDuration * 0.55f);
            set.counterStrike.recoveryDuration = Mathf.Max(0.05f, total - set.counterStrike.startupDuration - set.counterStrike.activeDuration);
            EditorUtility.SetDirty(set.counterStrike);
        }
        set.stepBackSpin   = Derive(set.spinKickMid, "Move_StepBackSpin", "move_step_back_spin", "Step-back Spin", CombatRole.SpinHeelKick);
        if (set.stepBackSpin != null) { set.stepBackSpin.lungeDistance = -0.35f; EditorUtility.SetDirty(set.stepBackSpin); }
        var all = set.moves.ToList();
        foreach (var m in new[] { set.punchHighSide, set.punchMidSide, set.punchLowSide, set.kickHighCenter, set.kickMidCenter, set.kickLowCenter,
                                  set.pushKickMid, set.pushKickHigh, set.closeUppercut, set.closeKnee, set.spinKickMid, set.spinSweep,
                                  set.airSpinHeel, set.axeKick, set.counterStrike, set.stepBackSpin })
            if (m != null && !all.Contains(m)) all.Add(m);
        set.moves = all.ToArray();
        EditorUtility.SetDirty(set);
        log.AppendLine("  axes: side hooks, centre kicks, push kicks, close uppercut / knee, spins mid / sweep, air spin heel, axe kick, counter, step-back spin");

        // ── Special: telegraphed wind-up ─────────────────────────────────────────────────────
        if (set.signatureSpecial != null)
        {
            var sp = set.signatureSpecial;
            sp.startupDuration = 0.9f;
            // Total = the EnergyPush clip (1.65 s) so it plays at speed 1 and the hands thrust (0.87 s) as Active opens.
            sp.recoveryDuration = Mathf.Max(0.1f, 1.65f - sp.startupDuration - sp.activeDuration);
            sp.role = CombatRole.SpecialCharge;
            // The fireball leaves from the hands (FighterAttack uses the real hands when an avatar is bound);
            // this offset is the fallback: chest height (the actor's origin is ≈ 1 m above the floor).
            if (sp.projectile != null) sp.projectile.localSpawnOffset = new Vector3(0.6f, 0.25f, 0f);
            sp.lockFacingDuringMove = true;
            sp.knockdownOnHit = true; // the fireball knocks down when it lands (unblockable: only a sidestep / lying down avoids it)
            EditorUtility.SetDirty(sp);
            log.AppendLine($"  Special '{sp.debugName}': startup 0.9 s (visible wind-up), role SpecialCharge");
        }

        // ── Combo set: two kicks are two kicks ──────────────────────────────────────────────
        var combos = AssetDatabase.LoadAssetAtPath<FightComboSetSO>(ComboSet);
        // The Special's own input (Down+Forward+Punch) beats a "Forward, Punch" lunge read from the same presses.
        var fireball = combos != null ? combos.combos.FirstOrDefault(c => c != null && c.id == "special_fireball") : null;
        if (fireball != null && fireball.priority < 10) { fireball.priority = 10; EditorUtility.SetDirty(combos); log.AppendLine("  combo 'special_fireball' priority 10 (beats Forward+A read from the same presses)"); }
        // First person (user design 2026-11-05): Up is FORWARD and the HIGH modifier at once, so "Forward, Punch" stays the
        // high punch there — the lunge punch is a third-person (side view) combo only.
        var lungePunch = combos != null ? combos.combos.FirstOrDefault(c => c != null && c.id == "forward_a") : null;
        if (lungePunch != null && !lungePunch.thirdPersonOnly) { lungePunch.thirdPersonOnly = true; EditorUtility.SetDirty(combos); log.AppendLine("  combo 'forward_a' third person only"); }
        // First person only swaps the axis (Up/Down = towards / away; Left/Right = side strike, side guard, double tap sidestep).
        var flow = Resources.Load<AppConfigSO>("AppConfig")?.fightFlow;
        if (flow != null && !flow.firstPersonViewRelativeControls) { flow.firstPersonViewRelativeControls = true; EditorUtility.SetDirty(flow); log.AppendLine("  first person: view-relative controls ON (axis swap, side strikes / guard)"); }
        if (combos != null && combos.combos.Any(c => c != null && c.id == "bb"))
        {
            combos.combos = combos.combos.Where(c => c == null || c.id != "bb").ToArray();
            EditorUtility.SetDirty(combos);
            log.AppendLine("  combo 'bb' removed (K K = two kicks; K K K fast = kick + strong kick)");
        }

        // ── Arena: snappier jump, the tackle staggers instead of flooring ───────────────────
        var arena = AssetDatabase.LoadAssetAtPath<FightArenaConfig>(Arena);
        if (arena != null)
        {
            arena.jumpVelocity = 6.5f;
            arena.gravity = 22f;
            arena.airAcceleration = 6f;
            arena.tackleKnocksDown = false;
            EditorUtility.SetDirty(arena);
            log.AppendLine("  arena: jump 6.5 m/s, gravity 22 (≈0.6 s in the air), tackle = stagger (no knockdown)");
        }

        // ── Balance: short knockdown flow, guard speed, strong-hit values ───────────────────
        var bal = AssetDatabase.LoadAssetAtPath<FightCombatBalanceConfig>(Balance);
        if (bal != null)
        {
            bal.knockdownDuration = 0.8f;
            bal.downedDuration = 0.45f;
            bal.getUpDuration = 0.9f;
            // 2026-11-05 "tot va en funció de les reaccions": a held guard sees the strike (0.08 s) then turns (0.16 s), all
            // ÷ Agility, racing the strike's startup (0.22–0.30 s ÷ Agility): equal fighters — some blocked, some not; the
            // faster one wins; a late press (the AI's reaction time, the player's own) decides the rest.
            bal.guardSideReactTime = 0.16f;
            bal.guardSidePerception = 0.08f;
            // Close-range techniques only where they land: the uppercut reaches 0.73 m (FightStrikeContactProbe).
            bal.closeStrikeRange = 0.72f;
            EditorUtility.SetDirty(bal);
            log.AppendLine("  knockdown flow 0.8 + 0.45 + 0.9 s (was 1.2 + 0.8 + 1.6)");
        }

        // ── Distance: bodies can come close enough to really touch (FightReachProbe: the clips reach
        //    ≈ 0.5–0.75 m hips-to-hips; the old 1 m minimum meant hits "landed" with nothing touching).
        if (arena != null)
        {
            // Mixamo strikes reach 0.72–1.5 m hips-to-hips (FightReachProbe): bodies no longer need to overlap.
            arena.minimumFighterSeparation = 0.60f; // roots; hips ≈ 0.7 m apart at the closest
            arena.tackleContactPadding = 0.12f;
            EditorUtility.SetDirty(arena);
            log.AppendLine("  arena: minimumFighterSeparation 0.60 m (roots; Mixamo strikes reach ≥ 0.72 m hips-to-hips)");
        }
        // Data hitboxes = the same reach as the real bodies (placeholder capsules, AI range estimate).
        foreach (var m in new[] { set.normalPunch, set.highNormalPunch, set.lowNormalPunch, set.runNormalPunch, set.airNormalPunch })
            Reach(m, 0.5f);
        foreach (var m in new[] { set.normalKick, set.highNormalKick, set.lowNormalKick, downB, set.runNormalKick, set.airNormalKick, heel, fly })
            Reach(m, 0.55f);
        foreach (var m in set.moves)
            if (m != null && m.attackDelivery == AttackDelivery.Melee && m.hits != null && m.hits.Length > 0 && m.hits[0] != null && m.hits[0].localOffset.x > 0.6f)
                Reach(m, 0.55f);

        log.Append(Animations());
        // 2026-11-05: strikes aimed STRAIGHT at the opponent in the game's frame (several went out to the side).
        log.Append(FightStrikeAim.Run(AssetDatabase.LoadAssetAtPath<FighterAnimationSetSO>(CombatContentSetup.AnimationSetPath)));

        // Mixamo clips (≈0.6 s punch / 0.7–0.8 s kick windows): move totals close to the clip length (speed ≈ 1–1.2).
        // FightReachProbe -alignActive then puts Active on each clip's real impact, keeping these totals.
        Retime(set.normalPunch, 0.62f, log); Retime(set.highNormalPunch, 0.62f, log); Retime(set.lowNormalPunch, 0.62f, log);
        Retime(set.normalKick, 0.70f, log); Retime(set.highNormalKick, 0.72f, log); Retime(set.lowNormalKick, 0.60f, log); Retime(downB, 0.60f, log);
        Retime(heel, 0.66f, log);   // 2026-11-05 "la fa molt lenta": window starts nearer the turn, impact ≈ 0.3 s
        // The KICK-ending combos (B = Kick: scalingStat KickPower → contact is checked on the FEET) played the strong PUNCH
        // clip, so no foot ever touched: they get kick clips (mid / high) and kick timings.
        KickCombo(set.GetByMoveId("move_bb"), CombatRole.Kick, 0.70f, log);
        KickCombo(set.GetByMoveId("move_bbab"), CombatRole.HighKick, 0.74f, log);
        KickCombo(set.GetByMoveId("move_back_aab"), CombatRole.Kick, 0.72f, log);

        log.Append(RivalLevelScaling(bal));

        AssetDatabase.SaveAssets();
        log.AppendLine("PASS");
        return log.ToString();
    }

    // 2026-11-05 playtest: "quan més nivell té, més combos, especials i tal" — the rival's charges per round and its combo
    // skill grow clearly with the fight difficulty (0.15 = first match … 0.85 = last level, 1 = final rival). The sparks
    // come back ("no tinc sensació de cop"); the hit-stop stays off.
    private static string RivalLevelScaling(FightCombatBalanceConfig bal)
    {
        if (bal == null) return "";
        bal.rivalSpecialsPerRound = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.65f, 2f), new Keyframe(1f, 2.5f));
        bal.rivalTriplesPerRound = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 1f), new Keyframe(0.7f, 2f), new Keyframe(1f, 3f));
        bal.rivalQuadsPerRound = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.75f, 1f), new Keyframe(1f, 1.5f));
        bal.rivalComboSkill = new AnimationCurve(new Keyframe(0.15f, 0.1f), new Keyframe(0.5f, 0.45f), new Keyframe(0.85f, 0.8f), new Keyframe(1f, 0.92f));
        bal.hitSparkEnabled = true;
        bal.hitStopEnabled = false;
        EditorUtility.SetDirty(bal);
        return "  rival by level: Specials/round 0 (<0.3) → 1 → 2 (≥0.65); x3 0 (<0.4) → 1 → 2 (≥0.7) → 3; x4 only ≥0.75; combo skill 0.1 → 0.92; sparks ON\n";
    }

    private static void Reach(FightMoveDefinition m, float x)
    {
        if (m == null || m.hits == null) return;
        foreach (var h in m.hits) if (h != null) h.localOffset = new Vector3(x, h.localOffset.y, h.localOffset.z);
        EditorUtility.SetDirty(m);
    }

    // ── Animations: new windows from the packs we have (mocap scan 2026-11-04, scratchpad/mocap/segments.json).
    //    Each window is cut so the strike peaks ≈ 35 % in (FightReachProbe aligns the Active windows after).
    private const string Shadow = "Fight_ShadowBoxing_mixamo", Boxing = "Fight_Boxing_mixamo", IronMan = "Superhero_IronMan_Combat_mixamo";
    // CMU strikes (Animations/Source/CMU/Fight, one FBX per strike already cut around it; the window here
    // trims the lead-in so the strike peaks early enough). Entry name = the FBX name.
    private const string CmuHighPunch = "CMU_HighPunch_best_15_13", CmuLowKick = "CMU_LowKick_best_143_24", CmuMidKick = "CMU_MidKick_best_143_24",
                         CmuHighKick = "CMU_HighKick_best_135_07", CmuSpin = "CMU_SpinHeelKick_best_90_06", CmuFlying = "CMU_FlyingKick_best_75_16";
    // Alternates (same scan, rank "alt"): each window keeps the strike peak at the SAME fraction of the clip as its
    // base role (the move's Active window is shared), with a similar length.
    private const string CmuMidPunchAlt = "CMU_MidPunch_alt_14_01", CmuHighPunchAlt = "CMU_HighPunch_alt_13_18", CmuLowKickAlt = "CMU_LowKick_alt_74_03",
                         CmuMidKickAlt = "CMU_MidKick_alt_143_24", CmuHighKickAlt = "CMU_HighKick_alt_135_11";
    private static readonly (string source, string name, float start, float end, CombatAnimationCategory cat, bool loop, CombatRole role, string note, bool keepFacing, bool heightToMotion)[] Segments =
    {
        (Shadow, "Cross",        6.50f, 7.10f, CombatAnimationCategory.AttackPunch, false, CombatRole.LightAttack, "right cross to the chest (peak 6.71) — MID punch", true, false),
        (Shadow, "BodyShot",    11.50f, 12.15f, CombatAnimationCategory.AttackPunch, false, CombatRole.LowPunch, "crouching right body shot (peak 11.75) — LOW punch", true, false),
        (Boxing, "FootworkFwd",  0.917f, 1.667f, CombatAnimationCategory.Movement, true, CombatRole.GuardWalkForward, "guarded forward footwork loop", true, false),
        (Boxing, "FootworkBack", 3.208f, 4.042f, CombatAnimationCategory.Movement, true, CombatRole.GuardWalkBack, "guarded backward footwork loop", true, false),
        (IronMan, "EnergyPush", 33.75f, 35.40f, CombatAnimationCategory.Special, false, CombatRole.SpecialCharge, "both hands drawn back to the hip (~0.7 s gather), then thrust forward at mid height (release ≈ 34.62 = 0.87 s in) — the Special's wind-up + throw", true, false),
        (CmuHighPunch, "HighPunch",    0.15f, 0.95f, CombatAnimationCategory.AttackPunch, false, CombatRole.HighPunch, "CMU 15_13: right punch rising from low guard to the face (peak 0.47) — HIGH punch", true, false),
        (CmuLowKick,   "LowKick",      0.05f, 0.78f, CombatAnimationCategory.AttackKick, false, CombatRole.LowKick, "CMU 143_24: low front kick to the ankle / shin (foot ≈ 0.3 m, peak 0.30) — LOW kick", true, false),
        (CmuMidKick,   "MidKick",      0.05f, 0.77f, CombatAnimationCategory.AttackKick, false, CombatRole.Kick, "CMU 143_24: kick to the hip / waist (≈ 1.05 m, peak 0.33) — MID kick", true, false),
        (CmuHighKick,  "HighKick",     0.10f, 0.85f, CombatAnimationCategory.AttackKick, false, CombatRole.HighKick, "CMU 135_07: roundhouse to the head (≈ 1.5 m, peak 0.41) — HIGH kick (facing kept: the kata turns 107°)", true, false),
        (CmuSpin,      "SpinHeelKick", 0.15f, 1.10f, CombatAnimationCategory.AttackKick, false, CombatRole.SpinHeelKick, "CMU 90_06: jumping spin kick, heel ≈ 1.9 m at ~220° of turn (peak 0.66) — the spin is baked into the pose", false, false),
        (CmuFlying,    "FlyingKick",   0.25f, 0.90f, CombatAnimationCategory.AttackKick, false, CombatRole.FlyingKick, "CMU 75_16: jump kick, leg extended forward (peak 0.46) — height left to the gameplay jump", true, true),
        (CmuMidPunchAlt,  "CrossStep",    0.08f, 0.80f, CombatAnimationCategory.AttackPunch, false, CombatRole.LightAttackAlt, "CMU 14_01: boxer right cross with a small step in, chest height (peak 0.41) — MID punch alt", true, false),
        (Boxing,          "BodyHook",    13.50f, 14.15f, CombatAnimationCategory.AttackPunch, false, CombatRole.LowPunchAlt, "Boxing: right hook-ish body punch, upper belly (peak 13.75) — LOW punch alt", true, false),
        (CmuHighPunchAlt, "HeadStraight", 0.10f, 0.90f, CombatAnimationCategory.AttackPunch, false, CombatRole.HighPunchAlt, "CMU 13_18: right straight/hook to the head with a step in (peak 0.42) — HIGH punch alt", true, false),
        (CmuLowKickAlt,   "ShinKick",     0.08f, 0.81f, CombatAnimationCategory.AttackKick, false, CombatRole.LowKickAlt, "CMU 74_03: right swing kick at shin / knee height (peak 0.33) — LOW kick alt", true, false),
        (CmuMidKickAlt,   "HipKick",      0.00f, 0.65f, CombatAnimationCategory.AttackKick, false, CombatRole.KickAlt, "CMU 143_24: right kick at the hip, clean and stationary (peak 0.49) — MID kick alt", true, false),
        (CmuHighKickAlt,  "SideKickHead", 0.12f, 0.85f, CombatAnimationCategory.AttackKick, false, CombatRole.HighKickAlt, "CMU 135_11: yoko-geri side kick at head height (peak 0.43) — HIGH kick alt", true, false),

        // ── MIXAMO (playtest 2026-11-04: the boxing/CMU strikes above went out ≈90° to the side in the game's frame —
        //    MixamoStrikeScan "STRIKE YAW" — and reached ≈0.6 m; these go straight, −4°..+9°, and reach 0.9–1.35 m).
        //    Listed LAST: they override the roles above. Windows ≈ 0.33 s (punch: ≈0.2 s startup, readable and blockable like a Tekken jab) / 0.35–0.45 s (kick) before the peak.
        (MxMidPunch,      "Strike", 0.04f, 0.72f, CombatAnimationCategory.AttackPunch, false, CombatRole.LightAttack,    "Mixamo Cross Punch: right cross, chest height (peak 0.37) — MID punch", true, false),
        (MxMidPunchAlt,   "Strike", 0.32f, 1.00f, CombatAnimationCategory.AttackPunch, false, CombatRole.LightAttackAlt, "Mixamo Short Jab Cross: the cross (peak 0.67) — MID punch alt", true, false),
        (MxHighPunch,     "Strike", 0.10f, 0.75f, CombatAnimationCategory.AttackPunch, false, CombatRole.HighPunch,      "Mixamo Med Head Jab: lead jab to the face, 1.35 m (peak 0.43) — HIGH punch", true, false),
        (MxHighPunchAlt,  "Strike", 0.32f, 1.05f, CombatAnimationCategory.AttackPunch, false, CombatRole.HighPunchAlt,   "Mixamo Boxing Back Hand Uppercut (peak 0.70) — HIGH punch alt", true, false),
        (MxLowPunch,      "Strike", 0.17f, 0.85f, CombatAnimationCategory.AttackPunch, false, CombatRole.LowPunch,       "Mixamo Mid Body Jab: lead jab to the belly, 0.91 m (peak 0.50) — LOW punch", true, false),
        (MxLowPunchAlt,   "Strike", 0.37f, 1.05f, CombatAnimationCategory.AttackPunch, false, CombatRole.LowPunchAlt,    "Mixamo Short Hook Punch To The Body (peak 0.73) — LOW punch alt", true, false),
        (MxHeavyPunch,    "Strike", 0.68f, 1.30f, CombatAnimationCategory.AttackPunch, false, CombatRole.HeavyAttack,    "Mixamo Boxing Lead Hand Uppercut, steps in (peak 0.90) — strong punch", true, false),
        (MxHeavyPunchAlt, "Strike", 0.70f, 1.35f, CombatAnimationCategory.AttackPunch, false, CombatRole.HeavyAttackAlt, "Mixamo Long Hook Punch To The Head, steps in (peak 0.93, reach 1.28) — strong punch alt", true, false),
        (MxMidKick,       "Strike", 0.30f, 1.05f, CombatAnimationCategory.AttackKick,  false, CombatRole.Kick,           "Mixamo Mma Medium Kick: rear-leg kick to the ribs, 1.12 m (peak 0.70) — MID kick", true, false),
        (MxMidKickAlt,    "Strike", 0.25f, 0.95f, CombatAnimationCategory.AttackKick,  false, CombatRole.KickAlt,        "Mixamo Roundhouse Kick With The Rear Foot, 0.98 m (peak 0.63) — MID kick alt", true, false),
        (MxHighKick,      "Strike", 0.33f, 1.10f, CombatAnimationCategory.AttackKick,  false, CombatRole.HighKick,       "Mixamo Mma High Kick: head kick, 1.38 m (peak 0.73) — HIGH kick", true, false),
        (MxHighKickAlt,   "Strike", 0.25f, 1.00f, CombatAnimationCategory.AttackKick,  false, CombatRole.HighKickAlt,    "Mixamo Mma Side Kick at the head, 1.31 m (peak 0.63) — HIGH kick alt", true, false),
        (MxLowKick,       "Strike", 0.25f, 0.90f, CombatAnimationCategory.AttackKick,  false, CombatRole.LowKick,        "Mixamo Mma Low Kick: thigh / knee, 0.62 m (peak 0.60) — LOW kick", true, false),
        (MxSpinKick,      "Strike", 0.62f, 1.40f, CombatAnimationCategory.AttackKick,  false, CombatRole.SpinHeelKick,   "Mixamo Mma Spinning Back Kick, full turn baked into the pose, 1.49 m (peak 0.97) — Back + Kick", false, false),
        (MxHitHeadLight,  "React",  0.00f, 0.80f, CombatAnimationCategory.HitReaction, false, CombatRole.HitReaction,    "Mixamo Light Hit To Head (straight punch) — hit reaction", true, false),
        (MxHitBody,       "React",  0.00f, 1.00f, CombatAnimationCategory.HitReaction, false, CombatRole.HitReactionAlt, "Mixamo Receive Punch To The Body — hit reaction alt", true, false),
        // 2026-11-05 (first-person side guard): each block is cut at the moment the arms are up (held there — the clip is
        // not looped), before the head leans furthest away (a guard must still be TOUCHED to block).
        (MxGuardHighLeft,  "Guard", 0.00f, 0.35f, CombatAnimationCategory.Block, false, CombatRole.GuardHighLeft,  "Mixamo High Left Block: both forearms up on the left of the head (arms up ≈ 0.40)", true, false),
        (MxGuardHighRight, "Guard", 0.00f, 0.25f, CombatAnimationCategory.Block, false, CombatRole.GuardHighRight, "Mixamo High Right Block: forearms up on the right of the head (arms up ≈ 0.28)", true, false),
        (MxGuardLowLeft,   "Guard", 0.00f, 0.38f, CombatAnimationCategory.Block, false, CombatRole.GuardLowLeft,   "Mixamo Low Left Block: forearms down over the left ribs (≈ 0.45)", true, false),
        (MxGuardLowRight,  "Guard", 0.00f, 0.30f, CombatAnimationCategory.Block, false, CombatRole.GuardLowRight,  "Mixamo Low Right Block: forearms down over the right ribs (≈ 0.33)", true, false),
        (MxDodgeRight,     "Sway",  0.10f, 1.00f, CombatAnimationCategory.Dodge, false, CombatRole.Sidestep,       "Mixamo Dodging Right: the torso sways right (head +0.42 m at 0.55) and back — the sidestep (mirrored for the left)", true, false),
        (MxHitHeadRight,   "React", 0.00f, 1.00f, CombatAnimationCategory.HitReaction, false, CombatRole.HitReactionSide, "Mixamo Medium Hit To The Head From A Right Punch: head snaps to the right (mirrored for a left hand)", true, false),
    };
    // Axes pack (downloaded 2026-11-05, Blender scan scratchpad fight2/analyze_strikes.py): hooks = SIDE punches, the
    // cross = 2nd high (centre) punch, uppercut / knee = close range, the 360 sweep, the aerial crescent spin.
    private static readonly (string source, string name, float start, float end, CombatAnimationCategory cat, bool loop, CombatRole role, string note, bool keepFacing, bool heightToMotion)[] AxisSegments =
    {
        ("Mixamo_HookLead",     "Strike", 0.15f, 0.95f, CombatAnimationCategory.AttackPunch, false, CombatRole.PunchHighSide, "Mixamo Boxing Lead Hand Hook: left hook to the head (1.49 m, peak 0.50) — SIDE high punch", true, false),
        ("Mixamo_HookBack",     "Strike", 0.15f, 0.95f, CombatAnimationCategory.AttackPunch, false, CombatRole.PunchMidSide,  "Mixamo Boxing Back Hand Hook: right hook, 1.06 m (peak 0.73) — SIDE mid punch / close short hook", true, false),
        ("Mixamo_CrossPunch",   "Strike", 0.05f, 0.80f, CombatAnimationCategory.AttackPunch, false, CombatRole.HighPunchAlt,  "Mixamo Cross Punch: right cross to the face (1.20 m, peak 0.37) — CENTRE high punch alt", true, false),
        ("Mixamo_UppercutBack", "Strike", 0.35f, 1.10f, CombatAnimationCategory.AttackPunch, false, CombatRole.Uppercut,      "Mixamo Boxing Back Hand Uppercut (peak 0.70) — close range, centre", true, false),
        ("Mixamo_KneeMuayThai", "Strike", 0.35f, 1.15f, CombatAnimationCategory.AttackKick,  false, CombatRole.Knee,          "Mixamo Muay Thai Illegal Knee: right knee up (peak 0.73) — close range", true, false),
        ("Mixamo_Sweep360",     "Strike", 0.40f, 1.60f, CombatAnimationCategory.AttackKick,  false, CombatRole.SpinSweep,     "Mixamo 360 Degrees Back Leg Sweep: crouched full turn, foot at the floor (peak 1.03) — Back + Down + Kick", false, false),
        ("Mixamo_AirSpinKick",  "Strike", 0.90f, 1.80f, CombatAnimationCategory.AttackKick,  false, CombatRole.AirSpinHeel,   "Mixamo Aerial 360 Front Side Rotation Kick (inside crescent): right foot 1.29 m (peak 1.33, airborne 1.17–1.67) — back jump + Kick", false, false),
    };

    // Mixamo sided defence pack (downloaded 2026-11-05): blocks per side and height, the sway, a sided head hit.
    private const string MxGuardHighLeft = "Mixamo_BlockHighLeft", MxGuardHighRight = "Mixamo_BlockHighRight", MxGuardLowLeft = "Mixamo_BlockLowLeft",
                         MxGuardLowRight = "Mixamo_BlockLowRight", MxDodgeRight = "Mixamo_DodgeRight", MxHitHeadRight = "Mixamo_HitHeadRight";
    // Mixamo fight pack (Animations/Source/Mixamo/Fight, one FBX per move — downloaded 2026-11-04).
    private const string MxMidPunch = "Mixamo_MidPunch", MxMidPunchAlt = "Mixamo_MidPunchAlt", MxHighPunch = "Mixamo_HighPunch", MxHighPunchAlt = "Mixamo_HighPunchAlt",
                         MxLowPunch = "Mixamo_LowPunch", MxLowPunchAlt = "Mixamo_LowPunchAlt", MxHeavyPunch = "Mixamo_HeavyPunch", MxHeavyPunchAlt = "Mixamo_HeavyPunchAlt",
                         MxMidKick = "Mixamo_MidKick", MxMidKickAlt = "Mixamo_MidKickAlt", MxHighKick = "Mixamo_HighKick", MxHighKickAlt = "Mixamo_HighKickAlt",
                         MxLowKick = "Mixamo_LowKick", MxSpinKick = "Mixamo_SpinKick", MxHitHeadLight = "Mixamo_HitHeadLight", MxHitBody = "Mixamo_HitBody";

    private static string Animations()
    {
        var log = new StringBuilder();
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        if (library == null) return "  FAIL no combat animation library" + System.Environment.NewLine;
        bool added = false;
        foreach (var s in Segments.Concat(AxisSegments))
        {
            bool cmu = s.source.StartsWith("CMU_");
            if (cmu && !File.Exists($"{CombatAnimationImporter.CmuSourceFolder}/{s.source}.fbx")) { log.AppendLine($"  WARNING CMU source {s.source}.fbx missing"); continue; }
            if (!cmu && library.Find(s.source) == null) { log.AppendLine($"  WARNING source entry {s.source} missing"); continue; }
            if (library.Find(s.source + "__" + s.name) == null) added = true;   // never imported yet
            var seg = library.segments.FirstOrDefault(x => x.sourceEntry == s.source && x.name == s.name);
            if (seg == null) { seg = new CombatSegmentDefinition { sourceEntry = s.source, name = s.name }; library.segments.Add(seg); added = true; }
            string notes = "Mocap scan 2026-11-04: " + s.note;
            if (seg.start != s.start || seg.end != s.end || seg.category != s.cat || seg.loop != s.loop || seg.keepFacing != s.keepFacing ||
                seg.heightToRootMotion != s.heightToMotion || seg.notes != notes)
            {
                seg.start = s.start; seg.end = s.end; seg.category = s.cat; seg.loop = s.loop; seg.keepFacing = s.keepFacing;
                seg.heightToRootMotion = s.heightToMotion; seg.review = CombatReviewStatus.Approved; seg.notes = notes;
                added = true;
            }
        }
        if (added)
        {
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            CombatAnimationImporter.ImportAll(out bool ok);
            log.AppendLine($"  combat library: new segments imported (ok {ok})");
            library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        }

        var animSet = AssetDatabase.LoadAssetAtPath<FighterAnimationSetSO>(CombatContentSetup.AnimationSetPath);
        var clips = animSet.clips.ToList();
        void Map(CombatRole role, string entry, string note)
        {
            var e = library.Find(entry);
            if (e == null || e.inPlaceClip == null) { log.AppendLine($"  WARNING {entry} has no in-place clip"); return; }
            clips.RemoveAll(c => c.role == role);
            clips.Add(new FighterAnimationSetSO.RoleClip { role = role, clip = e.inPlaceClip, provisional = false, note = note });
            log.AppendLine($"  {role} ← {entry}");
        }
        foreach (var s in Segments.Concat(AxisSegments)) Map(s.role, s.source + "__" + s.name, s.note);
        // Existing clips that are the other half of an axis: the short body hook = SIDE low punch, the MMA side kick at the
        // head = CENTRE high kick, the big front kick = CENTRE mid kick, the CMU low front kick = CENTRE low kick.
        Map(CombatRole.PunchLowSide,   MxLowPunchAlt + "__Strike", "Mixamo Short Hook Punch To The Body — SIDE low punch");
        Map(CombatRole.KickHighCenter, MxHighKickAlt + "__Strike", "Mixamo Mma Side Kick at the head — CENTRE high kick");
        Map(CombatRole.KickMidCenter,  "Fight_BigFrontKick_mixamo__FrontKick", "Rokoko big front kick — CENTRE mid kick / push kick");
        Map(CombatRole.KickLowCenter,  CmuLowKick + "__LowKick", "CMU 143_24 low front kick to the shin — CENTRE low kick");
        // The CMU shin kick alt went out at −35° with the guard backwards, and Mixamo's leg sweep turns 137° with 1 m of
        // travel: no alternate low kick for now — the role falls back to LowKick (AnimatorFighterAnimationDriver.Fallback).
        if (clips.RemoveAll(c => c.role == CombatRole.LowKickAlt) > 0) log.AppendLine("  LowKickAlt removed (falls back to LowKick)");
        // 2026-11-05 playtest "els cops de puny baixos no fan gaire": the body-hook alt only reached 0.70 m (vs 1.15 m for
        // the body jab), so every other low punch whiffed at a normal distance — one reliable clip instead.
        if (clips.RemoveAll(c => c.role == CombatRole.LowPunchAlt) > 0) log.AppendLine("  LowPunchAlt removed (falls back to LowPunch)");
        // Same for the uppercut alt of the high punch: in play it whiffed at every distance (FightStrikeContactProbe 2026-11-05).
        // (HighPunchAlt is now the Mixamo cross to the face — AxisSegments; the old uppercut alt whiffed.)
        // Guard stays the ShadowBoxing cover: the Mixamo High Center Block moves the head/torso out of the punch's path,
        // so a guarded hit never even touched the body (FightCombatTests "Block" failed) — to revisit with a better guard clip.
        Map(CombatRole.Block, Shadow + "__CoverBlock", "ShadowBoxing high cover (kept: the Mixamo block made punches whiff)");
        // Sided presentation clips: authored to the RIGHT (the sway goes right; the head hit comes from a right hand) —
        // the other side plays them mirrored (AnimatorFighterAnimationDriver.PlaySided).
        foreach (var c in clips)
            if (c.role == CombatRole.Sidestep || c.role == CombatRole.HitReactionSide) c.strikeSide = FighterAnimationSetSO.ClipStrikeSide.Right;
        animSet.clips = clips.ToArray();
        EditorUtility.SetDirty(animSet);
        return log.ToString();
    }

    private static void Normal(FightMoveDefinition m, CombatRole role, AttackHeight height, Vector3 offset, Vector3 size, float walkMultiplier, StringBuilder log)
    {
        if (m == null) return;
        m.role = role;
        m.movementLocked = false;            // you can keep walking forward / back while striking
        m.movementMultiplier = walkMultiplier;
        // The next attack / the strong hit chains out of the second HALF of Recovery (was 90 %: with queued presses now
        // starting as soon as the window opens, a mashed jab became an unblockable true-combo loop — RivalAIProbe).
        m.cancelWindow = m.recoveryDuration * 0.5f;
        if (m.hits != null && m.hits.Length > 0 && m.hits[0] != null)
        {
            m.hits[0].localOffset = offset;
            m.hits[0].size = size;
            m.hits[0].attackHeight = height;
        }
        EditorUtility.SetDirty(m);
        log.AppendLine($"  {m.debugName}: role {role}, {height}, walk ×{walkMultiplier}, cancel {m.cancelWindow:0.00}s");
    }

    private static void KickCombo(FightMoveDefinition m, CombatRole role, float total, StringBuilder log)
    {
        if (m == null) return;
        m.role = role;
        m.animationState = role.ToString();
        Retime(m, total, log, 0.18f);
    }

    private static void Retime(FightMoveDefinition m, float total, StringBuilder log, float startup = -1f)
    {
        if (m == null) return;
        m.startupDuration = startup >= 0f ? startup : Mathf.Min(m.startupDuration, total - m.activeDuration - 0.1f);
        m.recoveryDuration = Mathf.Max(0.1f, total - m.startupDuration - m.activeDuration);
        if (m.cancelWindow > 0f) m.cancelWindow = m.recoveryDuration * 0.5f;
        EditorUtility.SetDirty(m);
        log.AppendLine($"  {m.debugName}: total {m.TotalDuration:0.00} s");
    }

    private static void Lunge(FightMoveDefinition m, float distance, StringBuilder log)
    {
        if (m == null) return;
        m.lungeDistance = distance;
        EditorUtility.SetDirty(m);
        log.AppendLine($"  {m.debugName}: lunge {distance} m (capped at contact)");
    }

    /// <summary>A move derived from `src` (every serialized field copied, hits deep-copied), with its own id / name / role.</summary>
    private static FightMoveDefinition Derive(FightMoveDefinition src, string file, string id, string debugName, CombatRole role)
    {
        if (src == null) return null;
        var m = Create(file, id, debugName, role);
        EditorUtility.CopySerialized(src, m);
        m.name = file;
        m.id = id; m.debugName = debugName; m.role = role; m.animationState = role.ToString();
        EditorUtility.SetDirty(m);
        return m;
    }

    private static FightMoveDefinition Hit(FightMoveDefinition m, AttackHeight height, float damage, float knockback, bool unblockable)
    {
        if (m == null || m.hits == null) return m;
        foreach (var h in m.hits)
        {
            if (h == null) continue;
            h.attackHeight = height;
            h.baseDamage *= damage;
            h.baseKnockback *= knockback;
            if (unblockable) h.guardType = GuardType.Unblockable;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static FightMoveDefinition Create(string file, string id, string debugName, CombatRole role)
    {
        string path = $"{MovesDir}/{file}.asset";
        var m = AssetDatabase.LoadAssetAtPath<FightMoveDefinition>(path);
        if (m == null) { m = ScriptableObject.CreateInstance<FightMoveDefinition>(); AssetDatabase.CreateAsset(m, path); }
        m.id = id; m.debugName = debugName; m.role = role; m.animationState = role.ToString();
        m.moveType = FightMoveType.Normal; m.lockFacingDuringMove = true;
        return m;
    }
}
                                                                                                                                                                      