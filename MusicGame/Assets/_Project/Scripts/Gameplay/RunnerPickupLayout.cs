/// <summary>
/// Which pickup DISTRIBUTION the Runner plays — the seed that turns the song's musical events into
/// concrete pickups (GameplayTimeline.Generate), kept separate from the song analysis itself:
///   - SongProfile (analysis, cached per song in SongCache) = the musical events (onsets, beats,
///     impacts, bands). Identical for every match on that song.
///   - Pickup distribution = which candidates survive thinning, patterns, lateral/height rolls,
///     off-track / elevated picks, Life/Special slots. Generated ONCE at match preparation from the
///     events + this seed (deterministic: same events + same seed + same generator = same layout);
///     nothing is re-rolled during the race (activation only reads the generated events).
///
/// MATCH IDENTITY (when a distribution is RESUMED vs NEW):
///   - A NEW match = a new PendingRunData (RunAssignment.CreatePendingRun: CONTINUE with no valid pending
///     run, a route song after a victory, a confirmed SELECT SONG of a different song) → a NEW random seed,
///     saved with the pending run before the Runner starts. Local-file runs (no persistence) get a new
///     session-only seed per assignment.
///   - The SAME match = the same pending run: CONTINUE / re-selecting the pending song
///     (RunAssignment.TryRestorePending), Replay Song and the pause-menu Restart → the STORED seed,
///     so leaving and coming back replays the exact same distribution. A victory clears the pending run;
///     the next assignment is a new match.
///   - Old saves (pending run without a seed, pickupSeed == 0) get one seed the first time that pending
///     run is restored, saved at once — from then on it is fixed like any other.
/// No assignment at all (the Runner scene played directly in the editor) → the config's collectibleSeed.
///
/// SINCE P4 the layout no longer uses this per-match seed: BonusLayoutProvider derives the seed from the
/// combination (BonusLayoutKey — song/profile revision, position, difficulty, generator, config) and stores
/// the layout, so the same combination always replays the same layout and a retry never re-draws it. The
/// stored pickupSeed stays in saves (compatibility) and GeneratorVersion stays the generator's version stamp.
/// </summary>
public static class RunnerPickupLayout
{
    /// <summary>Generator version stamped next to the seed. Bump it when GameplayTimeline's placement
    /// changes in a way that makes an old seed produce a different layout (the stored seed is still
    /// reused — the match identity is kept — and the stamp is updated with a log line).</summary>
    // 3 (P4): selection from the detected attacks (onsetEvents) at a continuous difficulty, layout seed
    //    derived from the BonusLayoutKey (no longer this per-match seed), layouts stored per combination.
    public const int GeneratorVersion = 4; // 2: ground-true placement (real mesh) + trigger-volume reach model; 4: track-margin (edgeChance) progression

    /// <summary>The seed of the match being played (null = no assignment → config seed).</summary>
    public static int? CurrentSeed { get; private set; }

    /// <summary>The seed GameplayTimeline.Generate must use for the run that is starting now.</summary>
    public static int SeedFor(MusicRunnerCollectiblesConfig collectibles) =>
        CurrentSeed ?? (collectibles != null ? collectibles.collectibleSeed : 0);

    /// <summary>A fresh non-zero seed (0 means "not assigned yet" in PendingRunData).</summary>
    public static int NewSeed()
    {
        int seed = System.Guid.NewGuid().GetHashCode();
        return seed != 0 ? seed : 1;
    }

    /// <summary>Gives a pending run its seed if it has none (new run, or an old save) and stamps the
    /// generator version. Returns true when the run changed and must be saved.</summary>
    public static bool EnsureSeed(PendingRunData run)
    {
        if (run == null) return false;
        bool changed = false;
        if (run.pickupSeed == 0)
        {
            run.pickupSeed = NewSeed();
            changed = true;
        }
        if (run.pickupLayoutVersion != GeneratorVersion)
        {
            if (run.pickupLayoutVersion != 0)
                UnityEngine.Debug.Log($"[RunnerPickupLayout] Pending run seed {run.pickupSeed} was generated with layout v{run.pickupLayoutVersion}; " +
                                      $"now generated with v{GeneratorVersion} (same seed, updated generator).");
            run.pickupLayoutVersion = GeneratorVersion;
            changed = true;
        }
        return changed;
    }

    /// <summary>Makes `run`'s stored seed the one the next Runner start uses (call after EnsureSeed).</summary>
    public static void Apply(PendingRunData run)
    {
        CurrentSeed = run != null && run.pickupSeed != 0 ? run.pickupSeed : (int?)null;
    }

    /// <summary>A run that is not persisted (local file): new session-only seed for this assignment.</summary>
    public static void ApplySessionOnly() => CurrentSeed = NewSeed();
}
