using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "PLAYER PORTRAIT — VS — OPPONENT PORTRAIT" screen, shown for FightFlowConfig.versusDuration
/// right after the Opponent Selection roulette settles. Reads GameSession.SelectedOpponent for the
/// real name and GameSession.SelectedOpponentLevelConfig (cached at selection time — see its own
/// doc) for the portrait; the player side has no real portrait yet (see this class's own
/// placeholder, a plain neutral box — not a 3D-model dependency).
///
/// Never touches the music itself — FightMusicController's locked-in song from Opponent Selection
/// just keeps playing underneath, unbroken, exactly as GameSession.SelectedOpponentSong's own doc
/// asks for.
///
/// Prefers a real VersusScreen.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as every other Fight-flow screen.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly, plus a top-level GameFlowStateChangedEvent safety net that
/// hides this screen the instant Fight itself is exited (see OnEnable's own doc).
/// </summary>
public class VersusScreenController : MonoBehaviour
{
    private static readonly Color PlaceholderPortraitColor = new(1f, 1f, 1f, 0.2f);

    private RectTransform    _root;
    private Image            _playerPortrait, _opponentPortrait;
    private TextMeshProUGUI  _playerNameText, _opponentNameText, _vsText;

    private FightFlowConfig _config;
    private Coroutine        _routine;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<GameFlowStateChangedEvent>  _onGameFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.VersusScreen != null) WireUI(registry.VersusScreen);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.VersusIntro) Show();
            else if (e.Previous == FightFlowState.VersusIntro) Hide();
        };
        // Fight-exclusive screens only ever react to FightFlowStateChangedEvent above, which freezes
        // the instant GameFlowState leaves Fight entirely (FightFlowController's own state machine
        // stops publishing) — so a screen mid-sequence when Fight is exited (e.g. Main Menu from the
        // pause menu) would otherwise stay visible/running forever afterward. Same top-level safety
        // net FightController (the Fight HUD) already uses for exactly this reason — see its own
        // ExitFight/doc.
        _onGameFlowChanged = e => { if (e.Previous == GameFlowState.Fight) Hide(); };
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameFlowChanged);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
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

        view.vsText.text = Loc.Get("Fight.Versus");
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("VersusScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        (_playerPortrait, _playerNameText)     = BuildSide("Player",   new Vector2(0.25f, 0.5f));
        (_opponentPortrait, _opponentNameText) = BuildSide("Opponent", new Vector2(0.75f, 0.5f));

        _vsText = UIFactory.CreateText("VS", _root, Loc.Get("Fight.Versus"), 72, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
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

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void Show()
    {
        PopulateInfo();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(HoldThenAdvance());
    }

    private void Hide()
    {
        _root.gameObject.SetActive(false);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    private IEnumerator HoldThenAdvance()
    {
        float duration = _config != null ? Mathf.Max(0f, _config.versusDuration) : 2.5f;
        yield return new WaitForSeconds(duration);
        _routine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.RoundIntro);
    }

    private void PopulateInfo()
    {
        _playerNameText.text = Loc.Get("Fight.PlayerName");
        // No real player portrait yet — a clean neutral placeholder box, not a 3D-model dependency
        // (see this class's own doc).
        _playerPortrait.sprite = null;
        _playerPortrait.color  = PlaceholderPortraitColor;

        var opponent = GameSession.Instance?.SelectedOpponent;
        _opponentNameText.text = opponent != null ? opponent.displayName : Loc.Get("Fight.RivalUnknown");
        // Reads the SAME level config OpponentSelectionController already resolved and cached at
        // selection time (GameSession.SelectedOpponentLevelConfig), not a fresh GetConfigForLevel
        // call — guarantees the portrait shown here always matches what the roulette just showed.
        _opponentPortrait.sprite = GameSession.Instance?.SelectedOpponentLevelConfig?.portrait;
        _opponentPortrait.color  = _opponentPortrait.sprite != null ? Color.white : PlaceholderPortraitColor;
    }
}
