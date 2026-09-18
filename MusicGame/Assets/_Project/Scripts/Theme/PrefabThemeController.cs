using UnityEngine;

/// <summary>
/// Base for a PREFAB-SPECIFIC theme controller — the second half of the hybrid model (Section 4/14
/// of the Theme/UI refactor plan): generic receivers (ThemeColorReceiver/ThemeTextReceiver/
/// ThemeImageReceiver) resolve simple, repetitive token binding on individual children; this class
/// exists ONLY for a prefab's own special visual behavior that a generic receiver can't express —
/// activating/deactivating decoration, switching an internal layout variant (see
/// UIStyleSO.layoutVariant), coordinating a multi-part animation, etc.
///
/// A trivial prefab needs NO controller at all — only add one (e.g. TopBarThemeController,
/// MainMenuThemeController) when the prefab genuinely has that kind of behavior. This class must
/// NEVER re-implement what a generic receiver already does (no `_someText.color = ...` in here —
/// that belongs on a ThemeColorReceiver/ThemeTextReceiver sitting on that child instead).
///
/// Same event model as every other Theme consumer: OnThemeChanging fires as a swap begins (Old is
/// null on the very first resolve at boot), and OnThemeSettled catches this controller up
/// immediately if it becomes active with an already-resolved theme (Section 15: never depend on
/// having been alive for the actual transition).
/// </summary>
public abstract class PrefabThemeController : MonoBehaviour
{
    private System.Action<ThemeChangingEvent> _onThemeChanging;

    protected virtual void OnEnable()
    {
        _onThemeChanging = e => OnThemeChanging(e.Old, e.New);
        EventBus.Subscribe(_onThemeChanging);

        if (ThemeManager.Instance != null && ThemeManager.Instance.CurrentTheme != null)
            OnThemeSettled(ThemeManager.Instance.CurrentTheme);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe(_onThemeChanging);
    }

    /// <summary>A theme swap just started — `oldTheme` is null on the very first resolve at boot.
    /// Override to kick off/coordinate special behavior alongside the generic receivers (which
    /// react to this same moment independently).</summary>
    protected virtual void OnThemeChanging(ResolvedTheme oldTheme, ResolvedTheme newTheme) { }

    /// <summary>Called once, immediately, whenever this controller becomes active with an
    /// already-resolved CurrentTheme — apply this prefab's special behavior for the CURRENT theme
    /// here, not only in reaction to future changes.</summary>
    protected virtual void OnThemeSettled(ResolvedTheme currentTheme) { }
}
