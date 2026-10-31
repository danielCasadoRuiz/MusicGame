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
    [Tooltip("Fight: how much one round of difference counts against a full health bar of difference.")]
    [Range(0f, 1f)] public float fightRoundWeight = 0.5f;

    [Header("Title")]
    public List<TitleSkin> titles = new();
    [Tooltip("Title shown for a style without its own title art (Classical / Unknown).")]
    public GameMusicStyle titleFallback = GameMusicStyle.Cinematic;

    public ButtonSkin Button(GameMusicStyle style) => buttons.Find(b => b.style == style && b.normal != null);

    public TitleSkin Title(GameMusicStyle style) =>
        titles.Find(t => t.style == style && t.background != null) ?? titles.Find(t => t.style == titleFallback);

    /// <summary>The style of the song being played (Unknown before Song Analysis).</summary>
    public static GameMusicStyle CurrentStyle() =>
        GameSession.Instance != null ? GameSession.Instance.DetectedGameStyle : GameMusicStyle.Unknown;

    /// <summary>Runner: weight of the song's style (0 = classic) from the played fraction of the song.</summary>
    public float RunnerWeight(float progress01) =>
        runnerProgressTowardsStyle ? Mathf.Clamp01(progress01) : 1f - Mathf.Clamp01(progress01);

    /// <summary>Fight: 0.5 when even, towards 1 when the player leads (health + rounds), towards 0 when the rival does.</summary>
    public float FightWeight(float playerHealth01, float rivalHealth01, int playerRounds, int rivalRounds)
    {
        float lead = (playerHealth01 - rivalHealth01) + fightRoundWeight * (playerRounds - rivalRounds);
        return Mathf.Clamp01(0.5f + 0.5f * Mathf.Clamp(lead, -1f, 1f));
    }
}
