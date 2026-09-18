/// <summary>
/// Fired by ThemeManager right as it starts swapping to a new resolved theme — carries BOTH the
/// theme being left (Old, null on the very first-ever resolve at boot) and the one being swapped to
/// (New, never null) so a receiver can interpolate between them (Section 4 of the multi-scene
/// refactor plan: "ThemeChanging(old, new) → receivers transition → ThemeChanged(new)"). See
/// ThemeReceiverBehaviour — every screen already gets this transition for free through it.
/// </summary>
public struct ThemeChangingEvent
{
    public ResolvedTheme Old;
    public ResolvedTheme New;
}

/// <summary>
/// Fired by ThemeManager right after CurrentTheme has been swapped to a newly resolved theme —
/// the one event anything showing Theme content should react to (a future UI/World/Player/
/// Collectible/VFX consumer refreshes itself from ThemeManager.CurrentTheme when this fires).
/// </summary>
public struct ThemeChangedEvent
{
    public ResolvedTheme Theme;
}
