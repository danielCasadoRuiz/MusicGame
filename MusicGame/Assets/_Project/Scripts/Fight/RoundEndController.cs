using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// "KO"/"TIME UP" -> round result — one big centered text, same visual shape as RoundIntroView/
/// FinishBannerView, reused across every phase by swapping its content. Only ever DISPLAYS data
/// FightMatchController already decided (via RoundEndedEvent, cached the instant it arrives) — never
/// itself decides a winner or whether the match continues (see FightMatchController's own doc on why
/// that branch was moved out of every screen controller).
///
/// ONE SEQUENCE SHAPE, always: reason ("KO!"/"TIME UP!") -> result ("X WINS THE ROUND" / "DRAW").
/// A Draw round is ALWAYS shown at face value here — never "X WINS ON POINTS" — even for the very
/// last round of the match: that phrasing belongs on the MATCH result screen instead (see
/// MatchResultController's own doc on MatchResolution), never faked as a round win here (this
/// format's own explicit "Round result != Match resolution" rule).
///
/// When its own display sequence finishes, it calls FightMatchController.NotifyRoundEndDisplayComplete()
/// — a pure "my animation is done" notification, exactly the kind of thing this phase's own
/// architecture note says a screen MAY do, as opposed to deciding RoundIntro-vs-MatchWon itself.
///
/// Prefers a real RoundEnd.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as every other Fight-flow screen.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class RoundEndController : MonoBehaviour
{
    private RectTransform   _root;
    private TextMeshProUGUI _text;

    private FightFlowConfig _config;
    private RoundEndedEvent? _pendingData;
    private Coroutine        _routine;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<RoundEndedEvent>             _onRoundEnded;
    private System.Action<GameFlowStateChangedEvent>   _onGameFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.RoundEnd != null) WireUI(registry.RoundEnd);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // Cached the instant it arrives — FightMatchController publishes RoundEndedEvent BEFORE
        // requesting FightFlowState.RoundEnd (see its own EndRound), so this data is always ready
        // by the time BeginSequence below actually runs.
        _onRoundEnded = e => _pendingData = e;
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.RoundEnd) BeginSequence();
        };
        // Top-level safety net — see VersusScreenController.OnEnable's own doc on why this is needed
        // (FightFlowStateChangedEvent alone freezes the instant Fight itself is exited — this
        // sequence has no other way to know Fight ended mid-KO/TIME-UP/result display).
        _onGameFlowChanged = e => { if (e.Previous == GameFlowState.Fight) ExitFight(); };
        EventBus.Subscribe(_onRoundEnded);
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onRoundEnded);
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameFlowChanged);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    private void ExitFight()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_root != null) _root.gameObject.SetActive(false);
        _pendingData = null;
    }

    // ── Prefab path — see RoundEndView's own doc ─────────────────────────────────

    private void WireUI(RoundEndView view)
    {
        _root = view.root.GetComponent<RectTransform>();
        _text = view.text;
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("RoundEndScreen", canvas);
        UIFactory.Stretch(_root);

        _text = UIFactory.CreateText("Text", _root, "", 80, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(800f, 220f));
        _text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
    }

    // ── Sequence ──────────────────────────────────────────────────────────────────

    private void BeginSequence()
    {
        if (_routine != null) StopCoroutine(_routine);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _routine = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        var data = _pendingData ?? default;

        bool  isKO            = data.Reason == RoundEndReason.KO;
        float reasonDuration  = _config != null
            ? Mathf.Max(0f, isKO ? _config.koBannerDuration : _config.timeUpBannerDuration)
            : 1f;
        float resultDuration  = _config != null ? Mathf.Max(0f, _config.roundResultDuration) : 1.5f;

        _text.text = Loc.Get(isKO ? "Fight.KO" : "Fight.TimeUp");
        yield return new WaitForSeconds(reasonDuration);

        _text.text = ResultText(data);
        yield return new WaitForSeconds(resultDuration);

        _root.gameObject.SetActive(false);
        _routine = null;

        // The one real branch point — handed back to FightMatchController, never decided here.
        FightMatchController.Instance?.NotifyRoundEndDisplayComplete();
    }

    private static string ResultText(RoundEndedEvent data)
    {
        if (data.Resolution == RoundResolution.Draw) return Loc.Get("Fight.RoundDraw");
        if (data.Winner == FighterSide.Player)   return Loc.Get("Fight.RoundWinner", Loc.Get("Fight.PlayerName"));
        if (data.Winner == FighterSide.Opponent) return Loc.Get("Fight.RoundWinner", OpponentDisplayName());
        return Loc.Get("Fight.RoundDraw"); // safety net — shouldn't normally be reached
    }

    private static string OpponentDisplayName() =>
        GameSession.Instance?.SelectedOpponent != null
            ? GameSession.Instance.SelectedOpponent.displayName
            : Loc.Get("Fight.RivalUnknown");
}
