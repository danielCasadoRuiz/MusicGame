using System;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// BONUS LAYOUTS (contract prepared in P3, implemented in P4 — BonusLayoutProvider / FileBonusLayoutStore).
//
// Two separate things, never mixed:
//   1. the MUSIC PROFILE  — prepared ONCE per song + revision (published, SongCatalogIndexSO / the binary
//      profile). Level-, difficulty- and attempt-independent: it keeps every detection, family, strength,
//      beat, structure, style and the world/visual arrays. It NEVER contains a bonus distribution.
//   2. a BONUS LAYOUT     — the playable selection of those musical moments for one concrete combination
//      (song/profile revision × campaign position × difficulty × generator × geometry/movement). Generated
//      the FIRST time that combination is played, SAVED complete, and LOADED (never re-drawn, never
//      regenerated) on every later play of the same combination. Combinations are never precomputed.
//
// The generation seed is DERIVED from the key (BonusLayoutKey.Seed): variety between combinations,
// repeatability within one, and a missing/corrupt file is rebuilt identically from the same profile +
// config + generator version. A stored layout is still loaded rather than regenerated, so its file is the
// reference even if the generation code would now produce something else under the same version number.
//
// The REUSE KEY contains everything that changes the result and nothing that does not:
//   + song id and the PROFILE REVISION (master + playback revision + profile body CRC + pipeline/params);
//   + campaign POSITION (level + match slot, e.g. "L2#3") — the same song at another position is another layout;
//   + DIFFICULTY as a continuous value (it rises between matches of the same level — not 4 fixed variants);
//   + GENERATOR VERSION (the selection algorithm and its tuning);
//   + GEOMETRY / MOVEMENT hash (the config that decides selection, reach and placement — core movement,
//     collectibles, level generation; diagnostics excluded; dev layout overrides only while active);
//   + the campaign LAP — ONLY when the lap actually changes the difficulty (otherwise omitted, so laps share);
//   − never the ATTEMPT number (a retry replays the same layout).
// ─────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Identity of the music profile revision a layout was generated from. A different revision ⇒ a
/// different key (the layouts of the old revision are pruned when the new one saves).</summary>
[Serializable]
public struct ProfileRevision : IEquatable<ProfileRevision>
{
    public string songId;
    public ulong  masterIdentity;
    public ulong  playbackRevision;
    public uint   profileCrc;
    public int    pipelineVersion;
    public ulong  paramsHash;

    public static ProfileRevision Of(SongProfile p) => new()
    {
        songId = string.IsNullOrEmpty(p.songKey) ? "song" : p.songKey,
        masterIdentity = p.audioIdentity, playbackRevision = p.playbackRevision, profileCrc = p.profileCrc,
        pipelineVersion = p.pipelineVersion, paramsHash = p.paramsHash,
    };

    /// <summary>Short stable tag of the revision (file names, pruning).</summary>
    public string Tag
    {
        get
        {
            var h = MusicGame.Audio.Analysis.StableHash.Create();
            h.Add(masterIdentity); h.Add(playbackRevision); h.Add((long)profileCrc); h.Add(pipelineVersion); h.Add(paramsHash);
            return (h.Value & 0xFFFFFFFFUL).ToString("x8");
        }
    }

    public bool Equals(ProfileRevision o) =>
        songId == o.songId && masterIdentity == o.masterIdentity && playbackRevision == o.playbackRevision &&
        profileCrc == o.profileCrc && pipelineVersion == o.pipelineVersion && paramsHash == o.paramsHash;
    public override bool Equals(object obj) => obj is ProfileRevision o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(songId, masterIdentity, playbackRevision, profileCrc, pipelineVersion, paramsHash);
}

/// <summary>
/// The reuse key of a stored bonus layout. Equal keys ⇒ the stored layout is loaded as-is.
/// <see cref="LapIfItChangesDifficulty"/> stays null unless the campaign makes that lap harder. Difficulty is
/// quantised (<see cref="DifficultyQuantum"/>) so float noise never forks keys. No attempt number by design.
/// </summary>
[Serializable]
public struct BonusLayoutKey : IEquatable<BonusLayoutKey>
{
    public const float DifficultyQuantum = 0.001f;

    public ProfileRevision profile;
    public string campaignPosition;          // CampaignService position id ("L2#3"); "free" outside the campaign
    public int    difficultyMilli;           // continuous difficulty × 1000 (quantised)
    public int    generatorVersion;
    public ulong  geometryHash;              // movement/geometry/selection parameters that decide the layout
    public int    lapIfItChangesDifficulty;  // 0 = lap irrelevant (omitted); otherwise the lap number

    public float Difficulty => difficultyMilli * DifficultyQuantum;
    public int? LapIfItChangesDifficulty => lapIfItChangesDifficulty > 0 ? lapIfItChangesDifficulty : (int?)null;

    public static BonusLayoutKey Create(ProfileRevision profile, string campaignPosition, float difficulty,
                                        int generatorVersion, ulong geometryHash, int? lapIfItChangesDifficulty = null)
        => new()
        {
            profile = profile,
            campaignPosition = string.IsNullOrEmpty(campaignPosition) ? "free" : campaignPosition,
            difficultyMilli = (int)Math.Round(difficulty / DifficultyQuantum),
            generatorVersion = generatorVersion,
            geometryHash = geometryHash,
            lapIfItChangesDifficulty = lapIfItChangesDifficulty.GetValueOrDefault() > 0 ? lapIfItChangesDifficulty.Value : 0,
        };

    /// <summary>The generation seed of this combination: stable, derived from the whole key (never random).</summary>
    public int Seed
    {
        get
        {
            var h = MusicGame.Audio.Analysis.StableHash.Create();
            h.Add("MusicGame.BonusLayoutSeed");
            h.Add(ToString());
            int s = (int)(h.Value ^ (h.Value >> 32));
            return s != 0 ? s : 1;
        }
    }

    public bool Equals(BonusLayoutKey o) =>
        profile.Equals(o.profile) && campaignPosition == o.campaignPosition && difficultyMilli == o.difficultyMilli &&
        generatorVersion == o.generatorVersion && geometryHash == o.geometryHash && lapIfItChangesDifficulty == o.lapIfItChangesDifficulty;
    public override bool Equals(object obj) => obj is BonusLayoutKey o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(profile, campaignPosition, difficultyMilli, generatorVersion, geometryHash, lapIfItChangesDifficulty);

    /// <summary>Stable text form (stored in the file, hashed into its name). Same key ⇒ same string everywhere.</summary>
    public override string ToString() =>
        $"{profile.songId}_{profile.masterIdentity:x16}_{profile.playbackRevision:x16}_{profile.profileCrc:x8}_p{profile.pipelineVersion}_{profile.paramsHash:x16}" +
        $"_{campaignPosition}_d{difficultyMilli}_g{generatorVersion}_m{geometryHash:x16}" + (lapIfItChangesDifficulty > 0 ? $"_lap{lapIfItChangesDifficulty}" : "");
}

/// <summary>A stored layout: its key, seed, when it was generated, the selection counts, and the complete
/// placed events (immutable once saved). Macro events and rarity are rebuilt from the profile on load
/// (deterministic, no randomness).</summary>
[Serializable]
public sealed class BonusLayoutRecord
{
    public BonusLayoutKey key;
    public int            seed;
    public long           generatedAtUnixSeconds;
    public GameplayTimeline.SelectionStats stats;
    public TimelineEvent[] events = Array.Empty<TimelineEvent>();
}

/// <summary>Persistence of generated layouts — kept apart from the music profile, its cache and the save.</summary>
public interface IBonusLayoutStore
{
    /// <summary>False when absent; a present but unreadable / foreign file is reported through `problem`.</summary>
    bool TryLoad(in BonusLayoutKey key, out BonusLayoutRecord record, out string problem);
    void Save(BonusLayoutRecord record);
    /// <summary>Drops the layouts of `songId` generated from another profile revision (content update).</summary>
    int PruneStale(string songId, ProfileRevision current);
}

/// <summary>Load the stored layout of a key, or generate it ONCE and save it.</summary>
public interface IBonusLayoutProvider
{
    BonusLayoutResult GetOrCreate(SongProfile profile, MusicRunnerGameplayConfig config, MusicPath path, float difficulty,
                                  string campaignPosition, int lapIfItChangesDifficulty);
}

public enum BonusLayoutOutcome
{
    /// <summary>First play of this combination: generated and saved.</summary>
    Generated,
    /// <summary>Loaded from its stored file — nothing regenerated.</summary>
    Loaded,
    /// <summary>A stored file existed but was unreadable / not this key: rebuilt with the same seed and saved.</summary>
    Rebuilt,
}

public readonly struct BonusLayoutResult
{
    public readonly GameplayTimeline Timeline;
    public readonly BonusLayoutRecord Record;
    public readonly BonusLayoutOutcome Outcome;
    public readonly double Milliseconds;
    public readonly string Problem;

    public BonusLayoutResult(GameplayTimeline timeline, BonusLayoutRecord record, BonusLayoutOutcome outcome, double ms, string problem)
    { Timeline = timeline; Record = record; Outcome = outcome; Milliseconds = ms; Problem = problem; }
}
