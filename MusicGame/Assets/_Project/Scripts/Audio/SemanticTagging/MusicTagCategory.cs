using System.Collections.Generic;

public enum MusicTagCategory { Style, Vibe, Other }

/// <summary>
/// Purely a PRESENTATION grouping of MSD_musicnn's raw 50-tag vocabulary into three simple
/// buckets (STYLE = genre-ish, VIBE = mood/energy, OTHER = decade/vocal-presence/misc) so the
/// UI can show something more scannable than one long flat list. The raw MusicTagScore[] on
/// SongProfile is never altered by this — a tag's real score is always the model's own output.
/// </summary>
public static class MusicTagClassifier
{
    private static readonly Dictionary<string, MusicTagCategory> Map = new()
    {
        // ── Style (genre) ──────────────────────────────────────────────────────
        { "rock", MusicTagCategory.Style }, { "pop", MusicTagCategory.Style },
        { "alternative", MusicTagCategory.Style }, { "indie", MusicTagCategory.Style },
        { "electronic", MusicTagCategory.Style }, { "alternative rock", MusicTagCategory.Style },
        { "jazz", MusicTagCategory.Style }, { "metal", MusicTagCategory.Style },
        { "classic rock", MusicTagCategory.Style }, { "soul", MusicTagCategory.Style },
        { "indie rock", MusicTagCategory.Style }, { "electronica", MusicTagCategory.Style },
        { "folk", MusicTagCategory.Style }, { "instrumental", MusicTagCategory.Style },
        { "punk", MusicTagCategory.Style }, { "blues", MusicTagCategory.Style },
        { "hard rock", MusicTagCategory.Style }, { "ambient", MusicTagCategory.Style },
        { "acoustic", MusicTagCategory.Style }, { "experimental", MusicTagCategory.Style },
        { "guitar", MusicTagCategory.Style }, { "Hip-Hop", MusicTagCategory.Style },
        { "country", MusicTagCategory.Style }, { "funk", MusicTagCategory.Style },
        { "electro", MusicTagCategory.Style }, { "heavy metal", MusicTagCategory.Style },
        { "Progressive rock", MusicTagCategory.Style }, { "rnb", MusicTagCategory.Style },
        { "indie pop", MusicTagCategory.Style }, { "House", MusicTagCategory.Style },

        // ── Vibe (mood / energy) ───────────────────────────────────────────────
        { "dance", MusicTagCategory.Vibe }, { "beautiful", MusicTagCategory.Vibe },
        { "chillout", MusicTagCategory.Vibe }, { "Mellow", MusicTagCategory.Vibe },
        { "chill", MusicTagCategory.Vibe }, { "party", MusicTagCategory.Vibe },
        { "sexy", MusicTagCategory.Vibe }, { "catchy", MusicTagCategory.Vibe },
        { "easy listening", MusicTagCategory.Vibe }, { "sad", MusicTagCategory.Vibe },
        { "happy", MusicTagCategory.Vibe },

        // ── Other (decade / vocal presence / misc) ─────────────────────────────
        { "female vocalists", MusicTagCategory.Other }, { "00s", MusicTagCategory.Other },
        { "male vocalists", MusicTagCategory.Other }, { "80s", MusicTagCategory.Other },
        { "90s", MusicTagCategory.Other }, { "female vocalist", MusicTagCategory.Other },
        { "70s", MusicTagCategory.Other }, { "60s", MusicTagCategory.Other },
        { "oldies", MusicTagCategory.Other },
    };

    public static MusicTagCategory Classify(string tag) =>
        Map.TryGetValue(tag, out var cat) ? cat : MusicTagCategory.Other;
}
