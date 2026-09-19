using UnityEngine;

/// <summary>
/// One zone of a FighterActor that can RECEIVE a hit — deliberately its own, independent shape, not
/// the capsule visual (see FighterActor's own doc: "La capsule visual NO és automàticament la
/// hurtbox"). A FighterActor always has at least one (created by FighterActor.Initialize) but this
/// is deliberately a standalone component, not a single field on FighterActor, so a future humanoid
/// prefab can carry SEVERAL of these as children (head/torso/legs/...) — each one self-registers
/// with its owning FighterActor here in Awake, so FighterAttack's damage pipeline (which just
/// iterates FighterActor.Hurtboxes) never needs to change to support that.
///
/// Defined in local space relative to the GAMEPLAY ROOT (the owning FighterActor's own Transform),
/// exactly like FightHitDefinition's own offsets — never relative to VisualRoot/mesh/bones, so this
/// stays correct verbatim once a real humanoid replaces the capsule.
///
/// Always an axis-aligned box (see FightCombatShapes) — simple and sufficient for a capsule-only V1;
/// per-zone shape variety is a future concern once real body parts exist to justify it.
/// </summary>
public class FighterHurtbox : MonoBehaviour
{
    public FighterActor Owner { get; private set; }

    [SerializeField] private Vector3 localOffset = Vector3.zero;
    [SerializeField] private Vector3 size = new Vector3(1f, 2f, 1f);

    public Vector3 WorldCenter => (Owner != null ? Owner.transform.position : transform.position) + localOffset;
    public Vector3 Size => size;

    public void Initialize(FighterActor owner, Vector3 offset, Vector3 boxSize)
    {
        localOffset = offset;
        size = boxSize;
        RegisterWith(owner);
    }

    private void Awake() => RegisterWith(GetComponentInParent<FighterActor>());

    private void RegisterWith(FighterActor owner)
    {
        if (owner == null || Owner == owner) return;
        Owner = owner;
        Owner.RegisterHurtbox(this);
    }

    private void OnDrawGizmos()
    {
        if (!FightCombatDebugVisuals.Enabled) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireCube(WorldCenter, size);
    }
}

/// <summary>Zero-infrastructure debug toggle for hurtbox/hitbox Gizmos (Scene view, Play mode) — see
/// FighterHurtbox/FighterAttack's own OnDrawGizmos.</summary>
public static class FightCombatDebugVisuals
{
    public static bool Enabled = true;
}
