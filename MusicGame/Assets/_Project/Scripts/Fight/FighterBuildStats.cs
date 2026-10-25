using UnityEngine;

/// <summary>The combat-build stats a move can scale with (see FightMoveDefinition.scalingStat).</summary>
public enum FighterBuildStat
{
    None,
    PunchPower,
    KickPower,
    Agility,
    Resistance,
    /// <summary>RETIRED (was ImpactPower until 2026-10-25) — kept so serialized ints stay stable; a move still
    /// carrying it scales with nothing (treated as None).</summary>
    LegacyImpactPower,
}

/// <summary>
/// A fighter's combat-build stats as the combat runtime reads them — the FOUR values the Runner produces
/// (RunnerResults.CombatBuild), on the same point scale. Player: copied straight from
/// GameSession.RunnerResults.CombatBuild. Opponent: its FighterStatsProfileSO.buildStats proportions sized by its
/// level (FightCombatBalanceConfig.RivalBuild). What each stat DOES in combat lives in FightCombatBalanceConfig
/// (curves), never here:
///   PunchPower → punch damage (and its hit reaction) · KickPower → kick damage (and its hit reaction)
///   Resistance → damage / reaction taken · Agility → movement speed
/// </summary>
[System.Serializable]
public struct FighterBuildStats
{
    public float punchPower;
    public float kickPower;
    public float agility;
    public float resistance;

    public float Get(FighterBuildStat stat) => stat switch
    {
        FighterBuildStat.PunchPower  => punchPower,
        FighterBuildStat.KickPower   => kickPower,
        FighterBuildStat.Agility     => agility,
        FighterBuildStat.Resistance  => resistance,
        _                            => 0f,
    };

    public float Total => punchPower + kickPower + agility + resistance;

    /// <summary>Straight copy of the Runner's canonical build output.</summary>
    public static FighterBuildStats FromCombatBuild(MusicCombatBuild build) => build == null ? default : new FighterBuildStats
    {
        punchPower  = build.PunchPower,
        kickPower   = build.KickPower,
        agility     = build.Agility,
        resistance  = build.Resistance,
    };

    /// <summary>Even split of `budget` — the neutral fallback when a fighter has no real source.</summary>
    public static FighterBuildStats Even(float budget)
    {
        float v = Mathf.Max(0f, budget) / 4f;
        return new FighterBuildStats { punchPower = v, kickPower = v, agility = v, resistance = v };
    }

    public override string ToString() =>
        $"Punch {punchPower:0.0}  Kick {kickPower:0.0}  Resistance {resistance:0.0}  Agility {agility:0.0}";
}
