using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen opaque overlay — shows itself the INSTANT GameFlowState.SongAnalysis is entered
/// (not only once PreAnalysisStartedEvent fires), since SongSelectionController now requests that
/// state immediately on Play, before a catalog song's Addressables clip has necessarily finished
/// loading (see SongAnalysisController) — the player must see "something is happening" the moment
/// they press Play, not after that load quietly finishes in the background.
///
/// Three phases, always in this order, progress fill NEVER jumping backward or snapping to a fixed
/// value mid-flow:
///   1. DETECTING (0% .. ~28%) — from the instant this screen shows until MusicStyleDetectedEvent
///      arrives. There's no real signal for "how close is style detection" (semantic tagging is one
///      opaque ML call — see AudioPreAnalyzer's own doc on why it now runs BEFORE the rest of
///      analysis), so this is a self-paced ease toward a soft ceiling that never quite reaches it —
///      always visibly still creeping forward instead of stalling flat if tagging takes a while.
///      Normal random Tip rotation plays underneath.
///   2. STYLE DETECTED (pinned at 30%, ~StyleFlashSeconds) — MusicStyleDetectedEvent fires (this is
///      also when ThemeManager, subscribed independently, starts swapping CurrentTheme) — a fixed
///      "Style detected: X!" caption replaces whatever tip was showing, bar pinned at exactly 30%
///      (never snapped further; there's real analysis work — or a cache-hit's own fake beat — still
///      to account for in the remaining 70%).
///   3. FINISHING (30% .. 100%) — for a cache MISS, PreAnalysisProgressEvent keeps arriving from
///      AudioPreAnalyzer's still-running per-frame analysis (needed for level generation): REAL
///      progress, remapped from this event's own 0..1 into 30%..100% of the bar, with normal Tip
///      rotation resumed. For a cache HIT, no such event ever comes (the cache-hit branch skips the
///      whole per-frame loop) — another self-paced ease from 30% toward ~97% instead, over roughly
///      AudioPreAnalyzer's own CacheHitFakeDelaySeconds (not hard-synced to it — whichever finishes
///      first, Hide() cuts the other short, which is fine).
///
/// Stays up even AFTER GameFlowState leaves SongAnalysis for Gameplay — hiding right then would
/// reveal Runner's Mode Scene still being loaded/built (SceneFlowController's load is async, and
/// GameplayManager/MusicWorldManager/CameraFollow all still need to generate the level and hard-
/// snap the camera into position), which is exactly the camera "jump" this was covering up before.
/// The real Hide() trigger for that path is GameStartedEvent — GameplayManager only publishes it
/// once the level is generated AND the player/camera are already placed (see its own
/// GenerateAndStart) — so this overlay bridges the ENTIRE gap from "Play pressed" through "Runner
/// is fully ready", with the "3, 2, 1, GO" Countdown taking over the instant it's gone. The
/// failure path (a bad catalog load bouncing back to Song Selection) still hides immediately on
/// leaving SongAnalysis, since there's no scene load to wait for there. Not SongProfileReadyEvent
/// either way — that fires the instant analysis itself is done, before SongAnalysisController's
/// theme-transition wait even starts, so hiding on it would cut Phase 2/3 short.
///
/// Pure SCREEN CONTROLLER — theming lives entirely on generic receivers attached to each themed
/// child (Dim: ThemeColorReceiver(Background), Title: ThemeTextReceiver(Primary/Display), Tip:
/// ThemeTextReceiver(Secondary/Body), Progress fill: ThemeColorReceiver(Accent)). No special visual
/// behavior of its own, so no PrefabThemeController either.
///
/// Prefers a real AnalyzingScreen.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as GameplayHUD/PauseController.
///
/// All shown text is looked up via Loc.Get (see its own doc) — nothing user-facing is hardcoded
/// here, so adding a language later is a translation-only change in the "UIText" table.
/// </summary>
public class AnalyzingScreenController : MonoBehaviour
{
    private const string TitleKey = "Analyzing.Title";

    private static readonly string[] TipKeys =
    {
        "Analyzing.Tip01", "Analyzing.Tip02", "Analyzing.Tip03", "Analyzing.Tip04",
        "Analyzing.Tip05", "Analyzing.Tip06", "Analyzing.Tip07", "Analyzing.Tip08",
        "Analyzing.Tip09", "Analyzing.Tip10", "Analyzing.Tip11", "Analyzing.Tip12",
    };

    [Tooltip("Tip line changes every random(minTipInterval, maxTipInterval) seconds while shown.")]
    [SerializeField] private float minTipInterval = 1f;
    [SerializeField] private float maxTipInterval = 2f;

    // ── Progress phase tuning ─────────────────────────────────────────────────
    private const float PhaseOneCeiling      = 0.28f; // Phase 1 eases toward this, never quite reaching it
    private const float PhaseOneEaseSpeed    = 1.2f;  // higher = faster approach to the ceiling
    private const float StyleDetectedFill    = 0.30f; // pinned value for the whole Phase 2 flash
    private const float StyleFlashSeconds    = 1.4f;
    private const float CacheHitFinishSeconds = 1.6f; // Phase 3 duration when there's no real progress signal
    private const float CacheHitFinishCeiling = 0.97f;

    private RectTransform _root;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _tipText;
    private Image _progressFill;

    private Coroutine _tipRoutine;
    private Coroutine _progressRoutine;
    private int       _lastTipIndex = -1;
    private bool      _isCacheHit;
    private bool      _realProgressActive; // Phase 3, cache-miss only — _onProgress is allowed to drive the fill

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;
    private System.Action<PreAnalysisStartedEvent>   _onStarted;
    private System.Action<PreAnalysisProgressEvent>  _onProgress;
    private System.Action<MusicStyleDetectedEvent>   _onStyleDetected;
    private System.Action<GameStartedEvent>          _onGameStarted;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.Analyzing != null) WireUI(registry.Analyzing);
        else Build();
    }

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.SongAnalysis) Show();
            // Only the FAILURE path (bounced back to Song Selection — see
            // SongSelectionController.OnPlayClicked) hides here. The success path (→ Gameplay)
            // deliberately does NOT hide on this transition — Runner's Mode Scene has only just
            // started loading at this point (SceneFlowController.LoadMode is async), and
            // GameplayManager/MusicWorldManager/CameraFollow all still need to build the level and
            // hard-snap the camera into position afterward. Hiding here would reveal that whole
            // setup process — including the camera's very first snap to the player, previously
            // visible as a jarring jump — instead of covering it. GameStartedEvent (below) is the
            // real "Runner is fully ready" signal: GameplayManager only publishes it once the
            // level is generated AND the player/camera are already placed correctly (see its own
            // GenerateAndStart).
            else if (e.Previous == GameFlowState.SongAnalysis && e.Current != GameFlowState.Gameplay) Hide();
        };
        // Only recorded for Phase 3 to branch on later (see ShowStyleDetected) — Phase 1 already
        // started the instant this screen showed, well before analysis itself necessarily has.
        _onStarted  = e => _isCacheHit = e.IsCacheHit;
        _onProgress = e =>
        {
            if (!_realProgressActive || _progressFill == null) return;
            _progressFill.fillAmount = Mathf.Lerp(StyleDetectedFill, 1f, Mathf.Clamp01(e.Progress));
        };
        _onStyleDetected = e => ShowStyleDetected(e.Style);
        _onGameStarted = _ => Hide();
        EventBus.Subscribe(_onFlowStateChanged);
        EventBus.Subscribe(_onStarted);
        EventBus.Subscribe(_onProgress);
        EventBus.Subscribe(_onStyleDetected);
        EventBus.Subscribe(_onGameStarted);

        // Same UI-Scene-loads-asynchronously race as the other Frontend screens.
        if (AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.SongAnalysis)
            Show();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        EventBus.Unsubscribe(_onStarted);
        EventBus.Unsubscribe(_onProgress);
        EventBus.Unsubscribe(_onStyleDetected);
        EventBus.Unsubscribe(_onGameStarted);
    }

    // ── Prefab path — see AnalyzingScreenView's own doc ──────────────────────────

    private void WireUI(AnalyzingScreenView view)
    {
        _root         = view.root.GetComponent<RectTransform>();
        _titleText    = view.titleText;
        _tipText      = view.tipText;
        _progressFill = view.progressFill;

        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("AnalyzingScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling(); // always drawn above whatever other UI exists so far

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 0.96f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        _titleText = UIFactory.CreateText("Title", _root, "", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 40f), new Vector2(900f, 40f));
        _titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        _tipText = UIFactory.CreateText("Tip", _root, "", 16, new Color(0.75f, 0.75f, 0.8f), TextAlignmentOptions.Center);
        UIFactory.SetBox(_tipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(900f, 30f));
        _tipText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);

        var barBg = UIFactory.CreateFillBar("Progress", _root, new Color(1f, 1f, 1f, 0.12f), new Color(0.18f, 0.75f, 0.95f), out _progressFill);
        UIFactory.SetBox(barBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(500f, 6f));
        _progressFill.fillAmount = 0f;
        _progressFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);

        _root.gameObject.SetActive(false);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    // Phase 1 start — see this class's own doc.
    private void Show()
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _titleText.text = Loc.Get(TitleKey);
        if (_progressFill != null) _progressFill.fillAmount = 0f;

        _isCacheHit = false;
        _realProgressActive = false;
        StopPhaseRoutines();
        _lastTipIndex = -1;
        _tipRoutine      = StartCoroutine(RotateTips());
        _progressRoutine = StartCoroutine(EaseFillToward(PhaseOneCeiling, PhaseOneEaseSpeed));
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
        _realProgressActive = false;
        StopPhaseRoutines();
    }

    private void StopPhaseRoutines()
    {
        if (_tipRoutine != null)      { StopCoroutine(_tipRoutine);      _tipRoutine      = null; }
        if (_progressRoutine != null) { StopCoroutine(_progressRoutine); _progressRoutine = null; }
    }

    // Phase 2 — see this class's own doc. Never snaps the bar past StyleDetectedFill; Phase 3
    // (started from AfterStyleFlash) is what carries it the rest of the way.
    private void ShowStyleDetected(MusicStyleId style)
    {
        _realProgressActive = false;
        StopPhaseRoutines();
        if (_progressFill != null) _progressFill.fillAmount = StyleDetectedFill;
        if (_tipText != null) _tipText.text = Loc.Get("Analyzing.StyleDetected", style.ToString().ToUpperInvariant());

        _progressRoutine = StartCoroutine(AfterStyleFlash());
    }

    // Phase 3 handoff — see this class's own doc.
    private IEnumerator AfterStyleFlash()
    {
        yield return new WaitForSeconds(StyleFlashSeconds);

        if (_isCacheHit)
        {
            _progressRoutine = StartCoroutine(EaseFillOverTime(StyleDetectedFill, CacheHitFinishCeiling, CacheHitFinishSeconds));
        }
        else
        {
            _realProgressActive = true; // _onProgress takes over the fill from here
            _lastTipIndex = -1;
            _tipRoutine = StartCoroutine(RotateTips());
        }
    }

    private IEnumerator RotateTips()
    {
        while (true)
        {
            _tipText.text = Loc.Get(NextTipKey());
            yield return new WaitForSeconds(Random.Range(minTipInterval, maxTipInterval));
        }
    }

    // Never repeats the immediately-previous tip (as long as there's more than one to pick from).
    private string NextTipKey()
    {
        int idx;
        do { idx = Random.Range(0, TipKeys.Length); } while (TipKeys.Length > 1 && idx == _lastTipIndex);
        _lastTipIndex = idx;
        return TipKeys[idx];
    }

    // Asymptotic ease — creeps toward `target` forever without fully reaching it, so it always
    // reads as "still working" regardless of how long the real, unsignaled work behind it actually
    // takes (semantic tagging is one opaque ML call — see this class's own doc). Runs until
    // whichever phase started it stops it (StopPhaseRoutines) — never completes on its own.
    private IEnumerator EaseFillToward(float target, float speed)
    {
        while (true)
        {
            if (_progressFill != null)
                _progressFill.fillAmount = Mathf.Lerp(_progressFill.fillAmount, target, Time.deltaTime * speed);
            yield return null;
        }
    }

    // Fixed-duration ease — used only where the window IS roughly known (Phase 3 of a cache hit,
    // paced against AudioPreAnalyzer.CacheHitFakeDelaySeconds even though the two aren't hard-synced).
    private IEnumerator EaseFillOverTime(float from, float to, float duration)
    {
        float t = 0f;
        if (_progressFill != null) _progressFill.fillAmount = from;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (_progressFill != null) _progressFill.fillAmount = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
    }
}
