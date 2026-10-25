/// <summary>
/// The ONE map of what the player's fight charges pay for. FighterMoveController takes its costs from here (finishers,
/// Power) or from the move asset this names (Signature); the fight HUD, the preFight tip card and the moves help read
/// the same map, so names / icons always match what is really spent. (Design 2026-10-25: the runner combos were x2/x3
/// and became x3/x4; the x3 went with the "triple" strings.)
///
///   TripleCombo ("Combo x3": 3 musical bonuses in a row in the runner)
///       → the POWERED finishers: "Triple punch" (P P P) and "Triple kick" (K K K) — their finisher bonus
///         (FightCombatBalanceConfig.punchFinisher / kickFinisher) for 1 x3, spent when the finishing move starts.
///         Without one the same string ends on the plain normal: free, no bonus.
///   QuadCombo ("Combo x4": 4 in a row)
///       → hold ↓ + Punch + Kick: the Power state. Spends EVERY x4 held at once; lasts powerDuration (base, 1 charge)
///         + powerSecondsPerExtraCharge per extra charge (provisional design: few long activations, not many short ones).
///   Special (runner SPECIAL pickups)
///       → the ENHANCED Signature: FightMoveSetSO.signatureSpecial, whose resourceCost / resourceAmount (Special × 1)
///         are spent when it starts. Without one the same input does the free basic Signature.
/// Every other move (normals, grab, guard, dashes, every other combo string) costs nothing. A charge is never paid
/// with another kind.
/// </summary>
public static class CombatResourceUse
{
    /// <summary>The powered finishers' price (combos with attackBonus ≠ None).</summary>
    public const CombatResourceType FinisherCharge = CombatResourceType.TripleCombo;
    public const int FinisherAmount = 1;

    /// <summary>The Power state's price: all the x4 held (FighterMoveController.TryActivatePower).</summary>
    public const CombatResourceType PowerCost = CombatResourceType.QuadCombo;

    /// <summary>Power duration for `charges` x4 spent together: base + one step per charge beyond the first.</summary>
    public static float PowerSeconds(int charges, FightCombatBalanceConfig balance)
    {
        float baseSeconds = balance != null ? balance.powerDuration : 4f;
        float perExtra = balance != null ? balance.powerSecondsPerExtraCharge : 1f;
        return baseSeconds + System.Math.Max(0, charges - 1) * perExtra;
    }

    /// <summary>The enhanced Signature's price, straight from its move asset (None if it has none).</summary>
    public static CombatResourceType SignatureCost(FightMoveSetSO moveSet) =>
        moveSet != null && moveSet.signatureSpecial != null ? moveSet.signatureSpecial.resourceCost : CombatResourceType.None;

    public static bool IsFinisher(FightComboDefinition combo) => combo != null && combo.attackBonus != FightComboBonus.None;

    /// <summary>Charges the player can actually spend in a fight.</summary>
    public static bool IsSpendable(CombatResourceType type, FightMoveSetSO moveSet) =>
        type != CombatResourceType.None && (type == FinisherCharge || type == PowerCost || type == SignatureCost(moveSet));

    /// <summary>Same charge as a HUD / help icon kind.</summary>
    public static BonusKind ToBonusKind(CombatResourceType type) => type switch
    {
        CombatResourceType.Special => BonusKind.Special,
        CombatResourceType.TripleCombo => BonusKind.TripleCombo,
        CombatResourceType.QuadCombo => BonusKind.QuadCombo,
        _ => BonusKind.Special,
    };
}
