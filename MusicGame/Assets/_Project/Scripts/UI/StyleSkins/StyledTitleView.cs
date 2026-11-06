using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The TEMPO FURY title rebuilt from its layers: background, the nine letters as separate Images (each on its
/// own RectTransform at its place in the original 1920×1080 title, so they can later be animated one by one)
/// and the foreground. Show(style) swaps every layer to that style's art (StyleSkinLibrarySO.titles).
/// Built by TempoFuryUISetup from AssetsMaterial/UI/Titles/&lt;Style&gt;/Letters/layout.json — in the main menu (with the
/// composers behind it) and on the left of the Runner HUD.
/// Intro(tempoAt, furyAt) animates the title in like an arena announcer's call ("Tempoooooo… FURY!"): the background
/// bursts in, T-E-M-P-O slam down one after another from `tempoAt`, the O swells and wobbles while the name is drawn
/// out, and F-U-R-Y slam down fast and hard at `furyAt`, then the foreground and a last thump.
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

    // ── Intro animation ─────────────────────────────────────────────────────────

    private const float LetterTime = 0.26f, LetterStagger = 0.07f, LetterDrop = 70f, LetterSpin = 14f;
    private const float FuryLetterTime = 0.16f, FuryStagger = 0.04f, HoldSwell = 0.22f, HoldWobble = 0.06f, HoldWobbleHz = 6f;
    private const float BurstTime = 0.3f, ForegroundTime = 0.25f, ThumpTime = 0.32f;
    /// <summary>Intro timing when no voice drives it (seconds from the start): TEMPO, then FURY.</summary>
    public const float DefaultTempoAt = 0.15f, DefaultFuryAt = 0.9f;

    private Vector2[] _letterHome;
    private Vector3 _baseScale;
    private Coroutine _intro;

    /// <summary>Animates the title in (unscaled time). The letters land on their beats: the k-th letter of a word
    /// lands k×stagger after the word's moment. Safe to call again (restarts); StopIntro jumps to the end.</summary>
    public void Intro(float tempoAt = DefaultTempoAt, float furyAt = DefaultFuryAt)
    {
        StopIntro();
        if (!isActiveAndEnabled) return;
        _intro = StartCoroutine(IntroRoutine(tempoAt, furyAt));
    }

    public float IntroDuration(float furyAt = DefaultFuryAt) => furyAt + 3 * FuryStagger + ThumpTime;

    public void StopIntro()
    {
        if (_intro != null) { StopCoroutine(_intro); _intro = null; }
        PrepareLetters();
        for (int i = 0; i < letters.Length; i++) PoseLetter(i, 1f, 1f, 0f, 1f);
        Pose(background, 1f, 1f);
        Pose(foreground, 1f, 1f);
        transform.localScale = _baseScale;
    }

    private IEnumerator IntroRoutine(float tempoAt, float furyAt)
    {
        PrepareLetters();
        float end = IntroDuration(furyAt);
        for (float t = 0f; ; t += Time.unscaledDeltaTime)
        {
            // Background: a quick burst just before TEMPO. Letters: each with its own landing moment.
            float b = Mathf.Clamp01((t - (tempoAt - BurstTime * 0.5f)) / BurstTime);
            Pose(background, Back(b, 2.2f), Mathf.Clamp01(b * 2f));
            for (int i = 0; i < letters.Length; i++)
            {
                bool fury = i >= 5;
                float time = fury ? FuryLetterTime : LetterTime;
                float land = fury ? furyAt + (i - 5) * FuryStagger : tempoAt + i * LetterStagger;
                float k = Mathf.Clamp01((t - (land - time)) / time);   // lands (k = 1) exactly at `land`
                float punch = fury ? 1.35f : 1f;
                float s = k < 1f ? Mathf.Lerp(fury ? 2.4f : 1.9f, 1f, k * k) : 1f + 0.12f * punch * Settle(t - land);
                if (i == 4 && k >= 1f) s *= Hold(t - land, furyAt - land);   // the drawn-out "ooooo"
                PoseLetter(i, s, Mathf.Clamp01(k * 3f), (1f - k) * LetterSpin * punch * (i % 2 == 0 ? 1f : -1f), k);
            }
            // After the last letter: the foreground appears and the whole title thumps once.
            float last = furyAt + 3 * FuryStagger;
            Pose(foreground, 1f, Mathf.Clamp01((t - last) / ForegroundTime));
            transform.localScale = _baseScale * (1f + 0.05f * Settle(t - last));
            if (t >= end) break;
            yield return null;
        }
        _intro = null;
        StopIntro();
    }

    // The O while the name is held: swells and wobbles until FURY, then snaps back as F lands.
    private static float Hold(float since, float length)
    {
        if (length <= 0f || since >= length) return 1f;
        float k = since / length;
        return 1f + HoldSwell * Mathf.SmoothStep(0f, 1f, k) + HoldWobble * k * Mathf.Sin(since * HoldWobbleHz * 2f * Mathf.PI);
    }

    // A decaying bump after an impact (0 before it, 1 → 0 over ThumpTime).
    private static float Settle(float since) => since < 0f || since > ThumpTime ? 0f : Mathf.Sin(Mathf.PI * (1f - since / ThumpTime)) * (1f - since / ThumpTime);

    private static float Back(float k, float overshoot)
    {
        k -= 1f;
        return k * k * ((overshoot + 1f) * k + overshoot) + 1f;
    }

    // Letters are laid out with a corner pivot: re-pivot them on their centre (same place) so they scale and spin in place.
    private void PrepareLetters()
    {
        if (_letterHome != null) return;
        _baseScale = transform.localScale;   // the menu shows the title scaled down
        _letterHome = new Vector2[letters.Length];
        for (int i = 0; i < letters.Length; i++)
        {
            if (letters[i] == null) continue;
            var rt = letters[i].rectTransform;
            var shift = (new Vector2(0.5f, 0.5f) - rt.pivot) * rt.rect.size;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition += shift;
            _letterHome[i] = rt.anchoredPosition;
        }
    }

    private void PoseLetter(int i, float scale, float alpha, float spin, float k)
    {
        var img = letters[i];
        if (img == null) return;
        var rt = img.rectTransform;
        rt.anchoredPosition = _letterHome[i] + Vector2.up * LetterDrop * (1f - k);
        rt.localScale = Vector3.one * scale;
        rt.localRotation = Quaternion.Euler(0f, 0f, spin);
        var c = img.color; c.a = alpha; img.color = c;
    }

    private static void Pose(Image img, float scale, float alpha)
    {
        if (img == null) return;
        img.rectTransform.localScale = Vector3.one * scale;
        var c = img.color; c.a = alpha; img.color = c;
    }

    private void OnDisable() { if (_intro != null) StopIntro(); }

    private const string LastStyleKey = "TempoFury.LastTitleStyle";
    private static GameMusicStyle? _launchStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLaunchStyle() => _launchStyle = null;   // each Play session is a new launch

    /// <summary>The main menu's style for this launch: random among the styles with title art, never the same as
    /// the previous launch's; the same every time the menu comes back during one launch.</summary>
    public static GameMusicStyle MenuStyle(StyleSkinLibrarySO library)
    {
        if (_launchStyle.HasValue) return _launchStyle.Value;
        if (library == null || library.titles.Count == 0) return GameMusicStyle.Unknown;
        int last = PlayerPrefs.GetInt(LastStyleKey, -1);
        var choices = library.titles.FindAll(t => (int)t.style != last);
        if (choices.Count == 0) choices = library.titles;
        var style = choices[Random.Range(0, choices.Count)].style;
        PlayerPrefs.SetInt(LastStyleKey, (int)style);
        PlayerPrefs.Save();
        _launchStyle = style;
        return style;
    }

    private static void Set(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
