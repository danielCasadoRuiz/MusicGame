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
    private readonly Dictionary<CombatRole, bool> _altToggle = new();
    private CombatRole _current = CombatRole.CombatIdle;
    private bool _hasPlayed;
    private bool _currentMirrored;

    public string CurrentState => $"{_current}{(_currentMirrored ? MirrorSuffix : "")}{(_set != null && _set.Get(_current) != null ? "" : " (no clip)")}";
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

    public void PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale = 1f) =>
        PlayMoveAnimation(move, gameplayDuration, speedScale, 0);

    /// <summary>`side` (+1 right / −1 left / 0 = as authored): the limb that must strike. A strike clip authored with the
    /// other side plays MIRRORED (its "_M" state). Returns the side that really strikes (0 = not a sided strike).</summary>
    public int PlayMoveAnimation(FightMoveDefinition move, float gameplayDuration, float speedScale, int side)
    {
        var role = Resolve(move != null ? move.role : CombatRole.CombatIdle);
        // Variety: consecutive strikes of the same role alternate with its Alt clip (when the set has one) —
        // a P P P / K K K chain (including its strong hit) never repeats the same clip back to back.
        var alt = AltOf(role);
        if (alt != role && HasClip(alt))
        {
            _altToggle.TryGetValue(role, out bool useAlt);
            _altToggle[role] = !useAlt;
            if (useAlt) role = alt;
        }
        int natural = _set != null && IsMirrorable(role) ? _set.SideOf(role) : 0;
        bool mirror = natural != 0 && side != 0 && side != natural && HasState(MirrorState(role));
        Play(role, gameplayDuration, _set != null ? _set.moveCrossfade : 0.08f, restart: true, speedScale, mirror);
        // Side 0 = a CENTRE (straight) strike: it lands on the target's midline whatever hand throws it.
        return side == 0 || natural == 0 ? 0 : mirror ? -natural : natural;
    }

    /// <summary>Strike roles that may play mirrored (left ↔ right) — CombatContentSetup adds a "&lt;Role&gt;_M" state for each.</summary>
    public static bool IsMirrorable(CombatRole role) => role switch
    {
        CombatRole.LightAttack or CombatRole.LightAttackAlt or CombatRole.HeavyAttack or CombatRole.HeavyAttackAlt or
        CombatRole.LowPunch or CombatRole.LowPunchAlt or CombatRole.HighPunch or CombatRole.HighPunchAlt or
        CombatRole.Kick or CombatRole.KickAlt or CombatRole.LowKick or CombatRole.LowKickAlt or
        CombatRole.HighKick or CombatRole.HighKickAlt or CombatRole.Sidestep or CombatRole.HitReactionSide or
        CombatRole.PunchHighSide or CombatRole.PunchMidSide or CombatRole.PunchLowSide or CombatRole.SpinHeelKick or
        CombatRole.SpinSweep or CombatRole.AirSpinHeel or CombatRole.Knee => true,
        _ => false,
    };

    public const string MirrorSuffix = "_M";
    public static string MirrorState(CombatRole role) => role + MirrorSuffix;
    private bool HasState(string state) => _animator != null && _animator.HasState(0, Animator.StringToHash(state));

    public void PlayGuard(float raiseTime)
    {
        float fade = Mathf.Max(_set != null ? _set.reactionCrossfade : 0.05f, raiseTime);
        Play(CombatRole.Block, -1f, fade, restart: false);
    }

    public void PlayGuard(float raiseTime, FighterGuardState state, int side)
    {
        // Side guards: High = the high block of that side; Mid / Low = that side's forearm block (the low one is also crouched).
        var role = side == 0 ? CombatRole.Block
                 : state == FighterGuardState.HighGuard ? (side < 0 ? CombatRole.GuardHighLeft : CombatRole.GuardHighRight)
                 : (side < 0 ? CombatRole.GuardLowLeft : CombatRole.GuardLowRight);
        if (!HasClip(role)) role = CombatRole.Block;
        // A side guard's clip is fitted to the reaction time: it is complete exactly when it starts blocking.
        bool sided = role != CombatRole.Block;
        float fade = sided ? (_set != null ? _set.reactionCrossfade : 0.05f) : Mathf.Max(_set != null ? _set.reactionCrossfade : 0.05f, raiseTime);
        Play(role, sided && raiseTime > 0f ? raiseTime : -1f, fade, restart: false);
    }

    public void PlaySided(CombatRole role, int side, float duration = -1f)
    {
        if (!HasClip(role)) return;
        int natural = _set != null ? _set.SideOf(role) : 0;
        bool mirror = natural != 0 && side != 0 && side != natural && HasState(MirrorState(role));
        float fade = _set == null ? 0.08f : role == CombatRole.HitReactionSide ? _set.reactionCrossfade : _set.moveCrossfade;
        Play(role, duration, fade, restart: role == CombatRole.HitReactionSide, 1f, mirror);
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
            // Guarded footwork when the set has it (a fighter walks in stance), else the plain walk.
            var walk = direction == FightHorizontalDirection.Back ? CombatRole.GuardWalkBack : CombatRole.GuardWalkForward;
            walk = Resolve(walk);
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

    /// <summary>The most specific role this fighter can actually show: a newer role with no state / clip
    /// falls back to its generic one (height punches → LightAttack, height kicks → Kick, heel kick → the
    /// spinning Special clip, guarded walks → plain walks, …), finally CombatIdle.</summary>
    public CombatRole Resolve(CombatRole role)
    {
        for (int i = 0; i < 4 && !HasClip(role); i++)
        {
            var next = Fallback(role);
            if (next == role) break;
            role = next;
        }
        return role;
    }

    /// <summary>The alternate clip role of a strike role (itself when it has none).</summary>
    public static CombatRole AltOf(CombatRole role) => role switch
    {
        CombatRole.LightAttack => CombatRole.LightAttackAlt,
        CombatRole.HeavyAttack => CombatRole.HeavyAttackAlt,
        CombatRole.LowPunch    => CombatRole.LowPunchAlt,
        CombatRole.HighPunch   => CombatRole.HighPunchAlt,
        CombatRole.Kick        => CombatRole.KickAlt,
        CombatRole.LowKick     => CombatRole.LowKickAlt,
        CombatRole.HighKick    => CombatRole.HighKickAlt,
        _ => role,
    };

    public static CombatRole Fallback(CombatRole role) => role switch
    {
        CombatRole.LowPunchAlt => CombatRole.LowPunch,
        CombatRole.HighPunchAlt => CombatRole.HighPunch,
        CombatRole.KickAlt => CombatRole.Kick,
        CombatRole.LowKickAlt => CombatRole.LowKick,
        CombatRole.HighKickAlt => CombatRole.HighKick,
        CombatRole.LowPunch or CombatRole.HighPunch => CombatRole.LightAttack,
        CombatRole.LowKick or CombatRole.HighKick or CombatRole.FlyingKick => CombatRole.Kick,
        CombatRole.SpinHeelKick => CombatRole.Special,
        CombatRole.SpecialCharge => CombatRole.HeavyAttack,
        CombatRole.GuardWalkBack => CombatRole.WalkBack,
        CombatRole.GuardWalkForward => CombatRole.WalkForward,
        CombatRole.LightAttackAlt => CombatRole.LightAttack,
        CombatRole.HeavyAttackAlt => CombatRole.HeavyAttack,
        CombatRole.HitReactionAlt => CombatRole.HitReaction,
        CombatRole.PunchHighSide => CombatRole.HighPunch,
        CombatRole.PunchMidSide => CombatRole.LightAttack,
        CombatRole.PunchLowSide => CombatRole.LowPunch,
        CombatRole.KickHighCenter => CombatRole.HighKick,
        CombatRole.KickMidCenter => CombatRole.Kick,
        CombatRole.KickLowCenter => CombatRole.LowKick,
        CombatRole.Uppercut => CombatRole.HeavyAttack,
        CombatRole.Knee => CombatRole.Kick,
        CombatRole.SpinSweep => CombatRole.LowKick,
        CombatRole.AirSpinHeel => CombatRole.SpinHeelKick,
        _ => CombatRole.CombatIdle,
    };

    // speedScale (Agility) multiplies AFTER the clip-fitting clamp: the clamp only keeps each clip's own natural
    // tempo sane; an agile fighter must still look faster (and stay in sync with its faster gameplay clock).
    private void Play(CombatRole role, float duration, float fade, bool restart, float speedScale = 1f, bool mirror = false)
    {
        if (_animator == null) return;
        if (!_statesPresent.Contains(role)) role = Resolve(role);
        if (!_statesPresent.Contains(role)) role = CombatRole.CombatIdle;
        if (!restart && _hasPlayed && role == _current && mirror == _currentMirrored) return;

        var clip = _set != null ? _set.Get(role) : null;
        float speed = 1f;
        if (duration > 0f && clip != null && clip.length > 0f)
        {
            var range = _set != null ? _set.moveSpeedRange : new Vector2(0.75f, 1.6f);
            speed = Mathf.Clamp(clip.length / duration, range.x, range.y);
        }
        speed *= Mathf.Max(0.1f, speedScale);
        _animator.SetFloat(SpeedParameter, speed);
        // Before the first real frame an instant switch avoids a crossfade from the bind (T-)pose.
        _animator.CrossFadeInFixedTime(mirror ? MirrorState(role) : role.ToString(), _hasPlayed ? fade : 0f, 0, 0f);
        _current   = role;
        _currentMirrored = mirror;
        _hasPlayed = true;
    }
}
