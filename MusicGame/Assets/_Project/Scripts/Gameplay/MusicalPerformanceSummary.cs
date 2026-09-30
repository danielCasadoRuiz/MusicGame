using System.Collections.Generic;
using UnityEngine;

/// <summary>One musical pickup type's normalized result for a run.</summary>
public readonly struct MusicalTypeResult
{
    public readonly RingType Type;
    /// <summary>Pickups of this type the run actually offered (inside the played window).</summary>
    public readonly int Available;
    /// <summary>Physically collected (monotonic — never reduced by the fall penalty).</summary>
    public readonly int Collected;
    /// <summary>Collected / Available (0..1). Meaningless when !IsApplicable.</summary>
    public readonly float CollectionRate;
    /// <summary>Mean timing quality of the collected ones (0..1), for a future quality-aware blend.</summary>
    public readonly float TimingAccuracy;
    /// <summary>Normalized performance 0..1 — see MusicalPerformanceBuilder. Meaningless when !IsApplicable.</summary>
    public readonly float Performance;

    /// <summary>False = this song's gameplay never generated this type: NOT APPLICABLE, never "0%".</summary>
    public bool IsApplicable => Available > 0;

    public MusicalTypeResult(RingType type, int available, int collected, float collectionRate, float timingAccuracy, float performance)
    {
        Type           = type;
        Available      = available;
        Collected      = collected;
        CollectionRate = collectionRate;
        TimingAccuracy = timingAccuracy;
        Performance    = performance;
    }
}

/// <summary>
/// The run's musical performance, normalized so songs with very different event densities compare
/// fairly (16/20 Kicks and 52/65 Kicks are both 0.80). Covers EVERY musical type in
/// RingTypes.Musical order; a type the song never generated stays in the list as not applicable
/// (N/A) and is excluded from every average — it is never counted as zero.
/// </summary>
public class MusicalPerformanceSummary
{
    private readonly List<MusicalTypeResult> _types = new();

    public IReadOnlyList<MusicalTypeResult> Types => _types;

    /// <summary>Mean Performance over the APPLICABLE types only (equal weight per type, so a dense
    /// type can't dominate). 0 when no type was applicable.</summary>
    public float OverallPerformance { get; private set; }

    public int ApplicableCount { get; private set; }

    public bool TryGet(RingType type, out MusicalTypeResult result)
    {
        foreach (var t in _types)
            if (t.Type == type) { result = t; return true; }
        result = default;
        return false;
    }

    public bool IsApplicable(RingType type) => TryGet(type, out var r) && r.IsApplicable;

    internal void Add(MusicalTypeResult result) => _types.Add(result);

    internal void Finish()
    {
        float sum = 0f;
        int n = 0;
        foreach (var t in _types)
        {
            if (!t.IsApplicable) continue;
            sum += t.Performance;
            n++;
        }
        ApplicableCount    = n;
        OverallPerformance = n > 0 ? sum / n : 0f;
    }
}

/// <summary>
/// GamePerformance (per-type raw facts from GameplayManager) -> MusicalPerformanceSummary. Pure.
///   performance = CollectionRate                                   (timingInfluence = 0, default)
///   performance = CollectionRate * Lerp(1, TimingAccuracy, timingInfluence)   (quality-aware)
/// </summary>
public static class MusicalPerformanceBuilder
{
    public static MusicalPerformanceSummary Build(GamePerformance performance, float timingInfluence = 0f)
    {
        var summary = new MusicalPerformanceSummary();
        timingInfluence = Mathf.Clamp01(timingInfluence);
        foreach (var type in RingTypes.Musical)
        {
            if (performance != null && performance.ByType.TryGetValue(type, out var tp) && tp.HasData)
            {
                float perf = Mathf.Clamp01(tp.CollectionRate * Mathf.Lerp(1f, tp.TimingAccuracy, timingInfluence));
                summary.Add(new MusicalTypeResult(type, tp.Available, tp.Collected, tp.CollectionRate, tp.TimingAccuracy, perf));
            }
            else
            {
                summary.Add(new MusicalTypeResult(type, 0, 0, 0f, 0f, 0f));
            }
        }
        summary.Finish();
        return summary;
    }
}
