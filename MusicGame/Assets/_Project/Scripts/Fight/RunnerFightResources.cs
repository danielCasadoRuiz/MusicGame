using System.Collections.Generic;

/// <summary>
/// The NORMALIZED (0..1) combat potential a run earned, one entry per FightStatId — the middle
/// layer between RunnerResults (raw run facts) and FighterStats (final combat numbers), built
/// once by RunnerFightResourceBuilder the instant a run ends (see GameSession).
///
/// Kept around afterward, not just consumed transiently while building FighterStats — useful on
/// its own for UI/debug/balancing (it shows exactly what the run contributed, independent of
/// whatever curve/base/maxBonus FighterStats later applied on top), and because a future phase
/// may decide some of it should carry over or be spent differently than FighterStats itself.
///
/// Deliberately holds ONLY the derived 0..1 potential per stat plus enough provenance to explain
/// where it came from — RunnerResults stays the single source of truth for the raw
/// CollectionRate/TimingAccuracy/FallCount facts themselves.
/// </summary>
public class RunnerFightResources
{
    public readonly struct Entry
    {
        public readonly FightStatId StatId;

        /// <summary>Null for a transversal stat (e.g. Balance) that isn't tied to a single
        /// RingType.</summary>
        public readonly RingType? Source;

        /// <summary>False when this stat's RingType never appeared in this song's timeline
        /// (TypePerformance.HasData == false) — Value is then FightStatsConfig.neutralInput, a
        /// deliberately neutral stand-in, never a real collection/timing result. Always true for
        /// a transversal stat.</summary>
        public readonly bool HadSource;

        /// <summary>0..1 — the normalized potential this stat earned from the run.</summary>
        public readonly float Value;

        public Entry(FightStatId statId, RingType? source, bool hadSource, float value)
        {
            StatId    = statId;
            Source    = source;
            HadSource = hadSource;
            Value     = value;
        }
    }

    private readonly Dictionary<FightStatId, Entry> _entries = new();

    /// <summary>Every entry this run produced — for debug/UI enumeration (see
    /// GameplayDebugHUD's own Fight Stats section).</summary>
    public IReadOnlyCollection<Entry> All => _entries.Values;

    public float Get(FightStatId statId) => _entries.TryGetValue(statId, out var e) ? e.Value : 0f;

    public bool TryGet(FightStatId statId, out Entry entry) => _entries.TryGetValue(statId, out entry);

    public void Set(Entry entry) => _entries[entry.StatId] = entry;
}
