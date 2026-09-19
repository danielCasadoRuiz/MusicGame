using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which side of the arena this actor represents — the ONLY structural difference between a Player
/// and an Opponent FighterActor. Everything else (movement, facing, moves, visuals, the FULL combat
/// pipeline) is the exact same component set; what differs is configuration (FighterStats) and which
/// IFightInputSource drives it: Player gets HumanFightInputSource, Opponent gets AIFightInputSource
/// (see FighterAI's own doc) — both feed the exact same FighterInputController/Move System, so
/// neither side has any special-cased gameplay path.
/// </summary>
public enum FighterSide
{
    Player,
    Opponent,
}

/// <summary>
/// The common shell for ANY fighter in the arena — Player and Opponent are the exact same
/// FighterActor type (see FighterSide's own doc for why), never two parallel class hierarchies.
///
/// Deliberately separates:
///   - GAMEPLAY ROOT (this GameObject's own Transform) — what movement/facing/future
///     hitboxes/AI actually reason about. Always moves/rotates as a whole; never depends on
///     whatever is inside VisualRoot.
///   - VISUAL ROOT (a child Transform) — purely presentational. Today it holds either the
///     assigned fighterPrefab's instance, or a fallback debug capsule when none is assigned (see
///     Initialize). Swapping a capsule for a real humanoid+Animator later means reparenting a new
///     prefab under VisualRoot — nothing about movement/facing/FighterMoveController/future combat
///     changes, because none of them ever reach into VisualRoot's own hierarchy.
///
/// Facing is resolved through the same IFightFacingProvider boundary FighterInputController already
/// uses (see FightFacing.cs) — this class never computes "am I facing right" itself, it just asks
/// whatever provider it was given (RealFightFacingProvider once wired by FightSceneBootstrap) and
/// rotates the GAMEPLAY ROOT to match, except while MoveController.IsFacingLocked is true (see
/// FightMoveDefinition.lockFacingDuringMove's own doc) — facing simply holds its last value for
/// those frames rather than being special-cased per move type.
///
/// MoveController/Movement are OPTIONAL — null for the Opponent this phase (no AI/attacks exist for
/// it yet, see FightSceneBootstrap's own doc), always set for the Player. Both are prepared, not
/// implemented, as attach points for a future AIFightInputSource-driven Opponent: the exact same
/// FighterActor, just with those two references populated too.
///
/// DistanceToOpponent is the SINGLE place this relationship is computed — every future system
/// (attack range, AI spacing, camera) should read this instead of recomputing its own copy.
/// </summary>
public class FighterActor : MonoBehaviour
{
    public FighterSide Side { get; private set; }
    public Transform VisualRoot { get; private set; }
    public bool UsedFallbackCapsule { get; private set; }

    /// <summary>Present on BOTH sides now that the Opponent plays for real — see FighterAI's own
    /// doc. Each fighter gets its OWN instance (never shared/found via FindFirstObjectByType).</summary>
    public FighterMoveController MoveController { get; private set; }
    public FighterMovement Movement { get; private set; }
    public FighterAttack Attack { get; private set; }
    public FighterInputController InputController { get; private set; }
    /// <summary>Null for the Player — the Opponent's Utility AI "brain" driving its own
    /// AIFightInputSource. See FighterAI's own doc.</summary>
    public FighterAI AI { get; private set; }

    /// <summary>Always present on BOTH sides — Player and Opponent share the exact same health/
    /// hit-reaction/guard system (see FighterHealth/FighterHitReaction/FighterGuard's own doc).</summary>
    public FighterHealth Health { get; private set; }
    public FighterHitReaction HitReaction { get; private set; }
    public FighterGuard Guard { get; private set; }

    /// <summary>The other FighterActor in the arena — set once via SetOpponent, right after both
    /// exist. Public so FightProjectile (and future AI) can resolve "who do I actually target" the
    /// same way DistanceToOpponent already does, instead of re-deriving it.</summary>
    public FighterActor Opponent => _opponent;

    /// <summary>This fighter's physical stance — see FighterPosture's own doc. Standing by default;
    /// written exclusively by FighterMovement (Player this phase) via SetPosture.</summary>
    public FighterPosture Posture { get; private set; } = FighterPosture.Standing;
    /// <summary>This fighter's current locomotion gait — see FighterMovementState's own doc. Idle by
    /// default; written exclusively by FighterMovement via SetMovementState.</summary>
    public FighterMovementState MovementState { get; private set; } = FighterMovementState.Idle;

    /// <summary>This fighter's combat numbers — the Player's come from the Runner (GameSession.
    /// FighterStats), the Opponent's from OpponentLevelConfig.combatStats (or a flat 100-everywhere
    /// default) — see FightSceneBootstrap's own wiring. Read by FightHitResolver/FightDebugHUD;
    /// never mutated at runtime.</summary>
    public FighterStats Stats { get; private set; }

    /// <summary>One or more zones that can receive a hit — always at least the default one created
    /// in Initialize; a future humanoid can add more (head/torso/legs/...) simply by having
    /// FighterHurtbox children that self-register via RegisterHurtbox. See FighterHurtbox's own doc.</summary>
    public IReadOnlyList<FighterHurtbox> Hurtboxes => _hurtboxes;

    /// <summary>True while this actor's gameplay-root Forward is world +X. Frozen (not recomputed)
    /// while MoveController.IsFacingLocked OR HitReaction.IsInHitStun is true — see Update.</summary>
    public bool FacingRight { get; private set; } = true;

    /// <summary>Straight-line distance to the opponent along the arena's main axis (world X) — the
    /// one place this is computed; see class doc.</summary>
    public float DistanceToOpponent => _opponent != null
        ? Mathf.Abs(transform.position.x - _opponent.transform.position.x)
        : 0f;

    private FighterActor _opponent;
    private IFightFacingProvider _facingProvider;
    private readonly List<FighterHurtbox> _hurtboxes = new();

    /// <summary>Builds VisualRoot and its content — visualPrefab's instance if assigned, otherwise a
    /// debug capsule tinted debugColor. Called once, right after this GameObject is created.</summary>
    public void Initialize(FighterSide side, GameObject visualPrefab, Color debugColor)
    {
        Side = side;

        var visualRootGO = new GameObject("VisualRoot");
        visualRootGO.transform.SetParent(transform, false);
        VisualRoot = visualRootGO.transform;

        if (visualPrefab != null)
        {
            var instance = Instantiate(visualPrefab, VisualRoot);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            UsedFallbackCapsule = false;
        }
        else
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.SetParent(VisualRoot, false);
            capsule.transform.localPosition = Vector3.zero;
            var rend = capsule.GetComponent<Renderer>();
            if (rend != null) rend.material.color = debugColor;
            UsedFallbackCapsule = true;
        }

        // Matches the default FacingRight = true above — set explicitly since a fresh Transform's
        // identity rotation faces world +Z, not the arena's +X "facing right" convention.
        transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

        // Universal on both sides — see Health/HitReaction's own doc. FighterHurtbox self-registers
        // (see its own Awake), so no explicit RegisterHurtbox call is needed here.
        Health = gameObject.AddComponent<FighterHealth>();
        Health.SetOwner(this);

        var hurtboxGO = new GameObject("HurtboxDefault");
        hurtboxGO.transform.SetParent(transform, false);
        hurtboxGO.AddComponent<FighterHurtbox>().Initialize(this, Vector3.zero, new Vector3(1f, 2f, 1f));
    }

    public void SetOpponent(FighterActor opponent) => _opponent = opponent;
    public void SetFacingProvider(IFightFacingProvider provider) => _facingProvider = provider;
    public void SetMoveController(FighterMoveController controller) => MoveController = controller;
    public void SetMovement(FighterMovement movement) => Movement = movement;
    public void SetAttack(FighterAttack attack) => Attack = attack;
    public void SetInputController(FighterInputController input) => InputController = input;
    public void SetAI(FighterAI ai) => AI = ai;
    public void SetStats(FighterStats stats) => Stats = stats;
    public void SetPosture(FighterPosture posture) => Posture = posture;
    public void SetMovementState(FighterMovementState state) => MovementState = state;
    public void RegisterHurtbox(FighterHurtbox hurtbox)
    {
        if (!_hurtboxes.Contains(hurtbox)) _hurtboxes.Add(hurtbox);
    }

    /// <summary>Creates and wires this actor's FighterHitReaction/FighterGuard — separate from
    /// Initialize because both need the opponent reference (HitReaction for knockback direction,
    /// Guard indirectly via MoveController/Posture), only available after BOTH actors exist and
    /// SetOpponent has run (see FightSceneBootstrap's own call order).</summary>
    public void AttachHitReaction(FightArenaConfig arenaConfig, FighterInputController input)
    {
        var reaction = gameObject.AddComponent<FighterHitReaction>();
        reaction.Initialize(this, _opponent, arenaConfig);
        HitReaction = reaction;

        var guard = gameObject.AddComponent<FighterGuard>();
        guard.Initialize(this, input);
        Guard = guard;
    }

    /// <summary>
    /// The ONE explicit reset entry point FightMatchController calls between rounds — coordinates
    /// every component's OWN reset (see each one's own ResetForRound doc) rather than reaching into
    /// any of their private fields (task's own explicit "no accedeixis a camps privats" requirement).
    /// Covers: position (spawn), facing (recomputed immediately, not left for next Update), health/
    /// KO, hit stun, current/queued move, hitboxes + hit-target history, movement locks, and pending
    /// knockback/lunge — see this phase's own scope note for the full checklist this satisfies.
    /// Attack/MoveController/Movement are null-tolerant (Opponent has none this phase).
    /// </summary>
    public void ResetForRound(Vector3 spawnPosition)
    {
        transform.position = spawnPosition;

        Health?.ResetForRound();
        HitReaction?.ResetForRound();
        Movement?.ResetForRound();
        MoveController?.ResetForRound();
        Attack?.ResetForRound();
        InputController?.ResetForRound();
        AI?.ResetForRound();
        Posture = FighterPosture.Standing;
        MovementState = FighterMovementState.Idle;

        if (_opponent != null && _facingProvider != null)
        {
            FacingRight = _facingProvider.FacingRight;
            transform.rotation = Quaternion.LookRotation(FacingRight ? Vector3.right : Vector3.left, Vector3.up);
        }
    }

    private void Update()
    {
        if (_opponent == null || _facingProvider == null) return;

        // Facing freezes during a locked move phase, hit stun, AND block stun — one shared
        // mechanism, no per-move or per-hit special-casing (see this phase's own scope note).
        bool locked = (MoveController != null && MoveController.IsFacingLocked) ||
                      (HitReaction != null && (HitReaction.IsInHitStun || HitReaction.IsInBlockStun));
        if (!locked) FacingRight = _facingProvider.FacingRight;

        transform.rotation = Quaternion.LookRotation(FacingRight ? Vector3.right : Vector3.left, Vector3.up);
    }
}
