using System.Collections.Generic;

/// <summary>
/// Provisional IMusicStyleClassifier — deliberately NOT a new/separate ML model. The project
/// already runs a real 50-tag semantic classifier (SentisMusicTagger/MSD_musicnn) over every
/// song's audio; this just maps its STYLE-category tags (see MusicTagClassifier) onto the game's
/// own MusicStyleId vocabulary. Picks the highest-scoring Style tag with a known mapping, above a
/// modest confidence floor — profile.musicTags is already sorted by descending score (see
/// IMusicTagger's own doc), so the first match found IS the best one; no sorting/aggregation
/// needed here.
///
/// Tags with no real genre meaning (e.g. "instrumental", "guitar") are intentionally left
/// unmapped — Classify() just keeps looking at the next-highest Style tag instead of forcing a
/// meaningless mapping.
///
/// This can be swapped for a smarter classifier later (mixed styles, real confidence-weighted
/// blending, a dedicated genre model) without any consumer noticing — they only ever see
/// IMusicStyleClassifier/MusicStyleId.
/// </summary>
public class TagBasedMusicStyleClassifier : IMusicStyleClassifier
{
    // musicnn's multi-label sigmoid scores rarely all sit high at once — this is just a noise
    // floor, not a "confident detection" threshold. Below it, a song is treated as Unknown rather
    // than trusting a near-zero score.
    private const float MinConfidence = 0.15f;

    private static readonly Dictionary<string, MusicStyleId> TagToStyle =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "rock",              MusicStyleId.Rock },
        { "alternative",       MusicStyleId.Rock },
        { "alternative rock",  MusicStyleId.Rock },
        { "classic rock",      MusicStyleId.Rock },
        { "indie rock",        MusicStyleId.Rock },
        { "hard rock",         MusicStyleId.Rock },
        { "progressive rock",  MusicStyleId.Rock },
        { "indie",             MusicStyleId.Rock },

        { "pop",               MusicStyleId.Pop },
        { "indie pop",         MusicStyleId.Pop },

        { "electronic",        MusicStyleId.Electronic },
        { "electronica",       MusicStyleId.Electronic },
        { "electro",           MusicStyleId.Electronic },

        { "house",             MusicStyleId.House },

        { "jazz",              MusicStyleId.Jazz },

        { "metal",             MusicStyleId.Metal },
        { "heavy metal",       MusicStyleId.Metal },

        { "soul",              MusicStyleId.Soul },
        { "funk",              MusicStyleId.Funk },

        { "hip-hop",           MusicStyleId.HipHop },

        { "rnb",               MusicStyleId.RnB },

        { "folk",              MusicStyleId.Folk },
        { "country",           MusicStyleId.Country },
        { "punk",              MusicStyleId.Punk },
        { "blues",             MusicStyleId.Blues },
        { "ambient",           MusicStyleId.Ambient },
        { "acoustic",          MusicStyleId.Acoustic },
        { "experimental",      MusicStyleId.Experimental },
    };

    public MusicStyleId Classify(SongProfile profile)
    {
        if (profile == null || !profile.HasMusicTags) return MusicStyleId.Unknown;

        foreach (var t in profile.musicTags)
        {
            if (t.score < MinConfidence) break; // sorted descending — nothing after this scores higher
            if (MusicTagClassifier.Classify(t.tag) != MusicTagCategory.Style) continue;
            if (TagToStyle.TryGetValue(t.tag, out var style)) return style;
        }

        return MusicStyleId.Unknown;
    }
}
