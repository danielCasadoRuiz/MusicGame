using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The TEMPO FURY title rebuilt from its layers: background, the nine letters as separate Images (each on its
/// own RectTransform at its place in the original 1920×1080 title, so they can later be animated one by one)
/// and the foreground. Show(style) swaps every layer to that style's art (StyleSkinLibrarySO.titles).
/// Built by TempoFuryUISetup from AssetsMaterial/UI/Titles/&lt;Style&gt;/Letters/layout.json.
/// </summary>
public class StyledTitleView : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Image background;
    [Tooltip("T, E, M, P, O, F, U, R, Y")]
    public Image[] letters = new Image[9];
    public Image foreground;

    public GameMusicStyle Shown { get; private set; } = GameMusicStyle.Unknown;

    public bool Show(GameMusicStyle style)
    {
        var skin = library != null ? library.Title(style) : null;
        if (skin == null) return false;
        Shown = skin.style;
        Set(background, skin.background);
        Set(foreground, skin.foreground);
        for (int i = 0; i < letters.Length; i++) Set(letters[i], i < skin.letters.Length ? skin.letters[i] : null);
        return true;
    }

    private const string LastStyleKey = "TempoFury.LastTitleStyle";

    /// <summary>A random style with title art, never the same as the previous time the game started.</summary>
    public static GameMusicStyle MenuStyle(StyleSkinLibrarySO library)
    {
        if (library == null || library.titles.Count == 0) return GameMusicStyle.Unknown;
        int last = PlayerPrefs.GetInt(LastStyleKey, -1);
        var choices = library.titles.FindAll(t => (int)t.style != last);
        if (choices.Count == 0) choices = library.titles;
        var style = choices[Random.Range(0, choices.Count)].style;
        PlayerPrefs.SetInt(LastStyleKey, (int)style);
        PlayerPrefs.Save();
        return style;
    }

    private static void Set(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
