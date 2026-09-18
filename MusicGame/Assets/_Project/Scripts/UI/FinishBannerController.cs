using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// "FINISH!" banner — the closest sibling of CountdownController's own "3, 2, 1, GO", shown at
/// the OTHER end of a run: the instant the song's fade-out finishes (SongFinishedEvent — see
/// GameplayManager's own ending sequence), the player is still walking the silent/flat farewell
/// stretch that follows, well before GameEndedEvent actually swaps in the end screen. Unlike the
/// old approach (a small text nested inside LiveHud, visible for the WHOLE farewell stretch),
/// this is its own big, momentary, centered announcement that shows for a fixed couple of
/// seconds and then hides itself — independent of how long the farewell actually lasts.
///
/// Prefers a real FinishBanner.prefab instance (wired via UIRegistry, built once via
/// Tools > MusicGame > Build UI Prefabs) — falls back to the old procedural build only if that
/// hasn't been run yet, same pattern as CountdownController/GameplayHUD/PauseController.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to SongFinishedEvent
/// directly; never touches the Runner scene's own GameplayManager/AudioSource.
/// </summary>
public class FinishBannerController : MonoBehaviour
{
    private const float DisplaySeconds = 2f;

    private RectTransform   _root;
    private TextMeshProUGUI _text;

    private Coroutine _hideRoutine;
    private System.Action<SongFinishedEvent> _onSongFinished;

    private void Awake()
    {
        var registry = FindFirstObjectByType<UIRegistry>();
        if (registry != null && registry.FinishBanner != null) WireUI(registry.FinishBanner);
        else Build();
    }

    private void OnEnable()
    {
        _onSongFinished = _ => Show();
        EventBus.Subscribe(_onSongFinished);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onSongFinished);
    }

    // ── Prefab path — see FinishBannerView's own doc ─────────────────────────────

    private void WireUI(FinishBannerView view)
    {
        _root = view.root.GetComponent<RectTransform>();
        _text = view.text;

        // Baked once at Editor-bake time — re-apply from the current locale here, same reasoning
        // as every other prefab-backed screen's labels.
        _text.text = Loc.Get("Countdown.Finish");

        _root.gameObject.SetActive(false);
    }

    // ── Build (procedural fallback — no UIRegistry in the scene yet) ─────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("FinishBanner", canvas);
        UIFactory.Stretch(_root);
        _root.SetAsLastSibling();

        _text = UIFactory.CreateText("Text", _root, Loc.Get("Countdown.Finish"), 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(700f, 200f));
        _text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        _root.gameObject.SetActive(false);
    }

    // ── Show / auto-hide ──────────────────────────────────────────────────────

    private void Show()
    {
        if (_hideRoutine != null) StopCoroutine(_hideRoutine);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(DisplaySeconds);
        _root.gameObject.SetActive(false);
        _hideRoutine = null;
    }
}
