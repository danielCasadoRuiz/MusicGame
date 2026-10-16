using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// "PROGRESSIÓ" (Main Menu, under COL·LECCIÓ): the campaign as a scrollable table, level by level, each level
/// collapsible into its matches, with the persistent BEST runner / combat records of every position (each with
/// the song / rival it came from), level averages, mastery counters, the final-rival requirements and
/// challenge, and the seal.
///
/// Structure and look: prefabs (UIPrefabLibrarySO — ProgressionScreen, LevelHeaderRow, ColumnHeaderRow, MatchRow,
/// BossHeaderRow, BossStageRow, plus the EvaluationPanel inside the screen's detail overlay). This controller only
/// instantiates rows and fills data.
///
/// Averages: half-star means over the RATED positions of the level, shown with their base ("4,5 · 3/5") and
/// floored to a half step for the stars; pending positions stay visible, and the mastered counter checks every
/// position individually.
/// </summary>
public class ProgressionScreenController : MonoBehaviour
{
    public static ProgressionScreenController Instance { get; private set; }

    private ProgressionScreenView _view;
    private readonly List<GameObject> _spawned = new();
    private readonly HashSet<string> _expanded = new();
    private System.Action<GameFlowStateChangedEvent> _onFlow;
    private System.Action _onLanguage;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.progressionScreen : null, UIFactory.RootCanvas(), "ProgressionScreen");
        if (_view == null) return;
        UIFactory.Stretch((RectTransform)_view.transform);
        if (_view.backButton != null) _view.backButton.onClick.AddListener(Close);
        if (_view.challengeButton != null) _view.challengeButton.onClick.AddListener(OnChallengeClicked);
        if (_view.detailBackdrop != null) _view.detailBackdrop.onClick.AddListener(() => _view.detailRoot.gameObject.SetActive(false));
        if (_view.listContent == null || _view.detailRoot == null || _view.detailPanel == null)
            Debug.LogError("[ProgressionScreenController] ProgressionScreen.prefab references missing (listContent / detailRoot / detailPanel).");
        _view.gameObject.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.MainMenu) Close(); };
        _onLanguage = () => { if (_view != null && _view.gameObject.activeSelf) Refresh(); };
        EventBus.Subscribe(_onFlow);
        Loc.Refreshed += _onLanguage;
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlow);
        Loc.Refreshed -= _onLanguage;
    }

    public void Open()
    {
        if (_view == null) return;
        if (_expanded.Count == 0 && CampaignService.CurrentLevel != null) _expanded.Add(CampaignService.CurrentLevel.id);
        Refresh();
        if (_view.detailRoot != null) _view.detailRoot.gameObject.SetActive(false);
        _view.gameObject.SetActive(true);
        _view.transform.SetAsLastSibling();
    }

    public void Close() { if (_view != null) _view.gameObject.SetActive(false); }

    /// <summary>Opens a level's match list (also used by tests).</summary>
    public void Expand(string levelId) { if (!string.IsNullOrEmpty(levelId)) _expanded.Add(levelId); }

    // ── Content ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        foreach (var go in _spawned) if (go != null) { go.SetActive(false); Destroy(go); }
        _spawned.Clear();
        var data = CampaignService.Data;
        var config = CampaignService.Config;
        if (data == null || config == null)
        {
            if (_view.summary != null) Loc.Set(_view.summary, "Progression.Unavailable");
            SetText(_view.requirements, ""); SetText(_view.seal, "");
            if (_view.challengeButton != null) _view.challengeButton.gameObject.SetActive(false);
            return;
        }

        string where = CampaignModel.IsLapFinished(data, config)
            ? Loc.Get("Progression.LapFinished")
            : Loc.Get("Campaign.Position", (data.levelIndex + 1).ToString(), data.matchNumber.ToString());
        var cycle = PlayerProgressService.Instance?.SongCycle;
        SetText(_view.summary, Loc.Get("Progression.Summary", data.lap.ToString(), where, (cycle != null ? cycle.cycle : 1).ToString()));

        var req = CampaignService.BossRequirements();
        string state = BossState(data);
        SetText(_view.requirements, data.bossRevealed
            ? Loc.Get("Progression.Requirements", req.Mastered.ToString(), req.TotalPositions.ToString(), req.Cards.ToString(), req.CardsRequired.ToString(), state)
            : Loc.Get("Progression.MasteredOnly", req.Mastered.ToString(), req.TotalPositions.ToString()));
        SetText(_view.seal, data.boss.sealGranted ? "— " + Loc.Get("Campaign.SealName") + " —" : "");

        bool canChallenge = CampaignService.CanChallengeBoss;
        if (_view.challengeButton != null) _view.challengeButton.gameObject.SetActive(canChallenge);
        if (canChallenge) SetText(_view.challengeLabel, Loc.Get("Progression.Challenge", (CampaignService.NextBossStage + 1).ToString(), CampaignService.Boss.StageCount.ToString()));

        for (int i = 0; i < config.LevelCount; i++) BuildLevel(data, config, i);
        if (data.bossRevealed && CampaignService.Boss != null) BuildBossSection(data);
    }

    private void BuildLevel(CampaignProgressData data, CampaignConfigSO config, int index)
    {
        var lib = UIPrefabLibrarySO.Instance;
        var level = config.Level(index);
        var s = CampaignModel.Summarize(data, config, index);
        bool open = _expanded.Contains(level.id);

        var header = Spawn(lib != null ? lib.progressionLevelHeader : null, "LevelHeaderRow");
        if (header != null)
        {
            string name = string.IsNullOrEmpty(level.nameKey) ? Loc.Get("Campaign.Level", (index + 1).ToString()) : Loc.Get(level.nameKey);
            int r = Avg(s.AvgRunner), c = Avg(s.AvgCombat), t = Avg(s.AvgTotal);
            header.Set($"{(open ? "▼" : "►")} {name}",
                       Loc.Get("Progression.LevelLine", s.CompletedThisLap.ToString(), s.MatchCount.ToString(), s.Mastered.ToString()),
                       $"{Loc.Get("Progression.Runner")}  {EvaluationPanel.Stars(r)} · {s.RatedRunner}/{s.MatchCount}", r,
                       $"{Loc.Get("Progression.Combat")}  {EvaluationPanel.Stars(c)} · {s.RatedCombat}/{s.MatchCount}", c,
                       $"{Loc.Get("Progression.Total")}  {EvaluationPanel.Stars(t)} · {s.RatedTotal}/{s.MatchCount}", t);
            string id = level.id;
            if (header.button != null) header.button.onClick.AddListener(() => { if (!_expanded.Remove(id)) _expanded.Add(id); Refresh(); });
        }
        if (!open) return;

        if (lib != null && lib.progressionColumnHeader != null) _spawned.Add(Instantiate(lib.progressionColumnHeader, _view.listContent, false));
        for (int m = 1; m <= level.matchCount; m++)
            BuildRow(CampaignModel.Find(data, CampaignConfigSO.PositionId(level.id, m)), index, m,
                     data.levelIndex == index && data.matchNumber == m && !CampaignModel.IsLapFinished(data, config));
    }

    private void BuildRow(PositionRecordData p, int levelIndex, int match, bool current)
    {
        var lib = UIPrefabLibrarySO.Instance;
        var row = Spawn(lib != null ? lib.progressionMatchRow : null, "MatchRow");
        if (row == null) return;
        string status = p == null ? Loc.Get("Eval.Pending")
                      : CampaignModel.IsMastered(p) ? Loc.Get("Progression.Mastered")
                      : p.completed ? (p.legacyUnrated && !CampaignModel.HasRunner(p) && !CampaignModel.HasCombat(p) ? Loc.Get("Progression.Unrated") : Loc.Get("Progression.Completed"))
                      : Loc.Get("Eval.Pending");
        bool hasRunner = p != null && CampaignModel.HasRunner(p);
        bool hasCombat = p != null && CampaignModel.HasCombat(p);
        var rival = hasCombat ? Rival(p.bestCombat.opponentId) : null;
        int version = hasCombat ? p.bestCombat.opponentLevel : 0;
        row.Set(match, current,
                $"<b>{Loc.Get("Progression.Match", match.ToString())}</b>\n<size=80%>{status}</size>",
                $"{Loc.Get("Progression.Runner")}: {(hasRunner ? SongName(p.bestRunner.songId) : "—")}", hasRunner ? p.bestRunner.halfStars : -1,
                rival != null ? Portrait(rival, version) : null,
                $"{Loc.Get("Progression.Combat")}: {(rival != null ? $"{rival.displayName} · {Loc.Get("Rivals.Level", version.ToString())}" : "—")}",
                hasCombat ? p.bestCombat.halfStars : -1,
                p != null ? CampaignModel.CombinedHalfStars(p) : -1);
        var pos = p;
        if (row.button != null) row.button.onClick.AddListener(() => OpenDetail(pos, levelIndex, match));
    }

    private void BuildBossSection(CampaignProgressData data)
    {
        var lib = UIPrefabLibrarySO.Instance;
        var boss = CampaignService.Boss;
        var header = Spawn(lib != null ? lib.progressionBossHeader : null, "BossHeaderRow");
        if (header != null) header.Set($"<b>{Loc.Get(boss.nameKey)}</b>  ·  {BossState(data)}");
        for (int i = 0; i < boss.StageCount; i++)
        {
            var stage = CampaignService.BossStage(i);
            string st = stage != null && stage.defeated ? Loc.Get("Progression.Completed") : Loc.Get("Eval.Pending");
            var row = Spawn(lib != null ? lib.progressionBossStage : null, "BossStageRow");
            if (row != null)
                row.Set($"{Loc.Get("Progression.BossStage", (i + 1).ToString())} · {st}",
                        stage != null && stage.bestCombat.has && stage.bestCombat.valid ? stage.bestCombat.halfStars : -1);
        }
    }

    private void OpenDetail(PositionRecordData p, int levelIndex, int match)
    {
        var panel = _view.detailPanel;
        if (panel == null) return;
        panel.Clear();
        panel.AddHeader(Loc.Get("Campaign.Position", (levelIndex + 1).ToString(), match.ToString()));
        if (p == null || (!CampaignModel.HasRunner(p) && !CampaignModel.HasCombat(p)))
            panel.AddLine(p != null && p.legacyUnrated ? Loc.Get("Progression.UnratedHint") : Loc.Get("Progression.PendingHint"));
        if (p != null)
        {
            if (CampaignModel.HasRunner(p))
            {
                var r = p.bestRunner;
                panel.AddStars($"{Loc.Get("Eval.BestRunner")} — {SongName(r.songId)} ({Loc.Get("Progression.Lap", r.lap.ToString())})", r.halfStars);
                panel.AddNote(Loc.Get("Eval.Points", EvaluationPanel.Points(r.score, r.maxScore)) + "  ·  " + EvaluationPanel.Breakdown(r.metrics));
            }
            else panel.AddStars(Loc.Get("Eval.BestRunner"), -1);
            if (CampaignModel.HasCombat(p))
            {
                var c = p.bestCombat;
                var rival = Rival(c.opponentId);
                panel.AddStars($"{Loc.Get("Eval.BestCombat")} — {(rival != null ? rival.displayName : c.opponentId)} · {Loc.Get("Rivals.Level", c.opponentLevel.ToString())} ({Loc.Get("Progression.Lap", c.lap.ToString())})", c.halfStars);
                panel.AddNote(Loc.Get("Eval.Points", EvaluationPanel.Points(c.score, c.maxScore)) + "  ·  " + EvaluationPanel.Breakdown(c.metrics));
            }
            else panel.AddStars(Loc.Get("Eval.BestCombat"), -1);
            panel.AddStars(Loc.Get("Eval.Total"), CampaignModel.CombinedHalfStars(p));
            if (p.lastRunner != null && p.lastRunner.has) panel.AddStarsSmall(Loc.Get("Progression.LastRunner"), p.lastRunner.halfStars);
            if (p.lastCombat != null && p.lastCombat.has)
                panel.AddStarsSmall(Loc.Get("Progression.LastCombat") + (p.lastCombat.valid ? "" : "  " + Loc.Get("Progression.Lost")), p.lastCombat.halfStars);
            panel.AddHighlight(ImprovementHint(p));
        }
        _view.detailRoot.gameObject.SetActive(true);
        _view.detailRoot.SetAsLastSibling();
    }

    /// <summary>What is missing to master the position, concretely.</summary>
    private static string ImprovementHint(PositionRecordData p)
    {
        if (CampaignModel.IsMastered(p)) return Loc.Get("Progression.HintMastered");
        var hints = new List<string>();
        if (!CampaignModel.HasRunner(p)) hints.Add(Loc.Get("Progression.HintRunnerMissing"));
        else if (p.bestRunner.halfStars < StarMath.Max) hints.Add(Loc.Get("Progression.HintRunner", Mathf.RoundToInt(CampaignService.Evaluation.runnerFiveStarThreshold * 100f).ToString()));
        if (!CampaignModel.HasCombat(p)) hints.Add(Loc.Get("Progression.HintCombatMissing"));
        else if (p.bestCombat.halfStars < StarMath.Max) hints.Add(Loc.Get("Progression.HintCombat"));
        hints.Add(Loc.Get("Progression.HintNewLap"));
        return string.Join("\n", hints);
    }

    private void OnChallengeClicked()
    {
        if (!CampaignService.PrepareBossStage(CampaignService.NextBossStage)) return;
        Close();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Fight);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private T Spawn<T>(T prefab, string what) where T : Component
    {
        if (_view == null || _view.listContent == null) return null;
        var row = UIPrefabLibrarySO.Spawn(prefab, _view.listContent, what);
        if (row != null) _spawned.Add(row.gameObject);
        return row;
    }

    private static string BossState(CampaignProgressData data) =>
        data.boss.finalDefeated ? Loc.Get("Progression.BossDefeated") : data.bossUnlocked ? Loc.Get("Progression.BossAvailable") : Loc.Get("Progression.BossLocked");

    private static void SetText(TMPro.TMP_Text t, string s) { if (t != null) t.text = s; }

    private static int Avg(float exactHalfStars) => float.IsNaN(exactHalfStars) ? -1 : StarMath.AverageDown(exactHalfStars);

    private static string SongName(string songId)
    {
        if (string.IsNullOrEmpty(songId)) return "—";
        string address = SongCatalog.AddressForId(songId);
        return string.IsNullOrEmpty(address) ? songId : System.IO.Path.GetFileNameWithoutExtension(address);
    }

    private static OpponentDefinition Rival(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var roster = CampaignService.Roster;
        var found = roster != null ? roster.opponents.FirstOrDefault(o => o != null && o.id == id) : null;
        if (found == null && CampaignService.Boss != null && CampaignService.Boss.opponent != null && CampaignService.Boss.opponent.id == id) found = CampaignService.Boss.opponent;
        return found;
    }

    private static Sprite Portrait(OpponentDefinition o, int version)
    {
        var cfg = o.levels?.FirstOrDefault(l => l != null && l.level == version);
        return cfg != null && cfg.portrait != null ? cfg.portrait : o.levels?.FirstOrDefault(l => l != null && l.portrait != null)?.portrait;
    }
}
