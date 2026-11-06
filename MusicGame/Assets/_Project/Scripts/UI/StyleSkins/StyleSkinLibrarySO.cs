using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Per-GameMusicStyle UI art: the Tempo Fury buttons (normal / selected, 1280×320 with the body at x64 y64
/// 1152×192) and the TEMPO FURY title (background, nine separate letters, foreground), plus the classic
/// button every styled button blends with and the display font of the big titles.
/// Filled by Tools > MusicGame > UI > Tempo Fury Titles + Style Buttons (TempoFuryUISetup) from
/// AssetsMaterial/UI/Buttons/TempoFury_boton and AssetsMaterial/UI/Titles; the art lives in Art/UI/StyleButtons
/// and Art/UI/Titles. Components hold a direct reference (set by the same tool), no global lookup.
/// </summary>
[CreateAssetMenu(fileName = "StyleSkinLibrary", menuName = "MusicGame/UI/Style Skin Library")]
public class StyleSkinLibrarySO : ScriptableObject
{
    [System.Serializable]
    public class ButtonSkin
    {
        public GameMusicStyle style;
        public Sprite normal;
        public Sprite selected;
    }

    [System.Serializable]
    public class TitleSkin
    {
        public GameMusicStyle style;
        public Sprite background;
        public Sprite foreground;
        [Tooltip("T, E, M, P, O, F, U, R, Y — same order as StyledTitleView.letters.")]
        public Sprite[] letters = new Sprite[9];
    }

    [System.Serializable]
    public class HudSkin
    {
        public GameMusicStyle style;
        [Tooltip("Runner top bar, 3072×320 (see HudBar* constants).")]
        public Sprite bar;
        [Tooltip("Pause glyph (256×256) for the bar's round slot: normal / pressed.")]
        public Sprite pause;
        public Sprite pauseSelected;
    }

    [System.Serializable]
    public class BonusSkin
    {
        public BonusKind kind;
        [Tooltip("The bonus' own colours (front view).")]
        public Sprite normal;
        [Tooltip("The gold 'classic' version it fades to as the run nears its end.")]
        public Sprite classic;
    }

    public const string TitleLetters = "TEMPOFURY";
    /// <summary>Button art: body rect inside the 1280×320 sprite (the rest is glow margin).</summary>
    public static readonly Vector2 ButtonCanvas = new(1280f, 320f);
    public static readonly Rect ButtonBody = new(64f, 64f, 1152f, 192f);

    [Header("Titles (TitleText) — one size per tier, one colour for all")]
    public TMP_FontAsset titleFont;
    public Color titleColor = new(0.953f, 0.918f, 0.827f, 1f);   // ivory
    [Tooltip("Countdown 3-2-1 TEMPO! / FURY!, ROUND, KO!, FINISH!, PERFECT!, VS.")]
    public float displaySize = 220f;
    [Tooltip("Title of a menu / screen: PAUSE, YOU WIN, RIVALS, PROFILE…")]
    public float screenTitleSize = 110f;
    [Tooltip("Section header inside a screen.")]
    public float sectionSize = 52f;

    public float TitleSize(TitleTier tier) => tier switch
    {
        TitleTier.Display => displaySize,
        TitleTier.Section => sectionSize,
        _ => screenTitleSize,
    };

    [Header("Buttons")]
    public Sprite classicNormal;
    public Sprite classicSelected;
    public List<ButtonSkin> buttons = new();
    [Tooltip("Label colour over the light 'normal' art and over the saturated 'selected' art.")]
    public Color labelNormal = new(0.10f, 0.10f, 0.13f, 1f);
    public Color labelSelected = Color.white;

    [Header("Classic ↔ style blend")]
    [Tooltip("Runner: OFF (default) = the song starts fully in the player's style and ends fully classic — the end of " +
             "the run is the classical rival's territory; ON = the reverse.")]
    public bool runnerProgressTowardsStyle = false;
    [Tooltip("Runner HUD: the classic look comes in as a curtain from the right; width of its soft edge (fraction of the bar).")]
    [Range(0.01f, 1f)] public float runnerCurtainSoftness = 0.2f;
    [Tooltip("UI material with the MusicGame/UI/CurtainFade shader (the classic bar over the style bar).")]
    public Material curtainMaterial;
    [Tooltip("Fight: how much one round of difference counts against a full health bar of difference.")]
    [Range(0f, 1f)] public float fightRoundWeight = 0.5f;

    [Header("Runner HUD")]
    [Tooltip("Per style, plus GameMusicStyle.Classical = the classic bar / pause every style fades to.")]
    public List<HudSkin> hud = new();
    [Tooltip("One set for every style: normal → classic.")]
    public List<BonusSkin> bonuses = new();

    [Header("Main menu logo")]
    [Tooltip("The composers (alpha), the same for every style; the TEMPO FURY title is drawn over them.")]
    public Sprite composers;

    [Header("Title")]
    public List<TitleSkin> titles = new();
    [Tooltip("Title shown for a style without its own title art (Classical / Unknown).")]
    public GameMusicStyle titleFallback = GameMusicStyle.Cinematic;

    /// <summary>Runner bar art: 3072×320 canvas; inner panel, counters row, separator, progress track and round
    /// pause slot in its pixels (top-left origin) — shared by every style's bar.</summary>
    public static readonly Vector2 HudBarCanvas = new(3072f, 320f);
    public static readonly Vector2 HudPauseCenter = new(2889.976f, 192.845f);
    public const float HudPauseDiameter = 167.44f;

    /// <summary>Accent shared by runner separators and song progress; fades to classic gold at their position.</summary>
    public Color HudAccent(GameMusicStyle style) => style switch
    {
        GameMusicStyle.Cinematic => new Color32(231, 188, 112, 255),
        GameMusicStyle.Acoustic => new Color32(148, 184, 133, 255),
        GameMusicStyle.Rock => new Color32(237, 37, 60, 255),
        GameMusicStyle.Heavy => new Color32(196, 23, 41, 255),
        GameMusicStyle.Electronic => new Color32(20, 125, 255, 255),
        GameMusicStyle.Dance => new Color32(216, 43, 250, 255),
        GameMusicStyle.Groove => new Color32(255, 181, 38, 255),
        GameMusicStyle.Funky => new Color32(255, 40, 111, 255),
        GameMusicStyle.Soulful => new Color32(42, 175, 186, 255),
        GameMusicStyle.Jazzy => new Color32(233, 186, 80, 255),
        GameMusicStyle.Catchy => new Color32(255, 73, 198, 255),
        GameMusicStyle.Energetic => new Color32(242, 245, 37, 255),
        GameMusicStyle.Chill => new Color32(65, 212, 223, 255),
        GameMusicStyle.Dreamy => new Color32(173, 138, 255, 255),
        GameMusicStyle.Dark => new Color32(137, 98, 187, 255),
        GameMusicStyle.Experimental => new Color32(191, 234, 23, 255),
        _ => new Color32(255, 199, 70, 255),
    };

    public HudSkin Hud(GameMusicStyle style) => hud.Find(h => h.style == style && h.bar != null);
    public HudSkin ClassicHud => Hud(GameMusicStyle.Classical);
    public BonusSkin Bonus(BonusKind kind) => bonuses.Find(b => b.kind == kind && b.normal != null);

    public ButtonSkin Button(GameMusicStyle style) => buttons.Find(b => b.style == style && b.normal != null);

    public TitleSkin Title(GameMusicStyle style) =>
        titles.Find(t => t.style == style && t.background != null) ?? titles.Find(t => t.style == titleFallback);

    /// <summary>The style of the song being played (Unknown before Song Analysis).</summary>
    public static GameMusicStyle CurrentStyle() =>
        GameSession.Instance != null ? GameSession.Instance.DetectedGameStyle : GameMusicStyle.Unknown;

    /// <summary>Runner: weight of the song's style (0 = classic) from the played fraction of the song.</summary>
    public float RunnerWeight(float progress01) =>
        runnerProgressTowardsStyle ? Mathf.Clamp01(progress01) : 1f - Mathf.Clamp01(progress01);

    /// <summary>Runner HUD curtain: where the classic layer starts (UV x of the bar) when `classicShare` (0..1, =
    /// 1 − RunnerWeight) of the song is classic — 1 = none (fully the style), −softness = all of it.</summary>
    public float CurtainEdge(float classicShare) => 1f - Mathf.Clamp01(classicShare) * (1f + runnerCurtainSoftness);

    /// <summary>How classic (0..1) the curtain makes a point at `x01` across the bar (pause slot, a bonus icon…).</summary>
    public float CurtainClassic(float classicShare, float x01)
    {
        float edge = CurtainEdge(classicShare);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x01 - edge) / runnerCurtainSoftness));
    }

    /// <summary>Fight: 0.5 when even, towards 1 when the player leads (health + rounds), towards 0 when the rival does.</summary>
    public float FightWeight(float playerHealth01, float rivalHealth01, int playerRounds, int rivalRounds)
    {
        float lead = (playerHealth01 - rivalHealth01) + fightRoundWeight * (playerRounds - rivalRounds);
        return Mathf.Clamp01(0.5f + 0.5f * Mathf.Clamp(lead, -1f, 1f));
    }
}
