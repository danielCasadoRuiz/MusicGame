using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "ROUND {n}" -> "3" -> "2" -> "1" -> "FIGHT!" — one big centered text, same visual shape as
/// CountdownScreenView/FinishBannerView, reused across all five phases by swapping its content.
/// Deliberately owns BOTH FightFlowState.RoundIntro and FightFlowState.Countdown as one continuous
/// self-contained sequence (mirrors how Runner's own CountdownController owns its whole "3, 2, 1,
/// GO" sequence internally rather than GameplayManager micromanaging each tick) — it requests the
/// RoundIntro -> Countdown -> Fighting transitions itself, at exactly the right moments, instead of
/// FightFlowController trying to time sub-phases it has no reason to know about.
///
/// THE ONE CURTAIN for the whole VS->RoundIntro->Countdown->Fighting reveal — by the time this
/// sequence starts, the arena AND FightController's own top-bar HUD are ALREADY visible underneath
/// (FightController shows its HUD the instant RoundIntro begins — see its own doc), so there is no
/// scene/screen swap left to mask here, only a dark `overlay` (FightFlowConfig.countdownOverlayAlpha)
/// for readability while the countdown text is up. That overlay fades progressively to 0 DURING the
/// "FIGHT!" banner itself (over the exact same fightBannerDuration the banner is shown for) — never a
/// separate fade afterward — so the instant "FIGHT!" disappears, the arena+HUD are already fully
/// revealed with nothing left to fade: one continuous visual composition, not two screens swapping.
/// FightController no longer owns a second, redundant transition overlay of its own (removed) — this
/// is the only curtain in the whole sequence.
///
/// Round is currently ALWAYS 1 (no round-end/next-round logic exists yet) — SetRound is already
/// exposed so a future round-management system can call it before RoundIntro fires again, with no
/// change needed here.
///
/// Prefers a real RoundIntro.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as CountdownController/FinishBannerController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// FightFlowStateChangedEvent directly.
/// </summary>
public class RoundIntroController : MonoBehaviour
{
    public static RoundIntroController Instance { get; private set; }

    private RectTransform   _root;
    private Image           _overlay;
    private TextMeshProUGUI _text;

    private FightFlowConfig _config;
    private int              _round = 1;
    private Coroutine        _routine;
    private System.Action<FightFlowStateChangedEvent> _onFightFlowChanged;

    /// <summary>Call before RoundIntro fires again to show a round other than 1 — see this
    /// class's own doc.</summary>
    public void SetRound(int round) => _round = round;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        _config = appConfig != null ? appConfig.fightFlow : null;

        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.RoundIntro != null) WireUI(registry.RoundIntro);
        else Build();

        _root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        _onFightFlowChanged = e =>
        {
            if (e.Current == FightFlowState.RoundIntro) BeginSequence();
        };
        EventBus.Subscribe(_onFightFlowChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFightFlowChanged);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    // ── Prefab path — see RoundIntroView's own doc ───────────────────────────────

    private void WireUI(RoundIntroView view)
    {
        _root    = view.root.GetComponent<RectTransform>();
        _overlay = view.overlay;
        _text    = view.text;
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("RoundIntroScreen", canvas);
        UIFactory.Stretch(_root);

        _overlay = UIFactory.CreatePanel("Overlay", _root, new Color(0f, 0f, 0f, 0f));
        UIFactory.Stretch(_overlay.rectTransform);

        _text = UIFactory.CreateText("Text", _root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(700f, 200f));
        _text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
    }

    // ── Sequence — owns RoundIntro AND Countdown, see class doc on why ───────────

    private void BeginSequence()
    {
        if (_routine != null) StopCoroutine(_routine);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling(); // above the arena + FightController's HUD, both already visible underneath
        _routine = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        float roundIntroDuration  = _config != null ? Mathf.Max(0f, _config.roundIntroDuration)  : 1.5f;
        float countdownStep       = _config != null ? Mathf.Max(0.05f, _config.countdownStepDuration) : 0.8f;
        float fightBannerDuration = _config != null ? Mathf.Max(0f, _config.fightBannerDuration)  : 1f;
        float overlayAlpha        = _config != null ? Mathf.Clamp01(_config.countdownOverlayAlpha) : 0.55f;

        SetOverlayAlpha(overlayAlpha); // instant — held flat through ROUND n/3/2/1, only fades during FIGHT! below

        _text.text = Loc.Get("Fight.RoundLabel", _round.ToString());
        yield return new WaitForSeconds(roundIntroDuration);

        FightFlowController.Instance?.RequestState(FightFlowState.Countdown);

        for (int n = 3; n >= 1; n--)
        {
            _text.text = n.ToString();
            yield return new WaitForSeconds(countdownStep);
        }

        _text.text = Loc.Get("Fight.Banner");

        // The overlay's ENTIRE fade-out happens here, spread across the exact same window "FIGHT!" is
        // shown for — by the time this loop ends, the overlay is already fully transparent, so hiding
        // this whole root an instant later (below) reveals nothing new underneath: one continuous
        // composition, not a screen swap followed by a separate fade (see class doc).
        float t = 0f;
        while (t < fightBannerDuration)
        {
            t += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(overlayAlpha, 0f, Mathf.Clamp01(t / fightBannerDuration)));
            yield return null;
        }
        SetOverlayAlpha(0f);

        _root.gameObject.SetActive(false);
        _routine = null;
        FightFlowController.Instance?.RequestState(FightFlowState.Fighting);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (_overlay == null) return;
        var c = _overlay.color;
        c.a = alpha;
        _overlay.color = c;
    }
}
