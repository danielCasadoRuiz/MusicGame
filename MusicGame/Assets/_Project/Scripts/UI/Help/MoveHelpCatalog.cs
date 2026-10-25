using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// How the player's moves are entered, as the help shows them. The buttons are the REAL bindings of
/// HumanFightInputSource (keyboard J = Punch, K = Kick, WASD / arrows = directions) or, on mobile, the touch
/// buttons; directions are facing-relative (→ = towards the rival). PENDING: there is no rebinding and no gamepad
/// support in the project, so these are fixed — if bindings ever become configurable, only this class changes.
/// </summary>
public static class FightInputGlyphs
{
    public const string PunchKey = "J", KickKey = "K";

    /// <summary>Same gating as HumanFightInputSource.Tick: the keyboard is read on desktop and in the editor's dual input.</summary>
    public static bool Keyboard => !PlatformService.IsMobile || PlatformService.DualInputInEditor;

    public static string Button(FightButton b) => b switch
    {
        FightButton.Punch => Cap(Keyboard ? PunchKey : Loc.Get("Mobile.Punch")),
        FightButton.Kick => Cap(Keyboard ? KickKey : Loc.Get("Mobile.Kick")),
        _ => "",
    };

    public static string Dir(FightHorizontalDirection h, FightVerticalDirection v) =>
        (v == FightVerticalDirection.Up ? "↑" : v == FightVerticalDirection.Down ? "↓" : "")
        + (h == FightHorizontalDirection.Forward ? "→" : h == FightHorizontalDirection.Back ? "←" : "");

    /// <summary>A key cap (TMP mark), the visual "button" of the help and tip texts.</summary>
    public static string Cap(string key) => $"<mark=#FFFFFF2E padding=\"8,8,3,3\"><b>{key}</b></mark>";

    public static string DirCap(FightHorizontalDirection h, FightVerticalDirection v) => Cap(Dir(h, v));

    public static string Step(FightComboDefinition.Step s)
    {
        string dir = s.anyDirection && s.button != FightButton.None ? "" : Dir(s.horizontal, s.vertical);
        if (s.button == FightButton.None) return Cap(dir);
        return dir.Length > 0 ? $"{Cap(dir)}+{Button(s.button)}" : Button(s.button);
    }

    public static string Sequence(FightComboDefinition c) => string.Join(" ", c.steps.Select(Step));
}

/// <summary>
/// The player's move list, built from the data the fight really uses: the combo set (FightFlowConfig.comboSet —
/// every combo, its steps and its timing window), the player's move set (signature Special and its cost), the
/// input windows (FightFlowConfig) and the power state (FightCombatBalanceConfig). What each move costs comes from
/// CombatResourceUse (the same map the fight spends by): basics and the other combos are free; the triple finishers
/// (x3), the Special and the Power state (all x4) spend charges. PENDING: the move set is shared
/// by every character today (one FightMoveSetSO); when characters get their own, pass that profile here.
/// </summary>
public static class MoveHelpCatalog
{
    /// <summary>Basics / free combos / moves that spend a charge (titles: GroupName).</summary>
    public enum Group { Basics, Combos, Special }

    public struct Entry
    {
        public Group Group;
        public string Keys;
        public string Name;
        /// <summary>Requirements / timing — shown in the full help only.</summary>
        public string Detail;
        /// <summary>Shown in the compact tip card (the essentials).</summary>
        public bool Essential;
        /// <summary>The card's one-line reminder of the requirement (Special / power / finisher), if any.</summary>
        public string Short;
        /// <summary>The charge this move spends (CombatResourceUse) — None = free. Shown as that charge's icon.</summary>
        public CombatResourceType Cost;
    }

    public static string GroupName(Group g) => g switch
    {
        Group.Combos => Loc.Get("Help.Moves.FreeCombos"),
        Group.Special => Loc.Get("Help.Moves.Charged"),
        _ => Loc.Get("Help.Moves.Basics"),
    };

    public static List<Entry> Build(FighterCombatResources wallet = null)
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var flow = app != null ? app.fightFlow : null;
        var balance = app != null ? app.combatBalance : null;
        var combos = flow != null && flow.comboSet != null ? flow.comboSet.combos : System.Array.Empty<FightComboDefinition>();
        var moveSet = flow != null && flow.defaultPlayerCombatProfile != null ? flow.defaultPlayerCombatProfile.moveSet : null;
        string signatureId = moveSet != null && moveSet.signatureSpecial != null ? moveSet.signatureSpecial.id : null;
        var list = new List<Entry>();
        string punch = FightInputGlyphs.Button(FightButton.Punch), kick = FightInputGlyphs.Button(FightButton.Kick);
        string up = FightInputGlyphs.Cap("↑"), down = FightInputGlyphs.Cap("↓"), back = FightInputGlyphs.Cap("←");

        // Basics: the two buttons, posture (↑ / ↓ + button), guard and grab (FighterInputController / FighterGuard).
        list.Add(new Entry { Group = Group.Basics, Keys = punch, Name = Loc.Get("Help.Move.Punch"), Essential = true });
        list.Add(new Entry { Group = Group.Basics, Keys = kick, Name = Loc.Get("Help.Move.Kick"), Essential = true });
        list.Add(new Entry { Group = Group.Basics, Keys = $"{up}/{down}+{punch}/{kick}", Name = Loc.Get("Help.Move.HighLow"),
                             Detail = Loc.Get("Help.Move.HighLowDetail"), Essential = true });
        list.Add(new Entry { Group = Group.Basics, Keys = back, Name = Loc.Get("Help.Move.Guard"), Detail = Loc.Get("Help.Move.GuardDetail"), Essential = true });
        list.Add(new Entry { Group = Group.Basics, Keys = $"{punch}+{kick}", Name = Loc.Get("Help.Move.Grab"),
                             Detail = Loc.Get("Help.Move.GrabDetail", BonusHelpCatalog.Num(flow != null ? flow.grabChordWindow : 0.08f)) });

        // Combos straight from the combo set, finishers (the triple combos) first; dashes are movement basics.
        FightComboDefinition special = null;
        int triples = wallet != null ? wallet.Get(CombatResourceUse.FinisherCharge) : 0;
        foreach (var c in combos.OrderBy(x => x == null || x.attackBonus == FightComboBonus.None ? 1 : 0))
        {
            if (c == null || c.steps == null || c.steps.Length == 0) continue;
            bool buttons = c.steps.Any(s => s.button != FightButton.None);
            // The Signature combo = the one whose move is the move set's signatureSpecial (FighterMoveController.OnCombo's rule).
            if (signatureId != null && c.moveId == signatureId) { special = c; continue; }
            var e = new Entry
            {
                Group = buttons ? Group.Combos : Group.Basics,
                Keys = FightInputGlyphs.Sequence(c),
                Name = ComboName(c),
                Detail = Loc.Get("Help.Move.ComboTiming", BonusHelpCatalog.Num(c.maxTimeBetweenInputs)) + " " + Loc.Get("Help.Move.NoCharge"),
                Essential = c.attackBonus != FightComboBonus.None,
            };
            float finisher = balance == null ? 0f : c.attackBonus == FightComboBonus.PunchFinisher ? balance.punchFinisher.damage
                           : c.attackBonus == FightComboBonus.KickFinisher ? balance.kickFinisher.damage : 0f;
            if (CombatResourceUse.IsFinisher(c))
            {
                // Triple punch / kick: powered by one x3 (CombatResourceUse) — plain normal ending without one.
                e.Group = Group.Special;
                e.Cost = CombatResourceUse.FinisherCharge;
                e.Detail = Loc.Get("Help.Move.ComboTiming", BonusHelpCatalog.Num(c.maxTimeBetweenInputs)) + " "
                         + Loc.Get("Help.Move.FinisherPowered", BonusHelpCatalog.Num(finisher));
                e.Short = Loc.Get("Help.Move.FinisherSpend", BonusHelpCatalog.Num(finisher), triples.ToString());
            }
            list.Add(e);
        }

        // Special (signature): enhanced only while a Special can be paid, otherwise the basic heavy version.
        var signatureCost = CombatResourceUse.SignatureCost(moveSet);
        int specials = wallet != null && signatureCost != CombatResourceType.None ? wallet.Get(signatureCost) : 0, quads = wallet != null ? wallet.Get(CombatResourceUse.PowerCost) : 0;
        if (special != null)
            list.Add(new Entry { Group = Group.Special, Keys = FightInputGlyphs.Sequence(special), Name = ComboName(special), Essential = true,
                                 Cost = signatureCost,
                                 Short = Loc.Get("Help.Move.SpecialSpendFixed", specials.ToString(), BonusHelpCatalog.Num(BonusHelpCatalog.SpecialPercent(moveSet))),
                                 Detail = Loc.Get("Help.Move.SpecialFixed", specials.ToString(), BonusHelpCatalog.Num(BonusHelpCatalog.SpecialPercent(moveSet))) });
        // Power state: hold Down + Punch + Kick (FighterInputController), paid with EVERY x4 held — longer the more.
        float hold = balance != null ? balance.powerActivationHoldTime : 1f;
        list.Add(new Entry { Group = Group.Special, Keys = $"{down}+{punch}+{kick}", Name = Loc.Get("Help.Move.Power"), Essential = true,
                             Cost = CombatResourceUse.PowerCost,
                             Short = Loc.Get("Help.Move.PowerAll", BonusHelpCatalog.Num(hold), quads.ToString(),
                                             BonusHelpCatalog.Num(CombatResourceUse.PowerSeconds(Mathf.Max(1, quads), balance))),
                             Detail = Loc.Get("Help.Move.PowerAllReq", BonusHelpCatalog.Num(hold), quads.ToString(),
                                              BonusHelpCatalog.Num(CombatResourceUse.PowerSeconds(1, balance)),
                                              BonusHelpCatalog.Num(balance != null ? balance.powerSecondsPerExtraCharge : 1f),
                                              BonusHelpCatalog.Num(CombatResourceUse.PowerSeconds(Mathf.Max(1, quads), balance)),
                                              BonusHelpCatalog.Num(balance != null ? balance.powerDamageMultiplier : 1.3f)) });
        return list;
    }

    /// <summary>Localized combo name (Help.Combo.&lt;id&gt;), the asset's debug name when the key is missing.</summary>
    public static string ComboName(FightComboDefinition c)
    {
        string key = $"Help.Combo.{c.id}";
        string name = Loc.Get(key);
        return name == key ? (string.IsNullOrEmpty(c.debugName) ? c.id : c.debugName) : name;
    }
}
