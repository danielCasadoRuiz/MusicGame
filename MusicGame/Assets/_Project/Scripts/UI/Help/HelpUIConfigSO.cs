using UnityEngine;

/// <summary>Every bonus the Runner can hand to the next fight, as the help / conversion UI shows it: the seven
/// musical pickups (RingType), the two resource pickups and the two pickup-combo charges.</summary>
public enum BonusKind
{
    Kick, Snare, HiHat, Beat, Onset, Impact, Peak,
    Life, Special,
    TripleCombo, QuadCombo,
}

public static class BonusKinds
{
    /// <summary>Display order: musical pickups in the HUD's order, then resources, then combo charges.</summary>
    public static readonly BonusKind[] All =
    {
        BonusKind.Kick, BonusKind.Snare, BonusKind.HiHat, BonusKind.Beat, BonusKind.Onset, BonusKind.Impact, BonusKind.Peak,
        BonusKind.Life, BonusKind.Special, BonusKind.TripleCombo, BonusKind.QuadCombo,
    };

    public static bool IsMusical(BonusKind k) => k <= BonusKind.Peak;

    public static RingType? Ring(BonusKind k) => k switch
    {
        BonusKind.Kick => RingType.Kick, BonusKind.Snare => RingType.Snare, BonusKind.HiHat => RingType.HiHat,
        BonusKind.Beat => RingType.Beat, BonusKind.Onset => RingType.Onset, BonusKind.Impact => RingType.Impact,
        BonusKind.Peak => RingType.Peak, BonusKind.Life => RingType.Life, BonusKind.Special => RingType.Special,
        _ => null,
    };

    public static BonusKind? FromRing(RingType t) => t switch
    {
        RingType.Kick => BonusKind.Kick, RingType.Snare => BonusKind.Snare, RingType.HiHat => BonusKind.HiHat,
        RingType.Beat => BonusKind.Beat, RingType.Onset => BonusKind.Onset, RingType.Impact => BonusKind.Impact,
        RingType.Peak => BonusKind.Peak, RingType.Life => BonusKind.Life, RingType.Special => BonusKind.Special,
        _ => null,
    };
}

/// <summary>
/// The ONE place the help / tip / conversion UI takes its sprites, tints and animation timings from
/// (Resources/UI/HelpUIConfig). Swapping a placeholder for the final icon = assigning another sprite here:
/// no prefab, animation or logic references a bonus sprite directly. Tints come from the collectibles config
/// (the same colours the pickups have in the world), never duplicated here.
/// Created / filled with the placeholder sprites by Tools > MusicGame > UI > Help Placeholders + Prefabs.
/// </summary>
[CreateAssetMenu(fileName = "HelpUIConfig", menuName = "MusicGame/UI/Help UI Config")]
public class HelpUIConfigSO : ScriptableObject
{
    [System.Serializable] public struct BonusIcon { public BonusKind kind; public Sprite sprite; }
    [System.Serializable] public struct StatIcon { public FightStatId stat; public Sprite sprite; public Color color; }

    [Header("Icons (placeholders until the final art: Art/UI/Icons/Placeholders)")]
    public BonusIcon[] bonusIcons = System.Array.Empty<BonusIcon>();
    [Tooltip("Destinations of the conversion: the five build stats of the fight HUD's build panel.")]
    public StatIcon[] statIcons = System.Array.Empty<StatIcon>();

    [Header("Rules + tints")]
    [Tooltip("The Runner's gameplay config: bonus colours (collectibles — the world pickups use the same ones) and " +
             "the resource / pickup-combo rules the help quotes (collectibles, scoring).")]
    public MusicRunnerGameplayConfig gameplay;
    public Color tripleComboColor = new(0.95f, 0.75f, 0.25f);
    public Color quadComboColor = new(1f, 0.45f, 0.15f);

    [Header("Build panel (fight HUD)")]
    [Tooltip("Stat points that fill a build bar completely. PENDING: the game defines no per-stat maximum (the " +
             "budget is shared, maxCombatBudget = 100); 40 = the last point of FightCombatBalanceConfig's " +
             "buildStatToDamage curve, where the damage bonus tops out.")]
    [Min(1f)] public float statBarFullPoints = 40f;

    [Header("Tip cards (preRun / preFight)")]
    [Tooltip("Top-left corner of the preRun card (canvas units, 1920x1080, from the top-left): under the runner HUD.")]
    public Vector2 runnerTipPosition = new(20f, -118f);
    [Tooltip("Top-left corner of the preFight card: right under the fight HUD's top bar (the build panel is inside it), left of the countdown.")]
    public Vector2 fightTipPosition = new(20f, -102f);
    [Tooltip("Card width; the bonuses card lays its rows in this many columns.")]
    [Min(200f)] public float tipWidth = 470f;
    [Min(1)] public int bonusTipColumns = 2;
    [Min(0.5f)] public float tipPageSeconds = 2.2f;
    [Min(0f)] public float tipFadeSeconds = 0.25f;

    [Header("Conversion animation (runner → fight)")]
    [Tooltip("Source tray appears.")]
    [Min(0f)] public float trayInSeconds = 0.35f;
    [Tooltip("Delay between one source's departure and the next (sources leave one after another).")]
    [Min(0f)] public float sourceStagger = 0.22f;
    [Tooltip("Flight time of one icon to its destination (accelerating).")]
    [Min(0.05f)] public float flightSeconds = 0.6f;
    [Tooltip("Sideways bow of the flight, as a fraction of its length.")]
    [Range(0f, 0.6f)] public float flightArc = 0.22f;
    [Tooltip("Icon scale on arrival (absorbed by the destination).")]
    [Range(0.05f, 1f)] public float arrivalScale = 0.25f;
    [Tooltip("The destination bar grows to its new value over this time, starting when the icon enters it.")]
    [Min(0f)] public float barGrowSeconds = 0.35f;
    [Tooltip("Destination pulse on arrival.")]
    [Min(0f)] public float pulseSeconds = 0.25f;
    [Min(1f)] public float pulseScale = 1.25f;
    [Tooltip("Hold on the final state before the round intro starts (only when the conversion plays at the round " +
             "intro — no runner results before it; during the Versus the HUD keeps the final values on screen anyway).")]
    [Min(0f)] public float endHoldSeconds = 0.5f;
    [Tooltip("At most this many icons in flight at once (the rest wait) — keeps the screen readable.")]
    [Min(1)] public int maxFlightsInAir = 4;

    [Header("Bonus block: runner results → fight presentation")]
    [Tooltip("The bonus block's move from its place in the runner results to the bottom of the fight presentation " +
             "(position + scale, eased; unscaled time, so the scene change never stops it).")]
    [Min(0.05f)] public float blockTravelSeconds = 0.8f;
    [Tooltip("Space kept between the top of the block and the rival's speech bubble.")]
    [Min(0f)] public float blockBubbleMargin = 16f;

    private static HelpUIConfigSO s_instance;

    public static HelpUIConfigSO Instance
    {
        get
        {
            if (s_instance == null) s_instance = Resources.Load<HelpUIConfigSO>("UI/HelpUIConfig");
            if (s_instance == null) Debug.LogError("[Help] Resources/UI/HelpUIConfig missing — run Tools > MusicGame > UI > Help Placeholders + Prefabs.");
            return s_instance;
        }
    }

    public Sprite Icon(BonusKind kind)
    {
        foreach (var b in bonusIcons) if (b.kind == kind) return b.sprite;
        return null;
    }

    public Color Tint(BonusKind kind)
    {
        var ring = BonusKinds.Ring(kind);
        var collectibles = gameplay != null ? gameplay.collectibles : null;
        if (ring.HasValue) return collectibles != null ? collectibles.RingColor(ring.Value) : Color.white;
        return kind == BonusKind.TripleCombo ? tripleComboColor : quadComboColor;
    }

    public Sprite StatSprite(FightStatId stat)
    {
        foreach (var s in statIcons) if (s.stat == stat) return s.sprite;
        return null;
    }

    public Color StatColor(FightStatId stat)
    {
        foreach (var s in statIcons) if (s.stat == stat) return s.color;
        return Color.white;
    }
}
