#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Localization.Settings;

/// <summary>
/// Play Mode probe of the tip cards, the full help and the runner → fight bonus conversion in the REAL flow
/// (boots Core.unity, rival forced, English): preRun card during the countdown (small, left, off the countdown),
/// "More info" pauses the countdown exactly and closes straight back to it; pause → Bonuses → Escape returns to the
/// pause menu still paused; the card is the same on a restart; synthetic pickups are collected so the run has
/// bonuses; the ONE bonus block: docked in the runner results (no second list of the same bonuses), carried on
/// Continue without disappearing, waiting at the bottom through the roulette / Versus card without covering them,
/// converted while the rival's line plays (HUD visible, bubble clear of the block and the panel), at most
/// maxFlightsInAir icons, ending on the real build / wallet values; the countdown starts once, only after both the
/// line and the conversion; a skipped replay (round-intro fallback) and a Versus skip during the conversion end on
/// exactly the same values without blocking the flow; a repeated call does nothing; preFight moves card, its
/// "More info" and the fight pause → Moves → Escape path.
/// Screenshots (UI layers) in Logs/HelpFlowProbe/. Writes the save (a real run + fight): back it up.
/// Batch (graphics needed): -executeMethod HelpFlowProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class HelpFlowProbe
{
    private const string KeyActive = "HelpFlowProbe.Active";
    private const string OutDir = "Logs/HelpFlowProbe";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline, s_mark;
    private static bool s_hooked, s_continued;
    private static string s_originalLocale, s_countText;
    private static float s_songTime;
    private static int s_frameMark;
    private static Dictionary<FightStatId, float> s_finalPanel;
    private static (int sp, int x3, int x4, int lives) s_walletAfter;
    private static FightFlowState s_fightState;
    private static float s_roundTimer;
    private static BonusConversionPlan s_plan;
    private static List<string> s_chips = new();
    private static readonly List<(FightFlowState state, bool busy, bool lineDone)> s_flow = new();

    private static void OnFightFlow(FightFlowStateChangedEvent e)
    {
        var versus = UnityEngine.Object.FindFirstObjectByType<VersusScreenController>();
        s_flow.Add((e.Current, BonusConversionController.IsBusy, versus == null || versus.Dialogue == null || versus.Dialogue.IsReadingComplete || !versus.Dialogue.IsActive));
    }

    private static bool EndReady()
    {
        if (Manager == null || Manager.IsRunning) return false;
        var end = Find<EndScreenView>();
        return end != null && end.continueButton.gameObject.activeInHierarchy;
    }

    // "Kind ×n" per chip, in tray order (the chip's icon sprite names its kind).
    private static List<string> ChipTexts()
    {
        var cfg = HelpUIConfigSO.Instance;
        var list = new List<string>();
        foreach (Transform t in Conv.Tray)
        {
            var chip = t.GetComponent<ConversionChipView>();
            if (chip == null || !chip.gameObject.activeSelf) continue;
            var kind = BonusKinds.All.FirstOrDefault(k => cfg.Icon(k) == chip.icon.sprite);
            list.Add($"{kind} {chip.countText.text}");
        }
        return list;
    }

    private static Canvas BlockCanvas => Conv.Block.GetComponentInParent<Canvas>();
    private static bool DrawsAbove(Transform other)
    {
        var mine = BlockCanvas; var theirs = other.GetComponentInParent<Canvas>();
        return mine != null && theirs != null && mine != theirs && mine.overrideSorting && mine.sortingOrder > theirs.sortingOrder;
    }
    private static string SortInfo(Transform other)
    {
        var mine = BlockCanvas; var theirs = other.GetComponentInParent<Canvas>();
        return $"block canvas override {mine?.overrideSorting} order {mine?.sortingOrder}, other {theirs?.name} order {theirs?.sortingOrder}";
    }

    private static bool Inside(Rect inner, Rect outer) =>
        inner.xMin >= outer.xMin - 1f && inner.xMax <= outer.xMax + 1f && inner.yMin >= outer.yMin - 1f && inner.yMax <= outer.yMax + 1f;

    private static void CheckBubbleClear(string when)
    {
        var bubble = Find<VersusScreenController>().Dialogue.Bubble;
        if (bubble == null || !bubble.IsVisible) { Log($"   ({when}: no bubble on screen)"); return; }
        Rect b = ScreenRect(bubble.BubbleRect), block = ScreenRect(Conv.Block), panel = ScreenRect((RectTransform)FightController.Instance.BuildPanel.transform);
        Check($"{when}: the rival's bubble covers neither the bonus block nor the build panel",
              !b.Overlaps(block) && !b.Overlaps(panel), $"bubble {b}, block {block}, panel {panel}");
    }
    private static InputSettings.BackgroundBehavior s_background;
    private static InputSettings.EditorInputBehaviorInPlayMode s_editorInput;

    private static MusicClock Clock => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static HelpTipsController Tips => HelpTipsController.Instance;
    private static HelpScreenController Help => HelpScreenController.Instance;
    private static BonusConversionController Conv => BonusConversionController.Instance;
    private static FightFlowState FightState => FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.OpponentSelection;

    static HelpFlowProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory(OutDir);
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot", null, () =>
        {
            GameSession.Instance.DebugForce("beethoven", 0);
            s_background = InputSystem.settings.backgroundBehavior;
            s_editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            s_originalLocale = LocalizationSettings.SelectedLocale != null ? LocalizationSettings.SelectedLocale.Identifier.Code : null;
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            var cfg = HelpUIConfigSO.Instance;
            Check("placeholders: every bonus + stat has its own sprite, all from the central config",
                  cfg != null && BonusKinds.All.All(k => cfg.Icon(k) != null) && BonusHelpCatalog.BuildStats.All(s => cfg.StatSprite(s) != null)
                  && BonusKinds.All.Select(k => cfg.Icon(k)).Distinct().Count() == BonusKinds.All.Length);
            EventBus.Subscribe<FightFlowStateChangedEvent>(OnFightFlow);
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (1f, "select + play", () => PlayableRows().Any(), () => PlayableRows().First().button.onClick.Invoke()),
        // Play until the flow leaves Song Selection (the row's selection can still be settling on the first press).
        (1f, "play", () => { if (Flow == GameFlowState.SongSelection) Find<SongSelectionView>().playButton.onClick.Invoke(); return Flow != GameFlowState.SongSelection; }, null),
        // ── preRun ──
        (0.3f, "preRun card", () => Manager != null && Manager.IsRunning && Tips.Showing == HelpTipsController.Card.Bonuses, () =>
        {
            var card = Find<TipCardView>();
            Rect r = ScreenRect(card.root), number = ScreenRect(Find<CountdownScreenView>().numberText.rectTransform);
            Check("preRun card: shown during the countdown, small and on the left edge, clear of the countdown number",
                  card.root.gameObject.activeInHierarchy && r.xMax < CanvasRect.xMin + CanvasRect.width * 0.3f && r.height < CanvasRect.height * 0.4f && !r.Overlaps(number),
                  $"card {r}, number {number}");
            Check("preRun card: every bonus with its icon and effect, in one page", Tips.PageCount == 1 && card.rows.childCount == BonusKinds.All.Length,
                  $"pages {Tips.PageCount}, rows {card.rows.childCount}");
        }),
        (0.4f, "preRun card shot", null, () =>
        {
            Capture("01_prerun_card");
            s_countText = Find<CountdownScreenView>().numberText.text;
            Find<TipCardView>().moreInfoButton.onClick.Invoke();
            s_songTime = Clock.SongTime;
            s_mark = EditorApplication.timeSinceStartup;
        }),
        (1.5f, "help from the card", null, () =>
        {
            var pause = PauseController.Instance;
            Check("More info: paused at once (timeScale 0), bonuses help open, no pause menu in between",
                  pause.IsPaused && Time.timeScale == 0f && HelpScreenController.IsOpen && Help.Topic == HelpTopic.Bonuses && Help.Origin == HelpOrigin.TipCard
                  && !Find<PauseView>().overlayRoot.activeInHierarchy);
            Check("…the countdown is frozen while the help is open (number + song time unchanged after 1.5 s)",
                  Find<CountdownScreenView>().numberText.text == s_countText && Mathf.Approximately(Clock.SongTime, s_songTime),
                  $"{s_countText} → {Find<CountdownScreenView>().numberText.text}, {s_songTime:0.000} → {Clock.SongTime:0.000}");
            var view = Find<HelpScreenView>();
            Check("bonuses help: one row per bonus + the attributes", view.content.GetComponentsInChildren<HelpRowView>().Length == BonusKinds.All.Length + BonusHelpCatalog.BuildStats.Length);
            Capture("02_help_bonuses_from_card");
            view.closeButton.onClick.Invoke();
        }),
        (0.4f, "resumed from the card", null, () =>
            Check("closing it resumes the countdown directly (no pause menu, not paused)",
                  !PauseController.Instance.IsPaused && Time.timeScale == 1f && !HelpScreenController.IsOpen && !Find<PauseView>().overlayRoot.activeInHierarchy)),
        (0f, "race starts", () => Clock.HasPlaybackStarted, () => { }),
        (0.4f, "card gone", null, () => Check("the card is withdrawn when the race starts", Tips.Showing == HelpTipsController.Card.None)),
        // ── pause → Bonuses → Escape ──
        (0f, "pause menu", null, () => { Find<PauseView>().pauseButton.onClick.Invoke(); }),
        (0.3f, "open bonuses from pause", null, () =>
        {
            Check("pause menu shown", Find<PauseView>().overlayRoot.activeInHierarchy && Find<PauseView>().bonusHelpButton.gameObject.activeInHierarchy);
            Find<PauseView>().bonusHelpButton.onClick.Invoke();
        }),
        (0.3f, "escape", null, () =>
        {
            Check("pause → Bonuses: the help replaces the pause menu", HelpScreenController.IsOpen && Help.Origin == HelpOrigin.PauseMenu && !Find<PauseView>().overlayRoot.activeInHierarchy);
            Capture("03_help_bonuses_from_pause");
            PressEscape();
        }),
        (0.4f, "back to pause", null, () =>
        {
            ReleaseKeys();
            Check("Escape closes the help back to the pause menu, still paused (the same Escape never unpauses)",
                  !HelpScreenController.IsOpen && PauseController.Instance.IsPaused && Time.timeScale == 0f && Find<PauseView>().overlayRoot.activeInHierarchy,
                  $"help open {HelpScreenController.IsOpen}, paused {PauseController.Instance.IsPaused}, menu {Find<PauseView>().overlayRoot.activeInHierarchy}, keyboard {Keyboard.current != null}");
            Find<PauseView>().restartButton.onClick.Invoke();   // restart: the countdown (and its card) again
        }),
        (0.3f, "card again on restart", () => Manager.IsRunning && Tips.Showing == HelpTipsController.Card.Bonuses, () =>
            Check("restart: the same card again (no first-time-only behaviour)", Tips.Showing == HelpTipsController.Card.Bonuses)),
        (0f, "collect bonuses", () => Clock.HasPlaybackStarted, () =>
        {
            // Synthetic pickups (the probe cannot steer): a quad combo (4 in a row), musical ones, a Special and a Life.
            float t = Clock.SongTime;
            foreach (var type in new[] { RingType.HiHat, RingType.HiHat, RingType.HiHat, RingType.HiHat, RingType.Kick, RingType.Kick, RingType.Snare,
                                         RingType.Beat, RingType.Onset, RingType.Impact, RingType.Peak, RingType.Special, RingType.Life })
                EventBus.Publish(new RingCollectedEvent { Type = type, ActualTime = t, ExpectedTime = t, ScoreMultiplier = 1f, Confidence = 1f });
            RunnerHazardSystem.Instance?.ClearGeneratedForTest();
            Clock.StartPlayback(Manager.SongPlayEnd - 1.5f, MusicTimeDiscontinuityReason.Seek);
            Manager.ResyncCursors(Clock.SongTime);
            s_deadline = EditorApplication.timeSinceStartup + 300;
        }),
        // ── runner results: the ONE bonus block, docked in the results panel ──
        (0.2f, "results block", () => EndReady() && Conv.State == BonusConversionController.BlockState.Docked && Conv.BlockAlpha >= 0.99f, () =>
        {
            var end = Find<EndScreenView>();
            var plan = Conv.BlockPlan;
            s_plan = plan;
            Rect block = ScreenRect(Conv.Block), panel = ScreenRect((RectTransform)end.bonusSlot.parent), list = ScreenRect((RectTransform)end.evaluationPanel.transform);
            Rect restart = ScreenRect((RectTransform)end.restartButton.transform), cont = ScreenRect((RectTransform)end.continueButton.transform);
            Check("results: the bonus block sits inside the results panel, clear of the results list and of the buttons",
                  Inside(block, panel) && !block.Overlaps(list) && !block.Overlaps(restart) && !block.Overlaps(cont), $"block {block}, panel {panel}, list {list}");
            Check("results: the block draws above the results screen (its own sorted canvas)", DrawsAbove(end.transform), SortInfo(end.transform));
            s_chips = ChipTexts();
            Check("results: the block shows every collected bonus with its count (the plan the fight will convert)",
                  plan != null && s_chips.Count == plan.Sources.Count && plan.Sources.All(c => s_chips.Contains($"{c.Kind} ×{c.Count}")), string.Join(", ", s_chips));
            Check("results: no second list of the same bonuses (no per-type rows, no 'for the fight' line)",
                  end.GetComponentsInChildren<RunnerPerformanceRowView>(false).Length == 0
                  && !end.evaluationPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Any(t => t.text.Contains(Loc.Get("Results.ForTheFight"))));
            Capture("04a_results_block");
            ClickContinue();
        }),
        (0.3f, "block travelling", null, () =>
        {
            Rect r = ScreenRect(Conv.Block);
            Check("Continue: the same block keeps on screen while it moves (no hide / re-show, no duplicate)",
                  Conv.Block.gameObject.activeInHierarchy && Conv.BlockAlpha >= 0.99f && Conv.BlockPlan == s_plan
                  && UnityEngine.Object.FindObjectsByType<BonusConversionView>(FindObjectsSortMode.None).Length == 1,
                  $"state {Conv.State}, rect {r}");
            Capture("04b_block_travel");
        }),
        (0f, "block at the bottom", () => Conv.State == BonusConversionController.BlockState.Waiting, () =>
        {
            Rect r = ScreenRect(Conv.Block);
            Check("the block lands at the bottom of the screen, full size, counts untouched (no conversion yet)",
                  r.yMin < CanvasRect.height * 0.08f && Mathf.Approximately(Conv.Block.localScale.x, 1f) && ChipTexts().SequenceEqual(s_chips) && !BonusConversionController.IsBusy,
                  $"rect {r}");
            var sel = Find<OpponentSelectionView>();
            if (sel != null && sel.root.activeInHierarchy)
                Check("roulette: the block does not cover the rival grid", !r.Overlaps(ScreenRect(sel.gridRoot)), $"grid {ScreenRect(sel.gridRoot)}");
            Capture("04c_block_bottom");
        }),
        (0.2f, "versus card", () => FightState == FightFlowState.VersusIntro && Find<VersusScreenView>().root.activeInHierarchy, () =>
        {
            var v = Find<VersusScreenView>();
            Rect r = ScreenRect(Conv.Block);
            var parts = new[] { v.playerPortrait.rectTransform, v.opponentPortrait.rectTransform, v.playerNameText.rectTransform, v.opponentNameText.rectTransform, v.vsText.rectTransform, v.loadingText.rectTransform };
            Check("versus: the block stays visible at the bottom with its counts, clear of portraits, names, VS and 'Loading'",
                  Conv.Block.gameObject.activeInHierarchy && Conv.BlockAlpha >= 0.99f && ChipTexts().SequenceEqual(s_chips) && !BonusConversionController.IsBusy
                  && parts.All(x => !r.Overlaps(ScreenRect(x))), string.Join(" ", parts.Select(x => x.name + ScreenRect(x))));
            Check("versus / roulette: the block draws above the card and the roulette", DrawsAbove(v.transform) && DrawsAbove(Find<OpponentSelectionView>().transform), SortInfo(v.transform));
            Capture("04d_versus_card");
        }),
        // ── conversion during the rival's opening line ──
        (0.6f, "conversion playing", () => BonusConversionController.IsBusy, () =>
        {
            var versus = Find<VersusScreenController>();
            Check("conversion starts with the rival's line, in the Versus (before ROUND 1 / the countdown), the HUD destinations visible",
                  FightState == FightFlowState.VersusIntro && FightController.Instance.BuildPanel.gameObject.activeInHierarchy
                  && versus.Dialogue.CurrentPhase != ComposerDialoguePresentation.Phase.Loading,
                  $"line {versus.Dialogue.CurrentPhase}");
            CheckBubbleClear("conversion start");
            Capture("05a_conversion_with_line");
        }),
        (0.7f, "conversion mid", null, () =>
        {
            if (BonusConversionController.IsBusy) { CheckBubbleClear("mid conversion"); Capture("05b_conversion_mid"); }
        }),
        (0f, "conversion done", () => !BonusConversionController.IsBusy, () =>
        {
            var plan = Conv.LastPlan;
            var panel = FightController.Instance.BuildPanel;
            var me = Actor(FighterSide.Player);
            var cfg = HelpUIConfigSO.Instance;
            Check("the fight converts exactly the plan the results showed (same run, nothing rebuilt)", plan == s_plan);
            Check("conversion: one icon per (bonus, destination), never one per unit, at most maxFlightsInAir in the air",
                  Conv.FlightsLaunched == plan.StatFlights.Count + plan.ResourceFlights.Count && Conv.MaxFlightsSeenInAir <= cfg.maxFlightsInAir && !Conv.LastWasSkipped,
                  $"flights {Conv.FlightsLaunched} (plan {plan.StatFlights.Count}+{plan.ResourceFlights.Count}), max in air {Conv.MaxFlightsSeenInAir}");
            var stats = Resources.Load<AppConfigSO>("AppConfig").fightStats;
            Check("every icon flies to a stat its bonus really feeds (no invented destination); Peak (score only) never flies",
                  plan.StatFlights.All(f => BonusHelpCatalog.Feeds(f.Source, stats).Any(x => x.stat == f.Stat) || IsFallback(f.Stat)) && plan.StatFlights.All(f => f.Source != BonusKind.Peak)
                  && plan.Sources.Where(c => c.Kind == BonusKind.Peak).All(c => !c.HasDestination),
                  string.Join(", ", plan.StatFlights.Select(f => $"{f.Source}->{f.Stat} {f.Points:0.#}")));
            Check("resources fly to their counters (Special, x4, Life)",
                  new[] { BonusKind.Special, BonusKind.QuadCombo, BonusKind.Life }.All(k => plan.ResourceFlights.Any(r => r.Source == k)),
                  string.Join(",", plan.ResourceFlights.Select(r => $"{r.Source} {r.From}->{r.To}")));
            s_finalPanel = PanelPoints(panel);
            bool same = BonusHelpCatalog.BuildStats.All(s => Mathf.Abs(s_finalPanel[s] - Real(me.BuildStats, s)) < 0.51f);
            Check("bars end on the real build (= the player's applied build stats); pieces add up to each bar",
                  same && BonusHelpCatalog.BuildStats.All(s => Mathf.Abs(plan.StartPoints(s) + plan.StatFlights.Where(f => f.Stat == s).Sum(f => f.Points) - plan.FinalPoints[s]) < 1e-3f),
                  string.Join(" ", BonusHelpCatalog.BuildStats.Select(s => $"{s}:{s_finalPanel[s]:0.#}/{Real(me.BuildStats, s):0.#}")));
            var w = me.CombatResources;
            s_walletAfter = (w.Specials, w.TripleCombos, w.QuadCombos, PlayerProgressService.Instance.ExtraLives);
            Check("resource counters end on the wallet / lives, nothing granted twice",
                  w.Specials == GameSession.Instance.Run.SpecialsCollectedThisRun && plan.ResourceFlights.All(r => r.To == Value(r.Source)),
                  $"wallet {w}, lives {PlayerProgressService.Instance.ExtraLives}");
            Check("the block is withdrawn once converted; the HUD keeps the final values",
                  Conv.State == BonusConversionController.BlockState.Hidden && !Conv.Block.gameObject.activeInHierarchy && panel.gameObject.activeInHierarchy);
            Capture("05c_conversion_end");
        }),
        // ── preFight card ──
        (0.2f, "preFight card", () => Tips.Showing == HelpTipsController.Card.Moves, () =>
        {
            Check("the countdown starts only after the conversion", FightState == FightFlowState.RoundIntro || FightState == FightFlowState.Countdown);
            var card = Find<TipCardView>();
            Rect r = ScreenRect(card.root), text = ScreenRect(Find<RoundIntroView>().text.rectTransform), panel = ScreenRect(FightController.Instance.BuildPanel.root);
            Check("preFight card: small, on the left, clear of the countdown text and of the build panel",
                  r.xMax < CanvasRect.xMin + CanvasRect.width * 0.3f && r.height < CanvasRect.height * 0.4f && !r.Overlaps(text) && !r.Overlaps(panel), $"card {r}, text {text}, panel {panel}");
            var rowsText = string.Join(" | ", card.rows.GetComponentsInChildren<TipRowView>().Select(x => x.keysText.text + " " + x.labelText.text));
            string p = FightInputGlyphs.Keyboard ? ">J<" : ">" + Loc.Get("Mobile.Punch") + "<", k = FightInputGlyphs.Keyboard ? ">K<" : ">" + Loc.Get("Mobile.Kick") + "<";
            Check("preFight card: the real buttons of the current input on its first page", rowsText.Contains(p) && rowsText.Contains(k), rowsText);
            Capture("06_prefight_card");
            card.moreInfoButton.onClick.Invoke();
            s_fightState = FightState;
            s_frameMark = Time.frameCount;
        }),
        (1.2f, "moves help from the card", null, () =>
        {
            Check("More info (fight): paused, moves help open, round intro / countdown frozen",
                  FightController.IsPaused && Time.timeScale == 0f && HelpScreenController.IsOpen && Help.Topic == HelpTopic.Moves && Help.Origin == HelpOrigin.TipCard
                  && FightState == s_fightState && !Find<FightHudView>().pausePanel.activeInHierarchy);
            var bubble = Find<VersusScreenController>().Dialogue.Bubble;
            if (bubble != null && bubble.IsVisible)
                Check("round intro: the rival's line (still on screen) does not cover the moves card",
                      !ScreenRect(bubble.BubbleRect).Overlaps(ScreenRect(Find<TipCardView>().root)), $"bubble {ScreenRect(bubble.BubbleRect)}, card {ScreenRect(Find<TipCardView>().root)}");
            var keys = string.Join(" | ", Find<HelpScreenView>().content.GetComponentsInChildren<HelpRowView>().Select(x => x.keysText.text));
            string punch = FightInputGlyphs.Button(FightButton.Punch);
            Check("moves help: built from the combo set (triple punch, special ↓→ + punch)", keys.Contains($"{punch} {punch} {punch}") && keys.Contains("↓→</b></mark>+" + punch), keys.Substring(0, Math.Min(300, keys.Length)));
            Capture("07_help_moves_from_card");
            Find<HelpScreenView>().closeButton.onClick.Invoke();
        }),
        (0.3f, "resumed (fight)", null, () =>
            Check("closing it resumes the countdown directly", !FightController.IsPaused && Time.timeScale == 1f && !Find<FightHudView>().pausePanel.activeInHierarchy)),
        (0.3f, "fighting", () => FightState == FightFlowState.Fighting, () =>
        {
            Check("the moves card is withdrawn when the fight starts", Tips.Showing == HelpTipsController.Card.None);
            Check("the countdown (RoundIntro) started once, only after BOTH the line and the conversion were over; the fight started once",
                  s_flow.Count(e => e.state == FightFlowState.RoundIntro) == 1 && s_flow.Where(e => e.state == FightFlowState.RoundIntro).All(e => !e.busy && e.lineDone)
                  && s_flow.Count(e => e.state == FightFlowState.Fighting) == 1,
                  string.Join(", ", s_flow.Select(e => $"{e.state}(busy {e.busy}, line {e.lineDone})")));
        }),
        // ── fight pause → Moves → Escape ──
        (0.3f, "fight pause", null, () => Find<FightHudView>().pauseButton.onClick.Invoke()),
        (0.3f, "moves from pause", null, () =>
        {
            Check("fight pause menu shown with MOVES", Find<FightHudView>().pausePanel.activeInHierarchy && Find<FightHudView>().movesHelpButton.gameObject.activeInHierarchy);
            Capture("08_fight_pause_menu");
            Find<FightHudView>().movesHelpButton.onClick.Invoke();
        }),
        (0.3f, "escape (fight)", null, () =>
        {
            Check("pause → Moves: the help replaces the pause menu", HelpScreenController.IsOpen && Help.Origin == HelpOrigin.PauseMenu && !Find<FightHudView>().pausePanel.activeInHierarchy);
            PressEscape();
        }),
        (0.4f, "back to fight pause", null, () =>
        {
            ReleaseKeys();
            Check("Escape closes the help back to the fight pause menu, still paused",
                  !HelpScreenController.IsOpen && FightController.IsPaused && Find<FightHudView>().pausePanel.activeInHierarchy,
                  $"help open {HelpScreenController.IsOpen}, paused {FightController.IsPaused}, menu {Find<FightHudView>().pausePanel.activeInHierarchy}");
            Capture("09_fight_pause_back");
            Find<FightHudView>().resumeButton.onClick.Invoke();
        }),
        // ── skip + repeat: same final values, nothing re-applied ──
        (0.3f, "replay + skip", null, () =>
        {
            Check("a repeated call does nothing (already shown for this run)", !Conv.RunIfPending(1).MoveNext() && !BonusConversionController.IsBusy);
            typeof(BonusConversionController).GetField("_shownFor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Conv, null);
            Conv.StartCoroutine(Conv.RunIfPending(1));
        }),
        (0.5f, "skip", null, () => Conv.Skip()),
        (0.3f, "after skip", () => !BonusConversionController.IsBusy, () =>
        {
            var after = PanelPoints(FightController.Instance.BuildPanel);
            var w = Actor(FighterSide.Player).CombatResources;
            Check("skipping (round-intro fallback) ends on exactly the same values as the full animation", Conv.LastWasSkipped && BonusHelpCatalog.BuildStats.All(s => Mathf.Approximately(after[s], s_finalPanel[s])),
                  string.Join(" ", BonusHelpCatalog.BuildStats.Select(s => $"{after[s]:0.##}/{s_finalPanel[s]:0.##}")));
            Check("…and the wallet / lives were not touched by any replay",
                  (w.Specials, w.TripleCombos, w.QuadCombos, PlayerProgressService.Instance.ExtraLives) == s_walletAfter);
            // The Versus again (like Fight Again) with the conversion pending once more: skip it with the Versus skip.
            typeof(BonusConversionController).GetField("_shownFor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Conv, null);
            s_flow.Clear();
            FightFlowController.Instance.RequestState(FightFlowState.VersusIntro);
        }),
        (0.4f, "versus skip during the conversion", () => BonusConversionController.IsBusy && FightState == FightFlowState.VersusIntro, () =>
        {
            var skip = UnityEngine.Object.FindObjectsByType<FightPresentationSkip>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.name == "VersusSkipButton");
            Check("the Versus skip is on screen while line + conversion play", skip.IsShown);
            skip.GetComponentInChildren<UnityEngine.UI.Button>().onClick.Invoke();
        }),
        (0.3f, "after the versus skip", () => !BonusConversionController.IsBusy, () =>
        {
            var after = PanelPoints(FightController.Instance.BuildPanel);
            Check("Versus skip: the conversion resolves to exactly the same values, the block is gone",
                  Conv.LastWasSkipped && BonusHelpCatalog.BuildStats.All(s => Mathf.Approximately(after[s], s_finalPanel[s])) && Conv.State == BonusConversionController.BlockState.Hidden,
                  string.Join(" ", BonusHelpCatalog.BuildStats.Select(s => $"{after[s]:0.##}/{s_finalPanel[s]:0.##}")));
        }),
        (0.3f, "flow continues after the skip", () => FightState == FightFlowState.Fighting, () =>
        {
            Check("…and the flow is not blocked: one RoundIntro, then the fight",
                  s_flow.Count(e => e.state == FightFlowState.RoundIntro) == 1 && s_flow.Count(e => e.state == FightFlowState.Fighting) == 1 && s_flow.All(e => !e.busy),
                  string.Join(", ", s_flow.Select(e => $"{e.state}(busy {e.busy})")));
            AppFlowController.Instance.RequestState(GameFlowState.MainMenu);
        }),
        (1f, "done", null, null),
    };

    // The build's own fallback: none of the stat's sources existed in the song (MusicCombatBuild.Stat.UsedFallback).
    private static bool IsFallback(FightStatId stat) =>
        GameSession.Instance.RunnerResults.CombatBuild.TryGet(stat, out var s) && s.UsedFallback;

    private static int Value(BonusKind k)
    {
        var w = Actor(FighterSide.Player).CombatResources;
        return k switch { BonusKind.Special => w.Specials, BonusKind.TripleCombo => w.TripleCombos, BonusKind.QuadCombo => w.QuadCombos,
                          BonusKind.Life => PlayerProgressService.Instance.ExtraLives, _ => -1 };
    }

    private static float Real(FighterBuildStats b, FightStatId s) => s switch
    {
        FightStatId.PunchPower => b.punchPower, FightStatId.KickPower => b.kickPower, FightStatId.Agility => b.agility,
        FightStatId.Resistance => b.resistance, FightStatId.ImpactPower => b.impactPower, _ => 0f,
    };

    // The panel's shown points (value text) — what the player reads.
    private static Dictionary<FightStatId, float> PanelPoints(FightBuildPanelView panel) =>
        panel.stats.ToDictionary(r => r.stat, r => float.Parse(r.value.text));

    private static void PressEscape()
    {
        var kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Escape));   // processed by the next player-loop update
    }

    private static void ReleaseKeys()
    {
        var kb = Keyboard.current;
        if (kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState());
    }

    private static Rect CanvasRect => ScreenRect(UIFactory.RootCanvas());

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static bool ClickContinue()
    {
        if (s_continued || Manager == null || Manager.IsRunning) return false;
        var end = Find<EndScreenView>();
        if (end == null || !end.continueButton.gameObject.activeInHierarchy) return false;
        s_continued = true;
        end.continueButton.onClick.Invoke();
        return false;
    }

    private static FighterActor Actor(FighterSide side) =>
        UnityEngine.Object.FindObjectsByType<FighterActor>(FindObjectsSortMode.None).FirstOrDefault(a => a.Side == side);

    // A song not completed yet when there is one; otherwise (a save where they are all done) any playable row.
    private static IEnumerable<SongRowView> PlayableRows()
    {
        var rows = UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None).OrderBy(r => r.transform.GetSiblingIndex())
            .Where(r => r.button != null && r.button.interactable).ToList();
        var fresh = rows.Where(r => r.button.interactable && !r.Played).ToList();
        return fresh.Count > 0 ? fresh : rows;
    }
    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    // UI layers only (every overlay canvas rendered through a temporary camera), like UIPlaymodeSmokeTests.
    private static void Capture(string name)
    {
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var camGo = new GameObject("[HelpProbeCamera]");
        camGo.transform.position = new Vector3(0f, -5000f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 10f; }
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        UnityEngine.Object.DestroyImmediate(tex);
        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camGo);
        Log($"   shot {name}");
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!s_hooked)
            {
                s_hooked = true;
                Application.logMessageReceived += (msg, _, type) => { if (type == LogType.Exception || type == LogType.Error) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); } };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 300; }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "")}) flow={Flow} fight={FightState}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow}, fight={FightState})");
            step.act?.Invoke();
            s_step++; s_stepStart = now;
        }
        catch (Exception e) { Log("EXCEPTION " + e); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[HelpProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        try
        {
            Time.timeScale = 1f;
            InputSystem.settings.backgroundBehavior = s_background;
            InputSystem.settings.editorInputBehaviorInPlayMode = s_editorInput;
            GameSession.Instance?.DebugForce(null, 0);
            EventBus.Unsubscribe<FightFlowStateChangedEvent>(OnFightFlow);
            if (s_originalLocale != null) LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(s_originalLocale);
        }
        catch { }
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText(Path.Combine(OutDir, "report.txt"), s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
