using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared neutral analysis, followed by the detected style and one direct HUD handoff.</summary>
public sealed class AnalysisPresentation : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Texture2D barAtlas; // Legacy prefab field; progress now uses a plain, tintable fill.
    public Texture2D motifAtlas;
    public Texture2D cornerAtlas;
    public Texture2D danceBackdrop;
    public Shader styleFlashShader;
    public AudioClip styleRevealClip;
    public Texture2D danceFrame;
    public Texture2D loadingWaveAtlas; // Legacy Dance atlas; no longer used by the neutral animation.
    public Texture2D neutralStaffAtlas;
    public Shader staffTintShader;
    public const int WaveColumns = 4, WaveRows = 8, WaveFrames = 32;
    public const float WaveFramesPerSecond = 20f;
    public const float BrushWordSpacing = 22f;
    [Range(0, 1)] public float motifOpacity = .72f;
    public Color provisionalBackground = new(.018f, .024f, .035f, 1);

    private static readonly string[] Hex = { "53BFFF", "E8BC65", "C99650", "DCA45C", "ED3944", "AD293D", "208FFF", "AC59FF", "42B9B0", "F166AD", "BE7B86", "D6AE63", "FF7EAB", "FFAA30", "78C9CE", "B4A4FF", "8776B8", "A9E883" };
    private static readonly GameMusicStyle[] MotifOrder = {
        GameMusicStyle.Electronic, GameMusicStyle.Dance, GameMusicStyle.Rock, GameMusicStyle.Heavy,
        GameMusicStyle.Acoustic, GameMusicStyle.Cinematic, GameMusicStyle.Groove, GameMusicStyle.Funky,
        GameMusicStyle.Soulful, GameMusicStyle.Jazzy, GameMusicStyle.Catchy, GameMusicStyle.Energetic,
        GameMusicStyle.Chill, GameMusicStyle.Dreamy, GameMusicStyle.Dark, GameMusicStyle.Experimental };
    public static Color Accent(GameMusicStyle style)
    {
        if (style == GameMusicStyle.Unknown) return new Color(.65f, .64f, .60f);
        ColorUtility.TryParseHtmlString("#" + Hex[Mathf.Clamp((int)style, 0, 17)], out var color); return color;
    }
    private AnalyzingScreenView view;
    private Image backdrop, wave, progressTrack;
    private RectTransform block;
    private CanvasGroup metadata;
    private TextMeshProUGUI song, artist, genre;
    private Sprite[] waveSprites, motifSprites;
    private Image[] motifs;
    private FrontendStyleBackground screenBackground;
    private Material staffMaterial;
    private GameMusicStyle current;
    private Color inheritedTint, displayedTint;
    private float revealBlend = 1, handoffProgress;
    private bool carryTint;
    private int waveIndex = -1;
    private Color ThemeAccent(GameMusicStyle style)=>library!=null?library.SongHeadingColor(style,false):Accent(style);
    private Color Ivory => library != null ? library.titleColor : new Color(.953f, .918f, .827f, 1);
    public GameMusicStyle CurrentStyle => current;
    public float HandoffProgress => handoffProgress;

    public void Build(AnalyzingScreenView target)
    {
        if (view != null) return;
        view = target;
        screenBackground = GetComponent<FrontendStyleBackground>();
        foreach (var receiver in GetComponentsInChildren<ThemeElementReceiver>(true)) receiver.enabled = false;
        backdrop = transform.Find("Dim")?.GetComponent<Image>();
        if (view.titleText.TryGetComponent<TitleText>(out var title)) title.enabled = false;
        view.titleText.gameObject.SetActive(false);
        block = UIFactory.CreateRect("AnalysisContent", (RectTransform)transform);
        Box(block, 0, 0, 1450, 850);
        var details = UIFactory.CreateRect("Metadata", block);
        Box(details, 0, 0, 1100, 700);
        metadata = details.gameObject.AddComponent<CanvasGroup>();
        song = Text("Song", details, 0, -150, 1100, 105, 78, Ivory);
        song.fontSizeMin = 8;
        song.wordSpacing = BrushWordSpacing;
        if (library != null && library.titleFont != null)
        { song.font = library.titleFont; song.fontSharedMaterial = library.titleFont.material; }
        var songInk=song.gameObject.AddComponent<StyleTitleInk>();songInk.library=library;
        artist = Text("Artist", details, 0, -220, 1100, 48, 30, Ivory);
        artist.font = view.tipText.font; artist.fontSharedMaterial = view.tipText.fontSharedMaterial;
        view.tipText.transform.SetParent(details, false);
        Box(view.tipText.rectTransform, 0, -315, 1100, 38);
        view.tipText.alignment = TextAlignmentOptions.MidlineLeft;
        view.tipText.fontStyle = FontStyles.Normal;
        view.tipText.enableAutoSizing = true; view.tipText.fontSizeMin = 17; view.tipText.fontSizeMax = 24;
        view.tipText.textWrappingMode = TextWrappingModes.NoWrap;
        view.tipText.color = new Color(.72f, .74f, .79f);
        var oldBar = view.progressFill.transform.parent.gameObject;
        view.progressFill.transform.SetParent(details, false); oldBar.SetActive(false);
        progressTrack = Panel("ProgressTrack", details, new Color(.2f, .19f, .23f));
        Box(progressTrack.rectTransform, 0, -270, 1100, 7);
        UIFactory.EnsureFillSprite(progressTrack);
        view.progressFill.transform.SetAsLastSibling();
        view.progressFill.sprite = progressTrack.sprite;
        Box(view.progressFill.rectTransform, 0, -270, 1100, 7);
        genre = Text("StyleSlot", block, 0, 150, 1000, 190, 180, Ivory);
        genre.alignment = TextAlignmentOptions.Center;
        genre.wordSpacing = BrushWordSpacing;
        if (library != null && library.titleFont != null)
        { genre.font = library.titleFont; genre.fontSharedMaterial = library.titleFont.material; }
        genre.gameObject.SetActive(false); // A single moving label appears here, never beside the song.
        BuildOtherStyleMotifs();
        if (neutralStaffAtlas != null)
        {
            waveSprites = new Sprite[WaveFrames];
            float width = neutralStaffAtlas.width / (float)WaveColumns, height = neutralStaffAtlas.height / (float)WaveRows;
            for (int n = 0; n < WaveFrames; n++)
                waveSprites[n] = Sprite.Create(neutralStaffAtlas,
                    new Rect(n % WaveColumns * width, (WaveRows - 1 - n / WaveColumns) * height, width, height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            wave = Panel("LoadingWave", block, Color.white);
            Box(wave.rectTransform, 0, 140, 1450, 455);
            wave.preserveAspect = true; wave.sprite = waveSprites[0];
            if (staffTintShader != null)
            {
                staffMaterial = new Material(staffTintShader) { name = "Analysis staff - dynamic tint" };
                staffMaterial.SetColor("_Highlight", Ivory); wave.material = staffMaterial;
            }
        }
        foreach(var label in new[]{song,artist,genre,view.tipText})
        {
            var originalColor=label.color;
            var contrast=label.GetComponent<AppearanceText>();
            if(contrast==null)contrast=label.gameObject.AddComponent<AppearanceText>();
            contrast.night=originalColor;contrast.background=screenBackground;
        }
        inheritedTint = Ivory; ApplyImmediate(GameMusicStyle.Unknown);
    }
    private void BuildOtherStyleMotifs()
    {
        var texture = motifAtlas;
        if (texture == null) return;
        motifSprites = new Sprite[18]; motifs = new Image[2];
        float width = texture.width / 4f, height = texture.height / 4f;
        float padding = .5f;
        for (int n = 0; n < MotifOrder.Length; n++)
            motifSprites[(int)MotifOrder[n]] = Sprite.Create(texture,
                new Rect(n % 4 * width + padding, (3 - n / 4) * height + padding, width - padding * 2, height - padding * 2),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        for (int n = 0; n < motifs.Length; n++)
        {
            motifs[n] = Panel("StyleMotif" + n, (RectTransform)transform, Color.clear);
            motifs[n].preserveAspect = true;
            motifs[n].transform.SetSiblingIndex(backdrop != null ? backdrop.transform.GetSiblingIndex() + 1 : 0);
        }
    }
    private static Image Panel(string name, RectTransform parent, Color color)
    { var image = UIFactory.CreatePanel(name, parent, color); image.raycastTarget = false; return image; }
    private static TextMeshProUGUI Text(string name, RectTransform parent, float x, float y, float width, float height, int size, Color color)
    {
        var text = UIFactory.CreateText(name, parent, "", size, color, TextAlignmentOptions.MidlineLeft);
        Box(text.rectTransform, x, y, width, height); text.raycastTarget = false; text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.enableAutoSizing = true;
        text.fontSizeMax = size; text.fontSizeMin = 8; text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }
    private static void Box(RectTransform rect, float x, float y, float width, float height) =>
        UIFactory.SetBox(rect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, y), new Vector2(width, height));
    private void ApplyImmediate(GameMusicStyle style)
    { current = style; revealBlend = 1; ApplyTheme(style == GameMusicStyle.Unknown ? inheritedTint : ThemeAccent(style), style == GameMusicStyle.Unknown ? 0 : 1); }
    private void ApplyTheme(Color tint, float reveal)
    {
        displayedTint = tint;
        if (backdrop != null) backdrop.color = Color.Lerp(provisionalBackground, tint, .075f);
        if (staffMaterial != null) staffMaterial.SetColor("_Tint", Color.Lerp(Ivory, tint, .68f));
        if (wave != null) wave.color = new Color(1, 1, 1, Mathf.Lerp(1, .26f, reveal) * (1 - handoffProgress));
        if (progressTrack != null) view.progressFill.color = Color.Lerp(Ivory, tint, .55f);
        bool hasStyleBackground = screenBackground != null && screenBackground.HomeTexture != null;
        if (screenBackground != null)
            screenBackground.Show(current, screenBackground.HomeStyle, reveal);
        if (motifs != null)
            for (int n = 0; n < motifs.Length; n++)
            {
                var image = motifs[n];
                image.sprite = motifSprites[(int)current];
                image.enabled = image.sprite != null && !hasStyleBackground;
                image.color = new Color(1, 1, 1, reveal * motifOpacity);
            }
    }
    public void Begin()
    {
        if (view == null) return;
        DetectedStyleLabel.Cancel();
        if (!carryTint) inheritedTint = ThemeAccent(StyledTitleView.MenuStyle(library));
        carryTint = false; handoffProgress = 0; metadata.alpha = 1;
        ApplyImmediate(GameMusicStyle.Unknown);
        var selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null;
        string name = selected?.DisplayName ?? "";
        if (string.IsNullOrWhiteSpace(name) && selected.HasValue && !string.IsNullOrWhiteSpace(selected.Value.LocalFilePath))
            name = System.IO.Path.GetFileNameWithoutExtension(selected.Value.LocalFilePath);
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\s+[—–-]\s+");
        song.text = match.Success ? name.Substring(match.Index + match.Length).Trim() : name;
        artist.text = match.Success ? name.Substring(0, match.Index).Trim() : "";
    }
    public void BeginPendingSelection()
    {
        var prior = GameSession.Instance != null ? GameSession.Instance.DetectedGameStyle : GameMusicStyle.Unknown;
        Begin(); inheritedTint = prior == GameMusicStyle.Unknown ? inheritedTint : ThemeAccent(prior);
        carryTint = true; ApplyImmediate(GameMusicStyle.Unknown); song.text = artist.text = "";
    }
    public void Reveal(GameMusicStyle style)
    {
        if (view == null) return;
        if (styleRevealClip != null && GameAudio.Instance != null) GameAudio.Instance.PlayClip(styleRevealClip, .65f, GameAudioScope.UI);
        Canvas.ForceUpdateCanvases();
        DetectedStyleLabel.Show(genre, style, (RectTransform)transform.parent, styleFlashShader, true, true);
        current = style; inheritedTint = displayedTint; revealBlend = 0;
        ApplyTheme(inheritedTint, 0);
    }
    public void SetHandoffProgress(float progress)
    {
        handoffProgress = Mathf.Clamp01(progress);
        metadata.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(handoffProgress / .6f));
        ApplyTheme(displayedTint, current == GameMusicStyle.Unknown ? 0 : Mathf.SmoothStep(0, 1, revealBlend));
    }
    private void Update()
    {
        if (view == null) return;
        if (revealBlend < 1)
        {
            revealBlend = Mathf.Min(1, revealBlend + Time.unscaledDeltaTime / .85f);
            float blend = Mathf.SmoothStep(0, 1, revealBlend);
            ApplyTheme(Color.Lerp(inheritedTint, ThemeAccent(current), blend), blend);
        }
        int frame = Mathf.FloorToInt(Time.unscaledTime * WaveFramesPerSecond) % WaveFrames;
        if (wave != null && frame != waveIndex) { wave.sprite = waveSprites[frame]; waveIndex = frame; }
        var rect = ((RectTransform)transform).rect;
        block.localScale = Vector3.one * Mathf.Max(.2f, Mathf.Min(1f, Mathf.Min((rect.width - 90) / 1600f, (rect.height - 90) / 900f)));
        if (motifs != null)
        {
            float size = Mathf.Min(620, Mathf.Min(rect.width * .32f, rect.height * .58f));
            for (int n = 0; n < motifs.Length; n++)
            {
                var rt = motifs[n].rectTransform;
                rt.anchorMin = rt.anchorMax = n == 0 ? new Vector2(0, 1) : new Vector2(1, 0);
                rt.pivot = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = n == 0 ? new Vector2(size * .26f, -size * .26f) : new Vector2(-size * .26f, size * .26f);
                rt.localRotation = Quaternion.Euler(0, 0, n == 0 ? 0 : 180);
            }
        }
    }
    private void OnDisable() { carryTint = false; }
    private void OnDestroy()
    {
        if (waveSprites != null) foreach (var sprite in waveSprites) Dispose(sprite);
        if (motifSprites != null) foreach (var sprite in motifSprites) Dispose(sprite);
        Dispose(staffMaterial);
    }
    private static void Dispose(Object value)
    { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
