using UnityEngine;

/// <summary>
/// The Runner's combat resources, as earned by one run — carried to the Fight through
/// GameEndedEvent → GameSession (see RunnerResults.Resources). Independent of TotalScore,
/// of musical performance and of each other.
/// </summary>
[System.Serializable]
public struct RunnerResourceCounts
{
    public int TripleCombos;
    public int QuadCombos;
    public int Lives;
    public int Specials;
}

/// <summary>
/// Tracks everything the Runner earns that is NOT score: Triple/Quad pickup combos (via
/// PickupComboTracker) and Life/Special resource pickups. Owned and driven by GameplayManager
/// (which forwards each collection / fall / restart / song-time tick to it), publishes
/// RunnerResourcesChangedEvent / PickupComboEvent for the HUD. Pure C#: no scene lookups.
/// </summary>
public class RunnerResourceTracker
{
    private readonly PickupComboTracker _combos;

    public RunnerResourceTracker(MusicRunnerScoringConfig scoring) => _combos = new PickupComboTracker(scoring);

    public RunnerResourceCounts Counts => new RunnerResourceCounts
    {
        TripleCombos = _combos.TripleCount,
        QuadCombos   = _combos.QuadCount,
        // Statistics of THIS run (RunSession) — the inventories themselves are
        // PlayerProgressService.ExtraLives (persistent) and RunSession.Wallet.Specials.
        Lives        = GameSession.Instance != null ? GameSession.Instance.Run.LivesCollectedThisRun : 0,
        Specials     = GameSession.Instance != null ? GameSession.Instance.Run.SpecialsCollectedThisRun : 0,
    };

    public int PendingComboPickups => _combos.PendingCount;

    /// <summary>A musical pickup was collected (songTime = when the player touched it).</summary>
    public void OnMusicalPickup(float songTime) => Track(_combos.Register(songTime));

    /// <summary>A Life/Special pickup was collected. LIFE → persistent extra life (saved at once) +
    /// run statistic; SPECIAL → the current run's wallet (temporary) + run statistic.</summary>
    public void OnResourcePickup(RingType type)
    {
        var run = GameSession.Instance != null ? GameSession.Instance.Run : null;
        if (type == RingType.Life)
        {
            PlayerProgressService.Instance?.AddLife(1, "Runner LIFE pickup");
            run?.CountLifeCollected();
        }
        else if (type == RingType.Special)
        {
            run?.CollectSpecial();
            Debug.Log($"[RunSession] +1 SPECIAL → {run?.Wallet.Specials} for the upcoming fight");
        }
        else return;
        EventBus.Publish(new RunnerResourcesChangedEvent { Counts = Counts });
    }

    public void Tick(float songTime) => Track(_combos.Tick(songTime));

    public void OnFall(bool cancelCombo)
    {
        if (cancelCombo) { _combos.Cancel(); Track(PickupComboTier.None, broken: true); }
    }

    /// <summary>End of run: resolves the sequence still open.</summary>
    public void Flush() => Track(_combos.Flush());

    // Publishes the combo result (if any) plus sequence progress for feedback: start/count, and
    // "broken" when an open sequence closes without reaching a combo (timed out or a fall).
    private int _lastPending;
    private void Track(PickupComboTier tier, bool broken = false)
    {
        Publish(tier);
        int pending = _combos.PendingCount;
        bool wasOpen = _lastPending > 0;
        if (pending == _lastPending && !(broken && wasOpen)) return;
        bool closedWithoutCombo = wasOpen && pending == 0 && tier == PickupComboTier.None;
        _lastPending = pending;
        EventBus.Publish(new PickupComboProgressEvent { Count = pending, Broken = (broken && wasOpen) || closedWithoutCombo });
    }

    public void Reset()
    {
        _combos.Reset();
        _lastPending = 0;
        EventBus.Publish(new RunnerResourcesChangedEvent { Counts = Counts });
    }

    private void Publish(PickupComboTier tier)
    {
        if (tier == PickupComboTier.None) return;
        var counts = Counts;
        EventBus.Publish(new PickupComboEvent { Tier = tier, Counts = counts });
        EventBus.Publish(new RunnerResourcesChangedEvent { Counts = counts });
    }
}
