using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Versus INTRO for FightFlowState.VersusIntro — also the fight's LOADING screen:
///
///   0. While the stage is not ready (FightSceneBootstrap.StageReady: both fighter avatars fully built + the
///      arena loaded) the card is OPAQUE (nothing being assembled is ever seen), a small "Loading…" line
///      shows, and the rival's Intro line already appears next to its portrait (bubble with a screen
///      anchor). Skip during loading only completes the line — the fight never starts on a half-built stage.
///      When ready, the veil clears to the light one over versusRevealSeconds and the sequence continues;
///      a line still being read moves to the bubble over the rival's head (step 3).
///   1. Versus card ("PLAYER — VS — RIVAL", portraits) over a light dim (FightFlowConfig.versusBackdropAlpha),
///      held then faded (versusCardHoldSeconds / versusCardFadeSeconds).
///   2. As soon as the rival is on stage (FightSceneBootstrap.OpponentVisualReady — a real readiness
///      signal; presentationReadyTimeoutSeconds is only a safety cap), the fight camera makes its pass
///      to the combat framing (FightCameraController.BeginIntroTravelling).
///   3. Once the card has faded, the rival's Intro line appears in a speech bubble over its head
///      (ComposerDialoguePresentation), typed, then held dialogueReadHoldSeconds.
///   4. When BOTH the camera pass and the reading are done → RoundIntro, exactly once (Advance).
///      The pass is never stretched to the line: a short pass waits on the final framing, a short
///      line waits for the pass to land. No line (disabled/missing/error/timeout) = pass only.
///   5. The bubble stays (frozen, above the countdown curtain) through ROUND n / 3-2-1 / FIGHT! and
///      fades when Fighting really starts.
///
/// SKIP (button or ESC, only during this state): camera snapped to the same final framing, line
/// completed (or dropped if still loading — it can never pop up later), hold skipped, ONE Advance.
/// Everything is one Update-driven timeline with one cancellation (StopIntro), so nothing stale can
/// move the camera, show a bubble or request a transition after leaving Fight / Fight Again.
///
/// Never touches the music (FightMusicController keeps playing). Structure and look: VersusScreen.prefab
/// (UIPrefabLibrarySO.versusScreen), instantiated once in Awake — its backdrop is turned into a plain,
/// configurable light veil here. A missing prefab is logged by UIPrefabLibrarySO.Spawn — no fallback.
/// </summary>
public class VersusScreenController : MonoBehaviour
{
    private enum Step { Inactive, WaitingForStage, Presenting, Countdown }

    private VersusScreenView _view;
    private RectTransform    _root;
    private CanvasGroup      _cardGroup;
    private Image            _backdrop;
    private Image            _playerPortrait, _opponentPortrait;
    private TextMeshProUGUI  _playerNameText, _opponentNameText, _vsText, _loadingText;

    private FightFlowConfig   _config;
    private FightCameraConfig _cameraConfig;
    private ComposerDialoguePresentation _dialogue;
    private FightPresentationSkip _skip;

    private Step  _step;
    private float _stageWait, _cardElapsed, _revealElapsed;
    private bool  _cardDone, _advanceRequested, _skipAfterLoading;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<GameFlowStateChangedEvent>  _onGameFlowChanged;

    /// <summary>Exposed for the player probe/tests.</summary>
    public ComposerDialoguePresentation Dialogue => _dialogue;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;
        _cameraConfig = appConfig != null ? appConfig.fightCamera : null;

        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.versusScreen : null, UIFactory.RootCanvas(), "VersusScreen");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        WireUI(_view);
        if (_cardGroup == null)
        {
            Debug.LogError("[VersusScreenController] VersusScreen.prefab: cardGroup (CanvasGroup) not assigned.");
            enabled = false;
            return;
        }
        MakeBackdropLight();
        _cardGroup.blocksRaycasts = false; // the arena presentation never eats clicks; Skip is its own button

        // Separate component prefabs (speech bubble, skip button), spawned after the card as before.
        _dialogue = new ComposerDialoguePresentation(ComposerSpeechBubble.Spawn(UIFactory.RootCanvas(), _config), _config);
        _skip = FightPresentationSkip.Spawn("VersusSkipButton");
        _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_view != null) Destroy(_view.gameObject);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            switch (e.Current)
            {
                case FightFlowState.VersusIntro:  BeginIntro(); break;
                case FightFlowState.RoundIntro:
                case FightFlowState.Countdown:    if (_dialogue?.Bubble != null) _dialogue.Bubble.AvoidCenterBand = true; break;
                case FightFlowState.Fighting:     FadeBubble(); break;
                default:                          StopIntro(); break; // OpponentSelection, RoundEnd, results…
            }
        };
        // Leaving Fight (menu, Replay Song, scene change): drop everything and give the dialogue tables
        // back (memory only — the Addressables download cache stays on disk).
        _onGameFlowChanged = e =>
        {
            if (e.Previous != GameFlowState.Fight) return;
            StopIntro();
            ComposerDialogueService.ReleaseAll();
            FightPreloader.Release(); // a preload never outlives the fight it was for
        };
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameFlowChanged);
        StopIntro(); // a disabled screen never advances the flow from a stale sequence
    }

    // ── Prefab wiring — see VersusScreenView's own doc ───────────────────────────

    private void WireUI(VersusScreenView view)
    {
        _root             = view.root.GetComponent<RectTransform>();
        _cardGroup        = view.cardGroup;
        _backdrop         = view.backdrop;
        _playerPortrait   = view.playerPortrait;
        _playerNameText   = view.playerNameText;
        _vsText           = view.vsText;
        _opponentPortrait = view.opponentPortrait;
        _opponentNameText = view.opponentNameText;
        _loadingText      = view.loadingText;
    }

    /// <summary>Makes the prefab's Dim a plain veil whose alpha is FightFlowConfig.versusBackdropAlpha (a
    /// theme receiver, if one was added to it by hand, would re-tint it opaque — removed).</summary>
    private void MakeBackdropLight()
    {
        if (_backdrop == null) return;
        var receiver = _backdrop.GetComponent<ThemeColorReceiver>();
        if (receiver != null) Destroy(receiver);
        _backdrop.raycastTarget = false;
        _backdrop.color = new Color(0f, 0f, 0f, _config != null ? _config.versusBackdropAlpha : 0.15f);
    }

    private void SetBackdropAlpha(float alpha)
    {
        if (_backdrop == null) return;
        var c = _backdrop.color; c.a = alpha; _backdrop.color = c;
    }

    /// <summary>Screen point just above the rival's portrait on the card (the bubble's anchor while loading).</summary>
    private Vector2? OpponentPortraitTop()
    {
        if (_opponentPortrait == null || !_root.gameObject.activeInHierarchy) return null;
        var corners = new Vector3[4];
        _opponentPortrait.rectTransform.GetWorldCorners(corners); // overlay canvas: world == screen pixels
        return new Vector2((corners[1].x + corners[2].x) * 0.5f, corners[1].y);
    }

    // ── Sequence ─────────────────────────────────────────────────────────────────

    private void BeginIntro()
    {
        StopIntro();
        PopulateInfo();
        MakeBackdropLight();
        SetBackdropAlpha(1f);      // LOADING: opaque until the stage is ready (step 0)
        if (_loadingText != null) _loadingText.gameObject.SetActive(true);
        _cardGroup.alpha = 1f;
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        _step = Step.WaitingForStage;
        _stageWait = _cardElapsed = _revealElapsed = 0f;
        _cardDone = _advanceRequested = _skipAfterLoading = false;

        // The definitive rival, captured NOW (Fight Again re-enters here with the same one). The line
        // starts loading at once but only shows once the Versus card is gone.
        var bootstrap = FightSceneBootstrap.Instance;
        float above = _config != null ? _config.bubbleAnchorAboveHead : 0.28f;
        _dialogue.Begin(GameSession.Instance?.SelectedOpponent, ComposerDialogueCategory.Intro,
                        () => ComposerAnchor.Resolve(bootstrap != null ? bootstrap.OpponentActor : null, above),
                        displayAllowed: true,
                        version: GameSession.Instance?.SelectedOpponentLevelConfig != null ? GameSession.Instance.SelectedOpponentLevelConfig.level : 0);
        // On the card the line points at the rival's portrait; it glides to the head when the card goes (EndCard).
        if (_dialogue.Bubble != null) _dialogue.Bubble.ScreenAnchorProvider = OpponentPortraitTop;
        _skip?.Show(SkipIntro);
    }

    private void Update()
    {
        if (_step != Step.WaitingForStage && _step != Step.Presenting) return;
        float dt = Time.unscaledDeltaTime;
        var bootstrap = FightSceneBootstrap.Instance;
        var cam = bootstrap != null ? bootstrap.CameraController : null;

        if (_step == Step.WaitingForStage)
        {
            _stageWait += dt;
            _dialogue.Tick(dt); // the line is read on the opaque card while everything loads
            if (_loadingText != null) _loadingText.alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_stageWait * 2.2f));
            float cap = _config != null ? _config.versusLoadingTimeoutSeconds : 30f;
            bool ready = bootstrap != null && bootstrap.StageReady; // both avatars built + arena loaded
            if (!ready && _stageWait < cap) return;
            if (!ready) Debug.LogWarning($"[VersusScreenController] Stage not ready after {cap:0.#}s — starting the presentation anyway.");
            if (_loadingText != null) _loadingText.gameObject.SetActive(false);
            FightPreloader.Release(); // the fight now holds its own handles
            if (_skipAfterLoading) { SkipIntro(); return; }
            cam?.BeginIntroTravelling(_cameraConfig != null ? _cameraConfig.introTravellingSeconds : 3f);
            _step = Step.Presenting;
            return; // the card timeline starts next frame, from a fully opaque card
        }

        // Reveal: the opaque loading veil clears to the light one over the now-ready arena.
        float reveal = _config != null ? _config.versusRevealSeconds : 0.5f;
        float light = _config != null ? _config.versusBackdropAlpha : 0.15f;
        _revealElapsed += dt;
        SetBackdropAlpha(reveal > 0f ? Mathf.Lerp(1f, light, Mathf.Clamp01(_revealElapsed / reveal)) : light);

        UpdateCard(dt);
        _dialogue.Tick(dt);

        bool cameraDone = cam == null || !cam.IsIntroTravelling;
        if (_cardDone && cameraDone && _dialogue.IsReadingComplete) Advance();
    }

    private void UpdateCard(float dt)
    {
        if (_cardDone) return;
        _cardElapsed += dt;
        float hold = _config != null ? _config.versusCardHoldSeconds : 1.2f;
        float fade = _config != null ? _config.versusCardFadeSeconds : 0.4f;
        float t = _cardElapsed - hold;
        _cardGroup.alpha = t <= 0f ? 1f : (fade > 0f ? Mathf.Clamp01(1f - t / fade) : 0f);
        if (_cardGroup.alpha <= 0f) EndCard();
    }

    private void EndCard()
    {
        _cardDone = true;
        _root.gameObject.SetActive(false);
        if (_dialogue.Bubble != null) _dialogue.Bubble.ScreenAnchorProvider = null; // glides to the rival's head
        _dialogue.AllowDisplay();
    }

    /// <summary>Skip/ESC: the same end state as the natural route, reached at once.</summary>
    private void SkipIntro()
    {
        if (_step != Step.WaitingForStage && _step != Step.Presenting) return;
        var bootstrap = FightSceneBootstrap.Instance;
        if (_step == Step.WaitingForStage && (bootstrap == null || !bootstrap.StageReady))
        {
            // Loading: the line is completed now, the rest of the skip happens the moment the stage is ready.
            _dialogue.SkipToEnd();
            _skipAfterLoading = true;
            return;
        }
        var cam = bootstrap != null ? bootstrap.CameraController : null;
        if (cam != null)
        {
            if (cam.IsIntroTravelling) cam.CompleteIntroTravelling();
            else if (_step == Step.WaitingForStage) cam.BeginIntroTravelling(0f); // pass never started: snap
        }
        if (!_cardDone) EndCard();
        SetBackdropAlpha(_config != null ? _config.versusBackdropAlpha : 0.15f);
        if (_loadingText != null) _loadingText.gameObject.SetActive(false);
        _dialogue.SkipToEnd();
        Advance();
    }

    /// <summary>The single exit of the Versus: at most one RoundIntro request per intro, only while
    /// the flow is still on VersusIntro.</summary>
    private void Advance()
    {
        if (_advanceRequested) return;
        _advanceRequested = true;
        _step = Step.Countdown;
        _skip?.Hide();               // the countdown itself is not skippable
        _dialogue.Freeze();         // line stays as it is (no late load, no timers) until Fighting
        if (_dialogue.Bubble != null) _dialogue.Bubble.AvoidCenterBand = true;
        var flow = FightFlowController.Instance;
        if (flow != null && flow.CurrentState == FightFlowState.VersusIntro) flow.RequestState(FightFlowState.RoundIntro);
    }

    private void FadeBubble()
    {
        _step = Step.Inactive;
        _skip?.Hide();
        _dialogue?.FadeOut(_config != null ? _config.dialogueBubbleFadeSeconds : 0.35f);
    }

    /// <summary>The one cancellation point: card, camera pass, line, skip — all of it.</summary>
    private void StopIntro()
    {
        _step = Step.Inactive;
        _skip?.Hide();
        _dialogue?.Cancel();
        if (_dialogue?.Bubble != null) _dialogue.Bubble.ScreenAnchorProvider = null;
        var cam = FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.CameraController : null;
        if (cam != null && cam.IsIntroTravelling) cam.CompleteIntroTravelling(); // never leave the camera mid-pass
        if (_root != null) _root.gameObject.SetActive(false);
    }

    private void PopulateInfo()
    {
        Loc.Set(_vsText, "Fight.Versus");
        Loc.Set(_playerNameText, "Fight.PlayerName");
        // No real player portrait yet — a clean neutral placeholder box, not a 3D-model dependency.
        _playerPortrait.sprite = null;
        _playerPortrait.color  = _view.placeholderPortraitColor;

        var opponent = GameSession.Instance?.SelectedOpponent;
        _opponentNameText.text = opponent != null ? opponent.displayName : Loc.Get("Fight.RivalUnknown");
        // The SAME level config OpponentSelectionController resolved at selection time, so the portrait
        // always matches what the roulette just showed.
        _opponentPortrait.sprite = GameSession.Instance?.SelectedOpponentLevelConfig?.portrait;
        _opponentPortrait.preserveAspect = true; // portraits are tall cut-outs, the slot is square
        _opponentPortrait.color  = _opponentPortrait.sprite != null ? Color.white : _view.placeholderPortraitColor;
    }

    /// <summary>Re-applies this screen's localized texts (language changed while it is up).</summary>
    public void RefreshTexts()
    {
        if (_vsText != null) Loc.Set(_vsText, "Fight.Versus");
        if (_playerNameText != null) Loc.Set(_playerNameText, "Fight.PlayerName");
        if (_opponentNameText != null && GameSession.Instance?.SelectedOpponent == null) Loc.Set(_opponentNameText, "Fight.RivalUnknown");
        _skip?.RefreshLabel();
    }
}
