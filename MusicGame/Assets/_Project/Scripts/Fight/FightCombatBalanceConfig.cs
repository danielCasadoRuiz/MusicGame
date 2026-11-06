using UnityEngine;

/// <summary>
/// The single place every combat-build stat's effect is defined — no formulas scattered across
/// FighterAttack/FightHitResolver/FighterMoveController. Each curve's X axis is the RAW build stat in points
/// (FighterBuildStats — the Runner's direct build: a low base plus what the collected bonuses added, capped at
/// FightStatsConfig.maxStatPoints); Y is a multiplier where 1 = neutral. FOUR stats since 2026-10-25:
///
///   attacker <move.scalingStat> (PunchPower / KickPower) → damage dealt AND (milder curve) the hit reaction it causes
///   defender Resistance                                  → damage taken + hit-stun / knockback taken
///   own Agility                                          → movement speed (walk / backpedal / run / side-walk)
///                                                          + attack speed: the WHOLE move plays faster (every phase,
///                                                          cancel and dodge window and the animation alike — never
///                                                          start-up alone, which drifted the visual impact off the hit)
/// Moves with
/// scalingStat None (block, taunt, specials with explicit rules…) scale with nothing.
/// Rivals: RivalTuningAt(fight difficulty) sizes their build and their per-round charges — independent of the player.
/// Legacy FighterStats (Strength/Defense/...) are no longer read by combat.
/// </summary>
[CreateAssetMenu(fileName = "FightCombatBalanceConfig", menuName = "MusicGame/Fight/Combat Balance Config")]
public class FightCombatBalanceConfig : ScriptableObject
{
    [Header("Health")]
    public float defaultMaxHealth = 100f;

    // 2026-10-25 rebalance: a LOW base and a big share from the runner. X = stat points (a run's 100-point budget split
    // over five stats: ~4 each for a poor run, ~10 for a middling one, ~16–18 for a very good one, 30+ when a run
    // specialises). 16 points = the old neutral 1.0. Curves clamp at their ends (no runaway values).
    [Header("Build stats → combat multipliers (X = stat points, Y = multiplier)")]
    [Tooltip("Attacker's move.scalingStat (PunchPower for punches, KickPower for kicks) → damage DEALT.")]
    public AnimationCurve buildStatToDamage = new AnimationCurve(
        new Keyframe(0f, 0.25f), new Keyframe(5f, 0.45f), new Keyframe(10f, 0.70f), new Keyframe(16f, 1.00f),
        new Keyframe(25f, 1.40f), new Keyframe(40f, 1.90f));
    [Tooltip("Attacker's move.scalingStat → knockback and hit stun DEALT (the move's own baseKnockback / baseHitStun " +
             "keep its character: a kick can push more than a punch). A separate, milder curve than damage — the attack " +
             "power is counted once per effect, never compounded.")]
    public AnimationCurve attackPowerToReaction = new AnimationCurve(
        new Keyframe(0f, 0.70f), new Keyframe(16f, 1.00f), new Keyframe(40f, 1.35f));
    [Tooltip("Defender Resistance → damage TAKEN (lower = tankier).")]
    public AnimationCurve resistanceToDamageTaken = new AnimationCurve(
        new Keyframe(0f, 1.50f), new Keyframe(8f, 1.25f), new Keyframe(16f, 1.00f), new Keyframe(25f, 0.80f), new Keyframe(40f, 0.60f));
    [Tooltip("Defender Resistance → incoming hit stun AND knockback (lower = shrugs hits off).")]
    public AnimationCurve resistanceToStagger = new AnimationCurve(
        new Keyframe(0f, 1.25f), new Keyframe(16f, 1.00f), new Keyframe(40f, 0.75f));
    [Tooltip("Bounds of the WHOLE reaction scale (attack power × defender resistance × finisher/Power bonus) applied to " +
             "a move's baseHitStun / baseKnockback: no stun-lock, never a weightless hit.")]
    public float minReactionScale = 0.55f;
    public float maxReactionScale = 1.6f;
    [Tooltip("Own Agility → ATTACK speed: how fast the whole move plays (move time per real second). Uniform over " +
             "startup, active, recovery, cancel and dodge windows AND the animation, so hits stay in sync; combo input " +
             "windows are not touched. 1.25 = a move takes 80 % of its authored time.")]
    public AnimationCurve agilityToAttackSpeed = new AnimationCurve(
        new Keyframe(0f, 0.82f), new Keyframe(8f, 0.9f), new Keyframe(16f, 1.0f), new Keyframe(25f, 1.1f), new Keyframe(40f, 1.25f));
    public float minAttackSpeed = 0.8f;
    public float maxAttackSpeed = 1.3f;
    [Tooltip("Own Agility → MOVEMENT speed: walk / backpedal / run / side-walk (FighterMovement.SpeedMultiplier). " +
             "Dashes, lunges and knockback keep their own fixed speeds.")]
    public AnimationCurve agilityToMoveSpeed = new AnimationCurve(
        new Keyframe(0f, 0.62f), new Keyframe(8f, 0.78f), new Keyframe(16f, 0.95f), new Keyframe(25f, 1.10f), new Keyframe(40f, 1.30f));
    [Tooltip("Hard floor / ceiling of the movement multiplier: never immobile, never uncontrollable.")]
    public float minMoveSpeedMultiplier = 0.6f;
    public float maxMoveSpeedMultiplier = 1.35f;

    [Header("Rival progression (difficulty — never the player's run)")]
    // X of every curve below = the FIGHT DIFFICULTY of the match (0..1, FightDifficulty.Resolve): the campaign position
    // (level + match inside the level, the same continuous scale the runner uses), so a rival gets harder level after
    // level AND after every opponent beaten inside a level. Final-rival stages sit at 1 + stage bonus.
    [Tooltip("Total build points of a rival (its FighterStatsProfileSO keeps the PROPORTIONS — its identity — this sets the size).")]
    public AnimationCurve rivalBudgetByDifficulty = new AnimationCurve(
        new Keyframe(0f, 40f), new Keyframe(0.15f, 48f), new Keyframe(0.5f, 80f), new Keyframe(0.85f, 112f), new Keyframe(1f, 125f));
    [Tooltip("Extra build points per final-rival stage (stage 0, 1, 2) on top of difficulty 1.")]
    public float rivalBossBudgetPerStage = 10f;
    [Tooltip("Specials the rival has EACH ROUND (refilled at every round start; floor of the curve).")]
    public AnimationCurve rivalSpecialsPerRound = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 1.99f), new Keyframe(1f, 3f));
    [Tooltip("x3 charges (powered triple punch / kick) the rival has each round.")]
    public AnimationCurve rivalTriplesPerRound = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.6f, 2f), new Keyframe(1f, 4f));
    [Tooltip("x4 charges (Power state) the rival has each round.")]
    public AnimationCurve rivalQuadsPerRound = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 0.99f), new Keyframe(1f, 1.5f));
    [Tooltip("Seconds into a round before the rival may throw a Special at all (just so it never opens with it).")]
    public AnimationCurve rivalFirstSpecialDelay = new AnimationCurve(new Keyframe(0f, 4f), new Keyframe(1f, 2f));
    [Tooltip("Minimum seconds between two rival Specials (it throws them on OPENINGS — FighterAI — this only avoids a double).")]
    public AnimationCurve rivalSpecialCooldown = new AnimationCurve(new Keyframe(0f, 5f), new Keyframe(1f, 2.5f));

    [Header("Rival fighting skill (AI) by difficulty — X = fight difficulty (0.15 first match … 0.85 last, 1 final rival)")]
    [Tooltip("Seconds the rival needs to react (decisions AND its reflex guard).")]
    public AnimationCurve rivalReactionTime = new AnimationCurve(new Keyframe(0.15f, 0.42f), new Keyframe(0.5f, 0.26f), new Keyframe(0.85f, 0.15f), new Keyframe(1f, 0.11f));
    [Tooltip("Chance it sees an attack coming and guards it with the right height.")]
    public AnimationCurve rivalDefense = new AnimationCurve(new Keyframe(0.15f, 0.35f), new Keyframe(0.5f, 0.62f), new Keyframe(0.85f, 0.85f), new Keyframe(1f, 0.92f));
    [Tooltip("How well it punishes an attack it blocked or the player's recovery.")]
    public AnimationCurve rivalPunish = new AnimationCurve(new Keyframe(0.15f, 0.25f), new Keyframe(0.85f, 0.88f), new Keyframe(1f, 0.95f));
    [Tooltip("How long / reliable its combos are.")]
    public AnimationCurve rivalComboSkill = new AnimationCurve(new Keyframe(0.15f, 0.25f), new Keyframe(0.85f, 0.85f), new Keyframe(1f, 0.92f));
    [Tooltip("How often it picks a deliberately poor action.")]
    public AnimationCurve rivalErrorRate = new AnimationCurve(new Keyframe(0.15f, 0.4f), new Keyframe(0.85f, 0.06f), new Keyframe(1f, 0.03f));
    [Tooltip("How keen it is to use its Special on an opening.")]
    public AnimationCurve rivalSpecialUsage = new AnimationCurve(new Keyframe(0.15f, 0.35f), new Keyframe(0.85f, 0.85f), new Keyframe(1f, 0.95f));

    /// <summary>The rival's fighting skill for this difficulty (a runtime profile): skill fields from the curves above,
    /// personality (aggression, spacing) from the version's own AIDifficultyProfile.</summary>
    public AIDifficultyProfile RivalAIProfile(float difficulty, AIDifficultyProfile personality)
    {
        float d = Mathf.Clamp01(difficulty);
        var p = CreateInstance<AIDifficultyProfile>();
        p.name = $"RivalAI_d{d:0.00}";
        p.reactionTime = Mathf.Max(0.05f, rivalReactionTime.Evaluate(d));
        p.defenseProbability = Mathf.Clamp01(rivalDefense.Evaluate(d));
        p.punishSkill = Mathf.Clamp01(rivalPunish.Evaluate(d));
        p.comboSkill = Mathf.Clamp01(rivalComboSkill.Evaluate(d));
        p.errorRate = Mathf.Clamp01(rivalErrorRate.Evaluate(d));
        p.specialUsage = Mathf.Clamp01(rivalSpecialUsage.Evaluate(d));
        p.aggression = personality != null ? personality.aggression : 0.5f;
        p.spacingAccuracy = personality != null ? Mathf.Max(personality.spacingAccuracy, Mathf.Lerp(0.3f, 0.9f, d)) : Mathf.Lerp(0.3f, 0.9f, d);
        return p;
    }

    /// <summary>Everything difficulty sets on a rival for one match.</summary>
    public struct RivalTuning
    {
        public float Difficulty, Budget, FirstSpecialDelay, SpecialCooldown;
        public int SpecialsPerRound, TriplesPerRound, QuadsPerRound;
    }

    public RivalTuning RivalTuningAt(float difficulty, int bossStage = -1)
    {
        float d = Mathf.Clamp01(difficulty);
        return new RivalTuning
        {
            Difficulty = d,
            Budget = Mathf.Max(0f, rivalBudgetByDifficulty.Evaluate(d)) + (bossStage >= 0 ? rivalBossBudgetPerStage * (bossStage + 1) : 0f),
            SpecialsPerRound = Mathf.Max(0, Mathf.FloorToInt(rivalSpecialsPerRound.Evaluate(d))) + (bossStage >= 0 ? 1 : 0),
            TriplesPerRound = Mathf.Max(0, Mathf.FloorToInt(rivalTriplesPerRound.Evaluate(d))),
            QuadsPerRound = Mathf.Max(0, Mathf.FloorToInt(rivalQuadsPerRound.Evaluate(d))),
            FirstSpecialDelay = Mathf.Max(0f, rivalFirstSpecialDelay.Evaluate(d)),
            SpecialCooldown = Mathf.Max(0f, rivalSpecialCooldown.Evaluate(d)),
        };
    }

    /// <summary>A rival's build: its profile's proportions scaled to `budget` points.</summary>
    public FighterBuildStats RivalBuild(FighterBuildStats profile, float budget)
    {
        budget = Mathf.Max(0f, budget);
        float total = profile.Total;
        if (total <= 0f) return FighterBuildStats.Even(budget);
        float k = budget / total;
        return new FighterBuildStats
        {
            punchPower = profile.punchPower * k, kickPower = profile.kickPower * k, agility = profile.agility * k,
            resistance = profile.resistance * k,
        };
    }

    [Tooltip("Build stats used for a fighter with no real source (no Runner run / no authored opponent build).")]
    public float neutralBuildBudget = 80f;

    [Header("Block — see FighterGuard/FightHitResolver's own doc")]
    [Tooltip("Applied on top of the defender's own resistance when a hit is blocked.")]
    public float blockStunMultiplier = 0.5f;
    [Tooltip("Applied on top of the attacker's impact / defender's resistance when a hit is blocked.")]
    public float blockKnockbackMultiplier = 0.5f;
    [Tooltip("BLOCK/CHIP DAMAGE: a correctly blocked hit still deals this fraction of its would-be damage " +
             "(never 0 — holding guard forever must not be a strategy). A move's own chipDamage is a floor.")]
    [Range(0f, 1f)] public float blockDamageMultiplier = 0.06f;
    [Tooltip("GUARD BREAK: the strong hit of a POWERED triple press (x3 finisher) breaks a correct guard — it lands as a " +
             "hit, scaled by this factor (damage, stun, push) so it is a bit weaker than the same hit unguarded. 0 = it is blocked.")]
    [Range(0f, 1f)] public float finisherGuardBreakFactor = 0.75f;

    [Header("Grab / takedown (FighterGrapple) — Punch + Kick together")]
    [Tooltip("Grab reach beyond FightArenaConfig.minimumFighterSeparation (short).")]
    public float grabRangePadding = 0.45f;
    [Tooltip("Seconds the defender has to break a standing grab with Punch + Kick.")]
    public float grabEscapeWindow = 0.45f;
    [Tooltip("Seconds the defender has to break a RUNNING takedown (shorter: it's a committed rush).")]
    public float takedownEscapeWindow = 0.3f;
    [Tooltip("Attacker forward speed (m/s) at or above which Punch + Kick becomes a running takedown.")]
    public float runningGrabMinSpeed = 5f;
    public float throwDamage = 12f;
    public float throwKnockback = 1.4f;
    public float takedownDamage = 9f;
    [Tooltip("Recovery lock after a WHIFFED grab (nobody in range) — grabs are not free.")]
    public float grabWhiffRecovery = 0.45f;
    [Tooltip("Lock for both fighters after a successful break, plus the push apart.")]
    public float throwBreakLock = 0.25f;
    public float throwBreakPush = 0.9f;

    [Header("Ground control (after a successful running takedown)")]
    [Tooltip("Hard maximum of the ground-control window (s). It ALWAYS ends here.")]
    public float groundControlDuration = 1.5f;
    [Tooltip("Max ground strikes (Punch) per takedown.")]
    public int groundStrikeMax = 3;
    public float groundStrikeDamage = 4f;
    [Tooltip("Min seconds between ground strikes.")]
    public float groundStrikeInterval = 0.3f;
    [Tooltip("The defender can escape early with Punch + Kick after this many seconds on the ground.")]
    public float groundEscapeMinTime = 0.6f;

    [Header("Knockdown flow — Knockdown → Downed → GetUp → CombatIdle (invulnerable while falling and getting up)")]
    [Min(0.05f)] public float knockdownDuration = 0.8f;
    [Tooltip("MINIMUM seconds lying on the floor before getting up is possible.")]
    [Min(0f)] public float downedDuration = 0.45f;
    [Tooltip("Tekken-like: a downed fighter STAYS on the floor until it presses Up (or Jump) — so it can let a Special " +
             "fly over it. False = it gets up by itself after downedDuration (old behaviour).")]
    public bool downedWaitsForGetUpInput = true;
    [Tooltip("Seconds after which a downed fighter gets up anyway, even without input (0 = never: it may lie as long as it wants).")]
    [Min(0f)] public float downedMaxDuration = 0f;
    [Tooltip("The rival AI gets up after a random delay in this range (s) once it can — longer if a Special is flying at it.")]
    public Vector2 aiGetUpDelay = new(0.15f, 0.9f);
    [Min(0.05f)] public float getUpDuration = 0.9f;
    [Tooltip("While DOWNED a fighter can still be hit by LOW attacks (ground hits), at this damage share…")]
    [Range(0f, 1f)] public float groundHitDamageMultiplier = 0.5f;
    [Tooltip("…at most this many per knockdown (then it is invulnerable until it is up again)…")]
    [Min(0)] public int groundHitsPerKnockdown = 2;
    [Tooltip("…and each one keeps it down this much longer (never more than once per hit).")]
    [Min(0f)] public float groundHitExtraDown = 0.15f;
    [Tooltip("A POWERED kick finisher (paid with an x3) knocks the defender down.")]
    public bool poweredKickFinisherKnocksDown = true;

    [Header("Guard speed — Agility vs Agility (FighterGuard)")]
    [Tooltip("Seconds a guard needs from the moment Back is pressed until it actually blocks, for a fighter of " +
             "attack speed ×1. Divided by the defender's own attack speed (Agility): a fast fighter raises it sooner, " +
             "and a fast attacker's shorter startup can beat a slow defender's guard.")]
    [Range(0f, 0.4f)] public float guardRaiseTime = 0.12f;

    [Header("Hit flinch — the struck body reacts where it was hit (FighterHitFlinch)")]
    [Tooltip("Overall strength of the body's reaction to a landed hit (0 = off).")]
    [Range(0f, 3f)] public float hitFlinchStrength = 1f;
    [Tooltip("Angular impulse (rad/s per metre of lever between hips and contact) for a light hit.")]
    [Range(0f, 20f)] public float hitFlinchImpulse = 5f;
    [Tooltip("Spring stiffness back to the clip pose: higher = snappier, shorter reaction.")]
    [Range(20f, 600f)] public float hitFlinchStiffness = 170f;
    [Tooltip("Damping ratio (1 = no overshoot, lower = a small rebound).")]
    [Range(0.1f, 1.5f)] public float hitFlinchDamping = 0.5f;
    [Tooltip("Upper limit of the whole spine chain's bend (degrees).")]
    [Range(5f, 60f)] public float hitFlinchMaxDegrees = 35f;
    [Tooltip("A BLOCKED hit still jolts the guard a little (× strength).")]
    [Range(0f, 1f)] public float hitFlinchBlockedScale = 0.35f;

    [Header("Hit effects (FightHitFeedback) — hit-stop off (2026-11-04 playtest), spark back on (2026-11-05: \"no tinc sensació de cop\")")]
    [Tooltip("Near-freeze of time on impact (the classic hit-stop).")]
    public bool hitStopEnabled = false;
    [Tooltip("Spark burst + flash at the contact point.")]
    public bool hitSparkEnabled = true;

    [Header("Recovery cancel (FighterMoveController.RecoveryCancellable)")]
    [Tooltip("Move-time seconds into an attack's Recovery after which guard, crouch, sidestep or jump cancel the rest " +
             "of it (the strike itself — Active + the late-contact margin — always plays out). Large = stiffer.")]
    [Range(0f, 0.5f)] public float recoveryCancelDelay = 0.1f;
    [Tooltip("FEINT (2026-11-05): a strike can be cut in the first part of its Startup — this fraction of it — by a FRESH " +
             "guard, sidestep, jump or dash (pressed after the strike began). 0 = strikes always commit.")]
    [Range(0f, 1f)] public float startupFeintFraction = 0.6f;
    [Tooltip("SIDE REACTION (user design 2026-11-05, both views): the guard ANIMATION towards the side a strike comes from " +
             "lasts this many seconds (÷ the defender's Agility attack speed), from the moment the defender reacts (the " +
             "strike starting with a held guard, a fresh guard press, a first-person side change). It blocks only once it is " +
             "complete — a race against the strike's own (Agility-scaled) startup: shorter than a whole strike, but close to " +
             "the time it takes to arrive, so it is never guaranteed — the faster fighter wins the mismatch.")]
    [Range(0f, 0.5f)] public float guardSideReactTime = 0.16f;
    [Tooltip("A guard ALREADY held only starts turning to the strike's side after seeing it: this perception delay (s, " +
             "÷ Agility). A guard pressed after the strike started needs none — that press IS the reaction.")]
    [Range(0f, 0.3f)] public float guardSidePerception = 0.08f;
    [Tooltip("Root-to-root distance at or under which a high / mid strike becomes its close-range technique " +
             "(uppercut, short hook, knee — FighterMoveController.ResolvePunch/ResolveKick).")]
    [Range(0.5f, 1.2f)] public float closeStrikeRange = 0.72f;
    [Tooltip("After a CORRECT block, a button within this many seconds throws the counter (unblockable).")]
    [Range(0f, 0.5f)] public float counterWindow = 0.2f;
    [Tooltip("A button within this many seconds after a sidestep starts throws the dodge-counter to that side; " +
             "first person: Kick this soon after a backdash = the step-back spinning kick.")]
    [Range(0f, 0.6f)] public float dodgeCounterWindow = 0.3f;

    [Header("Strike step-in (FighterMoveController.BeginMove)")]
    [Tooltip("A grounded strike thrown from farther than its clip reaches (FightMoveDefinition.contactReach) steps " +
             "towards the opponent by up to this much during its startup, so fighters do not have to stand glued " +
             "together to connect (the in-place mocap clips have no step of their own). 0 = off.")]
    [Range(0f, 1f)] public float strikeStepIn = 0.5f;
    [Tooltip("Root distance kept below the measured hips-to-hips reach (hips sit ≈ 5 cm behind each root + margin).")]
    [Range(0f, 0.3f)] public float strikeStepMargin = 0.14f;

    [Header("Double / triple press strong hit (FighterInputController)")]
    [Tooltip("The strong hit of a fast triple press WITHOUT an x3 to power it: still stronger than a normal.")]
    public FightAttackBonus unpoweredFinisher = new(1.2f, 1.3f, 1.4f);
    [Tooltip("Strong hit thrown while holding FORWARD: lunge into the opponent (capped at contact).")]
    [Min(0f)] public float finisherAdvance = 0.8f;
    [Tooltip("Strong hit thrown while holding BACK: steps back this far while hitting.")]
    [Min(0f)] public float finisherRetreat = 0.5f;

    [Header("Finishers (combo definitions with attackBonus = Punch/KickFinisher)")]
    [Tooltip("Multipliers on the Punch Finisher (combo P P P) — only when it is POWERED by one x3 (CombatResourceUse). Its timing lives on the combo definition.")]
    public FightAttackBonus punchFinisher = new(1.5f, 1.6f, 1.8f);
    [Tooltip("Multipliers on the Kick Finisher (combo K K K) — only when powered by one x3.")]
    public FightAttackBonus kickFinisher = new(1.5f, 1.6f, 2.0f);

    [Header("Signature Move (FightMoveSetSO.signatureBasic / signatureSpecial)")]
    [Tooltip("Multipliers on the BASIC Signature (no Special available) — keep below the Special version.")]
    public FightAttackBonus signatureBasic = new(0.75f, 0.8f, 0.8f);
    [Tooltip("Multipliers on the ENHANCED Signature (the projectile — costs 1 Special).")]
    public FightAttackBonus signatureSpecial = new(1.8f, 1.6f, 1.8f);

    [Header("Power State (hold Down + Punch + Kick)")]
    [Tooltip("Seconds Down + Punch + Kick must be held together to activate the Power State.")]
    [Min(0.1f)] public float powerActivationHoldTime = 1.0f;
    [Tooltip("Seconds the Power State lasts when ONE x4 is spent. Activating spends every x4 held (CombatResourceUse).")]
    [Min(0.1f)] public float powerDuration = 4f;
    [Tooltip("Extra seconds for each x4 spent beyond the first (4 s, 5 s with two, 6 s with three…). Provisional.")]
    [Min(0f)] public float powerSecondsPerExtraCharge = 1f;
    [Tooltip("Maximum seconds of Power, regardless of the number of x4 collected.")]
    [Min(0.1f)] public float powerMaxDuration = 12f;
    [Min(0f)] public float powerDamageMultiplier = 1.3f;
    [Min(0f)] public float powerStaggerMultiplier = 1.25f;
    [Min(0f)] public float powerKnockbackMultiplier = 1.25f;
    [Tooltip("Power look: the avatar's Muscle body shape grows by this much (added, clamped to 1) while Power lasts.")]
    [Range(0f, 1f)] public float powerMuscleBoost = 0.4f;
    [Tooltip("Seconds the Muscle shape takes to grow from 0 to powerMuscleBoost when Power starts (progressive).")]
    [Min(0.01f)] public float powerMuscleRampIn = 1f;
    [Tooltip("Seconds the Muscle shape takes to go back from powerMuscleBoost to 0 when Power ends (progressive).")]
    [Min(0.01f)] public float powerMuscleRampOut = 1.2f;

    public FightAttackBonus PowerMultipliers => new(powerDamageMultiplier, powerStaggerMultiplier, powerKnockbackMultiplier);

    /// <summary>Evaluates every curve against one fighter's build — the SAME computation FightHitResolver
    /// (real combat), FighterMovement (speed) and FightDebugHUD use, so they can never drift. `scalingStat` = the
    /// attacking move's stat (None / retired → no attack scaling).</summary>
    public FightCombatModifiers ComputeModifiers(FighterBuildStats stats, FighterBuildStat scalingStat = FighterBuildStat.None)
    {
        bool scales = scalingStat != FighterBuildStat.None && scalingStat != FighterBuildStat.LegacyImpactPower;
        float power = scales ? stats.Get(scalingStat) : 0f;
        return new FightCombatModifiers
        {
            DamageDealtMultiplier   = scales ? buildStatToDamage.Evaluate(power) : 1f,
            ReactionDealtMultiplier = scales ? attackPowerToReaction.Evaluate(power) : 1f,
            DamageTakenMultiplier   = resistanceToDamageTaken.Evaluate(stats.resistance),
            ResistanceMultiplier    = resistanceToStagger.Evaluate(stats.resistance),
            MoveSpeedMultiplier     = Mathf.Clamp(agilityToMoveSpeed.Evaluate(stats.agility), minMoveSpeedMultiplier, maxMoveSpeedMultiplier),
            AttackSpeedMultiplier   = Mathf.Clamp(agilityToAttackSpeed.Evaluate(stats.agility), minAttackSpeed, maxAttackSpeed),
        };
    }

    /// <summary>The bounded reaction scale of one hit (see minReactionScale / maxReactionScale).</summary>
    public float ReactionScale(float attackerReaction, float defenderResistance, float bonus) =>
        Mathf.Clamp(attackerReaction * defenderResistance * bonus, minReactionScale, maxReactionScale);
}

/// <summary>One fighter's fully-evaluated combat modifiers — see FightCombatBalanceConfig.ComputeModifiers.</summary>
public struct FightCombatModifiers
{
    public float DamageDealtMultiplier;    // attacker, from the move's scaling stat (PunchPower / KickPower)
    public float ReactionDealtMultiplier;  // attacker, same stat, milder curve (hit stun + knockback dealt)
    public float DamageTakenMultiplier;    // defender Resistance
    public float ResistanceMultiplier;     // defender Resistance (hit stun + knockback taken)
    public float MoveSpeedMultiplier;      // own Agility (movement speed)
    public float AttackSpeedMultiplier;    // own Agility (move time per real second — whole move, uniform)

    public static FightCombatModifiers Identity => new FightCombatModifiers
    {
        DamageDealtMultiplier = 1f, ReactionDealtMultiplier = 1f, DamageTakenMultiplier = 1f,
        ResistanceMultiplier = 1f, MoveSpeedMultiplier = 1f, AttackSpeedMultiplier = 1f,
    };
}
