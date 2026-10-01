using UnityEngine;

/// <summary>The five combat-build stats a move can scale with (see FightMoveDefinition.scalingStat).</summary>
public enum FighterBuildStat
{
    None,
    PunchPower,
    KickPower,
    Agility,
    Resistance,
    ImpactPower,
}

/// <summary>
/// A fighter's combat-build stats as the combat runtime reads them — the same five values the
/// Runner produces (RunnerResults.CombatBuild), on the same budget-point scale. Player: copied
/// straight from GameSession.RunnerResults.CombatBuild (never converted into legacy Strength/
/// Defense). Opponent: authored on its tier's FighterStatsProfileSO.buildStats. What each stat
/// DOES in combat lives in FightCombatBalanceConfig (curves), never here.
/// </summary>
[System.Serializable]
public struct FighterBuildStats
{
    public float punchPower;
    public float kickPower;
    public float agility;
    public float resistance;
    public float impactPower;

    public float Get(FighterBuildStat stat) => stat switch
    {
        FighterBuildStat.PunchPower  => punchPower,
        FighterBuildStat.KickPower   => kickPower,
        FighterBuildStat.Agility     => agility,
        FighterBuildStat.Resistance  => resistance,
        FighterBuildStat.ImpactPower => impactPower,
        _                            => 0f,
    };

    public float Total => punchPower + kickPower + agility + resistance + impactPower;

    /// <summary>Straight copy of the Runner's canonical build output.</summary>
    public static FighterBuildStats FromCombatBuild(MusicCombatBuild build) => build == null ? default : new FighterBuildStats
    {
        punchPower  = build.PunchPower,
        kickPower   = build.KickPower,
        agility     = build.Agility,
        resistance  = build.Resistance,
        impactPower = build.ImpactPower,
    };

    /// <summary>Even split of `budget` — the neutral fallback when a fighter has no real source.</summary>
    public static FighterBuildStats Even(float budget)
    {
        float v = Mathf.Max(0f, budget) / 5f;
        return new FighterBuildStats { punchPower = v, kickPower = v, agility = v, resistance = v, impactPower = v };
    }

    public override string ToString() =>
        $"Punch {punchPower:0.0}  Kick {kickPower:0.0}  Agility {agility:0.0}  Resistance {resistance:0.0}  Impact {impactPower:0.0}";
}
