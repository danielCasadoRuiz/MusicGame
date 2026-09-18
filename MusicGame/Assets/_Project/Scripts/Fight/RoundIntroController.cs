using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// "ROUND {n}" -> "3" -> "2" -> "1" -> "FIGHT!" — one big centered text, same visual shape as
/// CountdownScreenView/FinishBannerView, reused across all five phases by swapping its content.
/// Deliberately owns BOTH FightFlowState.RoundIntro and FightFlowState.Countdown as one continuous
/// self-contained sequence (mirrors how Runner's own CountdownController owns its whole "3, 2, 1,
/// GO" sequence internally rather than GameplayManager micromanaging each tick) — it requests the
/// RoundIntro -> Countdown -> Fighting transitions itself, at exactly the right moments, instead of
/// FightFlowController trying to time sub-phases it has no reason to know about.
///
/// Round is currently ALWAYS 1 (no round-end/next-round logic exists yet) — SetRound is already
/// exposed so a future round-management system can call it before RoundIntro fires again, with no
/// change needed here.
///
/// Prefers a real RoundIntro.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as CountdownController/FinishBannerController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class RoundIntroController : MonoBehaviour
{
    public static RoundIntroController Instance { get; private set; }

    private RectTransform   _root;
    private TextMeshProUGUI _text;

    private FightFlowConfig _config;
    private int              _round = 1;
    private Coroutine        _routine;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    /// <summary>Call before RoundIntro fires again to show a round other than 1 — see this
    /// class's own doc.</summary>
    public void SetRound(int round) => _round = round;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.RoundIntro != null) WireUI(registry.RoundIntro);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.RoundIntro) BeginSequence();
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    // ── Prefab path — see RoundIntroView's own doc ───────────────────────────────

    private void WireUI(RoundIntroView view)
    {
        _root = view.root.GetComponent<RectTransform>();
        _text = view.text;
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("RoundIntroScreen", canvas);
        UIFactory.Stretch(_root);

        _text = UIFactory.CreateText("Text", _root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(700f, 200f));
        _text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
    }

    // ── Sequence — owns RoundIntro AND Countdown, see class doc on why ───────────

    private void BeginSequence()
    {
        if (_routine != null) StopCoroutine(_routine);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _routine = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        float roundIntroDuration  = _config != null ? Mathf.Max(0f, _config.roundIntroDuration)  : 1.5f;
        float countdownStep       = _config != null ? Mathf.Max(0.05f, _config.countdownStepDuration) : 0.8f;
        float fightBannerDuration = _config != null ? Mathf.Max(0f, _config.fightBannerDuration)  : 1f;

        _text.text = Loc.Get("Fight.RoundLabel", _round.ToString());
        yield return new WaitForSeconds(roundIntroDuration);

        FightFlowController.Instance?.RequestState(FightFlowState.Countdown);

        for (int n = 3; n >= 1; n--)
        {
            _text.text = n.ToString();
            yield return new WaitForSeconds(countdownStep);
        }

        _text.text = Loc.Get("Fight.Banner");
        yield return new WaitForSeconds(fightBannerDuration);

        _root.gameObject.SetActive(false);
        _routine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.Fighting);
    }
}
