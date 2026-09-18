using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Intro screen — game logo/title placeholder, fades in, holds briefly, fades out, then advances to
/// GameFlowState.MainMenu. Tappable/clickable anywhere to skip straight to the fade-out (section 6
/// of the multi-scene refactor plan's "possibilitat de skip/input"). No art final: a plain title
/// string is the placeholder logo.
///
/// Pure SCREEN CONTROLLER now (navigation/interaction only) — it does NOT implement theming itself.
/// Each themed child gets a generic receiver instead (ThemeColorReceiver/ThemeTextReceiver — see
/// the Theme/UI hybrid model): Dim uses ThemeColorReceiver(Background), Title uses
/// ThemeTextReceiver(Primary/Display). This screen has no special visual behavior of its own, so it
/// doesn't need a PrefabThemeController either — generic receivers are the whole story here.
///
/// Prefers a real IntroScreen.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as GameplayHUD/PauseController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// GameFlowStateChangedEvent directly, same pattern as FightController, rather than a generic
/// GameFlowState-to-screen registry (see UIFlowController's own doc on why that registry doesn't
/// exist yet).
/// </summary>
public class IntroScreenController : MonoBehaviour
{
    private const float FadeInDuration  = 0.5f;
    private const float HoldDuration    = 1.5f;
    private const float FadeOutDuration = 0.5f;

    private RectTransform _root;
    private CanvasGroup   _canvasGroup;
    private TextMeshProUGUI _titleText;

    private Coroutine _sequence;
    private bool      _skipRequested;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.IntroScreen != null) WireUI(registry.IntroScreen);
        else Build();
    }

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.Intro) Show();
            else if (e.Previous == GameFlowState.Intro) Hide();
        };
        EventBus.Subscribe(_onFlowStateChanged);

        // This screen lives in the UI Scene, loaded asynchronously — GameFlowState can already have
        // advanced past Boot (even straight through Intro) by the time this OnEnable actually runs,
        // the same race RunnerSceneBootstrap already accounts for. Catch up once instead of only
        // reacting to the NEXT change, which might never come.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.Intro)
            Show();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
    }

    // ── Prefab path — see IntroScreenView's own doc ──────────────────────────────

    private void WireUI(IntroScreenView view)
    {
        _root        = view.root.GetComponent<RectTransform>();
        _canvasGroup = view.canvasGroup;
        _titleText   = view.titleText;
        view.skipButton.onClick.AddListener(RequestSkip);

        _titleText.text = Loc.Get("Intro.Title");
        _canvasGroup.alpha = 0f;
        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("IntroScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _canvasGroup = _root.gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        // Full-screen invisible button so a click/tap anywhere skips the wait.
        var skipButton = dim.gameObject.AddComponent<Button>();
        skipButton.transition = Selectable.Transition.None;
        skipButton.onClick.AddListener(RequestSkip);

        _titleText = UIFactory.CreateText("Title", _root, Loc.Get("Intro.Title"), 42, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1000f, 80f));
        _titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        _root.gameObject.SetActive(false);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _titleText.text = Loc.Get("Intro.Title");

        if (_sequence != null) StopCoroutine(_sequence);
        _skipRequested = false;
        _sequence = StartCoroutine(PlaySequence());
    }

    private void Hide()
    {
        if (_sequence != null) { StopCoroutine(_sequence); _sequence = null; }
        if (_root != null) _root.gameObject.SetActive(false);
        _canvasGroup.alpha = 0f;
    }

    private void RequestSkip() => _skipRequested = true;

    private IEnumerator PlaySequence()
    {
        yield return Fade(0f, 1f, FadeInDuration);

        float held = 0f;
        while (held < HoldDuration && !_skipRequested)
        {
            held += Time.deltaTime;
            yield return null;
        }

        yield return Fade(1f, 0f, FadeOutDuration);

        _sequence = null;
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(from, to, duration > 0f ? t / duration : 1f);
            yield return null;
        }
        _canvasGroup.alpha = to;
    }
}
