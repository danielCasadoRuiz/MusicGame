using UnityEngine;

/// <summary>
/// A visual override layer independent of music (Christmas, Halloween, a launch promo...) — NEVER
/// a MusicStyleId (see MusicStyleId's own doc: music info and Theme are deliberately separate
/// concepts). See VisualOverrideLayer for the shared optional-fields shape (null = fall through).
///
/// `mode` is authoring INTENT, not a different resolution algorithm — ThemeResolver applies the
/// exact same "Event ?? Style ?? Base" per-category merge either way. Overlay means the event's
/// author only expects to fill in a couple of categories (leaving the rest to Style/Base);
/// FullOverride means the event's author is expected to fill in every category itself. No
/// scheduling/date logic yet (see the app-flow/Theme refactor plan — that's intentionally not
/// built now, just left possible: nothing here assumes exactly one EventTheme can ever be active).
/// </summary>
public enum EventThemeMode { Overlay, FullOverride }

[CreateAssetMenu(fileName = "EventTheme", menuName = "MusicGame/Theme/Event Theme")]
public class EventThemeSO : VisualOverrideLayer
{
    public string         eventId;
    public EventThemeMode mode = EventThemeMode.Overlay;
}
