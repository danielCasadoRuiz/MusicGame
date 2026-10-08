using UnityEngine;

/// <summary>
/// Turns an IFightInputSource into combat commands — this is the ONLY thing that ever touches
/// IFightInputSource, FightDirectionResolver, the FightInputBuffer, or FightComboRecognizer.
/// Nothing downstream (a future Move System, FightDebugHUD) needs any of those; it just listens for
/// FightNormalPunchEvent/FightNormalKickEvent/FightComboDetectedEvent on EventBus.
///
/// ONE ACTION PER PRESS, IMMEDIATELY — every press is buffered (abstract FightInputEvent with a
/// press number) and resolved by FightComboRecognizer the same frame into EITHER the combo it
/// completes (FightComboDetectedEvent, possibly flagged ReplacesPrevious when it extends an already
/// completed combo — PPP → PPPK) OR its own normal (FightNormalPunchEvent/Kick). Never both, never
/// delayed to see whether something longer is coming — see FightComboRecognizer's class doc.
/// Single-step directional commands ("Forward + A", "Down + B", the Signature) are just 1-step
/// combos resolved the same way.
///
/// DIRECTION-ONLY TAPS (FightButton.None — see that enum's own doc): every EDGE transition into
/// Forward/Back (horizontal) or Up/Down (vertical) is ALSO buffered/fed to the recognizer, exactly
/// like a button press, just tagged Button.None — this is what lets a combo like "Forward, Forward"
/// (a dash) be authored and recognized through the EXACT SAME FightInputBuffer/FightComboRecognizer
/// pipeline as every button combo, with no separate detection system. Held levels (Down for Crouch,
/// Back for Guard, etc.) are still read directly off CurrentHorizontal/CurrentVertical by whoever
/// needs them (FighterMovement/FighterGuard) — only the EDGE is buffered, so holding a direction
/// never repeatedly "re-completes" a tap-based combo every frame.
///
/// POWER CHORD (Down + Punch + Kick, held): while Down is held, a Punch/Kick press is deferred for
/// FightFlowConfig.simultaneousPressWindow (~0.1 s) to see whether the OTHER button joins it. If both
/// buttons end up held with Down, the presses are swallowed (no normal, no buffered combo step) and a
/// charge starts; holding all three for FightCombatBalanceConfig.powerActivationHoldTime publishes
/// FightPowerRequestedEvent exactly ONCE — nothing more until all of them are released. Releasing
/// early cancels silently. Every other press (no Down, or the other button never came) is processed
/// exactly as before, with the direction it had when it was pressed — so Down + Kick, the Signature
/// (Down + Forward + Punch) etc. still work, just up to that window later.
///
/// Only active while FightFlowState is Fighting (see OnFightFlowChanged) — input is captured and
/// interpreted here; actual movement/posture/guard consequences live in FighterMovement/FighterGuard.
///
/// ONE INSTANCE PER FIGHTER — Player and Opponent (AIFightInputSource-driven, see FighterAI's own
/// doc) each get their own, attached directly to their own FighterActor by FightSceneBootstrap (no
/// longer a single UI-Scene-resident singleton). Every event this class publishes carries Source
/// (=this) so FighterMoveController/FighterMovement can tell which fighter's press/combo it was —
/// see FightNormalPunchEvent's own doc.
/// </summary>
public class FighterInputController : MonoBehaviour
{
    private IFightInputSource _inputSource = new HumanFightInputSource();
    private IFightFacingProvider _facing = new DebugFightFacingProvider();

    private FightFlowConfig _config;
    private FightInputBuffer _buffer;
    private FightComboRecognizer _recognizer;

    private bool _active;

    public FightHorizontalDirection CurrentHorizontal { get; private set; }
    public FightVerticalDirection CurrentVertical { get; private set; }
    /// <summary>True while the HUMAN fighter plays in the first-person fight camera: input is then
    /// view-relative — W/S = Forward/Back (towards / away from the opponent the view looks at),
    /// A/D = sideways (CurrentSide), Space = jump, C/Ctrl = crouch. Third person keeps the classic
    /// mapping. Only the INTERPRETATION of keys changes — moves, ranges and rules are identical.</summary>
    public bool ViewRelative { get; private set; }
    /// <summary>First-person sideways intent: −1 left, 0 none, +1 right (along the fighter's SideXZ).</summary>
    public int CurrentSide { get; private set; }

    private FightHorizontalDirection _previousHorizontal;
    private FightVerticalDirection _previousVertical;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    // Power chord state (see class doc).
    private float _powerHoldTime = 1f;
    private bool _hasPendingPress;
    // Direction presses that happen while a Punch/Kick is held back by the power-chord window are held
    // too, so the ordered stream stays chronological (the press goes in first, then these).
    private readonly System.Collections.Generic.List<FightInputEvent> _pendingDirections = new();
    private FightButton _pendingButton;
    private FightHorizontalDirection _pendingHorizontal;
    private FightVerticalDirection _pendingVertical;
    private float _pendingTime;
    private bool _charging;
    private bool _chargeFired;
    private float _chargeStart;
    private int _sequence; // per-fighter press number (FightInputEvent.Sequence)

    /// <summary>Debug: the recognizer (combo strings) and the recent abstract input buffer.</summary>
    public FightComboRecognizer Recognizer => _recognizer;
    public System.Collections.Generic.IReadOnlyList<FightInputEvent> RecentInputs => _buffer?.Events;

    /// <summary>Debug: 0..1 progress of a Power hold in progress (0 when not charging).</summary>
    public float PowerChargeProgress => _charging ? Mathf.Clamp01((Time.time - _chargeStart) / Mathf.Max(0.01f, _powerHoldTime)) : 0f;
    public bool IsChargingPower => _charging;

    /// <summary>Swap the input source — the seam a future AIFightInputSource plugs into. Never
    /// called yet (no AI exists this phase).</summary>
    public void SetInputSource(IFightInputSource source) => _inputSource = source ?? new HumanFightInputSource();

    /// <summary>Swap the facing provider — the seam a real per-fighter facing system plugs into
    /// once fighters have actual arena positions. Never called yet.</summary>
    public void SetFacingProvider(IFightFacingProvider facing) => _facing = facing ?? new DebugFightFacingProvider();

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;
        _powerHoldTime = appConfig != null && appConfig.combatBalance != null ? appConfig.combatBalance.powerActivationHoldTime : 1f;

        if (_config == null || _config.comboSet == null)
            Debug.LogWarning("[FighterInputController] No FightComboSetSO (AppConfig.fightFlow.comboSet) configured — normals will still fire, but no combo will ever be detected.");
        BuildBufferAndRecognizer();
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            _active = e.Current == FightFlowState.Fighting;
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
    }

    private void Update()
    {
        if (!_active || FightController.IsPaused) return; // paused: no input is read or queued

        _inputSource.Tick();

        ViewRelative = _inputSource is HumanFightInputSource && FightCameraController.Instance != null &&
                       FightCameraController.Instance.ViewMode == CameraViewMode.FirstPerson;
        if (ViewRelative)
        {
            var forwardBack = FightDirectionResolver.ResolveVertical(_inputSource.Vertical); // W = up = forward
            CurrentHorizontal = forwardBack == FightVerticalDirection.Up ? FightHorizontalDirection.Forward
                              : forwardBack == FightVerticalDirection.Down ? FightHorizontalDirection.Back
                              : FightHorizontalDirection.Neutral;
            CurrentSide = _inputSource.Horizontal > 0.5f ? 1 : _inputSource.Horizontal < -0.5f ? -1 : 0;
            CurrentVertical = _inputSource.JumpHeld ? FightVerticalDirection.Up
                            : _inputSource.CrouchHeld ? FightVerticalDirection.Down
                            : FightVerticalDirection.Neutral;
        }
        else
        {
            CurrentHorizontal = FightDirectionResolver.ResolveHorizontal(_inputSource.Horizontal, _facing.FacingRight);
            CurrentVertical   = FightDirectionResolver.ResolveVertical(_inputSource.Vertical);
            CurrentSide = 0;
        }

        // A direction PRESS is the abstract action "that direction" (only the axis that changed), so
        // rolling Down → Down+Forward records Down then Forward. Movement keeps reading the held
        // directions itself (CurrentHorizontal/CurrentVertical) — recording never blocks it.
        if (CurrentHorizontal != _previousHorizontal && CurrentHorizontal != FightHorizontalDirection.Neutral)
            BufferDirectionTap(CurrentHorizontal, FightVerticalDirection.Neutral);
        if (CurrentVertical != _previousVertical && CurrentVertical != FightVerticalDirection.Neutral)
            BufferDirectionTap(FightHorizontalDirection.Neutral, CurrentVertical);
        _previousHorizontal = CurrentHorizontal;
        _previousVertical   = CurrentVertical;

        ProcessButtons(_inputSource.PunchPressed, _inputSource.KickPressed);
    }

    private void BuildBufferAndRecognizer()
    {
        float window = _config != null ? Mathf.Max(0.2f, _config.inputBufferWindowSeconds) : 1.5f;
        _buffer = new FightInputBuffer(window, _config != null ? _config.inputBufferMaxEntries : 24);
        _recognizer = new FightComboRecognizer(_config != null ? _config.comboSet : null, _buffer, this,
                                               _config != null ? _config.comboContinuationWindow : 0.5f);
    }

    /// <summary>Ends the current combo string (FighterMoveController: hit stun, knockdown, match end) —
    /// the next press starts a new one; nothing typed before can be reused.</summary>
    public void ResetComboString() => _recognizer?.Reset();

    // POWER CHORD resolution (see class doc), then plain press handling.
    private void ProcessButtons(bool punchPressed, bool kickPressed)
    {
        bool down = CurrentVertical == FightVerticalDirection.Down;
        bool bothHeld = _inputSource.PunchHeld && _inputSource.KickHeld;

        if (_charging)
        {
            if (!down || !bothHeld) { _charging = false; return; } // released: cancel (or end of a fired hold)
            if (!_chargeFired && Time.time - _chargeStart >= _powerHoldTime)
            {
                _chargeFired = true; // once per hold
                EventBus.Publish(new FightPowerRequestedEvent { Source = this });
            }
            return; // presses during a charge never become normals or combo steps
        }

        if (down && bothHeld && (punchPressed || kickPressed || _hasPendingPress))
        {
            _charging = true;
            _chargeFired = false;
            _chargeStart = _hasPendingPress ? _pendingTime : Time.time;
            _hasPendingPress = false; // swallowed — the Punch/Kick of a Power hold never enter the stream
            FlushPendingDirections();
            return;
        }

        // GRAB CHORD (no Down — Down + both is the Power chord above): Punch + Kick together, or the
        // second within grabChordWindow of the first. No added latency: the first press already
        // started its normal; FighterGrapple converts it (cancel in Startup) into the grab. The
        // second press never becomes a normal/combo step itself.
        if (!down && !_hasPendingPress)
        {
            float now = Time.time, window = GrabChordWindow;
            bool chord = (punchPressed && kickPressed) ||
                         (punchPressed && now - _lastKickTime <= window) ||
                         (kickPressed && now - _lastPunchTime <= window);
            if (chord)
            {
                _lastPunchTime = _lastKickTime = float.NegativeInfinity;
                EventBus.Publish(new FightGrabRequestedEvent { Source = this });
                return;
            }
            if (punchPressed) _lastPunchTime = now;
            if (kickPressed)  _lastKickTime = now;
        }

        if (_hasPendingPress)
        {
            bool expired = Time.time - _pendingTime >= WindowSeconds || !down;
            bool sameAgain = (_pendingButton == FightButton.Punch && punchPressed) || (_pendingButton == FightButton.Kick && kickPressed);
            bool otherCame = (_pendingButton == FightButton.Punch && kickPressed) || (_pendingButton == FightButton.Kick && punchPressed);
            if (expired || sameAgain || otherCame) FlushPending();
        }

        if (punchPressed) PressOrDefer(FightButton.Punch, down);
        if (kickPressed)  PressOrDefer(FightButton.Kick, down);
    }

    private float WindowSeconds => _config != null ? _config.simultaneousPressWindow : 0.1f;
    private float GrabChordWindow => _config != null ? _config.grabChordWindow : 0.08f;
    private float _lastPunchTime = float.NegativeInfinity, _lastKickTime = float.NegativeInfinity;

    private void PressOrDefer(FightButton button, bool down)
    {
        if (down && WindowSeconds > 0f && !_hasPendingPress)
        {
            _hasPendingPress = true;
            _pendingButton = button;
            _pendingHorizontal = CurrentHorizontal;
            _pendingVertical = CurrentVertical;
            _pendingTime = Time.time;
            return;
        }
        HandlePress(button, CurrentHorizontal, CurrentVertical, Time.time);
    }

    private void FlushPending()
    {
        _hasPendingPress = false;
        HandlePress(_pendingButton, _pendingHorizontal, _pendingVertical, _pendingTime);
        FlushPendingDirections();
    }

    private void FlushPendingDirections()
    {
        foreach (var d in _pendingDirections) HandlePress(FightButton.None, d.Horizontal, d.Vertical, d.Time);
        _pendingDirections.Clear();
    }

    private void HandlePress(FightButton button, FightHorizontalDirection horizontal, FightVerticalDirection vertical, float pressTime)
    {
        // Press time (not "now") keeps combo timing exact for a press deferred by the power-chord window.
        var input = new FightInputEvent(button, horizontal, vertical, pressTime, ++_sequence);
        _buffer.Add(input);
        var resolution = _recognizer.OnNewInput(input);

        // Exactly one action for this press — see class doc.
        if (resolution.Combo != null)
            EventBus.Publish(new FightComboDetectedEvent { Source = this, Combo = resolution.Combo, StringId = resolution.StringId, ReplacesPrevious = resolution.ReplacesPrevious });
        else if (button == FightButton.Punch)
            EventBus.Publish(new FightNormalPunchEvent { Source = this, StringId = resolution.StringId });
        else if (button == FightButton.Kick)
            EventBus.Publish(new FightNormalKickEvent { Source = this, StringId = resolution.StringId });
    }

    // Edge-only (see class doc) — a bare direction tap (no button) buffered/fed to the recognizer
    // exactly like a button press, just tagged FightButton.None. Never fires a normal event.
    private void BufferDirectionTap(FightHorizontalDirection horizontal, FightVerticalDirection vertical)
    {
        if (_hasPendingPress) { _pendingDirections.Add(new FightInputEvent(FightButton.None, horizontal, vertical, Time.time)); return; }
        HandlePress(FightButton.None, horizontal, vertical, Time.time);
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset — rebuilds a fresh
    /// buffer/recognizer (same construction as Awake) so no buffered press or pending combo from
    /// the previous round can bleed into the next one. In practice the buffer's own time window
    /// already prunes stale presses well before RoundEnd+RoundIntro+Countdown elapses, but this
    /// makes the reset explicit and deterministic rather than relying on that timing coincidence.</summary>
    public void ResetForRound()
    {
        BuildBufferAndRecognizer();
        _previousHorizontal = FightHorizontalDirection.Neutral;
        _previousVertical   = FightVerticalDirection.Neutral;
        _hasPendingPress = false;
        _pendingDirections.Clear();
        _charging = false;
    }
}
