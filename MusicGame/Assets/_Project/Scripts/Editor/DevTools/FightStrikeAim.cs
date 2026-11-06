using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 2026-11-05 playtest "cap de les patades pràcticament contacta": in the GAME's frame (actor facing the opponent,
/// in-place clip, no root motion — FightStrikeContactProbe DIAG) several Mixamo strikes go out to the SIDE (mid kick
/// +0.89 m lateral, high kick ±0.6 m): the bladed stance + roundhouse arc. The edit-mode reach probe aligned the avatar
/// by its guard pose and never saw it. This turns each strike clip (orientationOffsetY of its in-place clip) so the
/// striking limb's farthest point goes STRAIGHT at the opponent. Idempotent: measures from offset 0 every run.
/// Called by FightFeelSetup after the clips are mapped (a reimport resets the clip settings).
/// </summary>
public static class FightStrikeAim
{
    private const float MinYaw = 5f, MaxYaw = 75f;

    public static string Run(FighterAnimationSetSO set)
    {
        var log = new StringBuilder();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(CanonicalAvatarSetup.PrefabPath); // the importer's avatar (PlayableGraph-ready)
        var idle = set != null ? set.Get(CombatRole.CombatIdle) : null;
        if (set == null || model == null || idle == null) return "  FAIL strike aim: no animation set / avatar / idle\n";
        var go = (GameObject)Object.Instantiate(model);
        var foe = (GameObject)Object.Instantiate(model);
        go.hideFlags = foe.hideFlags = HideFlags.DontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foe.transform.SetPositionAndRotation(Vector3.forward, Quaternion.LookRotation(Vector3.back));
            var animator = go.GetComponent<Animator>();
            var foeAnimator = foe.GetComponent<Animator>();
            animator.applyRootMotion = foeAnimator.applyRootMotion = false;
            Pose(foeAnimator, idle, 0.3f);   // the defender stands in guard (its pose stays after the graph)
            var aBody = new FighterBody(animator);
            var dBody = new FighterBody(foeAnimator);
            if (!aBody.IsValid || !dBody.IsValid) return "  FAIL strike aim: avatar bones\n";
            foreach (var rc in set.clips)
            {
                if (rc != null && rc.clip != null && IsSpin(rc.role))
                {
                    rc.strikeSide = NaturalSide(animator, rc.clip, true);
                    EditorUtility.SetDirty(set);
                    log.AppendLine($"  spin {rc.role,-14} {rc.clip.name,-44} side {rc.strikeSide} (not re-aimed)");
                    continue;
                }
                if (rc == null || rc.clip == null || !IsAimedStrike(rc.role)) continue;
                var settings = AnimationUtility.GetAnimationClipSettings(rc.clip);
                float before = settings.orientationOffsetY;
                var limb = IsKick(rc.role) ? FighterBody.Limb.Feet : FighterBody.Limb.Hands;
                float ReachAt(float offset)
                {
                    settings.orientationOffsetY = offset;
                    AnimationUtility.SetAnimationClipSettings(rc.clip, settings);
                    return ContactReach(animator, rc.clip, aBody, dBody, foe.transform, limb);
                }
                float r0 = ReachAt(0f);
                // The turn that lands the strike FARTHEST (root-to-root, real bodies, the defender in guard): coarse 5°
                // then 1°; a smaller turn wins when it is within 4 cm (every degree is a visible pivot out of the guard).
                var scan = new System.Collections.Generic.List<(float o, float r)>();
                for (float o = -MaxYaw; o <= MaxYaw + 0.01f; o += 5f) scan.Add((o, ReachAt(o)));
                float top = scan.Max(x => x.r);
                if (top < 0.3f) { log.AppendLine($"  WARNING aim {rc.role}: no contact at any turn — offset left at {before:+0;-0}°"); ReachAt(before); continue; }
                var pick = scan.Where(x => x.r >= top - 0.04f).OrderBy(x => Mathf.Abs(x.o)).First();
                float bestOff = pick.o, bestR = pick.r, coarse = pick.o;
                for (float o = coarse - 4f; o <= coarse + 4.01f; o += 1f) { float r = ReachAt(o); if (r > bestR + 0.01f) { bestR = r; bestOff = o; } }
                if (Mathf.Abs(bestOff) < MinYaw || bestR < r0 + 0.04f) { bestOff = 0f; bestR = r0; }
                ReachAt(bestOff);
                var (yaw2, _, lat2) = Measure(animator, rc.clip, limb == FighterBody.Limb.Feet);
                EditorUtility.SetDirty(rc.clip);
                // Which limb strikes as authored (left / right strikes play the clip mirrored for the other side).
                rc.strikeSide = NaturalSide(animator, rc.clip, limb == FighterBody.Limb.Feet);
                EditorUtility.SetDirty(set);
                log.AppendLine($"  aim {rc.role,-14} {rc.clip.name,-44} contact reach {r0:0.00} m straight → {bestR:0.00} m turned {bestOff:+0;-0}° (strike yaw {yaw2:+0;-0}°, lat {lat2:+0.00;-0.00}; was {before:+0;-0}°) side {rc.strikeSide}");
            }
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(foe); }
        return log.ToString();
    }

    private static void Pose(Animator animator, AnimationClip clip, float t)
    {
        var graph = PlayableGraph.Create("StrikeAimPose");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            playable.SetTime(t); graph.Evaluate(0f);
        }
        finally { graph.Destroy(); }
    }

    /// <summary>Largest root-to-root distance at which the limb touches the guarding defender, at any time of the clip.</summary>
    private static float ContactReach(Animator animator, AnimationClip clip, FighterBody a, FighterBody d, Transform foe, FighterBody.Limb limb)
    {
        var graph = PlayableGraph.Create("StrikeAimReach");
        float best = 0f;
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            bool Touch(float dist) { foe.position = new Vector3(0f, 0f, dist); return a.StrikeTouches(d, limb, FighterAttack.ContactSlack, out _, out _); }
            for (float t = 0.1f; t <= clip.length + 1e-4f; t += 1f / 30f)
            {
                playable.SetTime(t); graph.Evaluate(0f);
                if (!Touch(Mathf.Max(0.3f, best))) continue;
                float lo = Mathf.Max(0.3f, best), hi = 2.2f;
                for (int i = 0; i < 12; i++) { float mid = (lo + hi) * 0.5f; if (Touch(mid)) lo = mid; else hi = mid; }
                best = Mathf.Max(best, lo);
            }
        }
        finally { graph.Destroy(); }
        return best;
    }

    private static bool IsKick(CombatRole r) => r == CombatRole.Kick || r == CombatRole.KickAlt || r == CombatRole.HighKick || r == CombatRole.HighKickAlt ||
                                                r == CombatRole.LowKick || r == CombatRole.LowKickAlt || r == CombatRole.KickHighCenter ||
                                                r == CombatRole.KickMidCenter || r == CombatRole.KickLowCenter || r == CombatRole.Knee ||
                                                r == CombatRole.SpinHeelKick || r == CombatRole.SpinSweep || r == CombatRole.AirSpinHeel;

    // Spins: the turn is baked into the pose — never re-aimed, only their striking side measured (mirrored for the other).
    private static bool IsSpin(CombatRole r) => r == CombatRole.SpinHeelKick || r == CombatRole.SpinSweep || r == CombatRole.AirSpinHeel;

    private static bool IsAimedStrike(CombatRole r) => IsKick(r) ||
        r == CombatRole.LightAttack || r == CombatRole.LightAttackAlt || r == CombatRole.HeavyAttack || r == CombatRole.HeavyAttackAlt ||
        r == CombatRole.HighPunch || r == CombatRole.HighPunchAlt || r == CombatRole.LowPunch || r == CombatRole.LowPunchAlt ||
        r == CombatRole.PunchHighSide || r == CombatRole.PunchMidSide || r == CombatRole.PunchLowSide ||
        r == CombatRole.KickHighCenter || r == CombatRole.KickMidCenter || r == CombatRole.KickLowCenter ||
        r == CombatRole.Uppercut || r == CombatRole.Knee;

    /// <summary>The limb that strikes in the clip: the hand / foot that travels farthest from its start pose.</summary>
    public static FighterAnimationSetSO.ClipStrikeSide NaturalSide(Animator animator, AnimationClip clip, bool feet)
    {
        var graph = PlayableGraph.Create("StrikeAimSide");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            var r = feet ? B(HumanBodyBones.RightFoot) : B(HumanBodyBones.RightHand);
            var l = feet ? B(HumanBodyBones.LeftFoot) : B(HumanBodyBones.LeftHand);
            var hips = B(HumanBodyBones.Hips);
            playable.SetTime(0f); graph.Evaluate(0f);
            Vector3 r0 = r.position - hips.position, l0 = l.position - hips.position;
            float rMax = 0f, lMax = 0f;
            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 60f)
            {
                playable.SetTime(t); graph.Evaluate(0f);
                rMax = Mathf.Max(rMax, Vector3.Distance(r.position - hips.position, r0));
                lMax = Mathf.Max(lMax, Vector3.Distance(l.position - hips.position, l0));
            }
            if (Mathf.Abs(rMax - lMax) < 0.05f) return FighterAnimationSetSO.ClipStrikeSide.Unknown;
            return rMax > lMax ? FighterAnimationSetSO.ClipStrikeSide.Right : FighterAnimationSetSO.ClipStrikeSide.Left;
        }
        finally { graph.Destroy(); }
    }

    /// <summary>Farthest the striking limb tips get STRAIGHT ahead (+Z from the root) over the whole clip, in the game's frame.</summary>
    public static float Forward(Animator animator, AnimationClip clip, bool feet)
    {
        var graph = PlayableGraph.Create("StrikeAimFwd");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            var tips = feet ? new[] { B(HumanBodyBones.RightToes) ?? B(HumanBodyBones.RightFoot), B(HumanBodyBones.LeftToes) ?? B(HumanBodyBones.LeftFoot) }
                            : new[] { B(HumanBodyBones.RightHand), B(HumanBodyBones.LeftHand) };
            float best = float.MinValue;
            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 60f)
            {
                playable.SetTime(t); graph.Evaluate(0f);
                foreach (var tip in tips) best = Mathf.Max(best, tip.position.z - animator.transform.position.z);
            }
            return best;
        }
        finally { graph.Destroy(); }
    }

    /// <summary>The striking limb's farthest horizontal point from the guard hips (front half only), as seen in the game:
    /// avatar root at the origin facing +Z, in-place clip, no root motion. Returns its yaw (+ = to the right / +X).</summary>
    public static (float yaw, float reach, float lateral) Measure(Animator animator, AnimationClip clip, bool feet)
    {
        var graph = PlayableGraph.Create("StrikeAim");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            var tips = feet ? new[] { B(HumanBodyBones.RightToes) ?? B(HumanBodyBones.RightFoot), B(HumanBodyBones.LeftToes) ?? B(HumanBodyBones.LeftFoot) }
                            : new[] { B(HumanBodyBones.RightHand), B(HumanBodyBones.LeftHand) };
            playable.SetTime(0f); graph.Evaluate(0f);
            Vector3 hips0 = B(HumanBodyBones.Hips).position; hips0.y = 0f;
            var start = new Vector3[tips.Length];
            for (int i = 0; i < tips.Length; i++) start[i] = tips[i].position;
            // The limb that MOVES most is the striking one; its farthest point from the hips is the strike.
            float bestScore = float.MinValue; Vector3 best = Vector3.forward;
            for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 60f)
            {
                playable.SetTime(t); graph.Evaluate(0f);
                for (int i = 0; i < tips.Length; i++)
                {
                    Vector3 p = tips[i].position; p.y = 0f;
                    Vector3 rel = p - hips0;
                    if (rel.z < 0.05f) continue;                         // front half only
                    float moved = Vector3.Distance(tips[i].position, start[i]);
                    float score = rel.magnitude + moved * 0.5f;
                    if (score > bestScore) { bestScore = score; best = rel; }
                }
            }
            return (Mathf.Atan2(best.x, best.z) * Mathf.Rad2Deg, best.magnitude, best.x);
        }
        finally { graph.Destroy(); }
    }
}
