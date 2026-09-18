using UnityEngine;

/// <summary>
/// Base class for any UI screen/view that should restyle itself whenever the app's Theme changes
/// — handles the ThemeChangedEvent subscribe/unsubscribe lifecycle ONCE (see Section "Subscribe/
/// Unsubscribe" of the app-flow/Theme refactor plan: every subscriber manages its own lifecycle,
/// no shared EventBus.Clear() crutch) so every concrete screen doesn't re-implement the same
/// boilerplate — a screen only has to implement ApplyUITheme(UIStyleSO).
///
/// Deliberately NOT a ThemeManager dependency at the call site: a screen never reaches for
/// ThemeManager.Instance itself (beyond the one-time "apply immediately if a theme is already
/// resolved" catch-up below) — it just reacts to the notification, per this project's
/// Command-vs-Notification rule (a screen doesn't COMMAND the theme system, it's told when
/// something changed).
/// </summary>
public abstract class ThemeReceiverBehaviour : MonoBehaviour, IThemeReceiver
{
    private System.Action<ThemeChangedEvent> _onThemeChanged;

    protected virtual void OnEnable()
    {
        _onThemeChanged = e => ApplyUITheme(e.Theme != null ? e.Theme.UI : null);
        EventBus.Subscribe(_onThemeChanged);

        // ThemeManager resolves a theme before any scene even loads (see AppBootstrap), so the
        // common case is "a theme already exists by the time this component enables" — apply it
        // immediately instead of waiting for the NEXT change, which might never come if nothing
        // else swaps the theme while this screen is visible.
        if (ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null)
            ApplyUITheme(ThemeManager.Instance.CurrentTheme.UI);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe(_onThemeChanged);
    }

    public abstract void ApplyUITheme(UIStyleSO ui);
}
