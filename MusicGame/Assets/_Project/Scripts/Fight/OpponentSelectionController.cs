using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opponent Selection screen — a grid populated directly from AppConfigSO.opponentRoster
/// (OpponentRosterSO.opponents), one cell per entry, NEVER a hardcoded count (today's roster
/// happens to have 8, laid out 2x4 by the baked GridLayoutGroup's own FixedColumnCount=4 — see
/// UIPrefabBuilder.BuildOpponentSelection — but adding/removing an OpponentDefinition just
/// reflows the same grid, no code change).
///
/// Runs a roulette the instant this state begins (FightFlowState.OpponentSelection): the FINAL
/// opponent is chosen up front (Random.Range), then a fixed number of intermediate highlight steps
/// (pseudo-random, never repeating the immediately-previous one) visit other opponents — including,
/// deliberately, possibly the eventual winner itself. Excluding the winner from every intermediate
/// step would make it guessable before the reveal (the one opponent that never lit up has to be
/// it) — letting it appear like any other keeps the outcome genuinely unclear until the last step,
/// which always lands on it and holds there.
///
/// Never touches an AudioSource itself (see FightMusicController's own doc) — each step just
/// reports which opponent is highlighted and lets FightMusicController decide what to actually
/// play.
///
/// Every portrait/song comes from OpponentDefinition.GetConfigForLevel(CurrentPlayerLevel) — this
/// class has no idea HOW an opponent's content varies by level, only that it might; wiring in a
/// real Player Level later is a one-line change (CurrentPlayerLevel's own doc).
///
/// Prefers a real OpponentSelection.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) for the STATIC chrome only — the grid cells stay dynamic
/// runtime population either way, same split as SongSelectionView/SongSelectionController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class OpponentSelectionController : MonoBehaviour
{
    private static readonly Color NormalCellColor = new(1f, 1f, 1f, 0.10f);

    private const float DefaultCellWidth  = 180f;
    private const float DefaultCellHeight = 220f;
    private const int   PreferredColumns  = 4;

    // GameSession.PlayerLevel is now the real, authoritative session-wide Player Level (see its own
    // doc — increments once per match WIN; no persistence across app restarts yet). Every
    // OpponentDefinition.GetConfigForLevel call in this class reads from there. Falls back to 1 only
    // if GameSession somehow doesn't exist yet (shouldn't happen in the real flow).
    private static int CurrentPlayerLevel => GameSession.Instance != null ? GameSession.Instance.PlayerLevel : 1;

    private RectTransform _root;
    private RectTransform _gridRoot;

    private readonly List<Image> _cellBackgrounds = new();

    private OpponentRosterSO _roster;
    private FightFlowConfig  _config;

    private Coroutine _rouletteRoutine;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _roster = appConfig != null ? appConfig.opponentRoster : null;
        _config = appConfig != null ? appConfig.fightFlow : null;
        if (_roster == null)
            Debug.LogWarning("[OpponentSelectionController] No OpponentRosterSO (AppConfig.opponentRoster) configured — the grid will be empty.");

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.OpponentSelection != null) WireUI(registry.OpponentSelection);
        else Build();

        PopulateGrid();
        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.OpponentSelection) Show();
            else if (e.Previous == FightFlowState.OpponentSelection) Hide();
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        StopRoulette();
    }

    // ── Prefab path — see OpponentSelectionView's own doc ────────────────────────

    private void WireUI(OpponentSelectionView view)
    {
        _root     = view.root.GetComponent<RectTransform>();
        _gridRoot = view.gridRoot;
        view.titleText.text = Loc.Get("OpponentSelection.Title");
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("OpponentSelectionScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", _root, Loc.Get("OpponentSelection.Title"), 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        _gridRoot = UIFactory.CreateRect("Grid", _root);
        UIFactory.SetBox(_gridRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2((DefaultCellWidth + 16f) * PreferredColumns, (DefaultCellHeight + 16f) * 2f));
        BuildGridLayout(_gridRoot);
    }

    private static void BuildGridLayout(RectTransform gridRoot)
    {
        var layout = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize       = new Vector2(DefaultCellWidth, DefaultCellHeight);
        layout.spacing        = new Vector2(16f, 16f);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.constraint     = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = PreferredColumns;
    }

    // ── Grid population — one cell per OpponentRosterSO entry, never a hardcoded count ───────

    private void PopulateGrid()
    {
        if (_gridRoot.GetComponent<GridLayoutGroup>() == null) BuildGridLayout(_gridRoot); // baked prefab safety net
        var cellSize = _gridRoot.GetComponent<GridLayoutGroup>().cellSize;

        _cellBackgrounds.Clear();
        var opponents = _roster != null ? _roster.opponents : System.Array.Empty<OpponentDefinition>();
        for (int i = 0; i < opponents.Length; i++)
            BuildCell(i, opponents[i], cellSize);
    }

    private void BuildCell(int index, OpponentDefinition opponent, Vector2 cellSize)
    {
        // Real Button component (for its Image + color-tint machinery), left fully interactable
        // but with no onClick wired — display-only during the roulette; no manual picking in V1.
        string name = opponent != null ? opponent.displayName : "?";
        var cellBtn = UIFactory.CreateButton($"Opponent_{index}", _gridRoot, name, out var nameLabel);

        float nameHeight = 26f;
        UIFactory.SetBox(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 4f), new Vector2(-10f, nameHeight));
        nameLabel.fontSize = 13;

        var portraitRt = UIFactory.CreateRect("Portrait", cellBtn.GetComponent<RectTransform>());
        var portrait = portraitRt.gameObject.AddComponent<Image>();
        UIFactory.SetBox(portraitRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -8f), new Vector2(-10f, cellSize.y - nameHeight - 20f));
        // No portrait art assigned yet on a fresh placeholder OpponentDefinition/level entry — a
        // plain dim box reads clearly as "no art yet" without any special-case handling downstream.
        portrait.sprite = ResolveLevel(opponent)?.portrait;
        portrait.color  = portrait.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);

        var background = cellBtn.GetComponent<Image>();
        background.color = NormalCellColor;
        _cellBackgrounds.Add(background);
    }

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void Show()
    {
        FightMusicController.Instance?.Reset();
        ResetHighlights();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        StopRoulette();
        _rouletteRoutine = StartCoroutine(RunRoulette());
    }

    private void Hide()
    {
        _root.gameObject.SetActive(false);
        StopRoulette();
    }

    private void StopRoulette()
    {
        if (_rouletteRoutine == null) return;
        StopCoroutine(_rouletteRoutine);
        _rouletteRoutine = null;
    }

    // ── Roulette ──────────────────────────────────────────────────────────────────

    private IEnumerator RunRoulette()
    {
        var opponents = _roster != null ? _roster.opponents : System.Array.Empty<OpponentDefinition>();
        if (opponents.Length == 0)
        {
            Debug.LogWarning("[OpponentSelectionController] Empty roster — nothing to select from. Advancing to Versus anyway so the flow doesn't get stuck.");
            FightFlowController.Instance?.RequestState(FightFlowState.VersusIntro);
            yield break;
        }

        float stepDuration   = _config != null ? Mathf.Max(0.05f, _config.selectionStepDuration)   : 0.25f;
        float totalDuration  = _config != null ? Mathf.Max(stepDuration, _config.opponentSelectionDuration) : 4f;
        float holdDuration   = _config != null ? Mathf.Max(0f, _config.finalOpponentHoldDuration)   : 1.5f;
        int   steps          = Mathf.Max(1, Mathf.RoundToInt(totalDuration / stepDuration));

        int finalIndex = Random.Range(0, opponents.Length);
        int lastIndex  = -1;

        for (int step = 0; step < steps - 1; step++)
        {
            int index = NextRouletteIndex(opponents.Length, lastIndex);
            lastIndex = index;
            HighlightOnly(index);

            var clip = ResolveLevel(opponents[index])?.GetRandomSong();
            FightMusicController.Instance?.PlaySnippet(clip);

            yield return new WaitForSeconds(stepDuration);
        }

        // The definitive pick — always the last step, always lands here regardless of whatever
        // was visited above.
        HighlightOnly(finalIndex);
        var finalOpponent    = opponents[finalIndex];
        var finalLevelConfig = ResolveLevel(finalOpponent);
        var finalSong        = finalLevelConfig?.GetRandomSong();
        FightMusicController.Instance?.Lock(finalSong);

        if (GameSession.Instance != null)
        {
            GameSession.Instance.SelectedOpponent            = finalOpponent;
            GameSession.Instance.SelectedOpponentSong        = finalSong;
            GameSession.Instance.SelectedOpponentLevelConfig = finalLevelConfig;
        }
        Debug.Log($"[OpponentSelectionController] Final pick: " +
                  $"{(finalOpponent != null ? finalOpponent.displayName : "(null)")} — " +
                  $"song: {(finalSong != null ? finalSong.name : "(none)")}");

        yield return new WaitForSeconds(holdDuration);

        _rouletteRoutine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.VersusIntro);
    }

    // Single place this controller ever asks "what does this opponent look/sound like right now"
    // — see OpponentDefinition.GetConfigForLevel's own doc on why it never reasons about
    // levels[]'s ranges itself.
    private static OpponentLevelConfig ResolveLevel(OpponentDefinition opponent) =>
        opponent != null ? opponent.GetConfigForLevel(CurrentPlayerLevel) : null;

    // Avoids repeating the immediately-previous step's opponent — with 2+ opponents this always
    // terminates in a handful of iterations at worst.
    private static int NextRouletteIndex(int count, int lastIndex)
    {
        if (count <= 1) return 0;
        int index;
        do { index = Random.Range(0, count); } while (index == lastIndex);
        return index;
    }

    private void ResetHighlights() => HighlightOnly(-1);

    private void HighlightOnly(int index)
    {
        Color accent = CurrentAccentColor();
        Color highlighted = new(accent.r, accent.g, accent.b, 0.55f);
        for (int i = 0; i < _cellBackgrounds.Count; i++)
            _cellBackgrounds[i].color = i == index ? highlighted : NormalCellColor;
    }

    private static Color CurrentAccentColor() =>
        ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : new Color(0.18f, 0.75f, 0.95f);
}
