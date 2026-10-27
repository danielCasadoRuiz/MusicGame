using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Runner → fight: the bonuses collected in the race visibly turn into the fight preparation — ONE bonus block
/// (title, budget, tray of collected bonuses with their counts) that the player follows from the race results to
/// the fight:
///
///   1. Runner results (ShowInResults): the block is docked in the results panel's slot (EndScreenView.bonusSlot),
///      scaled to fit; it is the results' only list of collected bonuses.
///   2. Continue (GameFlowState → Fight): the same block glides (position + scale, eased, unscaled time) to the
///      bottom of the screen and stays there, untouched, through the roulette and the Versus card. Its own canvas
///      keeps it above the scene change, the roulette and the card, so it never disappears in between.
///   3. The rival's opening line starts (VersusScreenController → BeginInPresentation): the conversion plays while
///      the rival talks — the fight HUD (the destinations) is revealed and each bonus leaves towards what it really
///      modifies (BonusConversionPlan: a musical bonus to each build stat it feeds, Special / x3 / x4 to their
///      counters, Life to the extra-life counter; Peak, score only, stays), along a short accelerating curve,
///      shrinking as it is absorbed; the destination bar grows to its new value as the icon enters it and pulses.
///      Sources leave one after another, at most HelpUIConfigSO.maxFlightsInAir icons fly at once, one icon per
///      (bonus, destination), never one per unit; the tray counters count down as their icons leave.
///   4. Then the block fades out; the HUD keeps the final values. The Versus advances to the countdown only when
///      BOTH the line and this conversion are over (IsBusy).
/// No results before the fight (Fight entered directly) → the block simply fades in at the bottom when the
/// conversion starts. RunIfPending (round intro) is the last fallback for a fight whose presentation never ran it.
///
/// Display only: the build and the wallet were final before any of this (GameSession at run end); the plan is
/// built ONCE per run from those results and the same plan is docked, carried and converted — nothing here grants
/// or recomputes anything, so playing it, skipping it (Versus skip / ESC, Space / Enter, gamepad South, or the
/// skip button in the fallback) or not playing it at all ends with exactly the same values (Finish). Played once
/// per run (RunSession): Fight Again and later rounds show the final panel directly.
/// Lives in the UI scene (UIFlowController); BonusConversion.prefab (UIPrefabLibrarySO.bonusConversion).
/// </summary>
public class BonusConversionController : MonoBehaviour
{
    public enum BlockState { Hidden, Docked, Travelling, Waiting, Converting }

    private const int BlockSortingOrder = 140;

    public static BonusConversionController Instance { get; private set; }

    /// <summary>True while the conversion owns the build panel (FightController leaves it alone meanwhile) — from
    /// the moment it starts until the block has gone and the final values are set.</summary>
    public static bool IsBusy => Instance != null && Instance._busy;

    /// <summary>Where the bonus block is (diagnostics / tests).</summary>
    public BlockState State => _state;
    public RectTransform Block => _view != null ? _view.block : null;
    public float BlockAlpha => _view != null && _view.blockGroup != null ? _view.blockGroup.alpha : 0f;
    /// <summary>The plan the block's tray currently shows (null when hidden).</summary>
    public BonusConversionPlan BlockPlan => _trayPlan;
    public RectTransform Tray => _view != null ? _view.tray : null;

    /// <summary>Diagnostics / tests: the plan last shown, and whether it ended by skipping.</summary>
    public BonusConversionPlan LastPlan { get; private set; }
    public bool LastWasSkipped { get; private set; }
    public int FlightsLaunched { get; private set; }
    public int MaxFlightsSeenInAir { get; private set; }

    private BonusConversionView _view;
    private BlockState _state;
    private bool _busy, _skip;
    private RunSession _shownFor;
    private RectTransform _slot;
    private Vector2 _bottomPosition;
    private Coroutine _blockRoutine;
    private BonusConversionPlan _plan;
    private RunSession _planRun;
    private RunnerResults _planResults;
    private BonusConversionPlan _trayPlan;   // the plan the tray's chips were built from
    private readonly List<Flyer> _flyers = new();
    private readonly Dictionary<FightStatId, float> _shownPoints = new();
    private readonly Dictionary<BonusKind, int> _shownResources = new();
    private readonly Dictionary<BonusKind, ConversionChipView> _chips = new();
    private readonly Dictionary<BonusKind, int> _chipFlightsLeft = new();
    private readonly Dictionary<BonusKind, int> _chipFlightsTotal = new();
    private readonly List<Rect> _keepOut = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private System.Action<GameFlowStateChangedEvent> _onGameFlowChanged;

    private class Flyer { public Image image; public Coroutine routine; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.bonusConversion : null, UIFactory.RootCanvas(), "BonusConversion");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        if (_view.block == null)
            Debug.LogError("[BonusConversionController] BonusConversion.prefab: block not assigned — run Tools > MusicGame > UI > Help Placeholders + Prefabs.");
        else _bottomPosition = _view.block.anchoredPosition;
        _view.skipButton.onClick.AddListener(Skip);
        _view.root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_view != null) Destroy(_view.gameObject);
    }

    private void OnEnable()
    {
        // Continue from the results (→ Fight): the docked block travels down. Any other way out of the results
        // (main menu, a new song…): it goes. Leaving Fight is FightController.ExitFight → Abort.
        _onGameFlowChanged = e =>
        {
            if (_busy || _state == BlockState.Hidden) return;
            if (e.Current == GameFlowState.Fight) { if (_state == BlockState.Docked) Travel(); }
            else HideBlock();
        };
        EventBus.Subscribe(_onGameFlowChanged);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onGameFlowChanged);

    public void Skip() { if (_busy) _skip = true; }

    // ── The plan: built ONCE per run from its final results ──────────────────

    private BonusConversionPlan PlanFor(GameSession session)
    {
        if (_plan != null && _planRun == session.Run && _planResults == session.RunnerResults) return _plan;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        int lives = PlayerProgressService.Instance != null ? PlayerProgressService.Instance.ExtraLives : 0;
        _plan = BonusConversionPlan.Build(session.RunnerResults, app != null ? app.fightStats : null, session.Run, lives);
        _planRun = session.Run;
        _planResults = session.RunnerResults;
        return _plan;
    }

    // ── 1. Runner results ────────────────────────────────────────────────────

    /// <summary>GameplayHUD, once this run's results are ready: the block docked in the results panel's slot.
    /// Nothing collected → no block (nothing will travel or convert).</summary>
    public void ShowInResults(RectTransform slot)
    {
        var session = GameSession.Instance;
        if (_view == null || _view.block == null || slot == null || session == null || session.RunnerResults == null || _busy) return;
        var plan = PlanFor(session);
        if (plan.Sources.Count == 0) { HideBlock(); return; }
        _slot = slot;
        if (_state == BlockState.Docked && _trayPlan == plan) { FollowSlot(); return; }   // results re-filled: same block
        StopBlockRoutine();
        PrepareBlock(plan);
        _state = BlockState.Docked;
        FollowSlot();
        _blockRoutine = StartCoroutine(FadeBlock(0f, 1f, Cfg != null ? Cfg.trayInSeconds : 0.35f));
    }

    /// <summary>The results are gone without going to the fight (Restart): the block goes with them.</summary>
    public void HideFromResults()
    {
        if (!_busy && (_state == BlockState.Docked || _state == BlockState.Travelling)) HideBlock();
    }

    private void LateUpdate()
    {
        if (_state != BlockState.Docked) return;
        if (_slot == null || !_slot.gameObject.activeInHierarchy) { HideBlock(); return; }
        FollowSlot();
    }

    // Centred in the slot, scaled down to fit it (never up). Overlay canvases: world == screen pixels.
    private void FollowSlot()
    {
        var block = _view.block;
        _slot.GetWorldCorners(_corners);
        Vector2 slotSize = _corners[2] - _corners[0];
        float unit = block.parent.lossyScale.x;
        Vector2 natural = block.rect.size * unit;
        if (natural.x <= 0f || natural.y <= 0f) return;
        float s = Mathf.Min(1f, slotSize.x / natural.x, slotSize.y / natural.y);
        block.localScale = Vector3.one * s;
        Vector2 centre = (_corners[0] + _corners[2]) * 0.5f;
        block.position = new Vector3(centre.x, centre.y - natural.y * s * 0.5f, block.position.z);
    }

    // ── 2. Results → bottom of the fight presentation ────────────────────────

    private void Travel()
    {
        StopBlockRoutine();
        _slot = null;
        _state = BlockState.Travelling;
        _blockRoutine = StartCoroutine(TravelRoutine());
    }

    private IEnumerator TravelRoutine()
    {
        var block = _view.block;
        if (_view.blockGroup != null) _view.blockGroup.alpha = 1f;
        Vector2 from = block.anchoredPosition;
        float fromScale = block.localScale.x;
        float duration = Cfg != null ? Cfg.blockTravelSeconds : 0.8f;
        // Unscaled and step-capped: the results are time-frozen and the scene swap can hitch — the move never jumps.
        for (float t = 0f; t < duration; t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f))
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            block.anchoredPosition = Vector2.LerpUnclamped(from, _bottomPosition, k);
            block.localScale = Vector3.one * Mathf.Lerp(fromScale, 1f, k);
            yield return null;
        }
        block.anchoredPosition = _bottomPosition;
        block.localScale = Vector3.one;
        _blockRoutine = null;
        if (_state == BlockState.Travelling) _state = BlockState.Waiting;
    }

    // ── 3. Conversion during the rival's opening line ────────────────────────

    /// <summary>VersusScreenController, when the rival's opening line starts (or the Versus ends without one): plays
    /// the conversion if it is still pending for this run; otherwise the block (if any) just goes. IsBusy stays true
    /// until it is over — the Versus waits for it before the countdown.</summary>
    public void BeginInPresentation()
    {
        if (_busy || _view == null) return;
        var session = GameSession.Instance;
        var panel = FightController.Instance != null ? FightController.Instance.BuildPanel : null;
        if (session == null || panel == null || session.RunnerResults == null || session.Run == _shownFor) { FadeAway(); return; }
        _shownFor = session.Run;
        var plan = PlanFor(session);
        LastPlan = plan; LastWasSkipped = false; FlightsLaunched = 0; MaxFlightsSeenInAir = 0;
        if (plan.IsEmpty) { FadeAway(); return; }   // nothing converts (e.g. only Peak): no empty animation, no wait

        _busy = true; _skip = false;
        ApplyStart(plan, panel);
        FightController.Instance.ShowHudForConversion();   // the destinations, already at their start values
        StartCoroutine(Run(plan, panel, inPresentation: true));
    }

    /// <summary>RoundIntroController fallback: plays the conversion at the first round intro if the presentation
    /// never did (otherwise returns at once). Either way the build panel ends on the real final values.</summary>
    public IEnumerator RunIfPending(int round)
    {
        var session = GameSession.Instance;
        var panel = FightController.Instance != null ? FightController.Instance.BuildPanel : null;
        if (_view == null || _busy || session == null || panel == null || round != 1 || session.Run == _shownFor || session.RunnerResults == null) yield break;
        _shownFor = session.Run;
        var plan = PlanFor(session);
        LastPlan = plan; LastWasSkipped = false; FlightsLaunched = 0; MaxFlightsSeenInAir = 0;
        if (plan.IsEmpty) { FadeAway(); yield break; }

        _busy = true; _skip = false;
        ApplyStart(plan, panel);
        yield return Run(plan, panel, inPresentation: false);
    }

    private IEnumerator Run(BonusConversionPlan plan, FightBuildPanelView panel, bool inPresentation)
    {
        while (_state == BlockState.Travelling && !_skip) yield return null;   // the block lands first
        if (!_skip) yield return Play(plan, panel, inPresentation);
        Finish(plan, panel);
    }

    // Start state: bars at what no flying icon accounts for (normally 0), counters before this run's gains.
    private void ApplyStart(BonusConversionPlan plan, FightBuildPanelView panel)
    {
        foreach (var stat in BonusHelpCatalog.BuildStats) SetStat(panel, stat, plan.StartPoints(stat));
        foreach (var r in plan.ResourceFlights) SetResource(panel, r.Source, r.From);
    }

    private IEnumerator Play(BonusConversionPlan plan, FightBuildPanelView panel, bool inPresentation)
    {
        var cfg = Cfg;
        float trayIn = cfg != null ? cfg.trayInSeconds : 0.35f;
        StopBlockRoutine();
        bool alreadyShown = _state != BlockState.Hidden && _trayPlan == plan;
        if (!alreadyShown)
        {
            // No results before this fight: the block appears at its place in the presentation.
            PrepareBlock(plan);
            _view.block.anchoredPosition = _bottomPosition;
            _view.block.localScale = Vector3.one;
        }
        _state = BlockState.Converting;
        // In the presentation the Versus skip (button / ESC) covers it; the fallback keeps its own button.
        _view.skipButton.gameObject.SetActive(!inPresentation);
        if (!alreadyShown)
            for (float t = 0f; t < trayIn && !_skip; t += Time.deltaTime) { SetBlockAlpha(Mathf.SmoothStep(0f, 1f, t / trayIn)); yield return null; }
        SetBlockAlpha(1f);
        Canvas.ForceUpdateCanvases();   // chip positions (layout) before the first departure

        float stagger = cfg != null ? cfg.sourceStagger : 0.22f;
        int maxInAir = cfg != null ? cfg.maxFlightsInAir : 4;
        foreach (var chip in plan.Sources)
        {
            if (_skip) yield break;
            if (!chip.HasDestination) continue;
            // A bonus that feeds several stats (Onset → 4, Impact → 2) SPLITS: its smaller pieces leave together,
            // one per destination — one bonus, several bars, never shown as several pickups.
            int pieces = plan.Destinations(chip.Kind);
            if (pieces > 0)
            {
                yield return WaitForRoom(Mathf.Max(1, maxInAir - pieces + 1));
                if (_skip) yield break;
                float size = pieces > 1 ? (cfg != null ? cfg.splitPieceScale : 0.6f) : 1f;
                foreach (var f in plan.StatFlights)
                    if (f.Source == chip.Kind) LaunchStat(f, panel, cfg, size);
            }
            foreach (var r in plan.ResourceFlights)
                if (r.Source == chip.Kind) { yield return WaitForRoom(maxInAir); if (_skip) yield break; LaunchResource(r, panel, cfg); }
            for (float t = 0f; t < stagger && !_skip; t += Time.deltaTime) yield return null;
        }
        while (_flyers.Count > 0 && !_skip) yield return null;
        // The last bar finishes growing (presentation: the HUD keeps showing the final values, no extra hold);
        // the round-intro fallback keeps its short hold before "ROUND 1" covers the screen.
        float hold = inPresentation ? (cfg != null ? Mathf.Max(cfg.barGrowSeconds, cfg.pulseSeconds) : 0.35f) : (cfg != null ? cfg.endHoldSeconds : 0.5f);
        for (float t = 0f; t < hold && !_skip; t += Time.deltaTime) yield return null;
        for (float t = 0f; t < trayIn && !_skip; t += Time.deltaTime) { SetBlockAlpha(1f - t / trayIn); yield return null; }
    }

    private IEnumerator WaitForRoom(int maxInAir)
    {
        while (_flyers.Count >= maxInAir && !_skip) yield return null;
    }

    private void Update()
    {
        if (!_busy || Time.timeScale == 0f) return;   // paused: the pause / help own the input
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) ||
            (pad != null && pad.buttonSouth.wasPressedThisFrame))
            Skip();
    }

    // Final state = the plan's real values, whatever happened before (complete, skipped or interrupted).
    private void Finish(BonusConversionPlan plan, FightBuildPanelView panel)
    {
        LastWasSkipped = _skip;
        ClearFlyers();
        foreach (var stat in BonusHelpCatalog.BuildStats)
            SetStat(panel, stat, plan.FinalPoints.TryGetValue(stat, out var p) ? p : 0f);
        foreach (var r in plan.ResourceFlights) SetResource(panel, r.Source, r.To);
        HideBlock();
        _busy = false;
        _skip = false;
    }

    /// <summary>Leaving Fight mid-animation: drop it (the panel is rebuilt from the real values next time).</summary>
    public void Abort()
    {
        if (!_busy && _state == BlockState.Hidden) return;
        StopAllCoroutines();
        _blockRoutine = null;
        ClearFlyers();
        HideBlock();
        _busy = false;
        _skip = false;
    }

    // ── Block ────────────────────────────────────────────────────────────────

    private static HelpUIConfigSO Cfg => HelpUIConfigSO.Instance;

    private void PrepareBlock(BonusConversionPlan plan)
    {
        BuildTray(plan, Cfg);
        Loc.Set(_view.titleText, "Help.Conv.Title");
        Loc.Set(_view.skipLabel, "Fight.Skip");
        // Summary only: the four attributes added up (nothing is shared out of it).
        Loc.Set(_view.budgetText, "Help.Conv.Total", Mathf.RoundToInt(plan.Budget).ToString());
        _view.skipButton.gameObject.SetActive(false);
        if (_view.group != null) _view.group.alpha = 1f;
        _view.root.gameObject.SetActive(true);
        ApplySorting();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_view.block);   // its real size before it is placed / scaled
    }

    // Its own sorting, applied every time it is shown: a prefab's root Canvas never keeps overrideSorting (it is a
    // root canvas while the prefab is saved) and a nested canvas loses it when activated. Above the Runner end
    // screen, the roulette and the Versus card (order 100), so the block never disappears behind a screen change;
    // under the help (300).
    private void ApplySorting()
    {
        var canvas = _view.GetComponent<Canvas>();
        if (canvas == null) return;
        canvas.overrideSorting = true;
        canvas.sortingOrder = BlockSortingOrder;
    }

    // Unscaled: the results are time-frozen.
    private IEnumerator FadeBlock(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) { SetBlockAlpha(Mathf.Lerp(from, to, t / seconds)); yield return null; }
        SetBlockAlpha(to);
        _blockRoutine = null;
        if (to <= 0f) HideBlock();
    }

    // A block that has nothing to convert simply fades out (no animation, nothing to wait for).
    private void FadeAway()
    {
        if (_state == BlockState.Hidden) return;
        StopBlockRoutine();
        _state = BlockState.Waiting;
        _blockRoutine = StartCoroutine(FadeBlock(_view.blockGroup != null ? _view.blockGroup.alpha : 1f, 0f, Cfg != null ? Cfg.trayInSeconds : 0.35f));
    }

    private void SetBlockAlpha(float a)
    {
        if (_view.blockGroup != null) _view.blockGroup.alpha = a;
        else if (_view.group != null) _view.group.alpha = a;
    }

    private void HideBlock()
    {
        StopBlockRoutine();
        foreach (var c in _chips.Values) if (c != null) Destroy(c.gameObject);
        _chips.Clear();
        _trayPlan = null;
        _slot = null;
        _state = BlockState.Hidden;
        if (_view != null) _view.root.gameObject.SetActive(false);
    }

    private void StopBlockRoutine()
    {
        if (_blockRoutine != null) StopCoroutine(_blockRoutine);
        _blockRoutine = null;
    }

    /// <summary>What the rival's speech bubble must stay clear of, in the UI root canvas's local units: the bonus
    /// block while it is on screen and, during the conversion, the build panel the icons fly to.</summary>
    public static IReadOnlyList<Rect> KeepOutRects()
    {
        var c = Instance;
        if (c == null) return System.Array.Empty<Rect>();
        c._keepOut.Clear();
        if (UIFactory.RootCanvas() == null || c._state == BlockState.Hidden) return c._keepOut;
        float margin = Cfg != null ? Cfg.blockBubbleMargin : 16f;
        if (c._view.block != null && c._view.block.gameObject.activeInHierarchy) c._keepOut.Add(CanvasRect(c._view.block, margin));
        var panel = FightController.Instance != null ? FightController.Instance.BuildPanel : null;
        if (c._busy && panel != null && panel.gameObject.activeInHierarchy) c._keepOut.Add(CanvasRect((RectTransform)panel.transform, margin));
        return c._keepOut;
    }

    private static readonly Vector3[] s_corners = new Vector3[4];

    /// <summary>`rt`'s rectangle in the UI root canvas's local units (centre origin), grown by `margin`.</summary>
    public static Rect CanvasRect(RectTransform rt, float margin)
    {
        var canvas = UIFactory.RootCanvas();
        rt.GetWorldCorners(s_corners);
        Vector2 a = canvas.InverseTransformPoint(s_corners[0]), b = canvas.InverseTransformPoint(s_corners[2]);
        return Rect.MinMaxRect(a.x - margin, a.y - margin, b.x + margin, b.y + margin);
    }

    // ── Tray ─────────────────────────────────────────────────────────────────

    private void BuildTray(BonusConversionPlan plan, HelpUIConfigSO cfg)
    {
        foreach (var c in _chips.Values) if (c != null) Destroy(c.gameObject);
        _chips.Clear(); _chipFlightsLeft.Clear(); _chipFlightsTotal.Clear();
        foreach (var s in plan.Sources)
        {
            var chip = Instantiate(_view.chipPrefab, _view.tray, false);
            chip.gameObject.SetActive(true);
            chip.icon.sprite = cfg != null ? cfg.Icon(s.Kind) : null;
            chip.icon.color = cfg != null ? cfg.Tint(s.Kind) : Color.white;
            chip.countText.text = $"×{s.Count}";
            chip.group.alpha = s.HasDestination ? 1f : 0.4f;   // Peak: score only, nothing to fly to
            _chips[s.Kind] = chip;
            int flights = 0;
            foreach (var f in plan.StatFlights) if (f.Source == s.Kind) flights++;
            foreach (var r in plan.ResourceFlights) if (r.Source == s.Kind) flights++;
            _chipFlightsLeft[s.Kind] = _chipFlightsTotal[s.Kind] = flights;
        }
        _trayPlan = plan;
    }

    // The tray counter counts down as the source's icons leave (proportionally: one icon carries a group of units).
    private void Departed(BonusKind kind, BonusConversionPlan.SourceChip chip)
    {
        if (!_chips.TryGetValue(kind, out var view) || view == null) return;
        _chipFlightsLeft[kind]--;
        int total = Mathf.Max(1, _chipFlightsTotal[kind]);
        view.countText.text = $"×{Mathf.CeilToInt(chip.Count * (float)_chipFlightsLeft[kind] / total)}";
        if (_chipFlightsLeft[kind] <= 0) view.group.alpha = 0.35f;
    }

    private BonusConversionPlan.SourceChip ChipOf(BonusKind kind)
    {
        foreach (var s in LastPlan.Sources) if (s.Kind == kind) return s;
        return default;
    }

    // ── Flights ──────────────────────────────────────────────────────────────

    private void LaunchStat(BonusConversionPlan.Flight f, FightBuildPanelView panel, HelpUIConfigSO cfg, float size = 1f)
    {
        var row = Row(panel, f.Stat);
        if (row == null || !_chips.TryGetValue(f.Source, out var chip)) return;
        Departed(f.Source, ChipOf(f.Source));
        Launch(f.Source, chip.icon.rectTransform, row.icon != null ? row.icon.rectTransform : row.root, cfg, size, () =>
        {
            float from = _shownPoints.TryGetValue(f.Stat, out var p) ? p : 0f;
            StartCoroutine(Grow(panel, f.Stat, from, from + f.Points, cfg));
            Pulse(row.root, cfg);
        });
    }

    private void LaunchResource(BonusConversionPlan.ResourceFlight r, FightBuildPanelView panel, HelpUIConfigSO cfg)
    {
        var cell = Cell(panel, r.Source);
        if (cell == null || !_chips.TryGetValue(r.Source, out var chip)) return;
        Departed(r.Source, ChipOf(r.Source));
        Launch(r.Source, chip.icon.rectTransform, cell.icon != null ? cell.icon.rectTransform : cell.root, cfg, 1f, () =>
        {
            SetResource(panel, r.Source, r.To);
            Pulse(cell.root, cfg);
        });
    }

    private void Launch(BonusKind kind, RectTransform from, RectTransform to, HelpUIConfigSO cfg, float size, System.Action arrive)
    {
        var img = Instantiate(_view.flyerPrefab, _view.flightLayer, false);
        img.gameObject.SetActive(true);
        img.sprite = cfg != null ? cfg.Icon(kind) : null;
        img.color = cfg != null ? cfg.Tint(kind) : Color.white;
        img.rectTransform.sizeDelta = from.rect.size * size;
        var flyer = new Flyer { image = img };
        _flyers.Add(flyer);
        FlightsLaunched++;
        MaxFlightsSeenInAir = Mathf.Max(MaxFlightsSeenInAir, _flyers.Count);
        flyer.routine = StartCoroutine(Fly(flyer, Centre(from), to, cfg, arrive));
    }

    private void ClearFlyers()
    {
        foreach (var f in _flyers) { if (f.routine != null) StopCoroutine(f.routine); if (f.image != null) Destroy(f.image.gameObject); }
        _flyers.Clear();
    }

    // Quadratic curve bowed sideways, eased-in (accelerates into the destination), shrinking as it is absorbed.
    private IEnumerator Fly(Flyer f, Vector3 start, RectTransform target, HelpUIConfigSO cfg, System.Action arrive)
    {
        float duration = cfg != null ? cfg.flightSeconds : 0.6f, arc = cfg != null ? cfg.flightArc : 0.22f;
        float endScale = cfg != null ? cfg.arrivalScale : 0.25f;
        var rt = f.image.rectTransform;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            Vector3 end = Centre(target);
            Vector3 mid = (start + end) * 0.5f;
            Vector3 dir = end - start;
            Vector3 control = mid + new Vector3(-dir.y, dir.x, 0f) * arc;
            float k = Mathf.Clamp01(t / duration);
            float e = k * k * (1.6f - 0.6f * k);              // slow start, fast arrival
            rt.position = Vector3.Lerp(Vector3.Lerp(start, control, e), Vector3.Lerp(control, end, e), e);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, endScale, e);
            yield return null;
        }
        arrive?.Invoke();
        _flyers.Remove(f);
        if (f.image != null) Destroy(f.image.gameObject);
    }

    private static Vector3 Centre(RectTransform rt) => rt.TransformPoint(rt.rect.center);

    private IEnumerator Grow(FightBuildPanelView panel, FightStatId stat, float from, float to, HelpUIConfigSO cfg)
    {
        float duration = cfg != null ? cfg.barGrowSeconds : 0.35f;
        _shownPoints[stat] = to;   // the next icon of the same bar continues from here
        for (float t = 0f; t < duration && _busy && !_skip; t += Time.deltaTime)
        {
            ShowStat(panel, stat, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        if (_busy && !_skip) ShowStat(panel, stat, to);
    }

    private void Pulse(RectTransform rt, HelpUIConfigSO cfg)
    {
        if (rt != null) StartCoroutine(PulseRoutine(rt, cfg != null ? cfg.pulseSeconds : 0.25f, cfg != null ? cfg.pulseScale : 1.25f));
    }

    private static IEnumerator PulseRoutine(RectTransform rt, float seconds, float scale)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.one * Mathf.Lerp(scale, 1f, t / seconds);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // ── Panel ────────────────────────────────────────────────────────────────

    private void SetStat(FightBuildPanelView panel, FightStatId stat, float points)
    {
        _shownPoints[stat] = points;
        ShowStat(panel, stat, points);
    }

    private void SetResource(FightBuildPanelView panel, BonusKind kind, int value)
    {
        _shownResources[kind] = value;
        if (kind == BonusKind.QuadCombo)
            ShowPowerSeconds(Cell(panel, kind), CombatResourceUse.PowerSeconds(value, Resources.Load<AppConfigSO>("AppConfig")?.combatBalance));
        else ShowResource(Cell(panel, kind), value);
    }

    /// <summary>Shared with FightController: the one way a charge cell shows its count — dimmed at 0.</summary>
    public static void ShowResource(FightBuildPanelView.ResourceCell cell, int value)
    {
        if (cell == null) return;
        if (cell.value != null) { cell.value.text = value.ToString(); cell.value.alpha = value > 0 ? 1f : 0.4f; }
        if (cell.icon != null) { var c = cell.icon.color; c.a = value > 0 ? 1f : 0.3f; cell.icon.color = c; }
    }

    public static void ShowPowerSeconds(FightBuildPanelView.ResourceCell cell, float seconds)
    {
        ShowResource(cell, Mathf.CeilToInt(seconds));
        if (cell?.value != null) cell.value.text = $"{Mathf.CeilToInt(seconds)}s";
    }

    /// <summary>Shared with FightController: the one way a stat row shows its points.</summary>
    public static void ShowStat(FightBuildPanelView panel, FightStatId stat, float points)
    {
        var row = Row(panel, stat);
        if (row == null) return;
        var cfg = HelpUIConfigSO.Instance;
        var stats = Resources.Load<AppConfigSO>("AppConfig")?.fightStats;   // a bar is full at the real per-stat cap
        float full = stats != null ? stats.maxStatPoints : cfg != null ? cfg.statBarFullPoints : 40f;
        if (row.fill != null) row.fill.fillAmount = Mathf.Clamp01(points / full);
        if (row.value != null) row.value.text = Mathf.RoundToInt(points).ToString();
    }

    public static FightBuildPanelView.StatRow Row(FightBuildPanelView panel, FightStatId stat)
    {
        if (panel == null) return null;
        foreach (var r in panel.stats) if (r != null && r.stat == stat) return r;
        return null;
    }

    public static FightBuildPanelView.ResourceCell Cell(FightBuildPanelView panel, BonusKind kind)
    {
        if (panel == null) return null;
        foreach (var c in panel.resources) if (c != null && c.kind == kind) return c;
        return null;
    }
}
