using UnityEngine;

/// <summary>Semantic Runner presentation roles — one logical state family each in the shared
/// RunnerHumanoid controller (see RunnerAvatarAnimator).</summary>
public enum RunnerAnimationRole
{
    Idle,
    Locomotion,
    FastLocomotion,
    Jump,
    Land,
    AutoReturn,
    Flourish,
    Fall,
    /// <summary>Held while PlayerController.IsCrouching (looping duck pose). Appended — never reorder.</summary>
    Crouch,
}

/// <summary>One assignable clip. Any Humanoid AnimationClip works (Rokoko, CMU, future video-mocap
/// output…) — the runtime never looks at clip names or sources.</summary>
[System.Serializable]
public class RunnerAnimationEntry
{
    public AnimationClip clip;
    [Tooltip("Base playback multiplier for this clip (cadence matching).")]
    public float playbackSpeed = 1f;
    [Tooltip("Flourish only: play on the upper-body layer so the legs keep running (gestures from " +
             "standing takes). Off = full-body (spins, kicks) on the base layer.")]
    public bool upperBodyOnly = true;
    [Tooltip("Placeholder content to be replaced by a proper clip later.")]
    public bool provisional;
    [TextArea] public string note;
}

/// <summary>
/// HOW the player visually moves during the Runner — a data-only style (Default, Salsa, Electronic,
/// Classical…). Every role holds VARIANTS (arrays); any role left empty falls back to the Default
/// style's, and finally to Locomotion, so the Animator never runs without a clip.
///
/// Purely presentation: PlayerController stays the movement authority, root motion is always off.
/// The same style works on every MakeHuman body (slim/heavy/muscular/any Gender) — all clips are
/// Humanoid and retarget to the one canonical skeleton.
/// </summary>
[CreateAssetMenu(fileName = "RunnerAnimationStyle", menuName = "MusicGame/Player/Runner Animation Style")]
public class RunnerAnimationStyleSO : ScriptableObject
{
    public string displayName;

    [Header("Roles (variants)")]
    public RunnerAnimationEntry[] idle = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] locomotion = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] fastLocomotion = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] jump = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] land = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] autoReturn = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] flourish = System.Array.Empty<RunnerAnimationEntry>();
    public RunnerAnimationEntry[] fall = System.Array.Empty<RunnerAnimationEntry>();
    [Tooltip("Looping duck/crouch pose, held while crouching (empty = Default style's).")]
    public RunnerAnimationEntry[] crouch = System.Array.Empty<RunnerAnimationEntry>();

    [Header("Playback")]
    [Tooltip("Locomotion cadence follows the runner's real speed ratio (actual / music pace), clamped here.")]
    public Vector2 locomotionSpeedRange = new(0.85f, 1.2f);
    public Vector2 fastLocomotionSpeedRange = new(0.9f, 1.3f);
    [Tooltip("Speed range used to fit one-shots (Jump, Land, AutoReturn) to their gameplay duration.")]
    public Vector2 actionSpeedRange = new(0.7f, 1.6f);

    [Header("Transitions")]
    public float crossfade = 0.15f;
    public float quickCrossfade = 0.08f;
    public float upperBodyFade = 0.25f;

    [Header("AutoReturn")]
    [Tooltip("Minimum displacement (0..1 of the maximum) for a system return to get its animation.")]
    [Range(0f, 1f)] public float autoReturnMinAmount = 0.35f;
    public float autoReturnCooldown = 1.5f;

    [Header("Flourish")]
    public bool flourishEnabled = true;
    [Range(0f, 1f)] public float flourishChance = 0.45f;
    public float flourishMinCooldown = 7f;
    public float flourishMaxCooldown = 16f;

    public RunnerAnimationEntry[] Get(RunnerAnimationRole role) => role switch
    {
        RunnerAnimationRole.Idle           => idle,
        RunnerAnimationRole.Locomotion     => locomotion,
        RunnerAnimationRole.FastLocomotion => fastLocomotion,
        RunnerAnimationRole.Jump           => jump,
        RunnerAnimationRole.Land           => land,
        RunnerAnimationRole.AutoReturn     => autoReturn,
        RunnerAnimationRole.Flourish       => flourish,
        RunnerAnimationRole.Fall           => fall,
        RunnerAnimationRole.Crouch         => crouch,
        _                                  => System.Array.Empty<RunnerAnimationEntry>(),
    };

    /// <summary>Usable variants of `role` (entries with a clip), or empty.</summary>
    public RunnerAnimationEntry[] Usable(RunnerAnimationRole role) =>
        System.Array.FindAll(Get(role) ?? System.Array.Empty<RunnerAnimationEntry>(), e => e != null && e.clip != null);
}
