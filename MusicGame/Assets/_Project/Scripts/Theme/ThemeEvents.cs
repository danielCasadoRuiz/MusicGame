/// <summary>
/// Fired by ThemeManager right BEFORE it starts swapping to a new resolved theme (new content
/// already loaded, about to become current) — a future ThemeTransitionController can use this to
/// kick off a fade-out/crossfade start. Currently nothing subscribes to this (no real visual
/// transition exists yet — nothing in the game consumes ResolvedTheme for rendering as of this
/// phase), but the event exists now so that later work doesn't need to touch ThemeManager again.
/// </summary>
public struct ThemeChangingEvent { }

/// <summary>
/// Fired by ThemeManager right after CurrentTheme has been swapped to a newly resolved theme —
/// the one event anything showing Theme content should react to (a future UI/World/Player/
/// Collectible/VFX consumer refreshes itself from ThemeManager.CurrentTheme when this fires).
/// </summary>
public struct ThemeChangedEvent
{
    public ResolvedTheme Theme;
}
