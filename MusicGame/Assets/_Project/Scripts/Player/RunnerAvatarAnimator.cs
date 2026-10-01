using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Presentation-only animation driver for the Runner player's MakeHuman avatar. Translates the
/// EXISTING Runner state (PlayerController — the single movement authority) into semantic animation
/// roles; it never moves anything (root motion is always off) and never feeds back into gameplay.
///
/// ONE shared controller (RunnerHumanoid.controller, built by RunnerAnimationSetup):
///   Base layer      : "<Role>_<i>" states (i = variant slot), placeholder clips "RunnerHumanoid_<Role>_<i>",
///                     speed parameter "RoleSpeed".
///   UpperBody layer : avatar-masked "Empty" + "Flourish_<i>" (same Flourish placeholders), speed
///                     "UpperSpeed" — gesture flourishes from standing takes play here so the legs keep
///                     running; its weight is driven from code.
/// Each avatar gets its OWN AnimatorOverrideController filled from the active RunnerAnimationStyleSO
/// (variant slot i → the style's i-th usable clip for that role; empty roles fall back to the Default
/// style, then to Locomotion — never an unfilled placeholder, never a T-pose).
///
/// Mapping (priority top → bottom):
///   IsFalling                         → Fall (falls back to Jump)
///   PlayerController.Jumped           → Jump (fit to the jump's air time) → on ground: Land → locomotion
///   AutoReturnStarted (SYSTEM return) → AutoReturn one-shot (spin/turn), fit to the return duration —
///                                       only when grounded, displacement ≥ autoReturnMinAmount, off cooldown
///   not running                       → Idle
///   IsSurging                         → FastLocomotion
///   otherwise                         → Locomotion; occasionally a Flourish (chance + random cooldown)
/// Voluntary lane changes are NOT a signal at all: lateral input keeps the current locomotion.
/// Locomotion cadence = entry speed × clamp(actual / music pace, style range).
/// </summary>
public class RunnerAvatarAnimator : MonoBehaviour
{
    public const string PlaceholderPrefix = "RunnerHumanoid_";
    public const string BaseSpeedParameter = "RoleSpeed";
    public const string UpperSpeedParameter = "UpperSpeed";
    public const string UpperLayerName = "UpperBody";
    public const string UpperEmptyState = "Empty";
    private static readonly int BaseSpeedHash = Animator.StringToHash(BaseSpeedParameter);
    private static readonly int UpperSpeedHash = Animator.StringToHash(UpperSpeedParameter);

    private Animator _animator;
    private PlayerController _player;
    private RunnerAnimationStyleLibrarySO _library;
    private AnimatorOverrideController _override;
    private int _upperLayer = -1;

    // Per role: placeholder clip per slot, and the entry assigned to each usable slot.
    private readonly Dictionary<RunnerAnimationRole, List<AnimationClip>> _placeholders = new();
    private readonly Dictionary<RunnerAnimationRole, RunnerAnimationEntry[]> _assigned = new();
    private readonly Dictionary<RunnerAnimationRole, int> _lastVariant = new();
    private System.Random _rng;

    public RunnerAnimationStyleSO Style { get; private set; }
    public RunnerAnimationRole CurrentRole { get; private set; } = RunnerAnimationRole.Idle;
    public int CurrentVariant { get; private set; }
    public bool UpperBodyFlourishActive => _upperActive;
    public string DebugForcedStyle { get; private set; } = "";

    // One-shot / state bookkeeping.
    private RunnerAnimationRole? _oneShot;
    private float _oneShotEnd;
    private float _oneShotStart;
    private bool _debugAirborne;          // a debug-forced jump has no real airtime: end it by time
    private float _autoReturnReadyAt;
    private float _nextFlourishCheck;
    private bool _upperActive;
    private float _upperStart, _upperEnd;
    private bool _debugForceFast;

    // Speed ratio (actual distance rate / music pace), smoothed.
    private float _lastCanonical, _lastActual, _speedRatio = 1f;
    private bool _hasDistances;

    public void Bind(Animator animator, RuntimeAnimatorController controller, PlayerController player,
                     RunnerAnimationStyleLibrarySO library, MusicStyleId musicStyle)
    {
        _animator = animator;
        _player = player;
        _library = library;
        if (_animator == null) return;
        _animator.applyRootMotion = false; // the root (PlayerController) is the only mover
        if (!_animator.isHuman)
            Debug.LogWarning("[RunnerAvatarAnimator] Avatar Animator is not Humanoid — Runner clips won't retarget.");
        if (controller == null)
        {
            Debug.LogWarning("[RunnerAvatarAnimator] No Runner controller assigned — the avatar will not animate.");
            return;
        }

        _override = new AnimatorOverrideController(controller); // per-avatar, isolated runtime state
        CollectPlaceholders();
        _animator.runtimeAnimatorController = _override;
        _upperLayer = _animator.GetLayerIndex(UpperLayerName);

        if (_player != null)
        {
            _player.Jumped += OnJumped;
            _player.AutoReturnStarted += OnAutoReturn;
        }

        var style = library != null ? library.Resolve(musicStyle) : null;
        ApplyStyle(style);
        Debug.Log($"[RunnerAvatarAnimator] Music style {musicStyle} → Runner animation style '{(Style != null ? Style.name : "(none)")}'.");
    }

    private void OnDestroy()
    {
        if (_player == null) return;
        _player.Jumped -= OnJumped;
        _player.AutoReturnStarted -= OnAutoReturn;
    }

    // ── Style / overrides ──────────────────────────────────────────────────────

    private void CollectPlaceholders()
    {
        _placeholders.Clear();
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        _override.GetOverrides(pairs);
        foreach (var pair in pairs)
        {
            var clip = pair.Key;
            if (clip == null || !clip.name.StartsWith(PlaceholderPrefix)) continue;
            var parts = clip.name.Substring(PlaceholderPrefix.Length).Split('_');
            if (parts.Length != 2 || !System.Enum.TryParse(parts[0], out RunnerAnimationRole role) || !int.TryParse(parts[1], out int slot)) continue;
            if (!_placeholders.TryGetValue(role, out var list)) _placeholders[role] = list = new List<AnimationClip>();
            while (list.Count <= slot) list.Add(null);
            list[slot] = clip;
        }
    }

    /// <summary>(Re)fills this avatar's overrides from `style` (null → library default).</summary>
    public void ApplyStyle(RunnerAnimationStyleSO style)
    {
        if (_override == null) return;
        var fallback = _library != null ? _library.defaultStyle : null;
        Style = style != null ? style : fallback;
        _rng = new System.Random(StableHash(Style != null ? Style.name : "")); // deterministic per style

        var locomotion = Resolve(RunnerAnimationRole.Locomotion, fallback);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        _assigned.Clear();
        _lastVariant.Clear(); // variant indices belong to the previous style
        foreach (RunnerAnimationRole role in System.Enum.GetValues(typeof(RunnerAnimationRole)))
        {
            var entries = Resolve(role, fallback);
            if (entries.Length == 0 && role == RunnerAnimationRole.Fall) entries = Resolve(RunnerAnimationRole.Jump, fallback);
            if (entries.Length == 0) entries = locomotion;
            if (!_placeholders.TryGetValue(role, out var slots)) continue;
            if (entries.Length > slots.Count)
                Debug.LogWarning($"[RunnerAvatarAnimator] '{Style?.name}' has {entries.Length} {role} variants but the controller has {slots.Count} slots — extra variants ignored (re-run RunnerAnimationSetup).");
            int usable = Mathf.Min(entries.Length, slots.Count);
            var kept = new RunnerAnimationEntry[usable];
            System.Array.Copy(entries, kept, usable);
            _assigned[role] = kept;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null) continue;
                var clip = usable > 0 ? entries[i % usable].clip : null;
                overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(slots[i], clip));
            }
        }
        _override.ApplyOverrides(overrides);

        _oneShot = null;
        _upperActive = false;
        if (_upperLayer >= 0) { _animator.SetLayerWeight(_upperLayer, 0f); _animator.Play(UpperEmptyState, _upperLayer, 0f); }
        ScheduleFlourish(Time.time);
        PlayRole(DesiredLocomotionRole(), instant: true);
    }

    private RunnerAnimationEntry[] Resolve(RunnerAnimationRole role, RunnerAnimationStyleSO fallback)
    {
        var entries = Style != null ? Style.Usable(role) : System.Array.Empty<RunnerAnimationEntry>();
        if (entries.Length == 0 && fallback != null && fallback != Style) entries = fallback.Usable(role);
        return entries;
    }

    // ── Gameplay signals ───────────────────────────────────────────────────────

    private void OnJumped() => StartOneShot(RunnerAnimationRole.Jump, _player != null ? _player.ExpectedJumpAirTime : 0.8f, debug: false);

    private void OnAutoReturn(RunnerAutoReturnKind kind, float amount, float duration)
    {
        if (Style == null || amount < Style.autoReturnMinAmount || Time.time < _autoReturnReadyAt) return;
        if (_player != null && (!_player.IsGrounded || _player.IsFalling)) return;
        if (_oneShot == RunnerAnimationRole.Jump || _oneShot == RunnerAnimationRole.Fall) return;
        StartOneShot(RunnerAnimationRole.AutoReturn, duration, debug: false);
        _autoReturnReadyAt = Time.time + Style.autoReturnCooldown;
    }

    private void StartOneShot(RunnerAnimationRole role, float gameplayDuration, bool debug)
    {
        if (_override == null || Style == null) return;
        StopUpperFlourish(); // jumps/returns interrupt gesture flourishes
        var entry = PlayRole(role, instant: false, forceRestart: true, fitDuration: gameplayDuration);
        float length = entry != null && entry.clip != null ? entry.clip.length : gameplayDuration;
        float speed = _animator.GetFloat(BaseSpeedHash);
        _oneShot = role;
        _oneShotStart = Time.time;
        _oneShotEnd = Time.time + (speed > 0.01f ? length / speed : length);
        _debugAirborne = debug && role == RunnerAnimationRole.Jump;
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_animator == null || _override == null || Style == null) return;
        UpdateSpeedRatio();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UpdateDebugKeys();
#endif
        UpdateUpperFlourish();
        bool falling = _player != null && _player.IsFalling;
        bool grounded = _player == null || _player.IsGrounded;

        if (falling)
        {
            if (_oneShot != RunnerAnimationRole.Fall) { StopUpperFlourish(); PlayRole(RunnerAnimationRole.Fall, false); _oneShot = RunnerAnimationRole.Fall; }
            return;
        }
        if (_oneShot == RunnerAnimationRole.Fall) _oneShot = null; // respawned

        if (_oneShot == RunnerAnimationRole.Jump)
        {
            float elapsed = Time.time - _oneShotStart;
            bool landed = _debugAirborne ? Time.time >= _oneShotEnd : grounded && elapsed > 0.15f;
            if (landed)
            {
                if (Style.Usable(RunnerAnimationRole.Land).Length > 0 || (_library?.defaultStyle?.Usable(RunnerAnimationRole.Land).Length ?? 0) > 0)
                    StartOneShot(RunnerAnimationRole.Land, 0f, debug: false);
                else { _oneShot = null; PlayRole(DesiredLocomotionRole(), false); }
            }
            return;
        }

        if (_oneShot.HasValue)
        {
            if (Time.time < _oneShotEnd - Style.crossfade) return;
            _oneShot = null;
            ScheduleFlourish(Time.time);
        }

        var desired = DesiredLocomotionRole();
        if (desired != CurrentRole) PlayRole(desired, false);
        else ApplyLocomotionSpeed(desired);

        if (desired == RunnerAnimationRole.Locomotion && grounded && !_upperActive && Time.time >= _nextFlourishCheck)
            TryFlourish();
    }

    private RunnerAnimationRole DesiredLocomotionRole()
    {
        if (_player == null || !_player.IsRunning) return RunnerAnimationRole.Idle;
        return _player.IsSurging || _debugForceFast ? RunnerAnimationRole.FastLocomotion : RunnerAnimationRole.Locomotion;
    }

    private void UpdateSpeedRatio()
    {
        if (_player == null) return;
        float canonical = _player.CanonicalDistance, actual = _player.ActualDistance;
        if (_hasDistances && Time.deltaTime > 0f)
        {
            float canonicalRate = (canonical - _lastCanonical) / Time.deltaTime;
            float actualRate = (actual - _lastActual) / Time.deltaTime;
            float ratio = canonicalRate > 0.5f ? actualRate / canonicalRate : 1f;
            _speedRatio = Mathf.Lerp(_speedRatio, ratio, 1f - Mathf.Exp(-Time.deltaTime / 0.25f));
        }
        _lastCanonical = canonical; _lastActual = actual; _hasDistances = true;
    }

    private void ApplyLocomotionSpeed(RunnerAnimationRole role)
    {
        if (role != RunnerAnimationRole.Locomotion && role != RunnerAnimationRole.FastLocomotion) return;
        var entry = AssignedEntry(role, CurrentVariant);
        var range = role == RunnerAnimationRole.FastLocomotion ? Style.fastLocomotionSpeedRange : Style.locomotionSpeedRange;
        _animator.SetFloat(BaseSpeedHash, (entry?.playbackSpeed ?? 1f) * Mathf.Clamp(_speedRatio, range.x, range.y));
    }

    // ── Playing ────────────────────────────────────────────────────────────────

    private RunnerAnimationEntry PlayRole(RunnerAnimationRole role, bool instant, bool forceRestart = false, float fitDuration = -1f)
    {
        if (!_assigned.TryGetValue(role, out var entries) || entries.Length == 0) return null;
        // Locomotion keeps its variant (never restarted needlessly); one-shots pick a new one.
        bool keepVariant = (role == RunnerAnimationRole.Locomotion || role == RunnerAnimationRole.FastLocomotion || role == RunnerAnimationRole.Idle)
                           && _lastVariant.TryGetValue(role, out int kept) && kept < entries.Length;
        int variant = keepVariant ? _lastVariant[role] : PickVariant(role, entries.Length);
        var entry = entries[variant];

        float speed = entry.playbackSpeed;
        if (fitDuration > 0f && entry.clip != null)
            speed = Mathf.Clamp(entry.clip.length / fitDuration, Style.actionSpeedRange.x, Style.actionSpeedRange.y) * entry.playbackSpeed;
        _animator.SetFloat(BaseSpeedHash, Mathf.Max(0.05f, speed));

        string state = $"{role}_{variant}";
        bool same = CurrentRole == role && CurrentVariant == variant;
        CurrentRole = role;
        CurrentVariant = variant;
        if (instant) _animator.Play(state, 0, 0f);
        else if (!same || forceRestart)
        {
            float fade = role == RunnerAnimationRole.Jump || role == RunnerAnimationRole.Land ? Style.quickCrossfade : Style.crossfade;
            _animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        }
        if (role == RunnerAnimationRole.Locomotion || role == RunnerAnimationRole.FastLocomotion) ApplyLocomotionSpeed(role);
        return entry;
    }

    // Deterministic per style; never the same variant twice in a row when there is a choice.
    private int PickVariant(RunnerAnimationRole role, int count)
    {
        int last = _lastVariant.TryGetValue(role, out var l) ? l : -1;
        int pick = count <= 1 ? 0 : (last < 0 ? _rng.Next(count) : (last + 1 + _rng.Next(count - 1)) % count);
        _lastVariant[role] = pick;
        return pick;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return hash & 0x7fffffff;
        }
    }

    private RunnerAnimationEntry AssignedEntry(RunnerAnimationRole role, int variant) =>
        _assigned.TryGetValue(role, out var e) && variant < e.Length ? e[variant] : null;

    // ── Flourish ───────────────────────────────────────────────────────────────

    private void ScheduleFlourish(float from)
    {
        if (Style == null) return;
        float min = Mathf.Max(0.5f, Style.flourishMinCooldown), max = Mathf.Max(min, Style.flourishMaxCooldown);
        _nextFlourishCheck = from + min + (float)_rng.NextDouble() * (max - min);
    }

    private void TryFlourish(bool force = false)
    {
        if (!force && (!Style.flourishEnabled || _rng.NextDouble() > Style.flourishChance)) { ScheduleFlourish(Time.time); return; }
        if (!_assigned.TryGetValue(RunnerAnimationRole.Flourish, out var entries) || entries.Length == 0) return;
        if (Style.Usable(RunnerAnimationRole.Flourish).Length == 0 && (_library?.defaultStyle?.Usable(RunnerAnimationRole.Flourish).Length ?? 0) == 0) return;

        int variant = PickVariant(RunnerAnimationRole.Flourish, entries.Length);
        var entry = entries[variant];
        float speed = Mathf.Max(0.05f, entry.playbackSpeed);
        float duration = entry.clip.length / speed;
        if (entry.upperBodyOnly && _upperLayer >= 0)
        {
            _animator.SetFloat(UpperSpeedHash, speed);
            _animator.CrossFadeInFixedTime($"Flourish_{variant}", 0f, _upperLayer, 0f);
            _upperActive = true;
            _upperStart = Time.time;
            _upperEnd = Time.time + duration;
        }
        else
        {
            _lastVariant[RunnerAnimationRole.Flourish] = variant;
            _animator.SetFloat(BaseSpeedHash, speed);
            _animator.CrossFadeInFixedTime($"Flourish_{variant}", Style.crossfade, 0, 0f);
            CurrentRole = RunnerAnimationRole.Flourish;
            CurrentVariant = variant;
            _oneShot = RunnerAnimationRole.Flourish;
            _oneShotStart = Time.time;
            _oneShotEnd = Time.time + duration;
        }
        ScheduleFlourish(Time.time + duration);
    }

    private void UpdateUpperFlourish()
    {
        if (_upperLayer < 0) return;
        if (!_upperActive) { _animator.SetLayerWeight(_upperLayer, Mathf.MoveTowards(_animator.GetLayerWeight(_upperLayer), 0f, Time.deltaTime / Mathf.Max(0.01f, Style.upperBodyFade))); return; }
        float fade = Mathf.Max(0.01f, Style.upperBodyFade);
        float w = Mathf.Min(Mathf.Clamp01((Time.time - _upperStart) / fade), Mathf.Clamp01((_upperEnd - Time.time) / fade));
        _animator.SetLayerWeight(_upperLayer, w);
        if (Time.time >= _upperEnd) StopUpperFlourish();
    }

    private void StopUpperFlourish()
    {
        if (!_upperActive) return;
        _upperActive = false; // weight fades out in UpdateUpperFlourish
        if (_upperLayer >= 0) _animator.CrossFadeInFixedTime(UpperEmptyState, Style != null ? Style.upperBodyFade : 0.2f, _upperLayer);
    }

    // ── Debug (never touches gameplay or GameSession) ─────────────────────────

    /// <summary>Forces a style (by index in the library's AllStyles: 0 Default, then mapped ones).</summary>
    public void DebugForceStyle(int index)
    {
        var all = _library != null ? _library.AllStyles() : null;
        if (all == null || index < 0 || index >= all.Count) return;
        ApplyStyle(all[index]);
        DebugForcedStyle = all[index].name;
    }

    public void DebugTrigger(RunnerAnimationRole role)
    {
        switch (role)
        {
            case RunnerAnimationRole.Locomotion:     _debugForceFast = false; _oneShot = null; StopUpperFlourish(); PlayRole(DesiredLocomotionRole(), false); break;
            case RunnerAnimationRole.FastLocomotion: _debugForceFast = !_debugForceFast; break;
            case RunnerAnimationRole.Jump:           StartOneShot(RunnerAnimationRole.Jump, _player != null ? _player.ExpectedJumpAirTime : 0.8f, debug: true); break;
            case RunnerAnimationRole.AutoReturn:     StartOneShot(RunnerAnimationRole.AutoReturn, 0.85f, debug: true); break;
            case RunnerAnimationRole.Flourish:       if (!_oneShot.HasValue) TryFlourish(force: true); break;
        }
    }

    public string DebugSummary =>
        Style == null ? "Runner anim: (no style)" :
        $"Runner anim: {Style.name}{(string.IsNullOrEmpty(DebugForcedStyle) ? "" : " [forced]")}  role {CurrentRole}#{CurrentVariant}" +
        $"{(_oneShot.HasValue ? $" (one-shot {_oneShot})" : "")}{(_upperActive ? " +upper flourish" : "")}  speed {(_animator != null ? _animator.GetFloat(BaseSpeedHash) : 0f):0.00}" +
        $"  ratio {_speedRatio:0.00}{(_debugForceFast ? "  [fast forced]" : "")}\n" +
        "  F5-F8 style (Default/Salsa/Electronic/Classical) · 1 Loco · 2 Fast toggle · 3 Jump · 4 AutoReturn · 5 Flourish";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void UpdateDebugKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f5Key.wasPressedThisFrame) DebugForceStyle(0);
        if (kb.f6Key.wasPressedThisFrame) DebugForceStyle(1);
        if (kb.f7Key.wasPressedThisFrame) DebugForceStyle(2);
        if (kb.f8Key.wasPressedThisFrame) DebugForceStyle(3);
        if (kb.digit1Key.wasPressedThisFrame) DebugTrigger(RunnerAnimationRole.Locomotion);
        if (kb.digit2Key.wasPressedThisFrame) DebugTrigger(RunnerAnimationRole.FastLocomotion);
        if (kb.digit3Key.wasPressedThisFrame) DebugTrigger(RunnerAnimationRole.Jump);
        if (kb.digit4Key.wasPressedThisFrame) DebugTrigger(RunnerAnimationRole.AutoReturn);
        if (kb.digit5Key.wasPressedThisFrame) DebugTrigger(RunnerAnimationRole.Flourish);
    }
#endif
}
