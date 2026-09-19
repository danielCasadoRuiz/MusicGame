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
/// Only active while FightFlowState is Fighting (see OnFightFlowChanged) — input is captured and
/// interpreted here, nothing about actual fighter movement/animation (that's a later phase, see
/// this class's own doc header in the design notes).
///
/// Lives in the always-loaded UI Scene (added by UIFlowController), like every other Fight-flow
/// controller — has no scene-local dependency (no real fighter GameObjects exist yet).
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
        _recognizer = new FightComboRecognizer(comboSet, _buffer);
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

        if (_inputSource.PunchPressed) HandlePress(FightButton.Punch);
        if (_inputSource.KickPressed)  HandlePress(FightButton.Kick);

        _recognizer.Tick();
    }

    private void HandlePress(FightButton button)
    {
        // Immediate — published before the buffer/recognizer even see this press.
        if (button == FightButton.Punch) EventBus.Publish(new FightNormalPunchEvent());
        else                             EventBus.Publish(new FightNormalKickEvent());

        _buffer.Add(new FightInputEvent(button, CurrentHorizontal, CurrentVertical, Time.time));
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
        _recognizer = new FightComboRecognizer(_config != null ? _config.comboSet : null, _buffer);
    }
}
