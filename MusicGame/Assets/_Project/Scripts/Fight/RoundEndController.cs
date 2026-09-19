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
/// TWO SEQUENCE SHAPES, both driven purely by RoundEndedEvent.Resolution:
///   - Decisive / TrueDraw: reason ("KO!"/"TIME UP!") -> result ("X WINS THE ROUND" / "DRAW").
///   - DrawResolvedByPoints: reason -> "DRAW" -> (a short configurable pause) -> "X WINS ON POINTS"
///     (+ the point differential, when nonzero) — the extra beat exists so the draw itself still
///     reads clearly before the tie-break reveal, per this phase's own explicit UI ask.
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
        EventBus.Subscribe(_onRoundEnded);
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onRoundEnded);
        EventBus.Unsubscribe(_onFightFlowChanged);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
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

        if (data.Resolution == RoundResolution.DrawResolvedByPoints)
        {
            float pointsPause = _config != null ? Mathf.Max(0f, _config.drawPointsPauseDuration) : 1f;

            _text.text = Loc.Get("Fight.RoundDraw");
            yield return new WaitForSeconds(pointsPause);

            _text.text = PointsWinnerText(data);
            yield return new WaitForSeconds(resultDuration);
        }
        else
        {
            _text.text = ResultText(data);
            yield return new WaitForSeconds(resultDuration);
        }

        _root.gameObject.SetActive(false);
        _routine = null;

        // The one real branch point — handed back to FightMatchController, never decided here.
        FightMatchController.Instance?.NotifyRoundEndDisplayComplete();
    }

    private static string ResultText(RoundEndedEvent data)
    {
        if (data.Resolution == RoundResolution.TrueDraw) return Loc.Get("Fight.RoundDraw");
        if (data.Winner == FighterSide.Player)   return Loc.Get("Fight.RoundWinner", Loc.Get("Fight.PlayerName"));
        if (data.Winner == FighterSide.Opponent) return Loc.Get("Fight.RoundWinner", OpponentDisplayName());
        return Loc.Get("Fight.RoundDraw"); // safety net — shouldn't normally be reached
    }

    private static string PointsWinnerText(RoundEndedEvent data)
    {
        string name = data.Winner == FighterSide.Player ? Loc.Get("Fight.PlayerName") : OpponentDisplayName();
        string headline = Loc.Get("Fight.WinsOnPoints", name);

        // Rounded to whole "points" (0.50 -> 50) purely for a cleaner display — the underlying
        // value stays a float everywhere else (FightMatchController.MatchPointDifferential).
        int points = Mathf.RoundToInt(Mathf.Abs(data.AccumulatedPointDifferential) * 100f);
        if (points == 0) return headline;

        string sign = data.AccumulatedPointDifferential >= 0f ? "+" : "-";
        return headline + "\n" + Loc.Get("Fight.PointDifference", sign + points);
    }

    private static string OpponentDisplayName() =>
        GameSession.Instance?.SelectedOpponent != null
            ? GameSession.Instance.SelectedOpponent.displayName
            : Loc.Get("Fight.RivalUnknown");
}
