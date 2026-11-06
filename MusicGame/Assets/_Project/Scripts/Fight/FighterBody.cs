using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fighter's REAL body for hit detection (2026-11-04) — capsules built every query from the bound
/// Humanoid Animator's bones, so a hit only counts where the animation visibly touches:
///   - HURT capsules (the defender): head, torso, thighs / shins / feet (not the arms: gloves meeting is no hit);
///   - STRIKE capsules (the attacker): fists (forearm → a bit past the hand) or feet (shin → toes).
/// FighterAttack uses it when BOTH fighters have one (a real avatar); the old data boxes
/// (FightHitDefinition / FighterHurtbox) remain the fallback for capsule placeholders, projectiles and
/// ground hits on a downed fighter.
/// Pure geometry: no physics, no colliders (FightCombatShapes' own rule).
/// </summary>
public class FighterBody
{
    public enum Limb { Hands, Feet, Both }

    private readonly struct Capsule
    {
        public readonly Vector3 A, B;
        public readonly float Radius;
        public Capsule(Vector3 a, Vector3 b, float r) { A = a; B = b; Radius = r; }
    }

    private readonly Animator _animator;
    private readonly Transform _hips, _neck, _head, _chest;
    private readonly Transform _lUpperArm, _lLowerArm, _lHand, _rUpperArm, _rLowerArm, _rHand;
    private readonly Transform _lUpperLeg, _lLowerLeg, _lFoot, _lToes, _rUpperLeg, _rLowerLeg, _rFoot, _rToes;
    private readonly List<Capsule> _scratch = new();

    public bool IsValid { get; }

    public FighterBody(Animator animator)
    {
        _animator = animator;
        if (animator == null || !animator.isHuman) return;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        _hips = B(HumanBodyBones.Hips); _neck = B(HumanBodyBones.Neck); _head = B(HumanBodyBones.Head);
        _chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
        _lUpperArm = B(HumanBodyBones.LeftUpperArm); _lLowerArm = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
        _rUpperArm = B(HumanBodyBones.RightUpperArm); _rLowerArm = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
        _lUpperLeg = B(HumanBodyBones.LeftUpperLeg); _lLowerLeg = B(HumanBodyBones.LeftLowerLeg); _lFoot = B(HumanBodyBones.LeftFoot); _lToes = B(HumanBodyBones.LeftToes);
        _rUpperLeg = B(HumanBodyBones.RightUpperLeg); _rLowerLeg = B(HumanBodyBones.RightLowerLeg); _rFoot = B(HumanBodyBones.RightFoot); _rToes = B(HumanBodyBones.RightToes);
        if (_neck == null) _neck = _head;
        IsValid = _hips != null && _head != null && _lHand != null && _rHand != null && _lFoot != null && _rFoot != null &&
                  _lLowerArm != null && _rLowerArm != null && _lLowerLeg != null && _rLowerLeg != null;
    }

    /// <summary>Which limbs a move strikes with: kicks → feet, everything else → hands (Special → both).</summary>
    public static Limb LimbFor(FightMoveDefinition move)
    {
        if (move == null) return Limb.Both;
        if (move.strikingLimb != FightStrikingLimb.Auto)
            return move.strikingLimb == FightStrikingLimb.Feet ? Limb.Feet : move.strikingLimb == FightStrikingLimb.Hands ? Limb.Hands : Limb.Both;
        if (move.scalingStat == FighterBuildStat.KickPower) return Limb.Feet;
        switch (move.role)
        {
            case CombatRole.Kick: case CombatRole.LowKick: case CombatRole.HighKick:
            case CombatRole.SpinHeelKick: case CombatRole.FlyingKick: return Limb.Feet;
            case CombatRole.Special: return Limb.Both;
            default: return Limb.Hands;
        }
    }

    /// <summary>Closest approach between this body's striking limbs and the defender's body. True when they
    /// touch (distance ≤ radii + `slack`); `contact` = the touching point (for the hit spark).</summary>
    public bool StrikeTouches(FighterBody defender, Limb limb, float slack, out Vector3 contact, out float gap)
    {
        contact = Vector3.zero;
        gap = float.PositiveInfinity;
        if (!IsValid || defender == null || !defender.IsValid) return false;
        var strikes = Strikes(limb);
        var hurts = defender.Hurts();
        bool touched = false;
        foreach (var s in strikes)
            foreach (var h in hurts)
            {
                float d = SegmentSegment(s.A, s.B, h.A, h.B, out var ps, out var ph) - s.Radius - h.Radius;
                if (d < gap)
                {
                    gap = d;
                    // On the surface between the two segments — where the spark shows.
                    Vector3 dir = (ph - ps).sqrMagnitude > 1e-8f ? (ph - ps).normalized : Vector3.up;
                    contact = ps + dir * Mathf.Min(s.Radius, Vector3.Distance(ps, ph));
                    touched = d <= slack;
                }
            }
        return touched;
    }

    /// <summary>GUARD CONTACT (2026-11-05): the striking limb meets the defender's FOREARMS (elbow → fist). Not a hurt zone —
    /// a block is the blow landing on the guarding arms; FighterAttack only resolves it when the guard really stops it.</summary>
    public bool StrikeTouchesArms(FighterBody defender, Limb limb, float slack, out Vector3 contact)
    {
        contact = Vector3.zero;
        if (!IsValid || defender == null || !defender.IsValid) return false;
        float best = float.PositiveInfinity;
        foreach (var s in Strikes(limb))
            foreach (var h in new[] { Forearm(defender._lLowerArm, defender._lHand), Forearm(defender._rLowerArm, defender._rHand) })
            {
                float d = SegmentSegment(s.A, s.B, h.A, h.B, out var ps, out var ph) - s.Radius - h.Radius;
                if (d < best) { best = d; contact = Vector3.Lerp(ps, ph, 0.5f); }
            }
        return best <= slack;
    }

    // Elbow → a little past the fist: the guarding forearm and glove.
    private static Capsule Forearm(Transform lower, Transform hand)
    {
        Vector3 forearm = hand.position - lower.position;
        Vector3 dir = forearm.sqrMagnitude > 1e-6f ? forearm.normalized : Vector3.forward;
        return new Capsule(lower.position, hand.position + dir * 0.08f, 0.07f);
    }

    /// <summary>Height (world Y) of the lowest / highest hurt point — debug.</summary>
    public Vector3 HeadPosition => _head != null ? _head.position : Vector3.zero;
    public Vector3 HipsPosition => _hips != null ? _hips.position : Vector3.zero;

    private List<Capsule> Strikes(Limb limb)
    {
        var list = new List<Capsule>(4);
        if (limb != Limb.Feet)
        {
            list.Add(Fist(_lLowerArm, _lHand));
            list.Add(Fist(_rLowerArm, _rHand));
        }
        if (limb != Limb.Hands)
        {
            list.Add(FootStrike(_lLowerLeg, _lFoot, _lToes));
            list.Add(FootStrike(_rLowerLeg, _rFoot, _rToes));
        }
        return list;
    }

    // Forearm's outer half → the fist, a little past the wrist bone.
    private static Capsule Fist(Transform lower, Transform hand)
    {
        Vector3 forearm = hand.position - lower.position;
        Vector3 dir = forearm.sqrMagnitude > 1e-6f ? forearm.normalized : Vector3.forward;
        return new Capsule(lower.position + forearm * 0.5f, hand.position + dir * 0.08f, 0.065f);
    }

    // Lower shin → the toes (the instep / ball of the foot / heel all strike).
    private static Capsule FootStrike(Transform lower, Transform foot, Transform toes)
    {
        Vector3 shin = foot.position - lower.position;
        Vector3 end = toes != null ? toes.position : foot.position + (foot.forward * 0.12f);
        return new Capsule(lower.position + shin * 0.45f, end, 0.07f);
    }

    private List<Capsule> Hurts()
    {
        _scratch.Clear();
        Vector3 headTop = _head.position + Vector3.up * 0.1f;
        _scratch.Add(new Capsule(_head.position + Vector3.up * 0.02f, headTop, 0.11f));                  // head
        _scratch.Add(new Capsule(_hips.position, _chest != null ? Vector3.Lerp(_chest.position, _neck.position, 0.5f) : _neck.position, 0.16f)); // torso
        // Arms are NOT hurt zones: fists meeting the opponent's gloves / forearms is a guard clash, not a
        // hit (blocking is FighterGuard's job) — two fighters in guard at close range already touch there.
        Add(_lUpperLeg, _lLowerLeg, 0.085f); Add(_lLowerLeg, _lFoot, 0.065f); Add(_lFoot, _lToes, 0.05f);
        Add(_rUpperLeg, _rLowerLeg, 0.085f); Add(_rLowerLeg, _rFoot, 0.065f); Add(_rFoot, _rToes, 0.05f);
        return _scratch;
    }

    private void Add(Transform a, Transform b, float r)
    {
        if (a != null && b != null) _scratch.Add(new Capsule(a.position, b.position, r));
    }

    /// <summary>Distance between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
    public static float SegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        const float eps = 1e-8f;
        if (a <= eps && e <= eps) { c1 = p1; c2 = p2; return Vector3.Distance(c1, c2); }
        if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
        return Vector3.Distance(c1, c2);
    }

    /// <summary>Debug gizmos of the current capsules.</summary>
    public void DrawGizmos(Color hurt, Color strike, Limb limb, bool drawStrikes)
    {
        if (!IsValid) return;
        Gizmos.color = hurt;
        foreach (var c in Hurts()) DrawCapsule(c);
        if (!drawStrikes) return;
        Gizmos.color = strike;
        foreach (var c in Strikes(limb)) DrawCapsule(c);
    }

    private static void DrawCapsule(Capsule c)
    {
        Gizmos.DrawWireSphere(c.A, c.Radius);
        Gizmos.DrawWireSphere(c.B, c.Radius);
        Gizmos.DrawLine(c.A, c.B);
    }
}
