using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen opaque overlay shown while AudioPreAnalyzer runs its (multi-second, no-cache) FFT
/// pass — PreAnalysisStartedEvent / PreAnalysisProgressEvent / SongProfileReadyEvent (all already
/// published by AudioPreAnalyzer itself) are the only hooks this reads; nothing here duplicates
/// analysis logic. A cache hit publishes SongProfileReadyEvent directly with no
/// PreAnalysisStartedEvent in between, so this overlay simply never appears in that case — correct,
/// since there's nothing worth waiting for.
///
/// Built entirely at runtime via UIFactory (no prefab/scene wiring needed), same pattern as
/// GameplayHUD's procedural fallback. Add it once, next to AudioSystemBootstrapper's own
/// AddComponent-chaining, and it works with zero scene setup.
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

    private RectTransform _root;
    private Text  _titleText;
    private Text  _tipText;
    private Image _progressFill;

    private Coroutine _tipRoutine;
    private int       _lastTipIndex = -1;

    private System.Action<PreAnalysisStartedEvent>  _onStarted;
    private System.Action<PreAnalysisProgressEvent> _onProgress;
    private System.Action<SongProfileReadyEvent>    _onReady;

    private void Awake() => Build();

    private void OnEnable()
    {
        _onStarted  = _ => Show();
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

    // ── Build (runtime-only, no prefab) ────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("AnalyzingScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling(); // always drawn above whatever other UI exists so far

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 0.96f));
        UIFactory.Stretch(dim.rectTransform);

        _titleText = UIFactory.CreateText("Title", _root, "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(_titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 40f), new Vector2(900f, 40f));

        _tipText = UIFactory.CreateText("Tip", _root, "", 16, new Color(0.75f, 0.75f, 0.8f), TextAnchor.MiddleCenter);
        UIFactory.SetBox(_tipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -10f), new Vector2(900f, 30f));

        var barBg = UIFactory.CreateFillBar("Progress", _root, new Color(1f, 1f, 1f, 0.12f), new Color(0.18f, 0.75f, 0.95f), out _progressFill);
        UIFactory.SetBox(barBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -60f), new Vector2(500f, 6f));
        _progressFill.fillAmount = 0f;

        _root.gameObject.SetActive(false);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────────

    private void Show()
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        if (_progressFill != null) _progressFill.fillAmount = 0f;
        _titleText.text = Loc.Get(TitleKey);

        _lastTipIndex = -1;
        if (_tipRoutine != null) StopCoroutine(_tipRoutine);
        _tipRoutine = StartCoroutine(RotateTips());
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
}
