using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opponent Selection screen — a grid populated directly from AppConfigSO.opponentRoster
/// (OpponentRosterSO.opponents), one cell per entry, NEVER a hardcoded count (today's roster
/// has 12, laid out 6 columns × 2 rows by ApplyGridLayout, overriding the prefab's bake — but adding/removing an OpponentDefinition just
/// reflows the same grid, no code change).
///
/// Runs a roulette the instant this state begins (FightFlowState.OpponentSelection): the FINAL
/// opponent was already ASSIGNED before the Runner (RunAssignment → GameSession.AssignedOpponent,
/// persisted as PlayerProgress.pendingRun; this screen only reveals it and never modifies it), then
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
/// Structure and look: OpponentSelection.prefab (UIPrefabLibrarySO.opponentSelection — title, grid with its
/// GridLayoutGroup, Skip button, highlight colours) plus one OpponentCell.prefab instance
/// (UIPrefabLibrarySO.opponentCell) per roster entry; this class only fills data and sizes the grid.
/// A missing prefab is logged by UIPrefabLibrarySO.Spawn — no procedural fallback.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class OpponentSelectionController : MonoBehaviour
{
    // The tier opponent content resolves at (GameSession.EffectiveOpponentTier: the real progression
    // tier, or a debug-forced one). Falls back to 1 only if GameSession doesn't exist.
    private static int CurrentTier => GameSession.Instance != null ? GameSession.Instance.EffectiveOpponentTier : 1;

    private OpponentSelectionView _view;
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

        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.opponentSelection : null, UIFactory.RootCanvas(), "OpponentSelection");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        _root     = _view.root.GetComponent<RectTransform>();
        _gridRoot = _view.gridRoot;
        _skipButton = _view.skipButton;
        if (_skipButton != null) _skipButton.onClick.AddListener(() => _skipRequested = true);

        PopulateGrid(lib.opponentCell);
        _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_view != null) Destroy(_view.gameObject);
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

    // ── Grid population — one cell per OpponentRosterSO entry, never a hardcoded count ───────

    private void PopulateGrid(OpponentCellView cellPrefab)
    {
        var opponents = _roster != null ? _roster.opponents : System.Array.Empty<OpponentDefinition>();
        SizeGrid(opponents.Length);

        _cellBackgrounds.Clear();
        for (int i = 0; i < opponents.Length; i++)
        {
            var cell = UIPrefabLibrarySO.Spawn(cellPrefab, _gridRoot, "OpponentCell");
            if (cell == null) break;
            var opponent = opponents[i];
            cell.name = $"Opponent_{i}";
            // No portrait art assigned yet on a fresh placeholder OpponentDefinition/level entry — a
            // plain dim box reads clearly as "no art yet" without any special-case handling downstream.
            cell.Set(opponent != null ? opponent.displayName : "?", ResolveLevel(opponent)?.portrait);
            if (cell.background != null) cell.background.color = _view.normalCellColor;
            _cellBackgrounds.Add(cell.background); // indexed like the roster (HighlightOnly) — even if null
        }
    }

    // The prefab's GridLayoutGroup (fixed column count, cell size, spacing) lays the cells out; the
    // container is sized to exactly columns × rows (rows grow with the roster).
    private void SizeGrid(int count)
    {
        var layout = _gridRoot.GetComponent<GridLayoutGroup>();
        if (layout == null) { Debug.LogError("[OpponentSelectionController] OpponentSelection.prefab Grid has no GridLayoutGroup."); return; }
        int columns = layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? Mathf.Max(1, layout.constraintCount) : Mathf.Max(1, count);
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        _gridRoot.sizeDelta = new Vector2(columns * layout.cellSize.x + (columns - 1) * layout.spacing.x + layout.padding.horizontal,
                                          rows * layout.cellSize.y + (rows - 1) * layout.spacing.y + layout.padding.vertical);
    }

    // ── Show / hide ───────────────────────────────────────────────────────────────

    private void Show()
    {
        _skipRequested = false;
        if (_skipButton != null) _skipButton.gameObject.SetActive(true);
        FightMusicController.Instance?.Reset();
        // A final-rival stage has no roulette: the rival (and its special song) is fixed by
        // CampaignService.PrepareBossStage — straight to the Versus.
        var session = GameSession.Instance;
        if (session != null && session.CurrentBossStage >= 0 && session.SelectedOpponent != null)
        {
            _root.gameObject.SetActive(false);
            FightMusicController.Instance?.Lock(session.SelectedOpponentSong, session.BossSongOverride != null ? null : session.SelectedOpponentLevelConfig);
            ComposerDialogueService.Preload(session.SelectedOpponent.id);
            ComposerVoiceService.Preload(session.SelectedOpponent.id);
            // Next frame: every other listener must finish handling OpponentSelection first.
            StopRoulette();
            _rouletteRoutine = StartCoroutine(AdvanceNextFrame());
            return;
        }
        ResetHighlights();
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        StopRoulette();
        _rouletteRoutine = StartCoroutine(RunRoulette());
    }

    private IEnumerator AdvanceNextFrame()
    {
        yield return null;
        _rouletteRoutine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.VersusIntro);
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

        float stepDuration = _config != null ? Mathf.Max(0.05f, _config.selectionStepDuration) : 0.75f;
        float holdDuration = _config != null ? Mathf.Max(0f, _config.finalOpponentHoldDuration) : 1.5f;
        int   fakeCount    = _config != null ? Mathf.Max(0, _config.fakeSelectionCount) : 6;

        // 1. The FINAL rival was already decided before the Runner (RunAssignment → GameSession.
        //    AssignedOpponent, persisted as PlayerProgress.pendingRun) — this screen only REVEALS it and
        //    never modifies the assignment. Fallback (Fight reached without an assignment, e.g. debug):
        //    pick from the shuffle bag here, as before.
        var session    = GameSession.Instance;
        var assigned   = session != null ? session.AssignedOpponent : null;
        var picked     = assigned != null ? assigned : (session != null ? session.PickNextOpponent(_roster) : null);
        int finalIndex = picked != null ? System.Array.IndexOf(opponents, picked) : -1;
        if (finalIndex < 0) finalIndex = Random.Range(0, opponents.Length);
        bool useAssigned = assigned != null && opponents[finalIndex] == assigned && session.AssignedOpponentLevelConfig != null;

        // 2. Fake rivals: drawn WITHOUT replacement (never the same rival twice in one roulette),
        //    never the final one, clamped to the unique candidates that exist.
        var fakes = BuildFakeSequence(opponents, finalIndex, fakeCount);
        // ONE tier for the whole flow: every fake and the final are that composer's CURRENT-tier
        // version (never Mozart Tier 1 next to Mozart Tier 3).
        int tier = useAssigned && session.AssignedOpponentTier > 0 ? session.AssignedOpponentTier : CurrentTier;
        Debug.Log($"[OpponentSelectionController] Final rival precomputed: {Name(opponents[finalIndex])} — fake sequence " +
                  $"({fakes.Count}/{fakeCount}): {string.Join(" -> ", fakes.ConvertAll(i => Name(opponents[i])))}");

        // Songs are chosen UP FRONT (fakes + the final one) and loaded in the background BEFORE the
        // first step — a step's timer never runs while its clip is still loading (that load used to
        // stall the first Play of each track, so the roulette moved on before the music started).
        var fakeClips = fakes.ConvertAll(i => opponents[i].GetConfigForTier(tier)?.GetRandomSong());
        var finalOpponent    = opponents[finalIndex];
        int resolvedTier     = 0;
        var finalLevelConfig = useAssigned ? session.AssignedOpponentLevelConfig
                             : finalOpponent != null ? finalOpponent.GetConfigForTier(tier, out resolvedTier) : null;
        if (useAssigned) resolvedTier = finalLevelConfig.level; // the exact persisted version
        var finalSong        = GameSession.Instance != null
            ? GameSession.Instance.PickOpponentSong(finalOpponent, finalLevelConfig)
            : finalLevelConfig?.GetRandomSong();
        var toLoad = new List<AudioClip>(fakeClips) { finalSong };
        foreach (var c in toLoad) if (c != null && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
        float preloadTimeout = _config != null ? _config.songPreloadTimeout : 3f;
        for (float t = 0f; t < preloadTimeout && !_skipRequested && toLoad.Exists(c => c != null && c.loadState == AudioDataLoadState.Loading); t += Time.unscaledDeltaTime)
            yield return null;

        bool radio = _config == null || _config.radioStaticEnabled;
        for (int k = 0; k < fakes.Count; k++)
        {
            if (_skipRequested) break;
            HighlightOnly(fakes[k]);
            var clip = fakeClips[k];
            if (radio && k > 0) FightMusicController.Instance?.PlayRadioStatic(_config != null ? _config.radioStaticDuration : 0.08f,
                                                                                _config != null ? _config.radioStaticVolume : 0.12f);
            FightMusicController.Instance?.PlaySnippet(clip, SnippetStart(clip)); // from the song's preview region
            for (float t = 0f; t < stepDuration && !_skipRequested; t += Time.deltaTime) yield return null;
        }
        if (_skipRequested) Debug.Log($"[OpponentSelectionController] Skip — revealing the precomputed rival {Name(opponents[finalIndex])} (no re-roll).");

        // 3. The definitive pick — always the last step, the SAME rival computed in step 1.
        HighlightOnly(finalIndex);
        if (radio && fakes.Count > 0) FightMusicController.Instance?.PlayRadioStatic(_config != null ? _config.radioStaticDuration : 0.08f,
                                                                                    _config != null ? _config.radioStaticVolume : 0.12f);
        FightMusicController.Instance?.Lock(finalSong, finalLevelConfig);
        // The fakes' tracks are not needed any more — free their audio data (the final one keeps playing).
        foreach (var c in fakeClips)
            if (c != null && c != finalSong && c.loadState == AudioDataLoadState.Loaded) c.UnloadAudioData();

        if (GameSession.Instance != null)
        {
            GameSession.Instance.SelectedOpponent            = finalOpponent;
            GameSession.Instance.SelectedOpponentSong        = finalSong;
            GameSession.Instance.SelectedOpponentLevelConfig = finalLevelConfig;
            GameSession.Instance.SelectedOpponentTier        = resolvedTier;
            GameSession.Instance.Run.OpponentId    = finalOpponent != null ? finalOpponent.id : null;
            GameSession.Instance.Run.OpponentLevel = finalLevelConfig != null ? finalLevelConfig.level : 0;
        }
        // The DEFINITIVE rival only (never the roulette's fakes): its dialogue table loads during the
        // final hold so the Versus line is usually ready on arrival; other composers' tables are released.
        if (finalOpponent != null) { ComposerDialogueService.Preload(finalOpponent.id); ComposerVoiceService.Preload(finalOpponent.id); }
        Debug.Log($"[OpponentSelectionController] Final pick: " +
                  $"{(finalOpponent != null ? finalOpponent.displayName : "(null)")} — tier {tier} (config tier {resolvedTier}, " +
                  $"{(useAssigned ? "precomputed assignment" : "picked here — no assignment")}) — " +
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
        Color highlighted = new(accent.r, accent.g, accent.b, _view.highlightAlpha);
        for (int i = 0; i < _cellBackgrounds.Count; i++)
            if (_cellBackgrounds[i] != null) _cellBackgrounds[i].color = i == index ? highlighted : _view.normalCellColor;
    }

    private Color CurrentAccentColor() =>
        ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null && ThemeManager.Instance.CurrentTheme.UI != null
            ? ThemeManager.Instance.CurrentTheme.UI.accentColor
            : _view.fallbackAccentColor;
}
