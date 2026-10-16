using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Versus INTRO, played over the live arena (no opaque screen any more) for FightFlowState.VersusIntro:
///
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
/// Never touches the music (FightMusicController keeps playing). Prefers VersusScreen.prefab (UIRegistry);
/// procedural fallback otherwise — either way its dim is turned into a plain, configurable light veil.
/// </summary>
public class VersusScreenController : MonoBehaviour
{
    private static readonly Color PlaceholderPortraitColor = new(1f, 1f, 1f, 0.2f);

    private enum Step { Inactive, WaitingForStage, Presenting, Countdown }

    private RectTransform    _root;
    private CanvasGroup      _cardGroup;
    private Image            _backdrop;
    private Image            _playerPortrait, _opponentPortrait;
    private TextMeshProUGUI  _playerNameText, _opponentNameText, _vsText;

    private FightFlowConfig   _config;
    private FightCameraConfig _cameraConfig;
    private ComposerDialoguePresentation _dialogue;
    private FightPresentationSkip _skip;

    private Step  _step;
    private float _stageWait, _cardElapsed;
    private bool  _cardDone, _advanceRequested;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<GameFlowStateChangedEvent>  _onGameFlowChanged;

    /// <summary>Exposed for the player probe/tests.</summary>
    public ComposerDialoguePresentation Dialogue => _dialogue;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;
        _cameraConfig = appConfig != null ? appConfig.fightCamera : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.VersusScreen != null) WireUI(registry.VersusScreen);
        else Build();
        MakeBackdropLight();
        _cardGroup = _root.gameObject.GetComponent<CanvasGroup>();
        if (_cardGroup == null) _cardGroup = _root.gameObject.AddComponent<CanvasGroup>();
        _cardGroup.blocksRaycasts = false; // the arena presentation never eats clicks; Skip is its own button

        // Built at runtime (prefab or fallback alike), so regenerating VersusScreen.prefab never removes them.
        _dialogue = new ComposerDialoguePresentation(ComposerSpeechBubble.Create(UIFactory.RootCanvas(), _config), _config);
        _skip = FightPresentationSkip.Create("VersusSkipButton");
        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            switch (e.Current)
            {
                case FightFlowState.VersusIntro:  BeginIntro(); break;
                case FightFlowState.RoundIntro:
                case FightFlowState.Countdown:    if (_dialogue != null) _dialogue.Bubble.AvoidCenterBand = true; break;
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

    // ── Prefab path — see VersusScreenView's own doc ─────────────────────────────

    private void WireUI(VersusScreenView view)
    {
        _root             = view.root.GetComponent<RectTransform>();
        _playerPortrait   = view.playerPortrait;
        _playerNameText   = view.playerNameText;
        _vsText           = view.vsText;
        _opponentPortrait = view.opponentPortrait;
        _opponentNameText = view.opponentNameText;
        var dim = _root.Find("Dim");
        _backdrop = dim != null ? dim.GetComponent<Image>() : null;

        Loc.Set(view.vsText, "Fight.Versus");
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("VersusScreen", canvas);
        UIFactory.Stretch(_root);

        _backdrop = UIFactory.CreatePanel("Dim", _root, new Color(0f, 0f, 0f, 0f));
        UIFactory.Stretch(_backdrop.rectTransform);

        (_playerPortrait, _playerNameText)     = BuildSide("Player",   new Vector2(0.25f, 0.5f));
        (_opponentPortrait, _opponentNameText) = BuildSide("Opponent", new Vector2(0.75f, 0.5f));

        _vsText = Loc.Track(UIFactory.CreateText("VS", _root, Loc.Get("Fight.Versus"), 72, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Fight.Versus");
        UIFactory.SetBox(_vsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 150f));
        _vsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
    }

    // anchor.x picks left/right half of the screen; portrait sits above its name, both centered
    // on that anchor.
    private (Image portrait, TextMeshProUGUI nameText) BuildSide(string label, Vector2 anchor)
    {
        var portraitRt = UIFactory.CreateRect(label + "Portrait", _root);
        var portrait = portraitRt.gameObject.AddComponent<Image>();
        UIFactory.SetBox(portraitRt, anchor, anchor, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(280f, 280f));

        var nameText = UIFactory.CreateText(label + "Name", _root, "", 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(nameText.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(320f, 40f));
        nameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        return (portrait, nameText);
    }

    /// <summary>The baked prefab's Dim is an opaque, theme-tinted screen: make it a plain veil whose
    /// alpha is FightFlowConfig.versusBackdropAlpha (the theme receiver would re-tint it opaque).</summary>
    private void MakeBackdropLight()
    {
        if (_backdrop == null) return;
        var receiver = _backdrop.GetComponent<ThemeColorReceiver>();
        if (receiver != null) Destroy(receiver);
        _backdrop.raycastTarget = false;
        _backdrop.color = new Color(0f, 0f, 0f, _config != null ? _config.versusBackdropAlpha : 0.15f);
    }

    // ── Sequence ─────────────────────────────────────────────────────────────────

    private void BeginIntro()
    {
        StopIntro();
        PopulateInfo();
        MakeBackdropLight();
        _cardGroup.alpha = 1f;
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        _step = Step.WaitingForStage;
        _stageWait = _cardElapsed = 0f;
        _cardDone = _advanceRequested = false;

        // The definitive rival, captured NOW (Fight Again re-enters here with the same one). The line
        // starts loading at once but only shows once the Versus card is gone.
        var bootstrap = FightSceneBootstrap.Instance;
        float above = _config != null ? _config.bubbleAnchorAboveHead : 0.28f;
        _dialogue.Begin(GameSession.Instance?.SelectedOpponent, ComposerDialogueCategory.Intro,
                        () => ComposerAnchor.Resolve(bootstrap != null ? bootstrap.OpponentActor : null, above),
                        displayAllowed: false);
        _skip.Show(SkipIntro);
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
            float cap = _config != null ? _config.presentationReadyTimeoutSeconds : 4f;
            bool ready = bootstrap != null && bootstrap.OpponentVisualReady;
            if (!ready && _stageWait < cap) return;
            if (!ready) Debug.LogWarning($"[VersusScreenController] Rival not on stage after {cap:0.#}s — starting the presentation anyway.");
            cam?.BeginIntroTravelling(_cameraConfig != null ? _cameraConfig.introTravellingSeconds : 3f);
            _step = Step.Presenting;
        }

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
        _dialogue.AllowDisplay();
    }

    /// <summary>Skip/ESC: the same end state as the natural route, reached at once.</summary>
    private void SkipIntro()
    {
        if (_step != Step.WaitingForStage && _step != Step.Presenting) return;
        var cam = FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.CameraController : null;
        if (cam != null)
        {
            if (cam.IsIntroTravelling) cam.CompleteIntroTravelling();
            else if (_step == Step.WaitingForStage) cam.BeginIntroTravelling(0f); // pass never started: snap
        }
        if (!_cardDone) EndCard();
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
        _skip.Hide();               // the countdown itself is not skippable
        _dialogue.Freeze();         // line stays as it is (no late load, no timers) until Fighting
        _dialogue.Bubble.AvoidCenterBand = true;
        var flow = FightFlowController.Instance;
        if (flow != null && flow.CurrentState == FightFlowState.VersusIntro) flow.RequestState(FightFlowState.RoundIntro);
    }

    private void FadeBubble()
    {
        _step = Step.Inactive;
        _skip.Hide();
        _dialogue?.FadeOut(_config != null ? _config.dialogueBubbleFadeSeconds : 0.35f);
    }

    /// <summary>The one cancellation point: card, camera pass, line, skip — all of it.</summary>
    private void StopIntro()
    {
        _step = Step.Inactive;
        _skip?.Hide();
        _dialogue?.Cancel();
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
        _playerPortrait.color  = PlaceholderPortraitColor;

        var opponent = GameSession.Instance?.SelectedOpponent;
        _opponentNameText.text = opponent != null ? opponent.displayName : Loc.Get("Fight.RivalUnknown");
        // The SAME level config OpponentSelectionController resolved at selection time, so the portrait
        // always matches what the roulette just showed.
        _opponentPortrait.sprite = GameSession.Instance?.SelectedOpponentLevelConfig?.portrait;
        _opponentPortrait.preserveAspect = true; // portraits are tall cut-outs, the slot is square
        _opponentPortrait.color  = _opponentPortrait.sprite != null ? Color.white : PlaceholderPortraitColor;
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
