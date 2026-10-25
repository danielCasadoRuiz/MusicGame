using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// What each bonus does, built from the REAL rules every time (never a hand-written table that could drift):
/// the build mapping of FightStatsConfig (musical pickup → stats + weights), the resource rules
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
        /// <summary>The build stats this bonus feeds, strongest first (empty: it changes no fight attribute).</summary>
        public List<(FightStatId stat, float weight)> Feeds;
    }

    /// <summary>The five build stats, in the build panel's order.</summary>
    public static readonly FightStatId[] BuildStats =
        { FightStatId.PunchPower, FightStatId.KickPower, FightStatId.Agility, FightStatId.Resistance, FightStatId.ImpactPower };

    public static string StatName(FightStatId stat) => Loc.Get($"Help.Stat.{stat}");
    public static string StatShortName(FightStatId stat) => Loc.Get($"Help.Stat.Short.{stat}");
    // Agility drives movement speed as well as attack timing since the 2026-10-25 rebalance (new key; tables never overwritten).
    public static string StatDescription(FightStatId stat) =>
        Loc.Get(stat == FightStatId.Agility ? "Help.Stat.Agility.Speed" : $"Help.Stat.{stat}.Full");

    public static string BonusName(BonusKind kind) => kind switch
    {
        BonusKind.TripleCombo => Loc.Get("Help.Bonus.TripleCombo"),
        BonusKind.QuadCombo => Loc.Get("Help.Bonus.QuadCombo"),
        _ => Loc.Get($"HUD.{kind}"),
    };

    /// <summary>Musical pickup → (stat, normalised weight) from the configured build mapping.</summary>
    public static List<(FightStatId stat, float weight)> Feeds(BonusKind kind, FightStatsConfig stats)
    {
        var list = new List<(FightStatId, float)>();
        var ring = BonusKinds.Ring(kind);
        if (!ring.HasValue || stats == null || stats.buildStats == null) return list;
        foreach (var m in stats.buildStats)
        {
            if (m?.sources == null) continue;
            float total = m.sources.Sum(s => Mathf.Max(0f, s.weight));
            foreach (var s in m.sources)
                if (s.source == ring.Value && s.weight > 0f && total > 0f) list.Add((m.statId, s.weight / total));
        }
        list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
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
                    e.ShortEffect = string.Join(" · ", e.Feeds.Select(f => StatShortName(f.stat)));
                    e.FullText = Loc.Get("Help.Bonus.Feeds", string.Join(" · ", e.Feeds.Select(f => $"{StatName(f.stat)} {f.weight * 100f:0}%")));
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
                    e.FullText = Loc.Get("Help.Bonus.SpecialFull", Num(balance != null ? balance.signatureSpecial.damage : 1.8f));
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

    /// <summary>The intro paragraph of the full help: total power from how well the bonuses were collected.</summary>
    public static string Intro()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        float budget = app != null && app.fightStats != null ? app.fightStats.maxCombatBudget : 100f;
        return Loc.Get("Help.Bonus.Intro", Num(budget));
    }

    public static string Num(float v) => v.ToString(Mathf.Approximately(v, Mathf.Round(v)) ? "0" : "0.##", CultureInfo.InvariantCulture);
}
