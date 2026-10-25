using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// What each bonus does, built from the REAL rules every time (never a hand-written table that could drift):
/// the direct build contributions of FightStatsConfig (musical pickup → points per stat), the resource rules
/// (MusicRunnerCollectiblesConfig / MusicRunnerScoringConfig) and the fight values that consume them
/// (FightMoveSetSO signature Special, FightCombatBalanceConfig power state). Used by the preRun tip card (short
/// effect) and the pause help (full text) — one source for both.
/// </summary>
public static class BonusHelpCatalog
{
    public struct Entry
    {
        public BonusKind Kind;
        public string Name;
        public string ShortEffect;
        public string FullText;
        /// <summary>The build stats this bonus feeds and the points ONE collected bonus adds to each, in the HUD's order
        /// (empty: it changes no fight attribute).</summary>
        public List<(FightStatId stat, float weight)> Feeds;
    }

    /// <summary>The four build stats, in the build panel's order.</summary>
    public static readonly FightStatId[] BuildStats = MusicCombatBuildConverter.Stats;

    public static string StatName(FightStatId stat) => Loc.Get($"Help.Stat.{stat}");
    public static string StatShortName(FightStatId stat) => Loc.Get($"Help.Stat.Short.{stat}");
    // Four-attribute model (2026-10-25): Agility = movement AND attack speed; punch / kick power also drive their hit reaction.
    // New keys where the meaning changed (tables are never overwritten).
    public static string StatDescription(FightStatId stat) => Loc.Get(stat switch
    {
        FightStatId.Agility => "Help.Stat.Agility.MoveAndHit",
        FightStatId.PunchPower => "Help.Stat.PunchPower.Hit",
        FightStatId.KickPower => "Help.Stat.KickPower.Hit",
        _ => $"Help.Stat.{stat}.Full",
    });

    public static string BonusName(BonusKind kind) => kind switch
    {
        BonusKind.TripleCombo => Loc.Get("Help.Bonus.TripleCombo"),
        BonusKind.QuadCombo => Loc.Get("Help.Bonus.QuadCombo"),
        _ => Loc.Get($"HUD.{kind}"),
    };

    /// <summary>Musical pickup → (stat, points per collected bonus) from FightStatsConfig.contributions.</summary>
    public static List<(FightStatId stat, float weight)> Feeds(BonusKind kind, FightStatsConfig stats)
    {
        var list = new List<(FightStatId, float)>();
        var ring = BonusKinds.Ring(kind);
        var row = ring.HasValue && stats != null ? stats.Contribution(ring.Value) : null;
        if (row == null) return list;
        foreach (var stat in BuildStats)
            if (row.Get(stat) > 0f) list.Add((stat, row.Get(stat)));
        return list;
    }

    public static List<Entry> Build()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var stats = app != null ? app.fightStats : null;
        var balance = app != null ? app.combatBalance : null;
        var moveSet = app != null && app.fightFlow != null && app.fightFlow.defaultPlayerCombatProfile != null
            ? app.fightFlow.defaultPlayerCombatProfile.moveSet : null;
        var gameplay = HelpUIConfigSO.Instance != null ? HelpUIConfigSO.Instance.gameplay : null;
        var collectibles = gameplay != null ? gameplay.collectibles : null;
        var scoring = gameplay != null ? gameplay.scoring : null;

        var entries = new List<Entry>();
        foreach (var kind in BonusKinds.All)
        {
            var e = new Entry { Kind = kind, Name = BonusName(kind), Feeds = Feeds(kind, stats) };
            if (BonusKinds.IsMusical(kind))
            {
                if (e.Feeds.Count > 0)
                {
                    // Direct wording per bonus (Help.Effect.Bonus.<Kind>) + what one collected bonus adds — never shares.
                    e.ShortEffect = Loc.Get($"Help.Effect.Bonus.{kind}");
                    e.FullText = e.ShortEffect + ". " + Loc.Get("Help.Bonus.Adds", string.Join(" · ", e.Feeds.Select(f => $"{StatName(f.stat)} +{Num(f.weight)}")));
                }
                else
                {
                    e.ShortEffect = Loc.Get("Help.Effect.ScoreOnly");
                    e.FullText = Loc.Get("Help.Bonus.ScoreOnlyFull");
                }
            }
            else switch (kind)
            {
                case BonusKind.Life:
                    e.ShortEffect = Loc.Get("Help.Effect.Life");
                    e.FullText = Loc.Get("Help.Bonus.LifeFull", (collectibles != null ? collectibles.maxLifePickups : 1).ToString());
                    break;
                case BonusKind.Special:
                    e.ShortEffect = Loc.Get("Help.Effect.Special");
                    e.FullText = Loc.Get("Help.Bonus.SpecialFixed", Num(SpecialPercent(moveSet)));
                    break;
                case BonusKind.TripleCombo:
                    e.ShortEffect = Loc.Get("Help.Effect.Triple", (scoring != null ? scoring.tripleComboPickups : 3).ToString());
                    e.FullText = Loc.Get("Help.Bonus.TripleUse", (scoring != null ? scoring.tripleComboPickups : 3).ToString(),
                                         Num(scoring != null ? scoring.comboWindowSeconds : 1.15f),
                                         Num(balance != null ? balance.punchFinisher.damage : 1.5f));
                    break;
                case BonusKind.QuadCombo:
                    e.ShortEffect = Loc.Get("Help.Effect.Quad", (scoring != null ? scoring.quadComboPickups : 4).ToString());
                    e.FullText = Loc.Get("Help.Bonus.QuadUse", (scoring != null ? scoring.quadComboPickups : 4).ToString(),
                                         Num(scoring != null ? scoring.comboWindowSeconds : 1.15f),
                                         Num(balance != null ? balance.powerActivationHoldTime : 1f),
                                         Num(balance != null ? balance.powerDamageMultiplier : 1.3f),
                                         Num(CombatResourceUse.PowerSeconds(1, balance)),
                                         Num(balance != null ? balance.powerSecondsPerExtraCharge : 1f));
                    break;
            }
            entries.Add(e);
        }
        return entries;
    }

    /// <summary>The intro paragraph of the full help: every bonus adds straight to the attributes it feeds.</summary>
    public static string Intro()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var stats = app != null ? app.fightStats : null;
        return Loc.Get("Help.Bonus.IntroDirect", Num(stats != null ? stats.baseStatPoints : 2f), Num(stats != null ? stats.maxStatPoints : 40f));
    }

    /// <summary>The Special's fixed share of the rival's max health, in % (FightMoveDefinition.damageAsMaxHealthFraction).</summary>
    public static float SpecialPercent(FightMoveSetSO moveSet) =>
        moveSet != null && moveSet.signatureSpecial != null ? moveSet.signatureSpecial.damageAsMaxHealthFraction * 100f : 30f;

    public static string Num(float v) => v.ToString(Mathf.Approximately(v, Mathf.Round(v)) ? "0" : "0.##", CultureInfo.InvariantCulture);
}
