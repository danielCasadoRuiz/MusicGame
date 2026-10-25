/// <summary>
/// The Fighter's combat-relevant properties for V1. Seven map 1:1 onto a RingType collected in
/// the Runner (see FightStatsConfig.ringStats / RunnerFightResourceBuilder); Balance is
/// transversal — built from the run as a whole (falls + overall precision) rather than any
/// single collectible type. Adding a stat later is just a new enum value plus a config entry —
/// nothing else in this layer assumes there are exactly eight.
/// </summary>
public enum FightStatId
{
    Strength,     // Kick
    Speed,        // Snare
    Agility,      // HiHat
    Defense,      // Beat
    Combo,        // Onset
    Knockback,    // Impact
    SpecialPower, // Peak
    Balance,      // transversal — falls + overall precision

    // ── Music build stats — the Runner's canonical combat-build output (MusicCombatBuild, read
    // from RunnerResults.CombatBuild; see MusicCombatBuildConverter). FOUR since 2026-10-25: PunchPower,
    // KickPower, Resistance and Agility (above, reused as the build's agility id). The build never writes
    // FighterStats — the legacy values above are a separate, untouched pipeline. Appended so serialized
    // ints of the values above never change.
    PunchPower,
    KickPower,
    Resistance,
    /// <summary>RETIRED build stat (was ImpactPower until 2026-10-25) — kept only so serialized ints stay stable.
    /// Nothing builds, shows or reads it; the Impact bonus now feeds PunchPower + KickPower.</summary>
    LegacyImpactPower,
}
