using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Warmup countdown ("3, 2, 1, GO") — Section 15 of the multi-scene refactor plan. Shown for
/// EXACTLY GameplayManager's own config.core.warmupTime duration, never a hardcoded 3 seconds:
/// GameStartedEvent carries that value (see GameplayEvents.cs), so this splits the real warmup into
/// round(warmupTime) whole-number ticks (falling back to a single tick for a very short warmup),
/// each held for warmupTime/tickCount seconds, then flashes "GO" briefly right as the song/movement
/// actually starts (GameplayManager plays the audio exactly warmupTime seconds after publishing
/// GameStartedEvent — the same value this reads).
///
/// Pure SCREEN CONTROLLER — the number text carries a ThemeTextReceiver(Accent/Display) directly;
/// no theming logic lives in this class.
///
/// Prefers a real CountdownScreen.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as GameplayHUD/PauseController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to GameStartedEvent
/// directly; never touches the Runner scene's own GameplayManager/AudioSource.
/// </summary>
public class CountdownController : MonoBehaviour
{
    private const float GoHoldSeconds = 0.35f;

    private RectTransform _root;
    private TextMeshProUGUI _numberText;

    private Coroutine _sequence;
    private System.Action<GameStartedEvent> _onGameStarted;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.Countdown != null) WireUI(registry.Countdown);
        else Build();
    }

    private void OnEnable()
    {
        _onGameStarted = e => Show(e.WarmupTime);
        EventBus.Subscribe(_onGameStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onGameStarted);
    }

    // ── Prefab path — see CountdownScreenView's own doc ──────────────────────────

    private void WireUI(CountdownScreenView view)
    {
        _root       = view.root.GetComponent<RectTransform>();
        _numberText = view.numberText;

        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("CountdownScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _numberText = UIFactory.CreateText("Number", _root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_numberText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(400f, 200f));
        _numberText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        _root.gameObject.SetActive(false);
    }

    // ── Show / sequence ───────────────────────────────────────────────────────

    private void Show(float warmupTime)
    {
        if (_sequence != null) StopCoroutine(_sequence);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _sequence = StartCoroutine(PlaySequence(warmupTime));
    }

    private IEnumerator PlaySequence(float warmupTime)
    {
        int ticks = Mathf.Max(1, Mathf.RoundToInt(warmupTime));
        float tickDuration = warmupTime > 0f ? warmupTime / ticks : 0f;

        for (int n = ticks; n >= 1; n--)
        {
            _numberText.text = n.ToString();
            if (tickDuration > 0f) yield return new WaitForSeconds(tickDuration);
        }

        _numberText.text = Loc.Get("Countdown.Go");
        yield return new WaitForSeconds(GoHoldSeconds);

        _root.gameObject.SetActive(false);
        _sequence = null;
    }
}
