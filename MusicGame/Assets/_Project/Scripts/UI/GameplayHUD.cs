using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live top-bar HUD + end-of-song results screen. Built entirely with uGUI (Canvas/Image/Text/
/// Button via UIFactory), not IMGUI — real Button components mean clicks are handled by
/// EventSystem, not by OnGUI's control-ID bookkeeping (which is what made Restart/Continue
/// occasionally need two clicks: a GUI.Button whose branch stopped being drawn between MouseDown
/// and MouseUp left GUIUtility.hotControl stuck on a control that would never be drawn again).
/// </summary>
public class GameplayHUD : MonoBehaviour
{
    [SerializeField] private AudioSource     audioSource;
    [SerializeField] private MusicRunnerGameplayConfig  config;
    [SerializeField] private GameplayManager manager;

    // Built from Initialize() (NOT Awake()) — Awake() fires synchronously the instant
    // GameplayManager.Awake() calls AddComponent<GameplayHUD>(), which is BEFORE Initialize()
    // supplies `config` (needed for per-ring-type colors); building here instead guarantees
    // config/manager are already set the first time the UI is constructed.
    private bool _built;

    public void Initialize(AudioSource source, MusicRunnerGameplayConfig cfg, GameplayManager mgr,
        LiveHudView liveHudView = null, EndScreenView endScreenView = null)
    {
        audioSource = source;
        config      = cfg;
        manager     = mgr;

        if (_built) return;
        _built = true;

        // Prefer the prefab instances (Tools > MusicGame > Build UI Prefabs) — hand-tunable in
        // the Prefab editor. Fall back to the old procedural build only if that tool hasn't been
        // run yet — LOUDLY, so a broken/missing wiring is never silently invisible.
        if (liveHudView != null) WireLiveHud(liveHudView);
        else { Debug.LogWarning("[GameplayHUD] No LiveHudView wired (UIRegistry missing/empty) — building the live HUD procedurally instead."); BuildLiveHud(); }

        if (endScreenView != null) WireEndScreen(endScreenView);
        else { Debug.LogWarning("[GameplayHUD] No EndScreenView wired (UIRegistry missing/empty) — building the end screen procedurally instead."); BuildEndScreen(); }

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
    private GamePerformance  _finalPerformance;

    private static readonly RingType[] PerformanceRowOrder =
        { RingType.Kick, RingType.Snare, RingType.HiHat, RingType.Beat, RingType.Onset, RingType.Impact, RingType.Peak };

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
    private TextMeshProUGUI  _ratingLabelText;
    private Image _ratingBarFill;
    private TextMeshProUGUI  _scoreSummaryText;
    private RectTransform _performanceRows;
    private TextMeshProUGUI  _fallsText;
    private TextMeshProUGUI  _noFallBonusText;
    private TextMeshProUGUI  _sessionText;
    private readonly List<GameObject> _rowObjects = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

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
            _finalPerformance      = e.Performance;
            PopulateEndScreen();
            SetGameEnded(true);
        };
        _onProfile = e => { _songDuration = e.Profile.duration; _profile = e.Profile; };
        // A full restart publishes CheckpointIndex == 0 — kept as a safety net (Restart already
        // hides the end screen synchronously on click; Continue hides it directly too).
        _onRespawned = e => { if (_built && e.CheckpointIndex == 0) SetGameEnded(false); };

        EventBus.Subscribe(_onLevel);
        EventBus.Subscribe(_onEnd);
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onRespawned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onLevel);
        EventBus.Unsubscribe(_onEnd);
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onRespawned);
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
        if (_gameEnded) return;
        UpdateLiveHud();
    }

    private void SetGameEnded(bool ended)
    {
        _gameEnded = ended;
        _liveRoot.gameObject.SetActive(!ended);
        _endRoot.gameObject.SetActive(ended);
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
        _progressFill   = view.progressFill;

        // Static column-header labels are baked into the prefab at Editor-bake time (whatever
        // locale was active then) — re-set them here from the CURRENT locale so the prefab path
        // behaves identically to the procedural one regardless of when/in what language the
        // prefab was last built.
        SetLabel(view.kickLabel,   RingType.Kick);
        SetLabel(view.snareLabel,  RingType.Snare);
        SetLabel(view.hiHatLabel,  RingType.HiHat);
        SetLabel(view.beatLabel,   RingType.Beat);
        SetLabel(view.onsetLabel,  RingType.Onset);
        SetLabel(view.impactLabel, RingType.Impact);
        if (view.scoreLabel != null) view.scoreLabel.text = Loc.Get("HUD.Score");
        if (view.totalLabel != null) view.totalLabel.text = Loc.Get("HUD.Total");

        _tagsStrip    = view.tagsStrip.GetComponent<RectTransform>();
        _tagStyleText = view.tagStyle;
        _tagVibeText  = view.tagVibe;
        _tagOtherText = view.tagOther;
        _tagsStrip.gameObject.SetActive(false);
    }

    // ── Build: live top bar (procedural fallback — no UIRegistry in the scene yet) ───────────

    private void BuildLiveHud()
    {
        var canvas = UIFactory.RootCanvas();
        _liveRoot = UIFactory.CreateRect("LiveHUD", canvas);
        UIFactory.SetBox(_liveRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));

        var topBar = UIFactory.CreatePanel("TopBar", _liveRoot, new Color(0.03f, 0.03f, 0.03f, 0.88f));
        UIFactory.SetBox(topBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 44f));
        topBar.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var cells = new (string labelKey, RingType? type)[]
        {
            ("HUD.Kick",   RingType.Kick),   ("HUD.Snare",  RingType.Snare), ("HUD.HiHat", RingType.HiHat), ("HUD.Beat", RingType.Beat),
            ("HUD.Onset",  RingType.Onset),  ("HUD.Impact", RingType.Impact),("HUD.Score",  null),           ("HUD.Total", null),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = UIFactory.CreateRect($"Cell_{cells[i].labelKey}", topBar.rectTransform);
            float xMin = i / 8f, xMax = (i + 1) / 8f;
            UIFactory.SetBox(cell, new Vector2(xMin, 0f), new Vector2(xMax, 1f), new Vector2(0f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));

            var label = UIFactory.CreateText("Label", cell, Loc.Get(cells[i].labelKey), 12, RingColorOr(cells[i].type, Color.white), TextAlignmentOptions.TopLeft);
            UIFactory.SetBox(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -3f), new Vector2(0f, 20f));

            var value = UIFactory.CreateText("Value", cell, "0", 14, Color.white, TextAlignmentOptions.TopLeft);
            UIFactory.SetBox(value.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -22f), new Vector2(0f, 20f));

            if (cells[i].labelKey == "HUD.Score")
            {
                _scoreValueText = value;
                value.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
            }
            else if (cells[i].labelKey == "HUD.Total")
            {
                _totalValueText = value;
                value.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
            }
            else
            {
                // Counter VALUES stay plain white (not ring-colored — only their LABEL above is,
                // via RingColorOr/MusicRunnerCollectiblesConfig, a separate data-driven color system
                // unrelated to the UI Theme tokens).
                _counterValues[cells[i].type.Value] = value;
            }
        }

        var progressBg = UIFactory.CreateFillBar("SongProgress", _liveRoot, new Color(0.10f, 0.10f, 0.10f), new Color(0.18f, 0.75f, 0.95f), out _progressFill);
        UIFactory.SetBox(progressBg.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(0f, 4f));
        _progressFill.fillAmount = 0f;
        _progressFill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);

        // Semantic tags strip (STYLE/VIBE/OTHER) — shown only when the song was tagged.
        _tagsStrip = UIFactory.CreateRect("TagsStrip", _liveRoot);
        UIFactory.SetBox(_tagsStrip, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 54f));
        var tagsBg = _tagsStrip.gameObject.AddComponent<Image>();
        tagsBg.color = new Color(0.03f, 0.03f, 0.03f, 0.75f);
        _tagsStrip.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        // Style/Vibe/Other keep their own distinctive tint (a data-category indicator, not UI
        // chrome) — left as-is rather than forced onto a token that doesn't really fit.
        _tagStyleText = UIFactory.CreateText("Style", _tagsStrip, "", 11, new Color(0.55f, 0.8f, 1f), TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(_tagStyleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -3f), new Vector2(-8f, 16f));

        _tagVibeText = UIFactory.CreateText("Vibe", _tagsStrip, "", 11, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(_tagVibeText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -19f), new Vector2(-8f, 16f));
        _tagVibeText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _tagOtherText = UIFactory.CreateText("Other", _tagsStrip, "", 11, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(_tagOtherText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(8f, -35f), new Vector2(-8f, 16f));
        _tagOtherText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _tagsStrip.gameObject.SetActive(false);
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

        _progressFill.fillAmount = (audioSource != null && audioSource.isPlaying && _songDuration > 0f)
            ? Mathf.Clamp01(audioSource.time / _songDuration)
            : 0f;

        UpdateSemanticTagsStrip();
    }

    private void UpdateSemanticTagsStrip()
    {
        if (_profile == null || !_profile.HasMusicTags) { _tagsStrip.gameObject.SetActive(false); return; }

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

    private Color RingColorOr(RingType? type, Color fallback) => type.HasValue && config != null ? config.collectibles.RingColor(type.Value) : fallback;

    // Single source of truth for "which localization key does this RingType's short HUD label
    // use" — shared by the top-bar column headers and the end-screen performance rows (RowLabel)
    // so there's exactly one place mapping RingType → text, never two that could drift apart.
    private static string RingTypeKey(RingType type) => $"HUD.{type}";

    private static void SetLabel(TextMeshProUGUI text, RingType type)
    {
        if (text != null) text.text = Loc.Get(RingTypeKey(type));
    }

    // ── Wire: end screen (prefab path — see EndScreenView's own doc) ─────────

    private void WireEndScreen(EndScreenView view)
    {
        _endRoot          = view.GetComponent<RectTransform>();
        _ratingLabelText  = view.ratingLabel;
        _ratingBarFill    = view.ratingBarFill;
        _scoreSummaryText = view.scoreSummary;
        _performanceRows  = view.performanceRowsContainer;
        _fallsText        = view.fallsText;
        _noFallBonusText  = view.noFallBonusText;
        _sessionText      = view.sessionText;

        // Same reasoning as WireLiveHud's label re-set — these were baked once at Editor-bake
        // time, so re-apply from the current locale here rather than trusting the prefab's value.
        if (view.titleText != null) view.titleText.text = Loc.Get("EndScreen.Title");
        if (view.restartButtonLabel  != null) view.restartButtonLabel.text  = Loc.Get("EndScreen.Restart");
        if (view.continueButtonLabel != null) view.continueButtonLabel.text = Loc.Get("EndScreen.Continue");

        view.restartButton.onClick.AddListener(OnRestartClicked);
        view.continueButton.onClick.AddListener(OnContinueClicked);
    }

    // ── Build: end screen (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void BuildEndScreen()
    {
        var canvas = UIFactory.RootCanvas();
        _endRoot = UIFactory.CreateRect("EndScreen", canvas);
        UIFactory.Stretch(_endRoot);

        var dim = UIFactory.CreatePanel("Dim", _endRoot, new Color(0f, 0f, 0f, 0.55f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        float pw = 420f, ph = 720f;
        var panel = UIFactory.CreatePanel("Panel", _endRoot, new Color(0.04f, 0.04f, 0.04f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pw, ph));
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor    = new Color(0.2f, 0.2f, 0.2f, 1f);
        border.effectDistance = new Vector2(2f, -2f);
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var content = panel.rectTransform;
        float y = -16f;

        var title = UIFactory.CreateText("Title", content, Loc.Get("EndScreen.Title"), 20, Color.white);
        UIFactory.StackTop(title.rectTransform, ref y, 30f);
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Rating label/bar are deliberately NOT theme receivers — PopulateEndScreen colors them
        // from the SCORE (a red-to-green gradient), not from the Theme.
        _ratingLabelText = UIFactory.CreateText("Rating", content, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.StackTop(_ratingLabelText.rectTransform, ref y, 32f);

        var ratingBar = UIFactory.CreateFillBar("RatingBar", content, new Color(0.12f, 0.12f, 0.12f), Color.white, out _ratingBarFill);
        UIFactory.StackTop(ratingBar.rectTransform, ref y, 16f, 46f);
        y -= 6f;

        _scoreSummaryText = UIFactory.CreateText("ScoreSummary", content, "", 12, new Color(0.6f, 0.6f, 0.6f));
        UIFactory.StackTop(_scoreSummaryText.rectTransform, ref y, 20f);
        _scoreSummaryText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        y -= 6f;

        _performanceRows = UIFactory.CreateRect("PerformanceRows", content);
        UIFactory.StackTop(_performanceRows, ref y, 24f * 7f);
        y -= 6f;

        _fallsText = UIFactory.CreateText("Falls", content, "", 13, Color.white);
        UIFactory.StackTop(_fallsText.rectTransform, ref y, 20f);
        _fallsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        _noFallBonusText = UIFactory.CreateText("NoFallBonus", content, "", 13, new Color(1f, 0.85f, 0.2f));
        UIFactory.StackTop(_noFallBonusText.rectTransform, ref y, 20f);
        _noFallBonusText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Positive, UIFontToken.Body);

        _sessionText = UIFactory.CreateText("Session", content, "", 11, new Color(0.55f, 0.55f, 0.6f));
        UIFactory.StackTop(_sessionText.rectTransform, ref y, 22f);
        _sessionText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        y -= 10f;
        float btnW = 150f, btnH = 40f, gap = 16f;
        var restartBtn = UIFactory.CreateButton("RestartButton", content, Loc.Get("EndScreen.Restart"), out var restartLabel);
        UIFactory.SetBox(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-(btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        restartBtn.onClick.AddListener(OnRestartClicked);
        restartBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        restartLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var continueBtn = UIFactory.CreateButton("ContinueButton", content, Loc.Get("EndScreen.Continue"), out var continueLabel);
        UIFactory.SetBox(continueBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2((btnW + gap) / 2f, y), new Vector2(btnW, btnH));
        continueBtn.onClick.AddListener(OnContinueClicked);
        continueBtn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        continueLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
    }

    // _gameEnded is cleared HERE, synchronously on click — not left to wait for
    // PlayerRespawnedEvent(0) (which only fires once the restart coroutine's fade-in finishes).
    // The end screen must vanish the instant the button is pressed.
    private void OnRestartClicked()
    {
        SetGameEnded(false);
        manager?.RequestRestartSong();
    }

    // TEMPORARY: Continue only closes the end screen for now — it does NOT restart the song.
    // Once there's an actual "next" destination (next song / scene), this is where that
    // transition will be kicked off instead of just hiding the panel.
    private void OnContinueClicked()
    {
        SetGameEnded(false);
        var s = _finalStats ?? manager?.Stats;
        EventBus.Publish(new ContinuePressedEvent { Stats = s });
    }

    private void PopulateEndScreen()
    {
        var s = _finalStats ?? manager?.Stats;
        if (s == null) return;

        float displayRating = config != null
            ? Mathf.Clamp01(config.scoring.performanceRatingCurve.Evaluate(_finalNormalizedScore))
            : _finalNormalizedScore;
        Color ratingColor = Color.Lerp(new Color(0.9f, 0.3f, 0.25f), new Color(0.3f, 0.95f, 0.4f), displayRating);

        _ratingLabelText.text     = RatingLabel(displayRating);
        _ratingLabelText.color    = ratingColor;
        _ratingBarFill.fillAmount = displayRating;
        _ratingBarFill.color      = ratingColor;

        _scoreSummaryText.text = Loc.Get("EndScreen.ScoreSummary",
            s.Score.ToString(), _finalMaxPossibleScore.ToString(), (_finalNormalizedScore * 100f).ToString("F0"));

        foreach (var go in _rowObjects) Destroy(go);
        _rowObjects.Clear();

        if (_finalPerformance != null)
        {
            float ry = 0f;
            foreach (var type in PerformanceRowOrder)
            {
                if (!_finalPerformance.ByType.TryGetValue(type, out var tp)) continue;
                _rowObjects.Add(BuildPerformanceRow(_performanceRows, ry, type, tp));
                ry -= 24f;
            }
        }

        _fallsText.text = Loc.Get("EndScreen.Falls", _finalFallCount.ToString());

        bool noFallBonus = _finalFallCount == 0 && config != null && config.scoring.noFallScoreMultiplier > 1f;
        _noFallBonusText.gameObject.SetActive(noFallBonus);
        if (noFallBonus)
        {
            int pctBonus = Mathf.RoundToInt((config.scoring.noFallScoreMultiplier - 1f) * 100f);
            _noFallBonusText.text = Loc.Get("EndScreen.NoFallBonus", pctBonus.ToString());
        }

        var session = manager?.Session;
        bool showSession = session != null && session.RunsCompleted > 0;
        _sessionText.gameObject.SetActive(showSession);
        if (showSession)
        {
            string key = session.RunsCompleted == 1 ? "EndScreen.SessionSingular" : "EndScreen.SessionPlural";
            _sessionText.text = Loc.Get(key, session.RunsCompleted.ToString(), (session.NormalizedScore * 100f).ToString("F0"));
        }
    }

    private GameObject BuildPerformanceRow(RectTransform parent, float y, RingType type, TypePerformance tp)
    {
        var row = UIFactory.CreateRect($"Row_{type}", parent);
        UIFactory.SetBox(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(0f, 20f));

        Color color = config != null ? config.collectibles.RingColor(type) : Color.white;

        var label = UIFactory.CreateText("Label", row, RowLabel(type), 12, color, TextAlignmentOptions.Left);
        UIFactory.SetBox(label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 0f));

        var barBg = UIFactory.CreateFillBar("Bar", row, new Color(0.12f, 0.12f, 0.12f), color * 0.8f, out var barFill);
        UIFactory.SetBox(barBg.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), new Vector2(0f, 0.5f), new Vector2(74f, 0f), new Vector2(-150f, 0f));
        barFill.fillAmount = Mathf.Clamp01(tp.CollectionRate);

        var info = UIFactory.CreateText("Info", row, $"{tp.CollectionRate * 100f:F0}%  {tp.Collected}/{tp.Available}", 10, Color.white, TextAlignmentOptions.Right);
        UIFactory.SetBox(info.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 0f));

        return row.gameObject;
    }

    private static string RatingLabel(float displayRating) => Loc.Get(displayRating switch
    {
        < 0.20f => "EndScreen.RatingPoor",
        < 0.40f => "EndScreen.RatingWeak",
        < 0.60f => "EndScreen.RatingDecent",
        < 0.85f => "EndScreen.RatingGreat",
        _       => "EndScreen.RatingInsane",
    });

    private static string RowLabel(RingType type) => Loc.Get(RingTypeKey(type));
}
