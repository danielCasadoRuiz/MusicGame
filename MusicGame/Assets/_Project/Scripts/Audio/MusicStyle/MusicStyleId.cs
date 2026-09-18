/// <summary>
/// Central, versioned identifier for "what musical style/genre is this song" — MUSIC information,
/// never to be confused with THEME (visual presentation, see the Theme system). Nothing in the
/// project should compare raw genre strings ("funk", "House", "Hip-Hop"...) directly; everything
/// downstream of Song Analysis (Theme resolution, UI, debug tools) reads this enum instead.
///
/// Deliberately a flat enum, not a free-form string: stable values here matter for anything ever
/// serialized (session data, presets, Theme→Style mappings) and give the compiler exhaustiveness
/// checks a string could never give. Unknown/Unclassified is a real, expected result — a song with
/// no confident Style tag should resolve here, not to an arbitrary guess.
///
/// A handful of values (Disco, Techno) don't have a mapped tag in the current MSD_musicnn
/// vocabulary yet (see TagBasedMusicStyleClassifier) — they're pre-declared anyway so a future,
/// smarter classifier can start producing them without renumbering/breaking anything already
/// relying on this enum's existing values.
/// </summary>
public enum MusicStyleId
{
    Unknown = 0,
    Rock,
    Pop,
    Electronic,
    House,
    Techno,
    Jazz,
    Metal,
    Soul,
    Funk,
    Disco,
    HipHop,
    RnB,
    Folk,
    Country,
    Punk,
    Blues,
    Ambient,
    Acoustic,
    Experimental,
}
