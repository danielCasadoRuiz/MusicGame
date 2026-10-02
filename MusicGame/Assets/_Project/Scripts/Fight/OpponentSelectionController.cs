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
/// opponent is chosen up front (GameSession.PickNextOpponent — persistent shuffle bag), then
/// FightFlowConfig.fakeSelectionCount FAKE rivals are shown, each with a short snippet from its
/// song's preview region — drawn without replacement (no rival twice in one roulette) and never the
/// final one, clamped to the unique candidates available — and the sequence ends on the precomputed
/// rival. SKIP only cuts the animation short: it reveals that same precomputed rival, never re-rolls.
///
/// Never touches an AudioSource itself (see FightMusicController's own doc) — each step just
/// reports which opponent is highlighted and lets FightMusicController decide what to actually
/// play.
///
/// Every portrait/song comes from OpponentDefinition.GetConfigForTier(CurrentTier) — this
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

    // The tier opponent content resolves at (GameSession.EffectiveOpponentTier: the real progression
    // tier, or a debug-forced one). Falls back to 1 only if GameSession doesn't exist.
    private static int CurrentTier => GameSession.Instance != null ? GameSession.Instance.EffectiveOpponentTier : 1;

    private RectTransform _root;
    private RectTransform _gridRoot;

    private readonly List<Image> _cellBackgrounds = new();

    private OpponentRosterSO _roster;
    private FightFlowConfig  _config;

    private Coroutine _rouletteRoutine;
    private Button _skipButton;
    private bool _skipRequested;   // Skip = skip the reveal ANIMATION, never a re-roll
    private SongPreviewConfigSO _preview;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;
    private System.Action<GameFlowStateChangedEvent>  _onGameFlowChanged;

    private void Awake()
    {
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _roster = appConfig != null ? appConfig.opponentRoster : null;
        _config = appConfig != null ? appConfig.fightFlow : null;
        _preview = appConfig != null ? appConfig.songPreview : null;
        if (_roster == null)
            Debug.LogWarning("[OpponentSelectionController] No OpponentRosterSO (AppConfig.opponentRoster) configured — the grid will be empty.");

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.OpponentSelection != null) WireUI(registry.OpponentSelection);
        else Build();

        PopulateGrid();
        BuildSkipButton();
        _root.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.OpponentSelection) Show();
            else if (e.Previous == FightFlowState.OpponentSelection) Hide();
        };
        // Top-level safety net — see VersusScreenController.OnEnable's own doc on why this is needed
        // (FightFlowStateChangedEvent alone freezes the instant Fight itself is exited).
        _onGameFlowChanged = e => { if (e.Previous == GameFlowState.Fight) Hide(); };
        EventBus.Subscribe(_onFightFlowChanged);
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        EventBus.Unsubscribe(_onGameFlowChanged);
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
        portrait.preserveAspect = true; // portraits are tall cut-outs, the cell is square
        portrait.color  = portrait.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);

        var background = cellBtn.GetComponent<Image>();
        background.color = NormalCellColor;
        _cellBackgrounds.Add(background);
    }

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void BuildSkipButton()
    {
        _skipButton = UIFactory.CreateButton("SkipButton", _root, Loc.Get("OpponentSelection.Skip"), out var label);
        UIFactory.SetBox(_skipButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(170f, 50f));
        _skipButton.onClick.AddListener(() => _skipRequested = true);
        _skipButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
    }

    private void Show()
    {
        _skipRequested = false;
        if (_skipButton != null) _skipButton.gameObject.SetActive(true);
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

        float stepDuration = _config != null ? Mathf.Max(0.05f, _config.selectionStepDuration) : 0.5f;
        float holdDuration = _config != null ? Mathf.Max(0f, _config.finalOpponentHoldDuration) : 1.5f;
        int   fakeCount    = _config != null ? Mathf.Max(0, _config.fakeSelectionCount) : 6;

        // 1. The REAL pick comes first, from GameSession's persistent shuffle bag (no repeats,
        //    cross-bag cooldown) — everything after this is theatre and can never change it.
        var picked     = GameSession.Instance != null ? GameSession.Instance.PickNextOpponent(_roster) : null;
        int finalIndex = picked != null ? System.Array.IndexOf(opponents, picked) : -1;
        if (finalIndex < 0) finalIndex = Random.Range(0, opponents.Length);

        // 2. Fake rivals: drawn WITHOUT replacement (never the same rival twice in one roulette),
        //    never the final one, clamped to the unique candidates that exist.
        var fakes = BuildFakeSequence(opponents, finalIndex, fakeCount);
        // ONE tier for the whole flow: every fake and the final are that composer's CURRENT-tier
        // version (never Mozart Tier 1 next to Mozart Tier 3).
        int tier = CurrentTier;
        Debug.Log($"[OpponentSelectionController] Final rival precomputed: {Name(opponents[finalIndex])} — fake sequence " +
                  $"({fakes.Count}/{fakeCount}): {string.Join(" -> ", fakes.ConvertAll(i => Name(opponents[i])))}");

        foreach (int index in fakes)
        {
            if (_skipRequested) break;
            HighlightOnly(index);
            var clip = opponents[index].GetConfigForTier(tier)?.GetRandomSong();
            FightMusicController.Instance?.PlaySnippet(clip, SnippetStart(clip)); // from the song's preview region
            for (float t = 0f; t < stepDuration && !_skipRequested; t += Time.deltaTime) yield return null;
        }
        if (_skipRequested) Debug.Log($"[OpponentSelectionController] Skip — revealing the precomputed rival {Name(opponents[finalIndex])} (no re-roll).");

        // 3. The definitive pick — always the last step, the SAME rival computed in step 1.
        HighlightOnly(finalIndex);
        var finalOpponent    = opponents[finalIndex];
        int resolvedTier     = 0;
        var finalLevelConfig = finalOpponent != null ? finalOpponent.GetConfigForTier(tier, out resolvedTier) : null;
        var finalSong        = GameSession.Instance != null
            ? GameSession.Instance.PickOpponentSong(finalOpponent, finalLevelConfig)
            : finalLevelConfig?.GetRandomSong();
        FightMusicController.Instance?.Lock(finalSong, finalLevelConfig);

        if (GameSession.Instance != null)
        {
            GameSession.Instance.SelectedOpponent            = finalOpponent;
            GameSession.Instance.SelectedOpponentSong        = finalSong;
            GameSession.Instance.SelectedOpponentLevelConfig = finalLevelConfig;
            GameSession.Instance.SelectedOpponentTier        = resolvedTier;
            GameSession.Instance.Run.OpponentId    = finalOpponent != null ? finalOpponent.id : null;
            GameSession.Instance.Run.OpponentLevel = finalLevelConfig != null ? finalLevelConfig.level : 0;
        }
        Debug.Log($"[OpponentSelectionController] Final pick: " +
                  $"{(finalOpponent != null ? finalOpponent.displayName : "(null)")} — tier {CurrentTier} (config tier {resolvedTier}) — " +
                  $"song: {(finalSong != null ? finalSong.name : "(none)")}");

        if (_skipButton != null) _skipButton.gameObject.SetActive(false);
        for (float t = 0f; t < holdDuration && !_skipRequested; t += Time.deltaTime) yield return null;

        _rouletteRoutine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.VersusIntro);
    }

    /// <summary>Up to `count` roster indices for the fake reveal, in random order, where each COMPOSER
    /// (OpponentDefinition.id — the identity, not a tier/version object) appears at most once and
    /// the final rival's composer never appears. Partial Fisher–Yates over the unique candidates — no
    /// rerolls, so no possible infinite loop; fewer candidates than `count` = a shorter sequence.</summary>
    private static List<int> BuildFakeSequence(OpponentDefinition[] opponents, int finalIndex, int count)
    {
        string finalKey = finalIndex >= 0 && finalIndex < opponents.Length ? IdentityKey(opponents[finalIndex]) : null;
        var seen = new HashSet<string>();
        if (finalKey != null) seen.Add(finalKey);
        var pool = new List<int>(opponents.Length);
        for (int i = 0; i < opponents.Length; i++)
            if (opponents[i] != null && seen.Add(IdentityKey(opponents[i]))) pool.Add(i); // one entry per composer
        int n = Mathf.Min(count, pool.Count);
        for (int i = 0; i < n; i++)
        {
            int j = Random.Range(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.GetRange(0, n);
    }

    // Composer identity: the stable id (falls back to the display name for an id-less asset).
    private static string IdentityKey(OpponentDefinition o) =>
        o == null ? null : !string.IsNullOrEmpty(o.id) ? o.id : o.displayName;

    // The SAME preview region the Song Selection preview uses (manual override or centred default).
    private float SnippetStart(AudioClip clip) =>
        clip == null ? 0f : _preview != null ? _preview.Resolve(clip.name, clip.length).start : Mathf.Max(0f, clip.length * 0.5f - 15f);

    private static string Name(OpponentDefinition o) => o != null ? o.displayName : "(null)";

    // Single place this controller ever asks "what does this opponent look/sound like right now"
    // — see OpponentDefinition.GetConfigForTier's own doc on how tiers resolve.
    private static OpponentLevelConfig ResolveLevel(OpponentDefinition opponent) =>
        opponent != null ? opponent.GetConfigForTier(CurrentTier) : null;

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
