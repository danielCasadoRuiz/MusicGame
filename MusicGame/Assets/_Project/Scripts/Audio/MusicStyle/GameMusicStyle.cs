/// <summary>
/// The GAME-facing musical style — a small, curated set of "music vibes" the game actually uses
/// (analyzing screen, theme choice), derived by MusicStyleResolver from the COMBINATION of the raw
/// musicnn tag probabilities and SongProfile features. Deliberately broad: the analysis cannot tell
/// real-world subgenres apart, so these are honest vibe buckets, not academic genres.
/// The raw tags (SongProfile.musicTags) are never modified; the legacy MusicStyleId stays the THEME
/// key (serialized in theme/animation assets) and is chosen per style by MusicStyleRulesSO.
/// Values are serialized as ints in MusicStyleRulesSO assets: never reorder, only append.
/// </summary>
public enum GameMusicStyle
{
    Unknown      = 0,
    Classical    = 1,  // instrumental, acoustic-orchestral feel, no beat-driven genre signal
    Cinematic    = 2,  // instrumental + big/intense dynamics
    Acoustic     = 3,  // acoustic / folk / country
    Rock         = 4,
    Heavy        = 5,  // metal / hard rock / punk, aggressive
    Electronic   = 6,
    Dance        = 7,  // dance / house / party, high danceability
    Groove       = 8,  // hip-hop / beat-driven
    Funky        = 9,
    Soulful      = 10, // soul / r&b / blues
    Jazzy        = 11,
    Catchy       = 12, // pop-ish, catchy, happy
    Energetic    = 13, // high intensity without a stronger genre signal
    Chill        = 14, // chillout / easy listening / mellow
    Dreamy       = 15, // ambient / beautiful
    Dark         = 16, // sad / minor / heavy mood
    Experimental = 17,
}
