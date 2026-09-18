using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for any UI screen/view that should restyle itself whenever the app's Theme changes
/// — handles the ThemeChangingEvent subscribe/unsubscribe lifecycle ONCE (see Section "Subscribe/
/// Unsubscribe" of the app-flow/Theme refactor plan: every subscriber manages its own lifecycle,
/// no shared EventBus.Clear() crutch) so every concrete screen doesn't re-implement the same
/// boilerplate — a screen only has to implement ApplyUITheme(UIStyleSO).
///
/// Deliberately NOT a ThemeManager dependency at the call site: a screen never reaches for
/// ThemeManager.Instance itself (beyond the one-time "apply immediately if a theme is already
/// resolved" catch-up below) — it just reacts to the notification, per this project's
/// Command-vs-Notification rule (a screen doesn't COMMAND the theme system, it's told when
/// something changed).
///
/// TRANSITION (Section 4 of the multi-scene refactor plan): reacting to ThemeChangingEvent (which
/// carries both Old and New) instead of ThemeChangedEvent (New only) is what lets this interpolate
/// colors over ThemeTransitionController.Duration instead of snapping instantly — every existing
/// concrete screen gets this for free purely by implementing ApplyUITheme(UIStyleSO), since the
/// blended intermediate values are delivered through that exact same method. A screen with its own
/// sprites/fonts that wants a real crossfade (rather than the default hard swap partway through the
/// blend — see ApplyBlendedTheme) overrides ApplyBlendedTheme itself; ThemeTransitionController
/// never needs to know that screen's internals either way.
/// </summary>
public abstract class ThemeReceiverBehaviour : MonoBehaviour, IThemeReceiver
{
    private System.Action<ThemeChangingEvent> _onThemeChanging;
    private Coroutine _transitionRoutine;
    private UIStyleSO _blendScratch;

    protected virtual void OnEnable()
    {
        _onThemeChanging = e => BeginTransition(e.Old?.UI, e.New?.UI);
        EventBus.Subscribe(_onThemeChanging);

        // ThemeManager resolves a theme before any scene even loads (see AppBootstrap), so the
        // common case is "a theme already exists by the time this component enables" — apply it
        // immediately instead of waiting for the NEXT change, which might never come if nothing
        // else swaps the theme while this screen is visible. This is a screen JOINING an already-
        // settled theme, not a change happening — always instant, never a transition.
        if (ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null)
            ApplyUITheme(ThemeManager.Instance.CurrentTheme.UI);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe(_onThemeChanging);
        if (_transitionRoutine != null) { StopCoroutine(_transitionRoutine); _transitionRoutine = null; }
        if (_blendScratch != null) { Destroy(_blendScratch); _blendScratch = null; }
    }

    private void BeginTransition(UIStyleSO oldUI, UIStyleSO newUI)
    {
        if (newUI == null) return;

        // No previous state (the very first resolve at boot) — nothing to interpolate FROM, so
        // just snap straight to the target instead of animating from an undefined starting point.
        if (oldUI == null) { ApplyUITheme(newUI); return; }

        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(TransitionRoutine(oldUI, newUI));
    }

    private IEnumerator TransitionRoutine(UIStyleSO oldUI, UIStyleSO newUI)
    {
        float duration = ThemeTransitionController.Duration;
        if (duration <= 0f) { ApplyUITheme(newUI); _transitionRoutine = null; yield break; }

        if (_blendScratch == null) _blendScratch = ScriptableObject.CreateInstance<UIStyleSO>();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            ApplyBlendedTheme(oldUI, newUI, Mathf.Clamp01(t / duration));
            yield return null;
        }

        ApplyUITheme(newUI); // land exactly on the real target — no float-accumulation drift
        _transitionRoutine = null;
    }

    /// <summary>Called every frame during a transition with `t` going 0→1 from `from` to `to`.
    /// Default behaviour matches Section 4's minimal contract: Colors interpolate; non-interpolable
    /// assets (sprite/font — "assets no interpolables: fade out → swap → fade in") hard-swap at the
    /// midpoint, which is the simplest valid instance of that shape without needing a real dual-
    /// image crossfade for content nothing currently uses. Override this (not ApplyUITheme) if a
    /// screen has its own sprite/font content and wants an actual crossfade instead.</summary>
    protected virtual void ApplyBlendedTheme(UIStyleSO from, UIStyleSO to, float t)
    {
        _blendScratch.primaryColor          = Color.Lerp(from.primaryColor,          to.primaryColor,          t);
        _blendScratch.secondaryColor        = Color.Lerp(from.secondaryColor,        to.secondaryColor,        t);
        _blendScratch.accentColor           = Color.Lerp(from.accentColor,           to.accentColor,           t);
        _blendScratch.backgroundColor       = Color.Lerp(from.backgroundColor,       to.backgroundColor,       t);
        _blendScratch.surfaceColor          = Color.Lerp(from.surfaceColor,          to.surfaceColor,          t);
        _blendScratch.surfaceSecondaryColor = Color.Lerp(from.surfaceSecondaryColor, to.surfaceSecondaryColor, t);
        _blendScratch.textPrimaryColor      = Color.Lerp(from.textPrimaryColor,      to.textPrimaryColor,      t);
        _blendScratch.textSecondaryColor    = Color.Lerp(from.textSecondaryColor,    to.textSecondaryColor,    t);
        _blendScratch.buttonPrimaryColor    = Color.Lerp(from.buttonPrimaryColor,    to.buttonPrimaryColor,    t);
        _blendScratch.buttonSecondaryColor  = Color.Lerp(from.buttonSecondaryColor,  to.buttonSecondaryColor,  t);
        _blendScratch.positiveColor         = Color.Lerp(from.positiveColor,         to.positiveColor,         t);
        _blendScratch.negativeColor         = Color.Lerp(from.negativeColor,         to.negativeColor,         t);
        _blendScratch.panelSprite           = t < 0.5f ? from.panelSprite      : to.panelSprite;
        _blendScratch.backgroundSprite      = t < 0.5f ? from.backgroundSprite : to.backgroundSprite;
        _blendScratch.patternSprite         = t < 0.5f ? from.patternSprite    : to.patternSprite;
        _blendScratch.displayFont           = t < 0.5f ? from.displayFont      : to.displayFont;
        _blendScratch.bodyFont              = t < 0.5f ? from.bodyFont         : to.bodyFont;
        _blendScratch.layoutVariant         = t < 0.5f ? from.layoutVariant    : to.layoutVariant;
        ApplyUITheme(_blendScratch);
    }

    public abstract void ApplyUITheme(UIStyleSO ui);
}
