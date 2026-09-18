/// <summary>
/// Contract for "this UI element knows how to restyle itself from a UIStyleSO" — see
/// ThemeReceiverBehaviour for the base class that actually wires this to ThemeChangedEvent. Kept
/// as a separate interface (rather than only the base class) so a screen that can't inherit
/// ThemeReceiverBehaviour for some reason (already extends something else) can still implement
/// this directly.
/// </summary>
public interface IThemeReceiver
{
    void ApplyUITheme(UIStyleSO ui);
}
