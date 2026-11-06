using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Body auras (2026-11-04), one per Fight scene (FightSceneBootstrap):
///   - POWER (the temporary "stronger" state, x4 → FighterPowerState): a WHITE aura streams off the body
///     (soft particles emitted from the avatar's bones, rising), a white light, and the avatar visibly
///     BULKS UP — its Muscle body-shape blendshape (AvatarBodyMorphController, body + compatible garments)
///     ramps up by MuscleBoost while the state lasts and back down after;
///   - SPECIAL wind-up (a Special move in Startup): a brighter GOLD aura with sparks circling the body,
///     a gold light, and a fireball gathering between the hands — until the move releases (Active: the
///     projectile, FightFireballVisual, takes over).
/// Presentation only: never touches gameplay values (the morph is the avatar's look, not its hit geometry).
/// </summary>
public class FighterAuraFx : MonoBehaviour
{
    // Muscle bulk amount and ramps live in FightCombatBalanceConfig (Power State: powerMuscleBoost / RampIn / RampOut).
    private static FightCombatBalanceConfig s_balance;
    private static FightCombatBalanceConfig Balance =>
        s_balance != null ? s_balance : (s_balance = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance);
    private static float MuscleBoost => Balance != null ? Balance.powerMuscleBoost : 0.4f;
    private static float MuscleRampIn => Balance != null ? Balance.powerMuscleRampIn : 1f;
    private static float MuscleRampOut => Balance != null ? Balance.powerMuscleRampOut : 1.2f;

    private class Aura
    {
        public readonly List<ParticleSystem> Emitters = new();
        public readonly List<GameObject> Objects = new();
        public Light Light;
        public float LightBase;
    }

    private class Bulk
    {
        public FighterActor Actor;
        public BodyMorphValues Base;
        public float K;
        public bool Active;
    }

    private class Charge
    {
        public FighterActor Actor;
        public Aura Aura;
        public Transform Orb;
        public Renderer OrbCore, OrbGlow;
        public float Elapsed, Duration;
    }

    private readonly Dictionary<FighterActor, Aura> _power = new();
    private readonly Dictionary<FighterActor, Bulk> _bulk = new();
    private readonly Dictionary<FighterMoveController, Charge> _charges = new();
    private System.Action<PowerStateChangedEvent> _onPower;
    private System.Action<FightMovePhaseChangedEvent> _onPhase;

    private static readonly Color White = new(0.92f, 0.96f, 1f), WhiteTail = new(0.6f, 0.75f, 1f);
    private static readonly Color Gold = new(1f, 0.88f, 0.45f), GoldTail = new(1f, 0.5f, 0.08f);

    private void OnEnable()
    {
        _onPower = e =>
        {
            if (e.Fighter == null) return;
            if (e.Active) StartPower(e.Fighter); else StopPower(e.Fighter);
        };
        _onPhase = e =>
        {
            if (e.Source == null) return;
            bool special = e.Move != null && (e.Move.resourceCost == CombatResourceType.Special || e.Move.role == CombatRole.Special ||
                                              e.Move.role == CombatRole.SpecialCharge);
            if (e.Current == FighterMoveState.Startup && special) StartCharge(e.Source, e.Move);
            else StopCharge(e.Source);
        };
        EventBus.Subscribe(_onPower);
        EventBus.Subscribe(_onPhase);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPower);
        EventBus.Unsubscribe(_onPhase);
        foreach (var a in _power.Values) Release(a, immediate: true);
        _power.Clear();
        foreach (var c in _charges.Values) { Release(c.Aura, immediate: true); if (c.Orb != null) Destroy(c.Orb.gameObject); }
        _charges.Clear();
        foreach (var b in _bulk.Values) if (b.Actor != null && b.Actor.BodyMorph != null) b.Actor.BodyMorph.Apply(b.Base);
        _bulk.Clear();
    }

    // ── Power: white aura + bulk ──────────────────────────────────────────────

    private void StartPower(FighterActor actor)
    {
        StopPower(actor);
        _power[actor] = BuildAura(actor, White, WhiteTail, 1f, 0.55f, gold: false);
        if (actor.BodyMorph != null)
        {
            if (!_bulk.TryGetValue(actor, out var b)) _bulk[actor] = b = new Bulk { Actor = actor, Base = actor.BodyMorph.CurrentValues };
            b.Active = true;
        }
    }

    private void StopPower(FighterActor actor)
    {
        if (_power.TryGetValue(actor, out var a)) Release(a, immediate: false);
        _power.Remove(actor);
        if (_bulk.TryGetValue(actor, out var b)) b.Active = false;
    }

    // ── Special wind-up: gold aura + gathering fireball ───────────────────────

    private void StartCharge(FighterMoveController source, FightMoveDefinition move)
    {
        StopCharge(source);
        var actor = source.Actor;
        if (actor == null) return;
        var c = new Charge
        {
            Actor = actor,
            Aura = BuildAura(actor, Gold, GoldTail, 2f, 0.85f, gold: true),
            Duration = Mathf.Max(0.1f, move.startupDuration / Mathf.Max(0.1f, source.AttackSpeed)),
        };
        var orb = new GameObject("SpecialChargeOrb").transform;
        orb.position = OrbPosition(actor);
        c.Orb = orb;
        c.OrbGlow = FightVfx.Glow(orb, "Glow", new Color(1f, 0.5f, 0.1f, 0.8f), 0.3f);
        c.OrbCore = FightVfx.Glow(orb, "Core", new Color(1f, 0.95f, 0.7f, 1f), 0.12f);
        var fire = FightVfx.Emitter(orb, "Flames", new FightVfx.EmitterSpec
        {
            rate = 70f, radius = 0.06f, lifeMin = 0.15f, lifeMax = 0.3f, sizeMin = 0.08f, sizeMax = 0.18f, speedMin = 0.05f, speedMax = 0.3f,
            upward = 0.4f, noise = 0.3f, color = FightVfx.Fade(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.25f, 0.03f)), world = true,
        });
        c.Aura.Emitters.Add(fire);
        _charges[source] = c;
    }

    private void StopCharge(FighterMoveController source)
    {
        if (!_charges.TryGetValue(source, out var c)) return;
        Release(c.Aura, immediate: false);
        if (c.Orb != null) Destroy(c.Orb.gameObject, 0.05f);
        _charges.Remove(source);
    }

    private static Vector3 OrbPosition(FighterActor actor)
    {
        var anim = actor.BoundAnimator;
        if (anim != null && anim.isHuman)
        {
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (l != null && r != null) return (l.position + r.position) * 0.5f + actor.ForwardXZ * 0.12f;
        }
        return actor.transform.position + actor.ForwardXZ * 0.45f + Vector3.up * 0.35f;
    }

    // ── Update: bulk ramps, light flicker, orb growth ─────────────────────────

    private readonly List<FighterActor> _done = new();

    private void Update()
    {
        float t = Time.time;
        _done.Clear();
        foreach (var b in _bulk.Values)
        {
            if (b.Actor == null || b.Actor.BodyMorph == null) { _done.Add(b.Actor); continue; }
            float target = b.Active ? 1f : 0f;
            float rate = 1f / Mathf.Max(0.01f, b.Active ? MuscleRampIn : MuscleRampOut);
            b.K = Mathf.MoveTowards(b.K, target, rate * Time.deltaTime);
            float eased = b.K * b.K * (3f - 2f * b.K);
            var v = b.Base;
            v.Muscle = Mathf.Lerp(b.Base.Muscle, Mathf.Min(1f, b.Base.Muscle + MuscleBoost), eased);
            b.Actor.BodyMorph.Apply(v);
            if (!b.Active && b.K <= 0f) { b.Actor.BodyMorph.Apply(b.Base); _done.Add(b.Actor); }
        }
        foreach (var a in _done) _bulk.Remove(a);

        foreach (var a in _power.Values)
            if (a.Light != null) a.Light.intensity = a.LightBase * (0.85f + 0.15f * Mathf.Sin(t * 9f) + 0.05f * Mathf.Sin(t * 23f));

        foreach (var c in _charges.Values)
        {
            c.Elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(c.Elapsed / c.Duration);
            if (c.Aura.Light != null) c.Aura.Light.intensity = c.Aura.LightBase * (0.6f + 0.6f * k) * (0.9f + 0.1f * Mathf.Sin(t * 27f));
            if (c.Orb != null && c.Actor != null)
            {
                c.Orb.position = OrbPosition(c.Actor);
                float pulse = 1f + 0.15f * Mathf.Sin(t * 35f);
                if (c.OrbCore != null) c.OrbCore.transform.localScale = Vector3.one * Mathf.Lerp(0.06f, 0.32f, k) * pulse;
                if (c.OrbGlow != null) c.OrbGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.8f, k) * (2f - pulse);
            }
        }
    }

    // ── Aura building ─────────────────────────────────────────────────────────

    // (bone, emitter radius, rate weight) — the aura hugs the body and follows the animation.
    private static readonly (HumanBodyBones bone, float radius, float weight)[] AuraBones =
    {
        (HumanBodyBones.Hips, 0.16f, 1.2f), (HumanBodyBones.Chest, 0.18f, 1.4f), (HumanBodyBones.Head, 0.12f, 0.8f),
        (HumanBodyBones.LeftUpperArm, 0.07f, 0.5f), (HumanBodyBones.RightUpperArm, 0.07f, 0.5f),
        (HumanBodyBones.LeftLowerArm, 0.06f, 0.6f), (HumanBodyBones.RightLowerArm, 0.06f, 0.6f),
        (HumanBodyBones.LeftHand, 0.06f, 0.7f), (HumanBodyBones.RightHand, 0.06f, 0.7f),
        (HumanBodyBones.LeftUpperLeg, 0.09f, 0.6f), (HumanBodyBones.RightUpperLeg, 0.09f, 0.6f),
        (HumanBodyBones.LeftLowerLeg, 0.07f, 0.5f), (HumanBodyBones.RightLowerLeg, 0.07f, 0.5f),
    };

    private Aura BuildAura(FighterActor actor, Color color, Color tail, float strength, float alpha, bool gold)
    {
        var aura = new Aura();
        var anim = actor.BoundAnimator;
        var body = FightVfx.Fade(color, tail, alpha);
        bool any = false;
        if (anim != null && anim.isHuman)
            foreach (var (bone, radius, weight) in AuraBones)
            {
                var t = anim.GetBoneTransform(bone);
                if (t == null) continue;
                any = true;
                aura.Emitters.Add(FightVfx.Emitter(Follower(aura, t, Vector3.zero), "Aura_" + bone, new FightVfx.EmitterSpec
                {
                    rate = 16f * weight * strength, radius = radius, lifeMin = 0.35f, lifeMax = 0.7f,
                    sizeMin = 0.07f * (gold ? 1.3f : 1f), sizeMax = 0.2f * (gold ? 1.3f : 1f), speedMin = 0f, speedMax = 0.15f,
                    upward = gold ? 1.3f : 0.9f, noise = 0.25f, color = body, world = true,
                }));
            }
        var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        Transform center = Follower(aura, hips != null ? hips : actor.transform, Vector3.zero);
        if (!any)
            aura.Emitters.Add(FightVfx.Emitter(center, "Aura_Body", new FightVfx.EmitterSpec
            {
                rate = 120f * strength, radius = 0.4f, lifeMin = 0.4f, lifeMax = 0.8f, sizeMin = 0.08f, sizeMax = 0.25f,
                upward = 1f, noise = 0.25f, color = body, world = true,
            }));
        // A soft halo around the whole body (large, faint, slow).
        aura.Emitters.Add(FightVfx.Emitter(center, "Aura_Halo", new FightVfx.EmitterSpec
        {
            rate = 10f * strength, radius = 0.3f, lifeMin = 0.5f, lifeMax = 0.9f, sizeMin = 0.6f, sizeMax = 1.0f,
            upward = 0.4f, color = FightVfx.Fade(color, tail, gold ? 0.3f : 0.16f), world = false, maxParticles = 40,
        }, new Vector3(0f, 0.15f, 0f)));
        if (gold)
            // Sparks circling the body and rising: the energy being gathered.
            aura.Emitters.Add(FightVfx.Emitter(Follower(aura, actor.transform, Vector3.zero), "Aura_Sparks", new FightVfx.EmitterSpec
            {
                rate = 70f, radius = 0.55f, lifeMin = 0.5f, lifeMax = 0.9f, sizeMin = 0.025f, sizeMax = 0.06f,
                upward = 1.6f, orbitalY = 5f, noise = 0.2f, color = FightVfx.Fade(new Color(1f, 0.97f, 0.8f), Gold, 1f), world = false,
            }, new Vector3(0f, -0.6f, 0f)));
        var light = FightVfx.PointLight(center, "Aura_Light", gold ? new Color(1f, 0.78f, 0.35f) : new Color(0.9f, 0.95f, 1f),
                                        gold ? 4f : 1.8f, gold ? 3.5f : 2.6f, new Vector3(0f, 0.3f, 0f));
        aura.Light = light;
        aura.LightBase = light.intensity;
        return aura;
    }

    /// <summary>An UNSCALED holder that tracks `target` (bones can carry import scale; the effects must not).</summary>
    private static Transform Follower(Aura aura, Transform target, Vector3 offset)
    {
        var go = new GameObject("AuraFollow_" + target.name);
        go.transform.position = target.position + offset;
        go.AddComponent<FightFollow>().Set(target, offset);
        aura.Objects.Add(go);
        return go.transform;
    }

    /// <summary>Stops emitting and lets the particles already out fade (or kills everything at once).</summary>
    private static void Release(Aura aura, bool immediate)
    {
        if (aura == null) return;
        foreach (var ps in aura.Emitters)
        {
            if (ps == null) continue;
            if (immediate) { Destroy(ps.gameObject); continue; }
            var main = ps.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (aura.Light != null) Destroy(aura.Light.gameObject);
        // Holders outlive their emitters' last particles (world-space ones fade where they are).
        foreach (var o in aura.Objects) if (o != null) Destroy(o, immediate ? 0f : 1.2f);
    }
}

/// <summary>Copies a target's position every frame (no parenting, so no inherited scale / rotation).</summary>
public class FightFollow : MonoBehaviour
{
    private Transform _target;
    private Vector3 _offset;
    public void Set(Transform target, Vector3 offset) { _target = target; _offset = offset; }
    private void LateUpdate()
    {
        if (_target == null) { Destroy(gameObject); return; }
        transform.position = _target.position + _offset;
    }
}
         