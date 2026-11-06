using UnityEngine;

/// <summary>
/// Small procedural touches ON TOP of the clip that plays (LateUpdate, after the Animator), where we have no dedicated
/// clip. FighterAttack reads the bones on the next Update, so a real contact sees them.
///
/// • DIVING air punch (playtest 2026-11-05 "el cop de puny aeri no toca"): the only punch clips are ground strikes, so
///   from the top of a jump the fist went over the defender's head. While an airborne fighter throws a hand strike, the
///   upper body pitches forward / down (spine → upper chest): the fist comes DOWN onto the opponent. Ramps in during
///   Startup, holds through Active, eases out in Recovery.
/// • SIDE GUARD (first person, user design 2026-11-05): the guard covers ONE side (FighterGuard.Side) — the torso turns
///   towards it, so the raised arms visibly cover that side.
/// One per Fight scene (FightSceneBootstrap).
/// </summary>
public class FighterPoseOverlay : MonoBehaviour
{
    private const float LeanDegrees = 38f;
    private const float GuardTwistDegrees = 10f; // the side-guard clips do most of it
    private const float BlendSpeed = 14f; // 1/s — ~0.1 s to settle
    private static readonly HumanBodyBones[] Chain = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
    private static readonly float[] Weights = { 0.4f, 0.35f, 0.25f };

    private FighterActor[] _actors = System.Array.Empty<FighterActor>();
    private float[] _lean = System.Array.Empty<float>();
    private float[] _twist = System.Array.Empty<float>(); // signed: −1 left … +1 right

    public void Initialize(params FighterActor[] actors)
    {
        _actors = actors ?? System.Array.Empty<FighterActor>();
        _lean = new float[_actors.Length];
        _twist = new float[_actors.Length];
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        for (int i = 0; i < _actors.Length; i++)
        {
            var a = _actors[i];
            if (a == null) continue;
            _lean[i] = Mathf.MoveTowards(_lean[i], LeanTarget(a), BlendSpeed * dt);
            _twist[i] = Mathf.MoveTowards(_twist[i], TwistTarget(a), BlendSpeed * dt);
            if (_lean[i] <= 0.001f && Mathf.Abs(_twist[i]) <= 0.001f) continue;
            var anim = a.BoundAnimator;
            if (anim == null || !anim.isHuman) continue;
            Vector3 fwd = a.ForwardXZ;
            if (fwd.sqrMagnitude < 1e-4f) continue;
            Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized); // +angle about it tips forward → down
            var rot = Quaternion.AngleAxis(LeanDegrees * _lean[i], right);
            var yaw = Quaternion.AngleAxis(GuardTwistDegrees * _twist[i], Vector3.up); // + = turns towards the fighter's right
            for (int b = 0; b < Chain.Length; b++)
            {
                var bone = anim.GetBoneTransform(Chain[b]);
                if (bone == null) continue;
                var q = Quaternion.Slerp(Quaternion.identity, yaw * rot, Weights[b]);
                bone.rotation = q * bone.rotation;
            }
        }
    }

    private static float LeanTarget(FighterActor a)
    {
        var mc = a.MoveController;
        if (mc == null || mc.CurrentMove == null || a.Posture != FighterPosture.Airborne) return 0f;
        if (mc.CurrentMove.attackDelivery != AttackDelivery.Melee || FighterBody.LimbFor(mc.CurrentMove) != FighterBody.Limb.Hands) return 0f;
        return mc.CurrentPhase switch
        {
            FighterMoveState.Startup => 1f,
            FighterMoveState.Active => 1f,
            FighterMoveState.Recovery => 1f - mc.PhaseProgress01,
            _ => 0f,
        };
    }

    private static float TwistTarget(FighterActor a) =>
        a.Guard != null && a.Guard.State != FighterGuardState.None ? a.Guard.CoverSide : 0f;
}
