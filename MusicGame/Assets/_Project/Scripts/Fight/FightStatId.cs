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
}
