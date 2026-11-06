using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The struck BODY reacts where it was hit (playtest 2026-11-04: "si el cop del puny afectés el cos").
/// On every landed / blocked hit an angular impulse = (contact − hips) × hit direction is given to the
/// defender's spine chain (spine → chest → upper chest → neck → head) and settles back through a damped
/// spring, ON TOP of whatever clip plays (hit reaction, guard, idle): a jab to the face snaps the head back,
/// a body shot folds the torso, a hook from the side twists it. Applied in LateUpdate after the Animator,
/// presentation only — never touches timing, hit detection or damage.
/// Tuning: FightCombatBalanceConfig "Hit flinch". One per Fight scene (FightSceneBootstrap).
/// </summary>
public class FighterHitFlinch : MonoBehaviour
{
    private sealed class State
    {
        public FighterActor Actor;
        public Vector3 Angle;     // world-space axis × radians (small-angle, summed)
        public Vector3 Velocity;  // rad/s
        public float HeadBias;    // 0 = torso hit, 1 = head hit (where the weight goes)
    }

    private static readonly HumanBodyBones[] Chain =
        { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head };
    // Share of the total rotation per bone — torso hit (bias 0) vs head hit (bias 1). Each sums to 1.
    private static readonly float[] TorsoWeights = { 0.32f, 0.3f, 0.18f, 0.1f, 0.1f };
    private static readonly float[] HeadWeights  = { 0.08f, 0.12f, 0.15f, 0.25f, 0.4f };

    private readonly Dictionary<FighterActor, State> _states = new();
    private FightCombatBalanceConfig _balance;
    private System.Action<HitLandedEvent> _onHit;
    private System.Action<HitBlockedEvent> _onBlock;

    private void Awake()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _balance = app != null ? app.combatBalance : null;
    }

    private void OnEnable()
    {
        _onHit = e => Kick(e.Result, e.Result.CausesKnockdown || !e.Result.Bonus.IsIdentity ? 1.6f : 1f);
        _onBlock = e => Kick(e.Result, _balance != null ? _balance.hitFlinchBlockedScale : 0.35f);
        EventBus.Subscribe(_onHit);
        EventBus.Subscribe(_onBlock);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onHit);
        EventBus.Unsubscribe(_onBlock);
        _states.Clear();
    }

    private void Kick(FightHitResult r, float scale)
    {
        if (r.Defender == null || r.Attacker == null) return;
        float strength = (_balance != null ? _balance.hitFlinchStrength : 1f) * scale;
        if (strength <= 0f) return;
        var anim = r.Defender.BoundAnimator;
        if (anim == null || !anim.isHuman) return;
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null) return;

        // Push direction: from the attacker towards the defender (horizontal).
        Vector3 dir = r.Defender.transform.position - r.Attacker.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = r.Attacker.ForwardXZ;
        dir.Normalize();
        Vector3 contact = r.ContactPoint == Vector3.zero ? hips.position + Vector3.up * 0.4f : r.ContactPoint;
        Vector3 lever = contact - hips.position;
        // τ = r × F: leans the upper body away from the blow, twists it when the blow lands off-centre.
        Vector3 torque = Vector3.Cross(lever, dir);
        if (torque.sqrMagnitude < 1e-5f) torque = Vector3.Cross(Vector3.up, dir) * 0.4f;
        float lean = _balance != null ? _balance.hitFlinchImpulse : 5f; // rad/s for a 1 m lever, light hit
        if (!_states.TryGetValue(r.Defender, out var s)) _states[r.Defender] = s = new State { Actor = r.Defender };
        s.Velocity += torque * (lean * strength);
        float headHeight = head != null ? head.position.y : hips.position.y + 0.65f;
        s.HeadBias = Mathf.Clamp01(Mathf.InverseLerp(hips.position.y + 0.35f, headHeight - 0.05f, contact.y));
    }

    private void LateUpdate()
    {
        if (_states.Count == 0) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        float k = _balance != null ? _balance.hitFlinchStiffness : 170f;
        float zeta = _balance != null ? _balance.hitFlinchDamping : 0.5f;
        float c = 2f * zeta * Mathf.Sqrt(k);
        float maxAngle = (_balance != null ? _balance.hitFlinchMaxDegrees : 35f) * Mathf.Deg2Rad;

        foreach (var s in _states.Values)
        {
            if (s.Actor == null) continue;
            // Damped spring back to the clip's pose (semi-implicit Euler).
            s.Velocity += (-k * s.Angle - c * s.Velocity) * dt;
            s.Angle += s.Velocity * dt;
            if (s.Angle.magnitude > maxAngle) { s.Angle = s.Angle.normalized * maxAngle; s.Velocity *= 0.5f; }
            float angle = s.Angle.magnitude;
            if (angle < 0.0005f && s.Velocity.sqrMagnitude < 1e-4f) { s.Angle = s.Velocity = Vector3.zero; continue; }
            // A downed fighter's lying pose is not flinched (the knockdown clip already sells the blow).
            if (s.Actor.HitReaction != null && s.Actor.HitReaction.State == FighterReactionState.Downed) continue;
            var anim = s.Actor.BoundAnimator;
            if (anim == null) continue;
            Vector3 axis = s.Angle / angle;
            for (int i = 0; i < Chain.Length; i++)
            {
                var bone = anim.GetBoneTransform(Chain[i]);
                if (bone == null) continue;
                float w = Mathf.Lerp(TorsoWeights[i], HeadWeights[i], s.HeadBias);
                bone.rotation = Quaternion.AngleAxis(angle * w * Mathf.Rad2Deg, axis) * bone.rotation;
            }
        }
    }
}
