using UnityEngine;

/// <summary>
/// Turns an IFightInputSource into combat commands — this is the ONLY thing that ever touches
/// IFightInputSource, FightDirectionResolver, the FightInputBuffer, or FightComboRecognizer.
/// Nothing downstream (a future Move System, FightDebugHUD) needs any of those; it just listens for
/// FightNormalPunchEvent/FightNormalKickEvent/FightComboDetectedEvent on EventBus.
///
/// NORMALS ARE IMMEDIATE — pressing Punch publishes FightNormalPunchEvent the SAME frame,
/// unconditionally, before the input is even handed to the combo recognizer. The recognizer runs
/// in PARALLEL off the same buffered input, never blocking or delaying a normal — see
/// FightComboRecognizer's own doc for how it resolves "B-B" vs "B-B-A-B" without holding up either
/// button press's own immediate normal.
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
/// DIRECTIONAL COMMAND PRIORITY: a plain button press (Neutral direction) always fires its normal
/// immediately, as before. But if the CURRENT direction (at the exact instant the button is
/// pressed) matches a SINGLE-STEP combo exactly (e.g. "Forward + A", "Down + B", "Down + Forward +
/// A") that more specific command fires INSTEAD of the plain normal — never both, never a normal
/// that gets superseded moments later. This only applies to single-step (steps.Length == 1) combos,
/// which can be resolved synchronously at press-time with no waiting; multi-step SEQUENTIAL combos
/// (A-A-A-B and friends) are unaffected — their first press still fires the plain normal
/// immediately, exactly as before, since there is no way to know a sequence is coming until later
/// presses actually arrive.
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

    private FightHorizontalDirection _previousHorizontal;
    private FightVerticalDirection _previousVertical;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

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

        float window = _config != null ? Mathf.Max(0.2f, _config.inputBufferWindowSeconds) : 1.5f;
        _buffer = new FightInputBuffer(window);

        var comboSet = _config != null ? _config.comboSet : null;
        if (comboSet == null)
            Debug.LogWarning("[FighterInputController] No FightComboSetSO (AppConfig.fightFlow.comboSet) configured — normals will still fire, but no combo will ever be detected.");
        _recognizer = new FightComboRecognizer(comboSet, _buffer, this);
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
        if (!_active) return;

        _inputSource.Tick();

        CurrentHorizontal = FightDirectionResolver.ResolveHorizontal(_inputSource.Horizontal, _facing.FacingRight);
        CurrentVertical    = FightDirectionResolver.ResolveVertical(_inputSource.Vertical);

        if (CurrentHorizontal != _previousHorizontal && CurrentHorizontal != FightHorizontalDirection.Neutral)
            BufferDirectionTap();
        if (CurrentVertical != _previousVertical && CurrentVertical != FightVerticalDirection.Neutral)
            BufferDirectionTap();
        _previousHorizontal = CurrentHorizontal;
        _previousVertical   = CurrentVertical;

        if (_inputSource.PunchPressed) HandlePress(FightButton.Punch);
        if (_inputSource.KickPressed)  HandlePress(FightButton.Kick);

        // Every frame regardless — see FightComboRecognizer.Tick's own doc (fires a pending short
        // combo once its grace window expires unextended, independent of any new input this frame).
        _recognizer.Tick();
    }

    private void HandlePress(FightButton button)
    {
        // See class doc on DIRECTIONAL COMMAND PRIORITY — a more specific single-step command for
        // THIS EXACT direction suppresses the plain normal entirely; otherwise the normal fires
        // immediately, unchanged from before.
        if (FindDirectionalCommand(button, CurrentHorizontal, CurrentVertical) == null)
        {
            if (button == FightButton.Punch) EventBus.Publish(new FightNormalPunchEvent { Source = this });
            else                             EventBus.Publish(new FightNormalKickEvent { Source = this });
        }

        // Buffered/fed to the recognizer either way — a matched directional command fires through
        // the SAME immediate, non-prefix path the recognizer already uses (see its own doc), so
        // nothing else needs to change there.
        _buffer.Add(new FightInputEvent(button, CurrentHorizontal, CurrentVertical, Time.time));
        _recognizer.OnNewInput();
    }

    // Single-step (steps.Length == 1) combos ONLY — resolvable synchronously, at press-time, unlike
    // multi-step sequences which necessarily need to wait for later presses. Ties broken by
    // priority, same convention as the recognizer's own longest-match tiebreak.
    private FightComboDefinition FindDirectionalCommand(FightButton button, FightHorizontalDirection horizontal, FightVerticalDirection vertical)
    {
        var combos = _config != null && _config.comboSet != null ? _config.comboSet.combos : null;
        if (combos == null) return null;

        FightComboDefinition best = null;
        foreach (var combo in combos)
        {
            if (combo == null || combo.steps == null || combo.steps.Length != 1) continue;
            var step = combo.steps[0];
            if (step.button != button || step.horizontal != horizontal || step.vertical != vertical) continue;
            if (best == null || combo.priority > best.priority) best = combo;
        }
        return best;
    }

    // Edge-only (see class doc) — a bare direction tap (no button) buffered/fed to the recognizer
    // exactly like a button press, just tagged FightButton.None. Never fires a normal event.
    private void BufferDirectionTap()
    {
        _buffer.Add(new FightInputEvent(FightButton.None, CurrentHorizontal, CurrentVertical, Time.time));
        _recognizer.OnNewInput();
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset — rebuilds a fresh
    /// buffer/recognizer (same construction as Awake) so no buffered press or pending combo from
    /// the previous round can bleed into the next one. In practice the buffer's own time window
    /// already prunes stale presses well before RoundEnd+RoundIntro+Countdown elapses, but this
    /// makes the reset explicit and deterministic rather than relying on that timing coincidence.</summary>
    public void ResetForRound()
    {
        float window = _config != null ? Mathf.Max(0.2f, _config.inputBufferWindowSeconds) : 1.5f;
        _buffer = new FightInputBuffer(window);
        _recognizer = new FightComboRecognizer(_config != null ? _config.comboSet : null, _buffer, this);
        _previousHorizontal = FightHorizontalDirection.Neutral;
        _previousVertical   = FightVerticalDirection.Neutral;
    }
}
