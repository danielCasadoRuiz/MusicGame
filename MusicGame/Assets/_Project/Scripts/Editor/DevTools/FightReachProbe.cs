using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Edit-mode measurement (no Play mode, no save touched): for every attack of the default move set, samples
/// its clip on the canonical MakeHuman avatar exactly as AnimatorFighterAnimationDriver plays it (clip
/// fitted to the move's duration, speed clamped by moveSpeedRange) and finds, with FighterBody, the LARGEST
/// centre-to-centre distance at which the striking limb still touches a defender standing in guard
/// during the Active window (+ FighterAttack's late-contact margin). That is each move's real reach —
/// compared with FightArenaConfig.minimumFighterSeparation (the closest two fighters can be).
/// Batch: -executeMethod FightReachProbe.RunFromCommandLine  (Logs/FightReachProbe.txt)
/// </summary>
public static class FightReachProbe
{
    private const string AvatarPath = "Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx";

    // 2026-11-05: the CMU low front kick lands every time at ≤ 0.75 m, misses 13–27 cm short at 0.9 m (its foot goes out
    // 25 cm to the side) although this static probe measures 1.07 m.
    private static readonly System.Collections.Generic.Dictionary<string, float> PlayReachCap = new() { ["move_kick_low_center"] = 0.80f };

    [MenuItem("Tools/MusicGame/Combat/Probe Move Reach (real bodies)")]
    public static void Menu() => Debug.Log(Run());

    /// <summary>-alignActive: also moves each move's Active window onto its clip's moment of deepest
    /// contact (same total duration; startup ≥ 0.06 s) and saves the moves.</summary>
    public static void RunFromCommandLine()
    {
        s_align = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-alignActive") >= 0;
        string report;
        try { report = Run(); } catch (System.Exception e) { report = "FAIL " + e; }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/FightReachProbe.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var flow = app.fightFlow;
        var set = flow.defaultMoveSet;
        var animSet = flow.defaultPlayerCombatProfile != null ? flow.defaultPlayerCombatProfile.animationSet : null;
        var arena = AssetDatabase.LoadAssetAtPath<FightArenaConfig>("Assets/_Project/Configs/Fight/FightArenaConfig.asset");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CanonicalAvatarSetup.PrefabPath); // PlayableGraph-ready (the importer's avatar)
        if (set == null || animSet == null || model == null) return "FAIL missing move set / animation set / avatar";

        var attacker = Spawn(model, Vector3.zero, Vector3.forward, out var aAnim);
        var defender = Spawn(model, Vector3.forward, Vector3.back, out var dAnim);
        var log = new StringBuilder("[FightReachProbe] reach = largest centre distance with real contact (m)\n");
        log.AppendLine($"  minimumFighterSeparation {arena.minimumFighterSeparation}");
        try
        {
            var aBody = new FighterBody(aAnim);
            var dBody = new FighterBody(dAnim);
            if (!aBody.IsValid || !dBody.IsValid) return "FAIL avatar bones";
            var idle = Clip(animSet, CombatRole.CombatIdle);
            // 2026-11-05: NO pose alignment — the game faces the actor ROOT at the opponent (FightStrikeContactProbe DIAG);
            // aligning by the guard pose turned the avatar ≈35° and hid every strike that goes out to the side.
            Pose(aAnim, idle, 0.3f);
            s_attackerRef = aAnim.GetBoneTransform(HumanBodyBones.Hips).position;
            s_attackerAnimator = aAnim;
            s_anchorHips = true;
            Pose(dAnim, idle, 0.3f);
            s_defenderOffset = dAnim.GetBoneTransform(HumanBodyBones.Hips).position - defender.transform.position;
            log.AppendLine($"  distances are hips-to-hips in guard (≈ the fighters' root distance)");
            log.AppendLine($"  guard pose: attacker facing {PoseForward(aAnim)}, hips z {aAnim.GetBoneTransform(HumanBodyBones.Hips).position.z:0.00} head z {aAnim.GetBoneTransform(HumanBodyBones.Head).position.z:0.00} hands z {aAnim.GetBoneTransform(HumanBodyBones.LeftHand).position.z:0.00}/{aAnim.GetBoneTransform(HumanBodyBones.RightHand).position.z:0.00}");

            var moves = new System.Collections.Generic.List<FightMoveDefinition>
            {
                set.normalPunch, set.highNormalPunch, set.lowNormalPunch, set.normalKick, set.highNormalKick, set.lowNormalKick,
                set.backNormalKick, set.runNormalPunch, set.runNormalKick, set.airNormalPunch, set.airNormalKick, set.flyingKick,
            };
            foreach (var m in set.moves) if (m != null && !moves.Contains(m) && m.hits != null && m.hits.Length > 0 && m.attackDelivery == AttackDelivery.Melee) moves.Add(m);

            var reachByMove = new System.Collections.Generic.Dictionary<FightMoveDefinition, float>();
            foreach (var move in moves)
            {
                if (move == null) continue;
                var baseRole = Resolve(animSet, move.role);
                // Also measures the role's ALTERNATE clip (AnimatorFighterAnimationDriver.AltOf): it shares the move's
                // Active window, so its strike must land inside it too. Only the base clip drives -alignActive.
                var alt = AnimatorFighterAnimationDriver.AltOf(baseRole);
                foreach (var role in alt != baseRole && Clip(animSet, alt) != null ? new[] { baseRole, alt } : new[] { baseRole })
                {
                bool isAlt = role != baseRole;
                var clip = Clip(animSet, role);
                if (clip == null) { log.AppendLine($"  {move.debugName,-34} no clip"); continue; }
                float total = move.TotalDuration;
                var range = animSet.moveSpeedRange;
                float speed = Mathf.Clamp(clip.length / Mathf.Max(0.01f, total), range.x, range.y);
                float a0 = move.startupDuration, a1 = move.startupDuration + move.activeDuration + 0.1f; // + late contact
                // Anchor: hips at the move's first frame = hips in guard (outside Play mode the sampled clip
                // places the body at its take position; only the move's own travel must count).
                attacker.transform.position = Vector3.zero;
                Pose(aAnim, clip, 0f);
                Vector3 off = aAnim.GetBoneTransform(HumanBodyBones.Hips).position - s_attackerRef;
                off.y = 0f;
                attacker.transform.position = s_usedSample ? -off : Vector3.zero; // the game: root fixed

                // Sample the window; for each sample find the farthest contact distance (binary search on d).
                float best = 0f, bestT = -1f, peakReach = 0f;
                for (float t = a0; t <= a1 + 1e-4f; t += 0.02f)
                {
                    Pose(aAnim, clip, Mathf.Min(clip.length, t * speed));
                    Pose(dAnim, idle, 0.3f);
                    float lo = 0.3f, hi = 2.5f;
                    if (!Touches(attacker, defender, aBody, dBody, move, lo)) continue;
                    for (int i = 0; i < 18; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Touches(attacker, defender, aBody, dBody, move, mid)) lo = mid; else hi = mid;
                    }
                    if (lo > best) { best = lo; bestT = t; }
                }
                // Clip's own strongest extension (any time) — to see whether the Active window is aligned with it.
                // The guard stance itself already "reaches" (front foot / glove grazing the opponent at close
                // range): the strike's peak must go BEYOND that stance reach, and never in the first 0.1 s.
                // A strike that never does (e.g. a low kick no longer than the stance) is timed by its limb's
                // farthest forward extension instead.
                float bestAny = 0f, bestAnyT = 0f, firstT = -1f, stanceReach = 0f, bestExt = float.MinValue, bestExtT = 0f, bestExtReach = 0f;
                float refDistance = arena.minimumFighterSeparation + 0.12f; // a normal close fighting distance
                var limb = FighterBody.LimbFor(move);
                for (float t = 0f; t <= total; t += 0.02f)
                {
                    Pose(aAnim, clip, Mathf.Min(clip.length, t * speed));
                    if (firstT < 0f && Touches(attacker, defender, aBody, dBody, move, refDistance)) firstT = t;
                    float lo = 0.3f, hi = 2.5f;
                    if (Touches(attacker, defender, aBody, dBody, move, lo))
                        for (int i = 0; i < 16; i++) { float mid = (lo + hi) * 0.5f; if (Touches(attacker, defender, aBody, dBody, move, mid)) lo = mid; else hi = mid; }
                    else lo = 0f;
                    if (t == 0f) { stanceReach = lo; continue; }
                    float ext = Extension(aAnim, limb);
                    if (t >= 0.1f && ext > bestExt) { bestExt = ext; bestExtT = t; bestExtReach = lo; }
                    if (t >= 0.1f && lo > stanceReach + 0.02f && lo > bestAny) { bestAny = lo; bestAnyT = t; }
                }
                if (bestAny <= 0f) { bestAny = Mathf.Max(bestExtReach, stanceReach); bestAnyT = bestExtT; }
                peakReach = bestAny;
                if (s_align && !isAlt && bestAny >= arena.minimumFighterSeparation && !move.name.EndsWith("_Test"))
                {
                    float old = move.startupDuration, active = move.activeDuration;
                    // Active opens when the strike FIRST reaches the body at a normal distance (else around the peak).
                    // Opens 0.12 s before the strike's deepest point (in stance the legs already graze the
                    // opponent at close range, so "first contact" is not a usable mark).
                    // 0.06 s (was 0.12): with the Mixamo clips a punch peaks ≈ 0.2 s in, and a 0.08 s startup was faster than any
                    // guard can rise (balance.guardRaiseTime 0.12) — the hard rival stopped blocking punch spam (RivalAIProbe).
                    float impact = bestAnyT - 0.06f;
                    float startup = Mathf.Clamp(impact, 0.06f, Mathf.Max(0.06f, total - active - 0.1f));
                    move.startupDuration = startup;
                    move.recoveryDuration = Mathf.Max(0.1f, total - startup - active);
                    if (move.cancelWindow > 0f) move.cancelWindow = Mathf.Min(move.cancelWindow, move.recoveryDuration * 0.9f);
                    EditorUtility.SetDirty(move);
                    log.AppendLine($"    ALIGNED {move.debugName}: startup {old:0.00} → {startup:0.00} s (active {active:0.00}, recovery {move.recoveryDuration:0.00})");
                    a0 = startup; a1 = startup + active + 0.1f;
                    best = bestAny; bestT = bestAnyT;
                }
                // Step-in distance (FightMoveDefinition.contactReach): the SHORTER of base / alt clip reach, so both land.
                if (best > 0f) reachByMove[move] = reachByMove.TryGetValue(move, out float prev) ? Mathf.Min(prev, best) : best;
                string verdict = best <= 0f ? "NO CONTACT in Active" : best < arena.minimumFighterSeparation ? "SHORT (below min separation)" : "ok";
                log.AppendLine($"  {move.debugName,-34} role {role,-13} clip {clip.name,-48} speed {speed:0.00}  Active {a0:0.00}-{a1 - 0.1f:0.00}s  " +
                               $"reach {best:0.00} m @ {bestT:0.00}s | clip peak {peakReach:0.00} m @ {bestAnyT:0.00}s  {verdict}");
                }
            }
            if (s_align)
                foreach (var kv in reachByMove)
                {
                    if (kv.Value < 0.5f) { log.AppendLine($"  WARNING contactReach {kv.Key.debugName}: {kv.Value:0.00} m measured — not saved (pose not sampled?)"); continue; }
                    kv.Key.contactReach = Mathf.Round(kv.Value * 100f) / 100f;
                    // PLAY-MODE caps (FightStrikeContactProbe, both directions): clips whose foot swings past the side of a
                    // live defender reach less than this static measure — the step-in must bring them closer.
                    if (PlayReachCap.TryGetValue(kv.Key.id ?? "", out float cap) && kv.Key.contactReach > cap) kv.Key.contactReach = cap;
                    EditorUtility.SetDirty(kv.Key);
                    log.AppendLine($"  contactReach {kv.Key.debugName}: {kv.Key.contactReach:0.00} m");
                }
        }
        finally
        {
            if (s_align) AssetDatabase.SaveAssets();
            Object.DestroyImmediate(attacker);
            Object.DestroyImmediate(defender);
        }
        return log.ToString();
    }

    /// <summary>How far forward (+Z, the attacker faces +Z) the striking hand / foot is past the hips.</summary>
    private static float Extension(Animator a, FighterBody.Limb limb)
    {
        float hips = a.GetBoneTransform(HumanBodyBones.Hips).position.z, best = float.MinValue;
        void Bone(HumanBodyBones b) { var t = a.GetBoneTransform(b); if (t != null) best = Mathf.Max(best, t.position.z - hips); }
        if (limb != FighterBody.Limb.Feet) { Bone(HumanBodyBones.LeftHand); Bone(HumanBodyBones.RightHand); }
        if (limb != FighterBody.Limb.Hands) { Bone(HumanBodyBones.LeftFoot); Bone(HumanBodyBones.RightFoot); Bone(HumanBodyBones.LeftToes); Bone(HumanBodyBones.RightToes); }
        return best;
    }

    private static Vector3 s_attackerRef, s_defenderOffset;
    private static bool s_align, s_anchorHips;
    private static Animator s_attackerAnimator;

    private static bool Touches(GameObject attacker, GameObject defender, FighterBody a, FighterBody d, FightMoveDefinition move, float distance)
    {
        // Defender's guard hips exactly `distance` in front of the attacker's guard hips.
        Vector3 target = new Vector3(s_attackerRef.x, 0f, s_attackerRef.z + distance);
        defender.transform.position = new Vector3(target.x - s_defenderOffset.x, 0f, target.z - s_defenderOffset.z);
        return a.StrikeTouches(d, FighterBody.LimbFor(move), FighterAttack.ContactSlack, out _, out _);
    }

    private static GameObject Spawn(GameObject model, Vector3 position, Vector3 forward, out Animator animator)
    {
        var go = (GameObject)Object.Instantiate(model);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.applyRootMotion = false;
        return go;
    }

    private static bool s_usedSample = false; // play-like: PlayableGraph, no root motion
    private static void Pose(Animator animator, AnimationClip clip, float time)
    {
        if (clip == null) return;
        // Humanoid clips sample straight onto an Animator-equipped object (editor), keeping the object's root.
        var root = animator.transform;
        Vector3 p = root.position; Quaternion r = root.rotation;
        if (s_usedSample) { clip.SampleAnimation(animator.gameObject, time); root.SetPositionAndRotation(p, r); }
        else Evaluate(animator, clip, time);
        // SampleAnimation applies the clip's travel to the object: keep the hips over the same floor point. The
        // PlayableGraph path is the game itself (in-place clip, no root motion, root fixed) and needs nothing.
        if (s_usedSample && s_anchorHips && animator == s_attackerAnimator)
        {
            Vector3 h = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            root.position += new Vector3(s_attackerRef.x - h.x, 0f, s_attackerRef.z - h.z);
        }
    }

    private static void Evaluate(Animator animator, AnimationClip clip, float time)
    {
        var graph = PlayableGraph.Create("reach");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            playable.SetTime(time);
            graph.Evaluate(0f);
        }
        finally { graph.Destroy(); }
    }

    private static AnimationClip Clip(FighterAnimationSetSO set, CombatRole role) => set.Get(role);

    private static Vector3 PoseForward(Animator a)
    {
        // In guard the fists are in front of the hips: that is where the fighter faces.
        Vector3 hands = (a.GetBoneTransform(HumanBodyBones.RightHand).position + a.GetBoneTransform(HumanBodyBones.LeftHand).position) * 0.5f;
        Vector3 f = hands - a.GetBoneTransform(HumanBodyBones.Hips).position;
        f.y = 0f;
        return f.normalized;
    }

    private static void Align(Animator a, AnimationClip idle, Vector3 desired)
    {
        Pose(a, idle, 0.3f);
        var f = PoseForward(a);
        a.transform.rotation = Quaternion.FromToRotation(f, desired) * a.transform.rotation;
    }

    private static CombatRole Resolve(FighterAnimationSetSO set, CombatRole role)
    {
        for (int i = 0; i < 4 && set.Get(role) == null; i++)
        {
            var next = AnimatorFighterAnimationDriver.Fallback(role);
            if (next == role) break;
            role = next;
        }
        return role;
    }
}
