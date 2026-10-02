/// <summary>What a move may cost (see FightMoveDefinition.resourceCost).</summary>
public enum CombatResourceType
{
    None,
    TripleCombo,
    QuadCombo,
    Special,
}

/// <summary>
/// A fighter's spendable combat resources — a mutable wallet the combat runtime checks and consumes
/// (FighterMoveController.TryExecuteMove):
///   Player   — the CURRENT RunSession's own wallet (GameSession.Run.Wallet): SPECIALs collected in the
///              Runner are added to it directly, so Runner, HUD and fight share one counter;
///   Opponent — a fresh wallet from its tier config (OpponentLevelConfig.specials/tripleCombos/quadCombos).
/// Lives are not here: the life inventory is persistent (PlayerProgressService.ExtraLives).
/// </summary>
public class FighterCombatResources
{
    public int TripleCombos { get; private set; }
    public int QuadCombos { get; private set; }
    public int Specials { get; private set; }
    public int Lives { get; private set; }

    public FighterCombatResources(int tripleCombos, int quadCombos, int specials, int lives = 0)
    {
        TripleCombos = System.Math.Max(0, tripleCombos);
        QuadCombos   = System.Math.Max(0, quadCombos);
        Specials     = System.Math.Max(0, specials);
        Lives        = System.Math.Max(0, lives);
    }

    public static FighterCombatResources FromRunner(RunnerResourceCounts run) =>
        new(run.TripleCombos, run.QuadCombos, run.Specials, run.Lives);

    public int Get(CombatResourceType type) => type switch
    {
        CombatResourceType.TripleCombo => TripleCombos,
        CombatResourceType.QuadCombo   => QuadCombos,
        CombatResourceType.Special     => Specials,
        _                              => int.MaxValue,
    };

    public bool CanAfford(CombatResourceType type, int amount) => type == CombatResourceType.None || amount <= 0 || Get(type) >= amount;

    /// <summary>Consumes `amount` if affordable; false (nothing consumed) otherwise.</summary>
    public bool TryConsume(CombatResourceType type, int amount)
    {
        if (!CanAfford(type, amount)) return false;
        if (amount <= 0) return true;
        switch (type)
        {
            case CombatResourceType.TripleCombo: TripleCombos -= amount; break;
            case CombatResourceType.QuadCombo:   QuadCombos   -= amount; break;
            case CombatResourceType.Special:     Specials     -= amount; break;
        }
        return true;
    }

    /// <summary>Adds earned resources (RunSession: SPECIAL pickups, end-of-run Triple/Quad combos).</summary>
    public void Add(CombatResourceType type, int amount)
    {
        if (amount <= 0) return;
        switch (type)
        {
            case CombatResourceType.TripleCombo: TripleCombos += amount; break;
            case CombatResourceType.QuadCombo:   QuadCombos   += amount; break;
            case CombatResourceType.Special:     Specials     += amount; break;
        }
    }

    /// <summary>DEBUG ONLY (FightDebugCombatInput) — never called by gameplay.</summary>
    public void DebugGrant(int triple, int quad, int special)
    {
        TripleCombos += triple;
        QuadCombos   += quad;
        Specials     += special;
    }

    public override string ToString() => $"x3 {TripleCombos}  x4 {QuadCombos}  Special {Specials}  Lives {Lives}";
}
