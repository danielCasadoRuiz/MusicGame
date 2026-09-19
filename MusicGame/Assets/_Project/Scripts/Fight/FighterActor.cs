using UnityEngine;

/// <summary>
/// Which side of the arena this actor represents — the ONLY structural difference between a Player
/// and an Opponent FighterActor. Everything else (movement, facing, moves, visuals) is the exact
/// same component set; what differs is configuration (FighterStats, someday), which prefab gets
/// instantiated, and — this phase — whether a real IFightInputSource drives it at all (Player: yes,
/// via the existing FighterInputController singleton; Opponent: no, so it simply never moves — see
/// FightSceneBootstrap's own doc on why no AIFightInputSource exists yet).
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

    /// <summary>Null for the Opponent this phase — see class doc.</summary>
    public FighterMoveController MoveController { get; private set; }
    /// <summary>Null for the Opponent this phase — see class doc.</summary>
    public FighterMovement Movement { get; private set; }

    /// <summary>True while this actor's gameplay-root Forward is world +X. Frozen (not recomputed)
    /// while MoveController.IsFacingLocked is true.</summary>
    public bool FacingRight { get; private set; } = true;

    /// <summary>Straight-line distance to the opponent along the arena's main axis (world X) — the
    /// one place this is computed; see class doc.</summary>
    public float DistanceToOpponent => _opponent != null
        ? Mathf.Abs(transform.position.x - _opponent.transform.position.x)
        : 0f;

    private FighterActor _opponent;
    private IFightFacingProvider _facingProvider;

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
    }

    public void SetOpponent(FighterActor opponent) => _opponent = opponent;
    public void SetFacingProvider(IFightFacingProvider provider) => _facingProvider = provider;
    public void SetMoveController(FighterMoveController controller) => MoveController = controller;
    public void SetMovement(FighterMovement movement) => Movement = movement;

    private void Update()
    {
        if (_opponent == null || _facingProvider == null) return;

        bool locked = MoveController != null && MoveController.IsFacingLocked;
        if (!locked) FacingRight = _facingProvider.FacingRight;

        transform.rotation = Quaternion.LookRotation(FacingRight ? Vector3.right : Vector3.left, Vector3.up);
    }
}
