using UnityEngine;

/// <summary>
/// FightFlowState.MatchFarewell — the decided match's last words, in the arena, BEFORE the result
/// screen. FightMatchController has already decided the winner, published MatchEndedEvent and applied
/// progression; this class only presents and then calls FightMatchController.NotifyFarewellComplete()
/// — the single exit of the state (it opens MatchWon/MatchLost). It never decides or re-publishes anything.
///
///   player won  → the composer's Defeat line;  player lost → its Victory line.
///   arena held FightFlowConfig.farewellLeadInSeconds → bubble over the rival (typed) → reading hold
///   (dialogueReadHoldSeconds) → bubble fades (dialogueBubbleFadeSeconds) → result screen.
///   No line (disabled/missing/error/timeout) → straight to the result screen after the lead-in.
///
/// SKIP (button or ESC): bubble gone, result screen at once, exactly once. The skip click is consumed by
/// the Skip button itself and the result screen keeps its own buttons inert for one frame, so the same
/// press never activates Continue / Fight Again / Replay Song.
///
/// Combat is already over here (RoundActive false since RoundEnd: no attacks, no clock); the fighters and
/// the fight camera simply stay as they are — the combat framing already keeps the rival in shot, and a
/// KO'd rival's low head just moves the bubble lower (clamped on screen), it is never stood back up.
/// </summary>
public class MatchFarewellController : MonoBehaviour
{
    private FightFlowConfig _config;
    private ComposerDialoguePresentation _dialogue;
    private FightPresentationSkip _skip;

    private MatchEndedEvent? _pendingMatch;
    private bool  _active, _finishing;
    private float _leadIn;

    private System.Action<MatchEndedEvent> _onMatchEnded;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<GameFlowStateChangedEvent> _onGameFlowChanged;

    public ComposerDialoguePresentation Dialogue => _dialogue;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;
        _dialogue = new ComposerDialoguePresentation(ComposerSpeechBubble.Spawn(UIFactory.RootCanvas(), _config), _config);
        _skip = FightPresentationSkip.Spawn("FarewellSkipButton");
    }

    private void OnEnable()
    {
        _onMatchEnded = e => _pendingMatch = e;
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.MatchFarewell) Begin();
            else if (e.Previous == FightFlowState.MatchFarewell || e.Current == FightFlowState.OpponentSelection) Stop();
        };
        _onGameFlowChanged = e => { if (e.Previous == GameFlowState.Fight) Stop(); };
        EventBus.Subscribe(_onMatchEnded);
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onMatchEnded);
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameFlowChanged);
        Stop();
    }

    private void Begin()
    {
        Stop();
        _active = true;
        _finishing = false;
        _leadIn = _config != null ? Mathf.Max(0f, _config.farewellLeadInSeconds) : 0.5f;

        // Winner from the ONE published result — never recomputed here.
        bool playerWon = _pendingMatch.HasValue && _pendingMatch.Value.Winner == FighterSide.Player;
        var category = playerWon ? ComposerDialogueCategory.Defeat : ComposerDialogueCategory.Victory;
        var bootstrap = FightSceneBootstrap.Instance;
        float above = _config != null ? _config.bubbleAnchorAboveHead : 0.28f;
        _dialogue.Begin(GameSession.Instance?.SelectedOpponent, category,
                        () => ComposerAnchor.Resolve(bootstrap != null ? bootstrap.OpponentActor : null, above),
                        displayAllowed: _leadIn <= 0f,
                        version: GameSession.Instance?.SelectedOpponentLevelConfig != null ? GameSession.Instance.SelectedOpponentLevelConfig.level : 0);
        _skip?.Show(Skip);
    }

    private void Update()
    {
        if (!_active || _finishing) return;
        float dt = Time.unscaledDeltaTime;
        if (_leadIn > 0f)
        {
            _leadIn -= dt;
            if (_leadIn <= 0f) _dialogue.AllowDisplay();
        }
        _dialogue.Tick(dt);
        if (_leadIn <= 0f && _dialogue.IsReadingComplete) Finish(fade: true);
    }

    private void Skip()
    {
        if (!_active || _finishing) return;
        _dialogue.Cancel();
        Finish(fade: false);
    }

    private void Finish(bool fade)
    {
        if (_finishing) return;
        _finishing = true;
        _skip?.Hide();
        float seconds = fade && _dialogue.Bubble != null && _dialogue.Bubble.IsVisible && _config != null ? _config.dialogueBubbleFadeSeconds : 0f;
        _dialogue.FadeOut(seconds, Complete);
    }

    private void Complete()
    {
        if (!_active) return;
        _active = false;
        FightMatchController.Instance?.NotifyFarewellComplete(); // the single exit; ignored if no longer in MatchFarewell
    }

    private void Stop()
    {
        _active = false;
        _finishing = false;
        _skip?.Hide();
        _dialogue?.Cancel();
    }

    public void RefreshTexts() => _skip?.RefreshLabel();
}
