using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Temporary "YOU WIN"/"YOU LOSE" screen — shown for FightFlowState.MatchWon/MatchLost, exactly the
/// simple placeholder this phase asked for. Never decides anything (FightMatchController already
/// published MatchEndedEvent and requested this state before this class ever shows itself) — purely
/// presentational, same "only reads/reacts, never resolves" rule as FightController's own HUD.
///
/// Deliberately minimal: no Continue/Replay Song/Level Up/rewarded-ad flow exists yet (see this
/// phase's own explicit scope note) — the only action available is returning to Main Menu, the same
/// honest "you're done" destination FightController's own pause menu already uses, for the same
/// reason (no Results screen reads GameSession state independently of a live Runner yet).
///
/// Prefers a real MatchResult.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as every other Fight-flow screen.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class MatchResultController : MonoBehaviour
{
    private RectTransform   _root;
    private TextMeshProUGUI _text;

    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.MatchResult != null) WireUI(registry.MatchResult);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.MatchWon) Show(true);
            else if (e.Current == FightFlowState.MatchLost) Show(false);
            // A fresh match starting over (see FightFlowController's own re-entry announcement) —
            // hide so a stale result never lingers behind the next Opponent Selection.
            else if (e.Current == FightFlowState.OpponentSelection) Hide();
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onFightFlowChanged);

    // ── Prefab path — see MatchResultView's own doc ──────────────────────────────

    private void WireUI(MatchResultView view)
    {
        _root = view.root.GetComponent<RectTransform>();
        _text = view.text;
        view.mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        view.mainMenuButtonLabel.text = Loc.Get("Fight.MainMenu");
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("MatchResultScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 0.85f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        _text = UIFactory.CreateText("Text", _root, "", 80, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 40f), new Vector2(800f, 200f));
        _text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        var mainMenuBtn = UIFactory.CreateButton("MainMenuButton", _root, Loc.Get("Fight.MainMenu"), out var mainMenuLabel);
        UIFactory.SetBox(mainMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -80f), new Vector2(240f, 52f));
        mainMenuBtn.onClick.AddListener(OnMainMenuClicked);
        mainMenuBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        mainMenuLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
    }

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void Show(bool playerWon)
    {
        _text.text = Loc.Get(playerWon ? "Fight.YouWin" : "Fight.YouLose");
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    private void Hide() => _root.gameObject.SetActive(false);

    private void OnMainMenuClicked() => AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.MainMenu);
}
