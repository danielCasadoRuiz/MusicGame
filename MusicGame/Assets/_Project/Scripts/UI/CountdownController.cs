using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Warmup countdown ("3, 2, 1, GO") — Section 15 of the multi-scene refactor plan. Shown for
/// EXACTLY GameplayManager's own config.core.warmupTime duration, never a hardcoded 3 seconds:
/// GameStartedEvent carries that value (see GameplayEvents.cs), so this splits the real warmup into
/// round(warmupTime) whole-number ticks (falling back to a single tick for a very short warmup),
/// each held for warmupTime/tickCount seconds, then flashes "GO" briefly right as the song/movement
/// actually starts (GameplayManager plays the audio exactly warmupTime seconds after publishing
/// GameStartedEvent — the same value this reads).
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to GameStartedEvent
/// directly; never touches the Runner scene's own GameplayManager/AudioSource.
/// </summary>
public class CountdownController : ThemeReceiverBehaviour
{
    private const float GoHoldSeconds = 0.35f;

    private RectTransform _root;
    private Text _numberText;

    private Coroutine _sequence;
    private System.Action<GameStartedEvent> _onGameStarted;

    private void Awake() => Build();

    protected override void OnEnable()
    {
        base.OnEnable();
        _onGameStarted = e => Show(e.WarmupTime);
        EventBus.Subscribe(_onGameStarted);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EventBus.Unsubscribe(_onGameStarted);
    }

    public override void ApplyUITheme(UIStyleSO ui)
    {
        if (ui == null) return;
        if (_numberText != null) _numberText.color = ui.accentColor;
    }

    // ── Build (runtime-only, no prefab) ────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("CountdownScreen", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _numberText = UIFactory.CreateText("Number", _root, "", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIFactory.SetBox(_numberText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(400f, 200f));

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
