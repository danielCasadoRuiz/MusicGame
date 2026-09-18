using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen opaque overlay shown while AudioPreAnalyzer runs its (multi-second, no-cache) FFT
/// pass — PreAnalysisStartedEvent / PreAnalysisProgressEvent / SongProfileReadyEvent (all already
/// published by AudioPreAnalyzer itself) are the only hooks this reads; nothing here duplicates
/// analysis logic. A cache hit resolves in well under a frame, so instead of skipping this screen
/// entirely it still shows for a short, deliberately fake beat (PreAnalysisStartedEvent.IsCacheHit —
/// see AudioPreAnalyzer's own CacheHitFakeDelaySeconds): a fixed, in-order caption sequence
/// ("Song cached" / "Finalizing" / "Changing theme...") plus a self-animated progress fill, instead
/// of the real random tip pool + externally-driven progress used for an actual analysis.
///
/// Pure SCREEN CONTROLLER now — theming lives entirely on generic receivers attached to each
/// themed child (Dim: ThemeColorReceiver(Background), Title: ThemeTextReceiver(Primary/Display),
/// Tip: ThemeTextReceiver(Secondary/Body), Progress fill: ThemeColorReceiver(Accent)). No special
/// visual behavior of its own, so no PrefabThemeController either.
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

    // Fixed, in-order sequence for the cache-hit fake beat — never randomized like TipKeys, since
    // it's meant to read as real progress ("found it, wrapping up, applying the look") rather than
    // trivia. Keep roughly in sync with AudioPreAnalyzer.CacheHitFakeDelaySeconds; not hard-synced
    // (see that constant's own doc) — Hide() cuts this coroutine short the instant the real
    // SongProfileReadyEvent arrives regardless of where it's at.
    private static readonly string[] CacheHitKeys =
    {
        "Analyzing.CacheHit01", "Analyzing.CacheHit02", "Analyzing.CacheHit03",
    };
    private const float CacheHitFakeDelaySeconds = 2f;

    [Tooltip("Tip line changes every random(minTipInterval, maxTipInterval) seconds while shown.")]
    [SerializeField] private float minTipInterval = 1f;
    [SerializeField] private float maxTipInterval = 2f;

    private RectTransform _root;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _tipText;
    private Image _progressFill;

    private Coroutine _tipRoutine;
    private int       _lastTipIndex = -1;

    private System.Action<PreAnalysisStartedEvent>  _onStarted;
    private System.Action<PreAnalysisProgressEvent> _onProgress;
    private System.Action<SongProfileReadyEvent>    _onReady;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.Analyzing != null) WireUI(registry.Analyzing);
        else Build();
    }

    private void OnEnable()
    {
        _onStarted  = e => Show(e.IsCacheHit);
        _onProgress = e => { if (_progressFill != null) _progressFill.fillAmount = Mathf.Clamp01(e.Progress); };
        _onReady    = _ => Hide();
        EventBus.Subscribe(_onStarted);
        EventBus.Subscribe(_onProgress);
        EventBus.Subscribe(_onReady);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onStarted);
        EventBus.Unsubscribe(_onProgress);
        EventBus.Unsubscribe(_onReady);
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

    private void Show(bool isCacheHit)
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        if (_progressFill != null) _progressFill.fillAmount = 0f;
        _titleText.text = Loc.Get(TitleKey);

        _lastTipIndex = -1;
        if (_tipRoutine != null) StopCoroutine(_tipRoutine);
        _tipRoutine = StartCoroutine(isCacheHit ? PlayCacheHitSequence() : RotateTips());
    }

    private void Hide()
    {
        if (_root != null) _root.gameObject.SetActive(false);
        if (_tipRoutine != null) { StopCoroutine(_tipRoutine); _tipRoutine = null; }
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

    // Purely cosmetic (see this class's own doc + AudioPreAnalyzer.CacheHitFakeDelaySeconds) — a
    // fixed caption per step plus a self-animated fill, since there's no real progress to report on
    // a cache hit.
    private IEnumerator PlayCacheHitSequence()
    {
        float step = CacheHitFakeDelaySeconds / CacheHitKeys.Length;
        for (int i = 0; i < CacheHitKeys.Length; i++)
        {
            _tipText.text = Loc.Get(CacheHitKeys[i]);
            float t = 0f;
            while (t < step)
            {
                t += Time.deltaTime;
                if (_progressFill != null)
                    _progressFill.fillAmount = (i + Mathf.Clamp01(t / step)) / CacheHitKeys.Length;
                yield return null;
            }
        }
    }
}
