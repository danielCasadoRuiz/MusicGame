using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Architectural SHELL for the Fight step of the app flow — deliberately NOT a real fighting game.
/// Its only job is to prove the wiring: entering GameFlowState.Fight gives this component
/// everything a real Fight system will eventually need (GameSession's SelectedSong/Profile/
/// RunnerResults/DetectedMusicStyleId, and CurrentTheme), and it uses that data to show a
/// placeholder screen and spawn two placeholder actors (player vs. AI enemy).
///
/// Scene-local (added next to SceneBootstrap on the same GameObject), not persistent — Fight is
/// entered from within the single existing scene rather than a real scene load (see SceneBootstrap's
/// own doc on why this project doesn't split scenes yet). Reached via the app's existing Continue
/// button: GameplayHUD.OnContinueClicked -> ContinuePressedEvent -> SceneBootstrap ->
/// GameFlowState.Fight -> this component.
///
/// Extension points for a REAL fight system later: SpawnPlayerPlaceholder/SpawnEnemyPlaceholder are
/// the two seams to replace with actual character loading/AI once that system gets designed — kept
/// as small, isolated methods rather than inlined, so that swap is a two-method change, not an
/// archaeology dig through this whole class. The underlying Gameplay scene/world is left completely
/// untouched (no pausing/hiding) — this is only an overlay on top of it, matching "shell, not a real
/// mode" for this phase.
/// </summary>
public class FightController : MonoBehaviour
{
    private RectTransform _root;
    private Text _infoText;

    private GameObject _playerPlaceholder;
    private GameObject _enemyPlaceholder;

    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void OnEnable()
    {
        _onFlowStateChanged = e =>
        {
            if (e.Current == GameFlowState.Fight) EnterFight();
            else if (e.Previous == GameFlowState.Fight) ExitFight();
        };
        EventBus.Subscribe(_onFlowStateChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
        ExitFight();
    }

    private void EnterFight()
    {
        BuildUiIfNeeded();
        PopulateInfo();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();

        SpawnPlayerPlaceholder();
        SpawnEnemyPlaceholder();
    }

    private void ExitFight()
    {
        if (_root != null) _root.gameObject.SetActive(false);
        if (_playerPlaceholder != null) { Destroy(_playerPlaceholder); _playerPlaceholder = null; }
        if (_enemyPlaceholder  != null) { Destroy(_enemyPlaceholder);  _enemyPlaceholder  = null; }
    }

    // ── UI shell (built at runtime via UIFactory, same pattern as AnalyzingScreenController) ─────

    private void BuildUiIfNeeded()
    {
        if (_root != null) return;

        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("FightScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.05f, 0.85f));
        UIFactory.Stretch(dim.rectTransform);

        var title = UIFactory.CreateText("Title", _root, Loc.Get("Fight.Title"), 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 80f), new Vector2(900f, 50f));

        _infoText = UIFactory.CreateText("Info", _root, "", 16, new Color(0.8f, 0.8f, 0.85f), TextAnchor.MiddleCenter);
        UIFactory.SetBox(_infoText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 10f), new Vector2(900f, 60f));

        var backBtn = UIFactory.CreateButton("BackButton", _root, Loc.Get("Fight.Back"), out _);
        UIFactory.SetBox(backBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -90f), new Vector2(200f, 44f));
        backBtn.onClick.AddListener(OnBackClicked);

        _root.gameObject.SetActive(false);
    }

    private void PopulateInfo()
    {
        var session = GameSession.Instance;
        string style = session != null ? session.DetectedMusicStyleId.ToString() : MusicStyleId.Unknown.ToString();
        int score = session?.RunnerResults?.Stats?.Score ?? 0;

        _infoText.text = Loc.Get("Fight.Info", style, score.ToString());
    }

    private void OnBackClicked()
    {
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Results);
    }

    // ── Placeholder actors ──────────────────────────────────────────────────────────────────────

    private void SpawnPlayerPlaceholder()
    {
        Color color = ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : new Color(0.2f, 0.6f, 1f);
        _playerPlaceholder = SpawnCapsule("FightPlayerPlaceholder", SpawnAnchor() - SpawnRight() * 1.5f, color);
    }

    private void SpawnEnemyPlaceholder()
    {
        _enemyPlaceholder = SpawnCapsule("FightEnemyPlaceholder", SpawnAnchor() + SpawnRight() * 1.5f, new Color(0.9f, 0.25f, 0.2f));
    }

    private GameObject SpawnCapsule(string name, Vector3 position, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = position;
        var rend = go.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;
        return go;
    }

    private Vector3 SpawnAnchor()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.position + cam.transform.forward * 5f : Vector3.zero;
    }

    private Vector3 SpawnRight()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.right : Vector3.right;
    }
}
