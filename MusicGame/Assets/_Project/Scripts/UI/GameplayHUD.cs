using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live top-bar HUD + end-of-song results screen. Both are uGUI PREFABS (LiveHud.prefab /
/// EndScreen.prefab, instanced in Runner.unity and handed over by GameplayManager from the scene's
/// UIRegistry) — this class only wires their views and fills values; it never builds UI. A missing view
/// is logged as an error and that part of the HUD simply stays off (no procedural fallback).
/// The end screen is ONE results list (EndScreenView.evaluationPanel — see PopulateEndScreen): race stars,
/// score, pickups/timing, falls + no-fall bonus, lives and the match record — plus, in the panel's bonus slot,
/// the collected bonuses as the SAME bonus block the fight later converts (BonusConversionController docks it
/// there; it replaced the per-ring-type bars and the "for the fight" line, so no bonus is shown twice). Real
/// Button components mean clicks are handled by EventSystem, not by OnGUI's control-ID bookkeeping
/// (which is what made Restart/Continue occasionally need two clicks with the old IMGUI version).
/// </summary>
public class GameplayHUD : MonoBehaviour
{
    [SerializeField] private AudioSource     audioSource;
    [SerializeField] private MusicRunnerGameplayConfig  config;
    [SerializeField] private GameplayManager manager;

    // Wired from Initialize() (NOT Awake()) — Awake() fires synchronously the instant
    // GameplayManager.Awake() calls AddComponent<GameplayHUD>(), which is BEFORE Initialize()
    // supplies `config` (needed for per-ring-type colors); wiring here instead guarantees
    // config/manager are already set the first time the UI is touched.
    private bool _built;

    public void Initialize(AudioSource source, MusicRunnerGameplayConfig cfg, GameplayManager mgr,
        LiveHudView liveHudView = null, EndScreenView endScreenView = null)
    {
        audioSource = source;
        config      = cfg;
        manager     = mgr;

        if (_built) return;
        _built = true;

        // The prefab instances in Runner.unity (UIRegistry) — hand-tunable in the Prefab editor.
        // No procedural fallback: a missing view is logged and that part stays off.
        if (liveHudView != null) WireLiveHud(liveHudView);
        else Debug.LogError("[GameplayHUD] No LiveHudView wired (UIRegistry in Runner.unity missing/empty) — the live HUD is disabled.", this);

        if (endScreenView != null) WireEndScreen(endScreenView);
        else Debug.LogError("[GameplayHUD] No EndScreenView wired (UIRegistry in Runner.unity missing/empty) — the end screen is disabled.", this);

        SetGameEnded(false);
    }

    // NOTE: no local CollectionStats here — GameplayManager.Stats is the single source of
    // truth (it's the same object mutated by ring pickups, fall penalties and the no-fall bonus).
    private int              _totalRings;
    private float            _songDuration;
    private SongProfile      _profile;
    private bool             _gameEnded;
    private CollectionStats  _finalStats;
    private int              _finalFallCount;
    private float            _finalNormalizedScore;
    private int              _finalMaxPossibleScore;
    private bool             _resultsReady;   // this run's RunnerResults exist (RunnerResultsReadyEvent)

    private Action<LevelGeneratedEvent>   _onLevel;
    private Action<GameEndedEvent>        _onEnd;
    private Action<SongProfileReadyEvent> _onProfile;
    private Action<PlayerRespawnedEvent>  _onRespawned;

    // ── uGUI refs: live top bar ─────────────────────────────────────────────
    private RectTransform _liveRoot;
    private readonly Dictionary<RingType, TextMeshProUGUI> _counterValues = new();
    private TextMeshProUGUI  _scoreValueText;
    private TextMeshProUGUI  _totalValueText;
    private Image _progressFill;
    private RectTransform _tagsStrip;
    private TextMeshProUGUI _tagStyleText, _tagVibeText, _tagOtherText;

    // ── uGUI refs: end screen ───────────────────────────────────────────────
    private RectTransform _endRoot;
    private EvaluationPanel _endPanel;          // the ONE results list (EndScreenView.evaluationPanel)
    private RectTransform _bonusSlot;           // where the bonus block is docked (EndScreenView.bonusSlot)
    private System.Action<RunnerResultsReadyEvent> _onResultsReady;
    // Campaign evaluation of THIS run (CampaignService.LastRunner, delivered by RunnerResultsReadyEvent in
    // the same GameEndedEvent dispatch); null until then and cleared whenever the end screen hides.
    private RunnerPhaseOutcome _runnerOutcome;

    // LIFE / SPECIAL — read straight from the authoritative state (PlayerProgressService.ExtraLives,
    // GameSession.Run): the ints below only detect changes, they are never a second counter.
    private TextMeshProUGUI _lifeValueText, _specialValueText;
    private int _shownLives = -1, _shownSpecials = -1;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void UpdateResourceHud()
    {
        if (_lifeValueText == null || _specialValueText == null) return;
        int lives = PlayerProgressService.Instance != null ? PlayerProgressService.Instance.ExtraLives : 0;
        int specials = GameSession.Instance != null ? GameSession.Instance.Run.Wallet.Specials : 0;
        if (lives == _shownLives && specials == _shownSpecials) return;
        _shownLives = lives;
        _shownSpecials = specials;
        _lifeValueText.text = lives.ToString();
        _specialValueText.text = specials.ToString();
    }

    private void OnEnable()
    {
        _onLevel = e => _totalRings = e.RingCount;
        _onEnd   = e =>
        {
            // An instance that never went through Initialize() (stray/duplicate component) has
            // no UI built yet. LOG loudly instead of throwing — an uncaught exception here would
            // abort the REST of GameEndedEvent's subscribers too (multicast delegate invocation
            // stops dead the instant one target throws), silently freezing the whole game.
            if (!_built)
            {
                Debug.LogError($"[GameplayHUD] GameEndedEvent received on '{name}' before Initialize() ran — " +
                               "this instance has no UI. Likely a stray duplicate GameplayHUD in the scene.", this);
                return;
            }

            _finalStats            = e.Stats;
            _finalFallCount        = e.FallCount;
            _finalNormalizedScore  = e.NormalizedScore;
            _finalMaxPossibleScore = e.MaxPossibleScore;
            SetGameEnded(true);
            PopulateEndScreen();
        };
        _onProfile = e => { _songDuration = e.Profile.duration; _profile = e.Profile; };
        // A full restart publishes CheckpointIndex == 0 — kept as a safety net (Restart already
        // hides the end screen synchronously on click; Continue hides it directly too).
        _onRespawned = e => { if (_built && e.CheckpointIndex == 0) SetGameEnded(false); };

        EventBus.Subscribe(_onLevel);
        EventBus.Subscribe(_onEnd);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onRespawned);
        // Campaign evaluation of this run (stars) — computed by GameSession inside the same GameEndedEvent
        // dispatch, so it may land before or after _onEnd above: whichever comes second completes the panel.
        _onResultsReady = _ =>
        {
            _runnerOutcome = CampaignService.LastRunner;
            _resultsReady = true;
            if (_gameEnded) PopulateEndScreen();
        };
        EventBus.Subscribe(_onResultsReady);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onLevel);
        EventBus.Unsubscribe(_onEnd);
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onRespawned);
        EventBus.Unsubscribe(_onResultsReady);
    }

    private bool _loggedNotBuilt;

    private void Update()
    {
        if (!_built)
        {
            if (!_loggedNotBuilt)
            {
                _loggedNotBuilt = true;
                Debug.LogError($"[GameplayHUD] '{name}' is running Update() but was never Initialize()'d — " +
                               "likely a stray duplicate GameplayHUD in the scene. It will stay inert.", this);
            }
            return;
        }
        if (_gameEnded || _liveRoot == null) return;
        UpdateLiveHud();
        UpdateResourceHud();
    }

    private void SetGameEnded(bool ended)
    {
        _gameEnded = ended;
        if (!ended)
        {
            _runnerOutcome = null; // a restart / new run: never show the previous run's evaluation
            _resultsReady = false;
            BonusConversionController.Instance?.HideFromResults();
        }
        if (_liveRoot != null) _liveRoot.gameObject.SetActive(!ended);
        if (_endRoot != null) _endRoot.gameObject.SetActive(ended);
    }

    // ── Wire: live top bar (prefab path — see LiveHudView's own doc) ─────────

    private void WireLiveHud(LiveHudView view)
    {
        _liveRoot = view.GetComponent<RectTransform>();

        _counterValues[RingType.Kick]   = view.kickValue;
        _counterValues[RingType.Snare]  = view.snareValue;
        _counterValues[RingType.HiHat]  = view.hiHatValue;
        _counterValues[RingType.Beat]   = view.beatValue;
        _counterValues[RingType.Onset]  = view.onsetValue;
        _counterValues[RingType.Impact] = view.impactValue;
        _scoreValueText = view.scoreValue;
        _totalValueText = view.totalValue;
        _progressFill   = UIFactory.EnsureFillSprite(view.progressFill);

        // Static column-header labels are baked into the prefab at Editor-bake time (whatever
        // locale was active then) — re-set them here from the CURRENT locale so they always
        // follow the language regardless of when/in what language the
        // prefab was last built.
        SetLabel(view.kickLabel,   RingType.Kick);
        SetLabel(view.snareLabel,  RingType.Snare);
        SetLabel(view.hiHatLabel,  RingType.HiHat);
        SetLabel(view.beatLabel,   RingType.Beat);
        SetLabel(view.onsetLabel,  RingType.Onset);
        SetLabel(view.impactLabel, RingType.Impact);
        if (view.scoreLabel != null) Loc.Set(view.scoreLabel, "HUD.Score");
        if (view.totalLabel != null) Loc.Set(view.totalLabel, "HUD.Total");

        _tagsStrip    = view.tagsStrip.GetComponent<RectTransform>();
        _tagStyleText = view.tagStyle;
        _tagVibeText  = view.tagVibe;
        _tagOtherText = view.tagOther;
        _tagsStrip.gameObject.SetActive(false);

        // LIFE / SPECIAL cells: static label from the prefab (LocalizedText), tint from the collectibles
        // config — the same colour the pickups have in the world.
        _lifeValueText    = view.lifeValue;
        _specialValueText = view.specialValue;
        if (view.lifeLabel != null && config != null)    view.lifeLabel.color    = config.collectibles.lifeColor;
        if (view.specialLabel != null && config != null) view.specialLabel.color = config.collectibles.specialColor;
        if (_lifeValueText == null || _specialValueText == null)
            Debug.LogError("[GameplayHUD] LiveHudView.lifeValue/specialValue not assigned — run Tools > MusicGame > UI > Build All UI Prefabs (patches LiveHud.prefab).", view);

        // Bonus icon slots: sprite + tint from the central HelpUIConfig (placeholders until the final art).
        var help = HelpUIConfigSO.Instance;
        if (help != null)
        {
            SetIcon(view.kickIcon, help, BonusKind.Kick);     SetIcon(view.snareIcon, help, BonusKind.Snare);
            SetIcon(view.hiHatIcon, help, BonusKind.HiHat);   SetIcon(view.beatIcon, help, BonusKind.Beat);
            SetIcon(view.onsetIcon, help, BonusKind.Onset);   SetIcon(view.impactIcon, help, BonusKind.Impact);
            SetIcon(view.lifeIcon, help, BonusKind.Life);     SetIcon(view.specialIcon, help, BonusKind.Special);
        }
    }

    private static void SetIcon(UnityEngine.UI.Image image, HelpUIConfigSO help, BonusKind kind)
    {
        if (image == null) return;
        image.sprite = help.Icon(kind);
        image.color = help.Tint(kind);
        image.enabled = image.sprite != null;
    }

    private void UpdateLiveHud()
    {
        var stats = manager != null ? manager.Stats : null;
        if (stats == null) return;

        foreach (var kv in _counterValues)
            kv.Value.text = stats.Get(kv.Key).ToString();
        _scoreValueText.text = stats.Score.ToString();

        int   total = stats.Total;
        float pct   = _totalRings > 0 ? (float)total / _totalRings * 100f : 0f;
        _totalValueText.text = $"{total}  {pct:F0}%";

        // Relative to the actual PLAYED window (manager.SongPlayStart..SongPlayEnd — the whole
        // song for a catalog/automatic pick, or a manually-configured sub-range for an uploaded
        // one — see GameplayManager.ResolvePlayRange), never the song's total duration: a 60-second
        // range out of a 4-minute song should read 0%..100% over those 60 seconds, not crawl from
        // ~4% to ~29%. Falls back to the AudioSource's own clip length when the range isn't
        // resolved yet (e.g. this HUD ticking before GenerateAndStart has run) so the bar still
        // fills correctly instead of silently sitting at 0% for the whole run.
        float rangeStart = manager != null ? manager.SongPlayStart : 0f;
        float rangeEnd   = manager != null && manager.SongPlayEnd > 0f ? manager.SongPlayEnd
            : (_songDuration > 0f ? _songDuration
                : (audioSource != null && audioSource.clip != null ? audioSource.clip.length : 0f));
        float rangeLength = rangeEnd - rangeStart;
        _progressFill.fillAmount = (audioSource != null && audioSource.isPlaying && rangeLength > 0f)
            ? Mathf.Clamp01((audioSource.time - rangeStart) / rangeLength)
            : _progressFill.fillAmount;

        UpdateSemanticTagsStrip();
    }

    private void UpdateSemanticTagsStrip()
    {
        // Raw detector tags are debug-only (F1 overlay); the player only ever sees the final GameMusicStyle.
        if (_profile == null || !_profile.HasMusicTags || !GameplayDebugHUD.IsVisible) { _tagsStrip.gameObject.SetActive(false); return; }

        string style = FormatCategory(MusicTagCategory.Style);
        string vibe  = FormatCategory(MusicTagCategory.Vibe);
        string other = FormatCategory(MusicTagCategory.Other);

        bool any = style != null || vibe != null || other != null;
        _tagsStrip.gameObject.SetActive(any);
        if (!any) return;

        SetTagLine(_tagStyleText, Loc.Get("HUD.TagStyle"), style);
        SetTagLine(_tagVibeText,  Loc.Get("HUD.TagVibe"),  vibe);
        SetTagLine(_tagOtherText, Loc.Get("HUD.TagOther"), other);
    }

    private void SetTagLine(TextMeshProUGUI text, string label, string content)
    {
        text.gameObject.SetActive(content != null);
        if (content != null) text.text = $"<b>{label}</b>  {content}";
    }

    private string FormatCategory(MusicTagCategory category)
    {
        var parts = new List<string>();
        foreach (var t in _profile.musicTags)
            if (MusicTagClassifier.Classify(t.tag) == category)
                parts.Add($"{t.tag} {t.score * 100f:F0}%");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    // Single source of truth for "which localization key does this RingType's short HUD label
    // use" — the top-bar column headers' one place mapping RingType → text.
    private static string RingTypeKey(RingType type) => $"HUD.{type}";

    private static void SetLabel(TextMeshProUGUI text, RingType type)
    {
        if (text != null) Loc.Set(text, RingTypeKey(type));
    }

    // ── Wire: end screen (prefab path — see EndScreenView's own doc) ─────────

    private void WireEndScreen(EndScreenView view)
    {
        _endRoot  = view.GetComponent<RectTransform>();
        _endPanel = view.evaluationPanel;
        _bonusSlot = view.bonusSlot;
        if (_bonusSlot == null)
            Debug.LogError("[GameplayHUD] EndScreenView.bonusSlot not assigned — run Tools > MusicGame > UI > Help Placeholders + Prefabs (patches EndScreen.prefab).", view);
        if (_endPanel == null)
            Debug.LogError("[GameplayHUD] EndScreenView.evaluationPanel not assigned — run Tools > MusicGame > UI > Build All UI Prefabs (patches EndScreen.prefab).", view);

        // Same reasoning as WireLiveHud's label re-set — these were baked once at Editor-bake
        // time, so re-apply from the current locale here rather than trusting the prefab's value.
        if (view.titleText != null) Loc.Set(view.titleText, "EndScreen.Title");
        if (view.restartButtonLabel  != null) Loc.Set(view.restartButtonLabel, "EndScreen.Restart");
        if (view.continueButtonLabel != null) Loc.Set(view.continueButtonLabel, "EndScreen.Continue");

        view.restartButton.onClick.AddListener(OnRestartClicked);
        view.continueButton.onClick.AddListener(OnContinueClicked);

    }

    // _gameEnded is cleared HERE, synchronously on click — not left to wait for
    // PlayerRespawnedEvent(0) (which FallRespawnSystem.RestartSong now fires right as its own
    // countdown begins, not once it finishes — see that method's own doc). The end screen must
    // vanish the instant the button is pressed, revealing the live HUD + countdown underneath.
    private void OnRestartClicked()
    {
        SetGameEnded(false);
        manager?.RequestRestartSong();
    }

    // RunnerSceneBootstrap turns ContinuePressedEvent into a transition to GameFlowState.Fight — a
    // genuinely different Mode Scene, which Runner (this one included) gets fully unloaded for.
    // PauseController.Resume() is what actually undoes the "frozen like a pause" state GameEndedEvent
    // put us in (see its own _onEnd) — Time.timeScale is a GLOBAL Unity setting, so leaving it at 0
    // here would silently freeze Fight too the instant it loads (its own Update()-driven timer/
    // animations would never advance). Resume() no-ops harmlessly if we were somehow never paused.
    //
    // Deliberately does NOT call SetGameEnded(false) — unlike Restart (which stays in Runner and
    // needs the end screen gone so the live HUD can take back over), Continue is LEAVING Runner
    // entirely. Revealing the live HUD/frozen 3D world for even one frame here used to flash the
    // Runner background before FightController's transition overlay (triggered by the
    // GameFlowStateChangedEvent this publish cascades into, still within this same call) had a
    // chance to cover it. Leaving the end screen exactly as it is until Runner unloads underneath
    // it means the player only ever sees results → black transition, never a glimpse of the frozen
    // world in between.
    private void OnContinueClicked()
    {
        PauseController.Instance?.Resume();
        var s = _finalStats ?? manager?.Stats;
        EventBus.Publish(new ContinuePressedEvent { Stats = s });
    }

    /// <summary>
    /// The ONE results list, top to bottom (each value once):
    ///   [campaign position]                      header — where this race counts (omitted for a free run)
    ///   RACE stars (+ NEW RECORD)                the run's result = its stars (score ÷ reachable maximum)
    ///   Score a / max (n%)                       the gameplay score behind those stars
    ///   Pickups · Timing                         what the score is made of
    ///   Falls (+ no-fall bonus / bonus lost)     only the bonus line when it applies
    ///   Lives +n · extra lives (Fight Again)     granted once per match (RunRewardRules)
    ///   Best race of this match / next step      the stored record (when not just beaten) + "the fight is next"
    /// and the collected bonuses (musical ones, Special, x3 / x4, Life) as the bonus block in the panel's slot — the
    /// same block, design and counts the fight presentation converts, never a second list of them.
    /// Rebuilt from scratch on each call (GameEndedEvent and RunnerResultsReadyEvent may arrive in any order); the
    /// block only once this run's results exist.
    /// </summary>
    private void PopulateEndScreen()
    {
        var s = _finalStats ?? manager?.Stats;
        if (s == null || _endPanel == null) return;
        var p = _endPanel;
        p.Clear();
        var o = _runnerOutcome;
        var run = GameSession.Instance != null ? GameSession.Instance.Run : null;

        // Result: where it counts + the race's stars.
        var position = o != null && o.Recorded && CampaignService.Available ? CampaignModel.Find(CampaignService.Data, o.PositionId) : null;
        if (position != null) p.AddHeader(CampaignService.PositionLabel(position.levelId, position.matchNumber));
        if (o != null)
            p.AddStars(Loc.Get("Results.RaceStars") + (o.Recorded && o.Record.NewRecord ? "  " + Loc.Get("Eval.NewRecord") : ""), o.Evaluation.HalfStars);

        // Score and what it is made of.
        p.AddLine(Loc.Get("Results.Score", s.Score.ToString("N0"), _finalMaxPossibleScore.ToString("N0"),
                          Mathf.RoundToInt(Mathf.Clamp01(_finalNormalizedScore) * 100f).ToString()));
        if (o != null && o.Evaluation.Metrics != null)
            p.AddNote(EvaluationPanel.Breakdown(o.Evaluation.Metrics.FindAll(m => m.key == "Collected" || m.key == "Timing")));

        // Falls and the no-fall bonus (only when the config has one).
        bool hasBonus = config != null && config.scoring.noFallScoreMultiplier > 1f;
        int bonusPct = hasBonus ? Mathf.RoundToInt((config.scoring.noFallScoreMultiplier - 1f) * 100f) : 0;
        if (_finalFallCount == 0 && hasBonus) p.AddHighlight(Loc.Get("Results.NoFallBonus", bonusPct.ToString()));
        else if (hasBonus) p.AddLine(Loc.Get("Results.FallsBonusLost", _finalFallCount.ToString(), bonusPct.ToString()));
        else p.AddLine(Loc.Get("Results.Falls", _finalFallCount.ToString()));

        // The collected bonuses (per type, Special, x3 / x4, Life): the bonus block, docked in the panel's slot.
        if (_resultsReady) BonusConversionController.Instance?.ShowInResults(_bonusSlot);

        // Lives: granted once per match; the inventory's one use is Fight Again.
        int livesCollected = run != null ? run.LivesCollectedThisRun : 0;
        int livesGranted = run != null ? run.LivesGrantedThisRun : 0;
        int extraLives = PlayerProgressService.Instance != null ? PlayerProgressService.Instance.ExtraLives : 0;
        p.AddLine(livesGranted == livesCollected
            ? Loc.Get("Results.Lives", livesGranted.ToString(), extraLives.ToString())
            : Loc.Get("Results.LivesCapped", livesGranted.ToString(), livesCollected.ToString(), extraLives.ToString()));

        // The match record and what comes next.
        if (o != null && o.Recorded)
        {
            if (!o.Record.NewRecord && o.Record.Best != null && o.Record.Best.has)
                p.AddStarsSmall(Loc.Get("Results.BestRace"), o.Record.Best.halfStars);
            p.AddHighlight(Loc.Get("Eval.NextFight"));
        }
        else if (o != null) p.AddNote(Loc.Get("Eval.NotCampaign"));
    }
}
