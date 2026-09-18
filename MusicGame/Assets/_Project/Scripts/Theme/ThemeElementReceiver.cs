using System.Collections;
using UnityEngine;

/// <summary>
/// Shared subscribe/transition lifecycle for the GENERIC, per-element Theme receivers
/// (ThemeColorReceiver/ThemeTextReceiver/ThemeImageReceiver) — the element-level equivalent of
/// ThemeReceiverBehaviour (which stays as the SCREEN-level base class for the handful of controllers
/// that haven't been split into prefab + receivers yet). Handles ThemeChangingEvent subscribe/
/// unsubscribe, the "already-settled theme" catch-up on enable (Section 15 of the Theme/UI plan:
/// a receiver that was inactive during a Theme swap must still show the CURRENT theme correctly the
/// instant it appears, never depend on having been alive for the transition), and the actual
/// interpolation timing — a concrete receiver only implements WHAT to apply, never HOW/WHEN.
/// </summary>
public abstract class ThemeElementReceiver : MonoBehaviour
{
    private System.Action<ThemeChangingEvent> _onThemeChanging;
    private Coroutine _transition;

    protected virtual void OnEnable()
    {
        _onThemeChanging = e => BeginTransition(e.Old?.UI, e.New?.UI);
        EventBus.Subscribe(_onThemeChanging);

        if (ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme?.UI != null)
            Apply(ThemeManager.Instance.CurrentTheme.UI);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe(_onThemeChanging);
        if (_transition != null) { StopCoroutine(_transition); _transition = null; }
    }

    private void BeginTransition(UIStyleSO oldUI, UIStyleSO newUI)
    {
        if (newUI == null) return;

        // No previous state (the very first resolve at boot) — nothing to interpolate FROM.
        if (oldUI == null) { Apply(newUI); return; }

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(TransitionRoutine(oldUI, newUI));
    }

    private IEnumerator TransitionRoutine(UIStyleSO oldUI, UIStyleSO newUI)
    {
        float duration = ThemeTransitionController.Duration;
        if (duration <= 0f) { Apply(newUI); _transition = null; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            ApplyBlend(oldUI, newUI, Mathf.Clamp01(t / duration));
            yield return null;
        }

        Apply(newUI); // land exactly on the real target — no float-accumulation drift
        _transition = null;
    }

    /// <summary>Apply the real, final token value(s) instantly — used for the OnEnable catch-up and
    /// to land exactly on target once a transition finishes.</summary>
    protected abstract void Apply(UIStyleSO ui);

    /// <summary>Apply a blended intermediate state, `t` going 0→1 from `from` to `to`. Default: a
    /// hard swap at the midpoint (valid for anything non-interpolable) — override for real color
    /// interpolation or a sprite fade.</summary>
    protected virtual void ApplyBlend(UIStyleSO from, UIStyleSO to, float t)
    {
        if (t >= 0.5f) Apply(to);
    }
}
