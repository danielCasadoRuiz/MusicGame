using UnityEngine;

/// <summary>
/// HOW a fighter's body performs each logical combat role — role → processed AnimationClip (the
/// Rokoko in-place clips already in Animations/Processed/Combat, referenced, never duplicated).
/// Applied at runtime onto the ONE shared FighterCombat controller through a per-fighter
/// AnimatorOverrideController (see AnimatorFighterAnimationDriver). Many fighters may share one set.
/// Presentation only: gameplay timing always comes from FightMoveDefinition.
/// </summary>
[CreateAssetMenu(fileName = "FighterAnimationSet", menuName = "MusicGame/Fight/Fighter Animation Set")]
public class FighterAnimationSetSO : ScriptableObject
{
    [System.Serializable]
    public class RoleClip
    {
        public CombatRole role;
        public AnimationClip clip;
        [Tooltip("Marks a slot whose clip is a stand-in (reused from another move / not ideal yet).")]
        public bool provisional;
        [TextArea] public string note;
    }

    public RoleClip[] clips = System.Array.Empty<RoleClip>();

    [Header("Playback tuning (shared by every fighter using this set)")]
    [Tooltip("Crossfade (s) into a move / out of a move back to CombatIdle.")]
    [Min(0f)] public float moveCrossfade = 0.08f;
    [Tooltip("Crossfade (s) into a hit/block reaction.")]
    [Min(0f)] public float reactionCrossfade = 0.05f;
    [Tooltip("Crossfade (s) into Knockdown (should be near-immediate).")]
    [Min(0f)] public float knockdownCrossfade = 0.04f;
    [Tooltip("Crossfade (s) between idle/locomotion/downed/get-up/pose states.")]
    [Min(0f)] public float stateCrossfade = 0.15f;
    [Tooltip("Move animations are sped up/slowed down so the clip spans the move's gameplay duration " +
             "(Startup+Active+Recovery — the AUTHORITATIVE timing), clamped to this range to avoid " +
             "distortion. Outside it the clip simply ends early (then crossfades to idle) or is cut.")]
    public Vector2 moveSpeedRange = new Vector2(0.75f, 1.6f);

    public AnimationClip Get(CombatRole role)
    {
        if (clips == null) return null;
        foreach (var c in clips) if (c != null && c.role == role) return c.clip;
        return null;
    }
}
