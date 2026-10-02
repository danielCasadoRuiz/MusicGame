using UnityEngine;

/// <summary>
/// Data for MusicStyleResolver: one rule per GameMusicStyle. A style's score =
///   Σ (tag weight × that tag's raw musicnn probability)          — the 50 MSD tag labels, case-insensitive
/// + Σ (feature weight × feature value 0..1)                       — only once the full SongProfile exists
/// The highest score wins if it reaches minScore (else Unknown). Negative tag weights penalise
/// (e.g. Classical loses points for electronic/rock/pop). `themeStyle` is the legacy MusicStyleId whose
/// visual theme / runner animation style this game style uses. Tune freely — no code involved.
/// AppConfigSO.musicStyleRules (optional: without an asset the defaults below are used).
/// </summary>
[CreateAssetMenu(fileName = "MusicStyleRules", menuName = "MusicGame/Audio/Music Style Rules")]
public class MusicStyleRulesSO : ScriptableObject
{
    [System.Serializable]
    public struct TagWeight { public string tag; public float weight; }

    [System.Serializable]
    public class Rule
    {
        public GameMusicStyle style;
        [Tooltip("Visual theme / runner animation key used for this style (legacy enum, serialized in theme assets).")]
        public MusicStyleId themeStyle;
        public TagWeight[] tags = System.Array.Empty<TagWeight>();
        [Header("Feature weights (× value 0..1, full profile only)")]
        public float danceability;
        [Tooltip("× (1 − danceability) — rewards NOT being dance music.")]
        public float lowDanceability;
        [Tooltip("× mean aggressiveness.")]
        public float aggressiveness;
        [Tooltip("× mean intensity.")]
        public float intensity;
        [Tooltip("× (1 − mean intensity) — rewards calm songs.")]
        public float calmness;
        [Tooltip("× minor-mode confidence (0 when major).")]
        public float minorMode;
        [Tooltip("× beat confidence.")]
        public float beatConfidence;
    }

    [Tooltip("A style needs at least this score to be chosen; otherwise the result is Unknown.")]
    public float minScore = 0.18f;

    public Rule[] rules = DefaultRules();

    static TagWeight T(string tag, float w) => new() { tag = tag, weight = w };

    public static Rule[] DefaultRules() => new[]
    {
        new Rule { style = GameMusicStyle.Heavy, themeStyle = MusicStyleId.Metal,
                   tags = new[] { T("metal", 1f), T("heavy metal", 1f), T("hard rock", .7f), T("punk", .5f) }, aggressiveness = .25f },
        new Rule { style = GameMusicStyle.Rock, themeStyle = MusicStyleId.Rock,
                   tags = new[] { T("rock", 1f), T("alternative rock", .9f), T("classic rock", .9f), T("indie rock", .8f),
                                  T("Progressive rock", .8f), T("alternative", .6f), T("indie", .4f), T("guitar", .4f) } },
        new Rule { style = GameMusicStyle.Electronic, themeStyle = MusicStyleId.Electronic,
                   tags = new[] { T("electronic", 1f), T("electronica", .9f), T("electro", .8f) } },
        new Rule { style = GameMusicStyle.Dance, themeStyle = MusicStyleId.House,
                   tags = new[] { T("dance", 1f), T("House", 1f), T("party", .6f), T("electro", .3f) }, danceability = .3f },
        new Rule { style = GameMusicStyle.Groove, themeStyle = MusicStyleId.HipHop,
                   tags = new[] { T("Hip-Hop", 1f), T("rnb", .3f), T("funk", .2f) }, beatConfidence = .15f },
        new Rule { style = GameMusicStyle.Funky, themeStyle = MusicStyleId.Funk,
                   tags = new[] { T("funk", 1f), T("soul", .3f), T("sexy", .3f) }, danceability = .2f },
        new Rule { style = GameMusicStyle.Soulful, themeStyle = MusicStyleId.Soul,
                   tags = new[] { T("soul", 1f), T("rnb", .9f), T("blues", .6f), T("sexy", .3f) } },
        new Rule { style = GameMusicStyle.Jazzy, themeStyle = MusicStyleId.Jazz,
                   tags = new[] { T("jazz", 1f), T("blues", .2f), T("easy listening", .2f) } },
        new Rule { style = GameMusicStyle.Acoustic, themeStyle = MusicStyleId.Acoustic,
                   tags = new[] { T("acoustic", 1f), T("folk", .9f), T("country", .8f), T("guitar", .3f), T("Mellow", .2f) } },
        new Rule { style = GameMusicStyle.Catchy, themeStyle = MusicStyleId.Pop,
                   tags = new[] { T("pop", .8f), T("indie pop", .8f), T("catchy", .6f), T("happy", .3f) } },
        new Rule { style = GameMusicStyle.Energetic, themeStyle = MusicStyleId.Punk,
                   tags = new[] { T("party", .4f), T("happy", .3f), T("punk", .3f), T("dance", .2f) }, intensity = .35f, danceability = .1f },
        new Rule { style = GameMusicStyle.Chill, themeStyle = MusicStyleId.Ambient,
                   tags = new[] { T("chillout", 1f), T("chill", 1f), T("easy listening", .7f), T("Mellow", .6f) }, calmness = .15f },
        new Rule { style = GameMusicStyle.Dreamy, themeStyle = MusicStyleId.Ambient,
                   tags = new[] { T("ambient", 1f), T("beautiful", .6f), T("Mellow", .4f), T("instrumental", .3f) }, calmness = .1f },
        new Rule { style = GameMusicStyle.Dark, themeStyle = MusicStyleId.Blues,
                   tags = new[] { T("sad", .9f), T("metal", .2f) }, minorMode = .25f },
        new Rule { style = GameMusicStyle.Experimental, themeStyle = MusicStyleId.Experimental,
                   tags = new[] { T("experimental", 1f), T("Progressive rock", .2f), T("electronica", .2f) } },
        new Rule { style = GameMusicStyle.Classical, themeStyle = MusicStyleId.Classical,
                   tags = new[] { T("instrumental", .6f), T("beautiful", .3f), T("Mellow", .2f),
                                  T("electronic", -.5f), T("rock", -.5f), T("pop", -.4f), T("dance", -.4f), T("Hip-Hop", -.5f) },
                   lowDanceability = .2f, calmness = .1f },
        new Rule { style = GameMusicStyle.Cinematic, themeStyle = MusicStyleId.Experimental,
                   tags = new[] { T("instrumental", .5f), T("beautiful", .2f), T("ambient", .2f), T("electronic", -.2f), T("pop", -.3f) },
                   intensity = .3f, lowDanceability = .1f },
    };
}
