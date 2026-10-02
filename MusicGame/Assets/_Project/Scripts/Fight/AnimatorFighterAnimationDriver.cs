using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The real IFighterAnimationDriver: shows the combat runtime's decisions on a Humanoid Animator.
///
///   FighterAnimationSetSO (role → clip)
///        ↓  per-fighter AnimatorOverrideController (created here, owned by this fighter only —
///           two fighters never share or mutate each other's mapping)
///   ONE shared FighterCombat.controller (one state per CombatRole, each with a placeholder clip
///   named "FighterCombat_<Role>", all states speed-driven by the "RoleSpeed" parameter)
///        ↓
///   the fighter's Animator
///
/// Presentation only — it never decides timing: FighterMoveController tells it which role to show and
/// for how long; the clip is fitted to that duration (FighterAnimationSetSO.moveSpeedRange). No
/// runtime editing of controller assets; root motion off (the arena/gameplay owns position).
/// </summary>
public class AnimatorFighterAnimationDriver : IFighterAnimationDriver
{
    public const string PlaceholderPrefix = "FighterCombat_";
    public const string SpeedParameter    = "RoleSpeed";

    private readonly Animator _animator;
    private readonly FighterAnimationSetSO _set;
    private readonly HashSet<CombatRole> _statesPresent = new();
    private bool _altLight, _altHeavy;
    private CombatRole _current = CombatRole.CombatIdle;
    private bool _hasPlayed;

    public string CurrentState => $"{_current}{(_set != null && _set.Get(_current) != null ? "" : " (no clip)")}";
    public CombatRole CurrentRole => _current;
    public AnimatorOverrideController Override { get; }

    public AnimatorFighterAnimationDriver(Animator animator, RuntimeAnimatorController baseController, FighterAnimationSetSO set)
    {
        _animator = animator;
        _set      = set;
        Override  = new AnimatorOverrideController(baseController) { name = $"{baseController.name} ({animator.name})" };

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(Override.overridesCount);
        Override.GetOverrides(overrides);
        var fallback = set != null ? set.Get(CombatRole.CombatIdle) : null;
        for (int i = 0; i < overrides.Count; i++)
        {
            var placeholder = overrides[i].Key;
            if (placeholder == null || !placeholder.name.StartsWith(PlaceholderPrefix)) continue;
            if (!System.Enum.TryParse(placeholder.name.Substring(PlaceholderPrefix.Length), out CombatRole role)) continue;
            _statesPresent.Add(role);
            var clip = set != null ? set.Get(role) : null;
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(placeholder, clip != null ? clip : fallback);
        }
        Override.ApplyOverrides(overrides);

        animator.runtimeAnimatorController = Override;
        animator.applyRootMotion = false;
        animator.SetFloat(SpeedParameter, 1f);
    }

    public void PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration)
    {
        var role = move != null ? move.role : CombatRole.CombatIdle;
        // Variety: consecutive light / heavy attacks alternate with their Alt clip (when the set has one).
        if (role == CombatRole.LightAttack && HasClip(CombatRole.LightAttackAlt) && (_altLight = !_altLight)) role = CombatRole.LightAttackAlt;
        else if (role == CombatRole.HeavyAttack && HasClip(CombatRole.HeavyAttackAlt) && (_altHeavy = !_altHeavy)) role = CombatRole.HeavyAttackAlt;
        Play(role, gameplayDuration, _set != null ? _set.moveCrossfade : 0.08f, restart: true);
    }

    public void PlayRole(CombatRole role, float duration = -1f)
    {
        float fade = _set == null ? 0.1f : role switch
        {
            CombatRole.HitReaction    => _set.reactionCrossfade,
            CombatRole.HitReactionAlt => _set.reactionCrossfade,
            CombatRole.Knockdown   => _set.knockdownCrossfade,
            CombatRole.Block       => _set.reactionCrossfade,
            _                      => _set.stateCrossfade,
        };
        if (role == CombatRole.HitReactionAlt && !HasClip(role)) role = CombatRole.HitReaction;
        bool restart = role == CombatRole.HitReaction || role == CombatRole.HitReactionAlt || role == CombatRole.Knockdown || role == CombatRole.GetUp;
        Play(role, duration, fade, restart);
    }

    public void SetLocomotion(FightHorizontalDirection direction, FighterMovementState state, FighterPosture posture, bool turning)
    {
        // Most specific first; a role without a clip in this fighter's set falls through to the next
        // candidate and finally to CombatIdle (guard), exactly like before locomotion clips existed.
        CombatRole role = CombatRole.CombatIdle;
        if (posture == FighterPosture.Airborne && HasClip(CombatRole.Jump)) role = CombatRole.Jump;
        else if (state == FighterMovementState.Run && HasClip(CombatRole.Run)) role = CombatRole.Run;
        else if (turning && HasClip(CombatRole.Turn)) role = CombatRole.Turn;
        else if (state == FighterMovementState.Walk || state == FighterMovementState.Run)
        {
            var walk = direction == FightHorizontalDirection.Back ? CombatRole.WalkBack : CombatRole.WalkForward;
            if (direction != FightHorizontalDirection.Neutral && HasClip(walk)) role = walk;
        }
        if (role != _current || !_hasPlayed)
        {
            // Back to guard keeps the original quick move crossfade; between locomotion states the
            // slightly longer state crossfade avoids pops when walking/running starts or stops.
            float fade = _set == null ? 0.08f : role == CombatRole.CombatIdle ? _set.moveCrossfade : _set.stateCrossfade;
            Play(role, -1f, fade, restart: false);
        }
    }

    private bool HasClip(CombatRole role) => _statesPresent.Contains(role) && _set != null && _set.Get(role) != null;

    private void Play(CombatRole role, float duration, float fade, bool restart)
    {
        if (_animator == null) return;
        if (!_statesPresent.Contains(role)) role = CombatRole.CombatIdle;
        if (!restart && _hasPlayed && role == _current) return;

        var clip = _set != null ? _set.Get(role) : null;
        float speed = 1f;
        if (duration > 0f && clip != null && clip.length > 0f)
        {
            var range = _set != null ? _set.moveSpeedRange : new Vector2(0.75f, 1.6f);
            speed = Mathf.Clamp(clip.length / duration, range.x, range.y);
        }
        _animator.SetFloat(SpeedParameter, speed);
        // Before the first real frame an instant switch avoids a crossfade from the bind (T-)pose.
        _animator.CrossFadeInFixedTime(role.ToString(), _hasPlayed ? fade : 0f, 0, 0f);
        _current   = role;
        _hasPlayed = true;
    }
}
