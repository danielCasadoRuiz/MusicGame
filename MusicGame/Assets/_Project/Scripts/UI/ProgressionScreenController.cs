using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "PROGRESSIÓ" (Main Menu, under COL·LECCIÓ): the campaign by level, each level collapsible into its
/// matches, with the persistent BEST runner / combat records of every position (each with the song /
/// rival it came from), level averages, mastery counters, the final-rival requirements and challenge,
/// and the seal. Provisional procedural UI (same pattern as RivalCollectionController); read-only
/// except for the final-rival challenge button.
///
/// Averages: half-star means over the RATED positions of the level, shown with their base
/// ("4,5 ★ · 3/5") and floored to a half step for the stars; pending positions stay visible as "pending",
/// and the mastered counter checks every position individually.
/// </summary>
public class ProgressionScreenController : MonoBehaviour
{
    public static ProgressionScreenController Instance { get; private set; }

    private RectTransform _root, _content, _detail;
    private TextMeshProUGUI _summaryText, _requirementsText, _sealText;
    private Button _challengeButton;
    private TextMeshProUGUI _challengeLabel;
    private EvaluationPanel _detailPanel;
    private readonly List<GameObject> _spawned = new();
    private readonly HashSet<string> _expanded = new();
    private System.Action<GameFlowStateChangedEvent> _onFlow;
    private System.Action _onLanguage;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Build();
        _root.gameObject.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.MainMenu) Close(); };
        _onLanguage = () => { if (_root != null && _root.gameObject.activeSelf) Refresh(); };
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
        var data = CampaignService.Data;
        if (_expanded.Count == 0 && data != null && CampaignService.Config != null)
        {
            var current = CampaignService.CurrentLevel; // open the level being played
            if (current != null) _expanded.Add(current.id);
        }
        Refresh();
        _detail.gameObject.SetActive(false);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    public void Close() { if (_root != null) _root.gameObject.SetActive(false); }

    /// <summary>Opens a level's match list (also used by tests).</summary>
    public void Expand(string levelId) { if (!string.IsNullOrEmpty(levelId)) _expanded.Add(levelId); }

    // ── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("ProgressionScreen", canvas);
        UIFactory.Stretch(_root);
        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = Loc.Track(UIFactory.CreateText("Title", _root, Loc.Get("Progression.Title"), 34, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Progression.Title");
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(1000f, 46f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        _summaryText = UIFactory.CreateText("Summary", _root, "", 18, Color.white);
        UIFactory.SetBox(_summaryText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1500f, 28f));
        _requirementsText = UIFactory.CreateText("Requirements", _root, "", 17, new Color(1f, 0.85f, 0.45f));
        UIFactory.SetBox(_requirementsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(1500f, 28f));
        _sealText = UIFactory.CreateText("Seal", _root, "", 20, new Color(1f, 0.9f, 0.4f), TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_sealText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(1500f, 30f));

        var scroll = UIFactory.CreateScrollRect("Levels", _root, out _content, 6f);
        var srt = scroll.GetComponent<RectTransform>();
        UIFactory.SetBox(srt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1500f, -330f));

        _challengeButton = UIFactory.CreateButton("ChallengeButton", _root, "", out _challengeLabel);
        UIFactory.SetBox(_challengeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 36f), new Vector2(420f, 54f));
        _challengeButton.onClick.AddListener(OnChallengeClicked);
        _challengeButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        _challengeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        var back = UIFactory.CreateButton("BackButton", _root, Loc.Get("Rivals.Back"), out var backLabel); Loc.Track(backLabel, "Rivals.Back");
        UIFactory.SetBox(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(170f, 36f), new Vector2(240f, 54f));
        back.onClick.AddListener(Close);
        back.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        backLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Detail overlay (one position).
        _detail = UIFactory.CreateRect("Detail", _root);
        UIFactory.Stretch(_detail);
        var ddim = UIFactory.CreatePanel("DetailDim", _detail, new Color(0f, 0f, 0f, 0.82f));
        UIFactory.Stretch(ddim.rectTransform);
        ddim.gameObject.AddComponent<Button>().onClick.AddListener(() => _detail.gameObject.SetActive(false));
        _detailPanel = EvaluationPanel.Create(_detail, "DetailPanel", 760f);
        var dr = _detailPanel.Rect;
        dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
        dr.pivot = new Vector2(0.5f, 0.5f);
        _detail.gameObject.SetActive(false);
    }

    // ── Content ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        foreach (var go in _spawned) if (go != null) Destroy(go);
        _spawned.Clear();
        var data = CampaignService.Data;
        var config = CampaignService.Config;
        if (data == null || config == null)
        {
            Loc.Set(_summaryText, "Progression.Unavailable");
            _requirementsText.text = ""; _sealText.text = "";
            _challengeButton.gameObject.SetActive(false);
            return;
        }

        string where = CampaignModel.IsLapFinished(data, config)
            ? Loc.Get("Progression.LapFinished")
            : Loc.Get("Campaign.Position", (data.levelIndex + 1).ToString(), data.matchNumber.ToString());
        var cycle = PlayerProgressService.Instance?.SongCycle;
        _summaryText.text = Loc.Get("Progression.Summary", data.lap.ToString(), where, (cycle != null ? cycle.cycle : 1).ToString());

        // Final rival requirements (always visible once revealed; a short hint before).
        var req = CampaignService.BossRequirements();
        string state = data.boss.finalDefeated ? Loc.Get("Progression.BossDefeated")
                     : data.bossUnlocked ? Loc.Get("Progression.BossAvailable") : Loc.Get("Progression.BossLocked");
        _requirementsText.text = data.bossRevealed
            ? Loc.Get("Progression.Requirements", req.Mastered.ToString(), req.TotalPositions.ToString(), req.Cards.ToString(), req.CardsRequired.ToString(), state)
            : Loc.Get("Progression.MasteredOnly", req.Mastered.ToString(), req.TotalPositions.ToString());
        _sealText.text = data.boss.sealGranted ? "— " + Loc.Get("Campaign.SealName") + " —" : "";

        bool canChallenge = CampaignService.CanChallengeBoss;
        _challengeButton.gameObject.SetActive(canChallenge);
        if (canChallenge)
            _challengeLabel.text = Loc.Get("Progression.Challenge", (CampaignService.NextBossStage + 1).ToString(), CampaignService.Boss.StageCount.ToString());

        for (int i = 0; i < config.LevelCount; i++) BuildLevel(data, config, i);
        if (data.bossRevealed && CampaignService.Boss != null) BuildBossSection(data);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
    }

    private void BuildLevel(CampaignProgressData data, CampaignConfigSO config, int index)
    {
        var level = config.Level(index);
        var s = CampaignModel.Summarize(data, config, index);
        bool open = _expanded.Contains(level.id);

        var header = UIFactory.CreateButton("Level_" + level.id, _content, "", out var label);
        header.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        var hrt = header.GetComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(0f, 74f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = 18;
        label.enableWordWrapping = true;
        UIFactory.SetBox(label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(560f, 0f));
        string name = string.IsNullOrEmpty(level.nameKey) ? Loc.Get("Campaign.Level", (index + 1).ToString()) : Loc.Get(level.nameKey);
        label.text = $"{(open ? "▼" : "►")} <b>{name}</b>\n<size=80%>{Loc.Get("Progression.LevelLine", s.CompletedThisLap.ToString(), s.MatchCount.ToString(), s.Mastered.ToString())}</size>";
        AddStarsAt(hrt, 600f, Loc.Get("Progression.Runner"), Avg(s.AvgRunner), $"{s.RatedRunner}/{s.MatchCount}");
        AddStarsAt(hrt, 900f, Loc.Get("Progression.Combat"), Avg(s.AvgCombat), $"{s.RatedCombat}/{s.MatchCount}");
        AddStarsAt(hrt, 1200f, Loc.Get("Progression.Total"), Avg(s.AvgTotal), $"{s.RatedTotal}/{s.MatchCount}");
        string id = level.id;
        header.onClick.AddListener(() => { if (!_expanded.Remove(id)) _expanded.Add(id); Refresh(); });
        _spawned.Add(header.gameObject);

        if (!open) return;
        // Table column titles for this level's matches.
        var cols = UIFactory.CreatePanel("Columns", _content, new Color(1f, 1f, 1f, 0.03f));
        var crt = cols.rectTransform;
        crt.sizeDelta = new Vector2(0f, 30f);
        var dimText = new Color(1f, 1f, 1f, 0.6f);
        Text(crt, Loc.Get("Progression.ColMatch"), 40f, 170f, 13, 0f).color = dimText;
        Text(crt, Loc.Get("Progression.ColRunner"), 220f, 330f, 13, 0f).color = dimText;
        Text(crt, Loc.Get("Progression.ColCombat"), 580f, 400f, 13, 0f).color = dimText;
        Text(crt, Loc.Get("Progression.Total"), 1060f, 160f, 13, 0f).color = dimText;
        _spawned.Add(cols.gameObject);
        for (int m = 1; m <= level.matchCount; m++)
            BuildRow(CampaignModel.Find(data, CampaignConfigSO.PositionId(level.id, m)), index, m,
                     data.levelIndex == index && data.matchNumber == m && !CampaignModel.IsLapFinished(data, config));
    }

    private void BuildRow(PositionRecordData p, int levelIndex, int match, bool current)
    {
        var row = UIFactory.CreateButton("Match", _content, "", out var label);
        row.GetComponent<Image>().color = current ? new Color(1f, 0.82f, 0.35f, 0.16f)
                                        : match % 2 == 0 ? new Color(1f, 1f, 1f, 0.07f) : new Color(1f, 1f, 1f, 0.03f); // zebra rows
        var rt = row.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 84f);
        label.text = "";

        // Match number + status
        string status = p == null ? Loc.Get("Eval.Pending")
                      : CampaignModel.IsMastered(p) ? Loc.Get("Progression.Mastered")
                      : p.completed ? (p.legacyUnrated && !CampaignModel.HasRunner(p) && !CampaignModel.HasCombat(p) ? Loc.Get("Progression.Unrated") : Loc.Get("Progression.Completed"))
                      : Loc.Get("Eval.Pending");
        Text(rt, $"<b>{Loc.Get("Progression.Match", match.ToString())}</b>\n<size=80%>{status}</size>", 40f, 170f, 17);

        // Runner best: song of THAT record
        string song = p != null && CampaignModel.HasRunner(p) ? SongName(p.bestRunner.songId) : "—";
        Text(rt, $"{Loc.Get("Progression.Runner")}: {song}", 220f, 330f, 15);
        Stars(rt, 220f, -22f, p != null && CampaignModel.HasRunner(p) ? p.bestRunner.halfStars : -1);

        // Combat best: rival + version of THAT record
        var rival = p != null && CampaignModel.HasCombat(p) ? Rival(p.bestCombat.opponentId) : null;
        int version = p != null && CampaignModel.HasCombat(p) ? p.bestCombat.opponentLevel : 0;
        var portraitRt = UIFactory.CreateRect("Portrait", rt);
        UIFactory.SetBox(portraitRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(580f, 0f), new Vector2(64f, 76f));
        var img = portraitRt.gameObject.AddComponent<Image>();
        img.sprite = rival != null ? Portrait(rival, version) : null;
        img.preserveAspect = true;
        img.color = img.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);
        img.raycastTarget = false;
        string rivalText = rival != null ? $"{rival.displayName} · {Loc.Get("Rivals.Level", version.ToString())}" : "—";
        Text(rt, $"{Loc.Get("Progression.Combat")}: {rivalText}", 656f, 330f, 15);
        Stars(rt, 656f, -22f, p != null && CampaignModel.HasCombat(p) ? p.bestCombat.halfStars : -1);

        // Total
        Text(rt, Loc.Get("Progression.Total"), 1060f, 120f, 15);
        Stars(rt, 1060f, -22f, p != null ? CampaignModel.CombinedHalfStars(p) : -1);

        var pos = p;
        row.onClick.AddListener(() => OpenDetail(pos, levelIndex, match));
        _spawned.Add(row.gameObject);
    }

    private void BuildBossSection(CampaignProgressData data)
    {
        var boss = CampaignService.Boss;
        var header = UIFactory.CreatePanel("BossHeader", _content, new Color(0.35f, 0.25f, 0.05f, 0.35f));
        var hrt = header.rectTransform;
        hrt.sizeDelta = new Vector2(0f, 56f);
        Text(hrt, $"<b>{Loc.Get(boss.nameKey)}</b>  ·  {(data.boss.finalDefeated ? Loc.Get("Progression.BossDefeated") : data.bossUnlocked ? Loc.Get("Progression.BossAvailable") : Loc.Get("Progression.BossLocked"))}", 16f, 1400f, 18);
        _spawned.Add(header.gameObject);
        for (int i = 0; i < boss.StageCount; i++)
        {
            var stage = CampaignService.BossStage(i);
            var row = UIFactory.CreatePanel("BossStage", _content, new Color(1f, 1f, 1f, 0.05f));
            var rt = row.rectTransform;
            rt.sizeDelta = new Vector2(0f, 60f);
            string st = stage != null && stage.defeated ? Loc.Get("Progression.Completed") : Loc.Get("Eval.Pending");
            Text(rt, $"{Loc.Get("Progression.BossStage", (i + 1).ToString())} · {st}", 40f, 600f, 16);
            Stars(rt, 656f, 0f, stage != null && stage.bestCombat.has && stage.bestCombat.valid ? stage.bestCombat.halfStars : -1);
            _spawned.Add(row.gameObject);
        }
    }

    private void OpenDetail(PositionRecordData p, int levelIndex, int match)
    {
        var panel = _detailPanel;
        panel.Clear();
        panel.AddHeader(Loc.Get("Campaign.Position", (levelIndex + 1).ToString(), match.ToString()));
        if (p == null || (!CampaignModel.HasRunner(p) && !CampaignModel.HasCombat(p)))
        {
            panel.AddLine(p != null && p.legacyUnrated ? Loc.Get("Progression.UnratedHint") : Loc.Get("Progression.PendingHint"));
        }
        if (p != null)
        {
            if (CampaignModel.HasRunner(p))
            {
                var r = p.bestRunner;
                panel.AddStars($"{Loc.Get("Eval.BestRunner")} — {SongName(r.songId)} ({Loc.Get("Progression.Lap", r.lap.ToString())})", r.halfStars);
                panel.AddLine(Loc.Get("Eval.Points", EvaluationPanel.Points(r.score, r.maxScore)) + "  ·  " + EvaluationPanel.Breakdown(r.metrics), 13, new Color(0.8f, 0.8f, 0.8f));
            }
            else panel.AddStars(Loc.Get("Eval.BestRunner"), -1);
            if (CampaignModel.HasCombat(p))
            {
                var c = p.bestCombat;
                var rival = Rival(c.opponentId);
                panel.AddStars($"{Loc.Get("Eval.BestCombat")} — {(rival != null ? rival.displayName : c.opponentId)} · {Loc.Get("Rivals.Level", c.opponentLevel.ToString())} ({Loc.Get("Progression.Lap", c.lap.ToString())})", c.halfStars);
                panel.AddLine(Loc.Get("Eval.Points", EvaluationPanel.Points(c.score, c.maxScore)) + "  ·  " + EvaluationPanel.Breakdown(c.metrics), 13, new Color(0.8f, 0.8f, 0.8f));
            }
            else panel.AddStars(Loc.Get("Eval.BestCombat"), -1);
            panel.AddStars(Loc.Get("Eval.Total"), CampaignModel.CombinedHalfStars(p));

            if (p.lastRunner != null && p.lastRunner.has)
                panel.AddStars(Loc.Get("Progression.LastRunner"), p.lastRunner.halfStars, 14);
            if (p.lastCombat != null && p.lastCombat.has)
                panel.AddStars(Loc.Get("Progression.LastCombat") + (p.lastCombat.valid ? "" : "  " + Loc.Get("Progression.Lost")), p.lastCombat.halfStars, 14);

            panel.AddLine(ImprovementHint(p), 15, new Color(1f, 0.82f, 0.35f));
        }
        _detail.gameObject.SetActive(true);
        _detail.SetAsLastSibling();
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

    // ── Final rival challenge ────────────────────────────────────────────────

    private void OnChallengeClicked()
    {
        if (!CampaignService.PrepareBossStage(CampaignService.NextBossStage)) return;
        Close();
        AppBootstrap.Context?.AppFlow.RequestState(GameFlowState.Fight);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int Avg(float exactHalfStars) => float.IsNaN(exactHalfStars) ? -1 : StarMath.AverageDown(exactHalfStars);

    private void AddStarsAt(RectTransform parent, float x, string label, int halfStars, string basis)
    {
        Text(parent, $"{label}  <size=80%>{EvaluationPanel.Stars(halfStars)} · {basis}</size>", x, 290f, 14, 14f);
        Stars(parent, x, -16f, halfStars);
    }

    private static TextMeshProUGUI Text(RectTransform parent, string text, float x, float width, int size, float y = 14f)
    {
        var t = UIFactory.CreateText("Text", parent, text, size, Color.white, TextAlignmentOptions.MidlineLeft);
        t.raycastTarget = false;
        t.enableWordWrapping = true;
        UIFactory.SetBox(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, y), new Vector2(width, 40f));
        return t;
    }

    private static void Stars(RectTransform parent, float x, float y, int halfStars)
    {
        var v = StarRatingView.Create(parent, 22f, 3f);
        var r = (RectTransform)v.transform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        v.SetHalfStars(halfStars);
    }

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
