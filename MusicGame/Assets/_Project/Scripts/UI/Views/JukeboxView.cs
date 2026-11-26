using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JukeboxView : MonoBehaviour
{
    public RectTransform list, cabinetContent;
    [Tooltip("Breathing room around the complete cabinet, at a 1080-high viewport.")]
    public Vector2 framingInsets=new Vector2(90,50);
    public TextMeshProUGUI summary, selected, nowPlaying, status, playLabel, backgroundLabel;
    public Image cover, progress, progressKnob, transportIcon;
    public Sprite defaultCover, playIcon, pauseIcon;
    public Sprite[] styleSleeves;
    public Image[] medallions;
    public JukeboxOwnerPlaque ownerPlaque;
    public JukeboxCarousel carousel;
    public StyledTitleView tempoFury;
    public TextMeshProUGUI archedTitle;
    public Sprite rockFuryLetter;

    private const float CabinetDrop = 22f;
    private bool _cabinetReframed;
    private GameObject _sleeveBezel;

    private void OnEnable()
    {
        if(archedTitle!=null&&tempoFury!=null)
        {
            var ink=archedTitle.GetComponent<StyleTitleInk>();if(ink==null)ink=archedTitle.gameObject.AddComponent<StyleTitleInk>();
            ink.library=tempoFury.library;ink.inverseOutline=true;ink.Apply();
        }
        ApplyTitleCurve();
        FitCabinet();
    }

    // A freshly spawned or re-opened canvas may finish its layout after OnEnable.
    // Fit the common art/control parent once that viewport size is available.
    private Vector2 _fittedViewport, _fittedDesign;
    private void LateUpdate()
    {
        if (cabinetContent == null) return;
        if (((RectTransform)transform).rect.size != _fittedViewport || cabinetContent.rect.size != _fittedDesign)
            FitCabinet();
    }

    public void ApplyTitleCurve()
    {
        if (archedTitle == null) return;
        archedTitle.OnPreRenderText -= CurveTitle;
        archedTitle.OnPreRenderText += CurveTitle;
        archedTitle.SetVerticesDirty();
    }

    private void OnDisable()
    {
        if (archedTitle != null) archedTitle.OnPreRenderText -= CurveTitle;
    }

    private static void CurveTitle(TMP_TextInfo info)
    {
        if (info.characterCount < 2) return;
        float first = info.characterInfo[0].origin;
        float last = info.characterInfo[info.characterCount - 1].xAdvance;
        float width = last - first;
        if (width <= 0) return;

        for (int i = 0; i < info.characterCount; i++)
        {
            var glyph = info.characterInfo[i];
            if (!glyph.isVisible) continue;
            var vertices = info.meshInfo[glyph.materialReferenceIndex].vertices;
            int v = glyph.vertexIndex;
            var pivot = new Vector3((vertices[v].x + vertices[v + 2].x) * .5f, glyph.baseLine, 0);
            float t = (pivot.x - first) / width * 2 - 1;
            float rise = -7 * t * t;
            var tilt = Quaternion.Euler(0, 0, -3 * t);
            for (int j = 0; j < 4; j++)
                vertices[v + j] = pivot + tilt * (vertices[v + j] - pivot) + Vector3.up * rise;
        }
    }

    public void ApplyBrand(GameMusicStyle current)
    {
        if (tempoFury == null) return;
        if (current == GameMusicStyle.Unknown) current = StyledTitleView.MenuStyle(tempoFury.library);
        if (!tempoFury.Show(current)) return;
        if (tempoFury.Shown == GameMusicStyle.Rock && rockFuryLetter != null && tempoFury.letters.Length > 5)
            tempoFury.letters[5].sprite = rockFuryLetter;
    }

    public Sprite SleeveFor(JukeboxTrack track)
    {
        if (track?.cover != null) return track.cover;
        if (styleSleeves == null || styleSleeves.Length < 9 || track == null) return defaultCover;
        int index = track.style switch
        {
            GameMusicStyle.Classical or GameMusicStyle.Cinematic => 0,
            GameMusicStyle.Jazzy => 1,
            GameMusicStyle.Catchy => 2,
            GameMusicStyle.Rock or GameMusicStyle.Heavy or GameMusicStyle.Energetic => 3,
            GameMusicStyle.Electronic or GameMusicStyle.Experimental => 4,
            GameMusicStyle.Acoustic => 5,
            GameMusicStyle.Funky or GameMusicStyle.Soulful => 6,
            GameMusicStyle.Dance => 7,
            GameMusicStyle.Groove => 8,
            _ => -1
        };
        return index >= 0 ? styleSleeves[index] : defaultCover;
    }
    public TextMeshProUGUI detail, elapsed, duration;
    public Button back, preview, full, previous, playPause, next, lyrics, delete;
    public Button background;
    public GameObject dialog;
    public TextMeshProUGUI dialogTitle, dialogBody, confirmLabel;
    public Button confirm, cancel;
    public GameObject libraryPanel, lyricsPanel;
    public ScrollRect lyricsScroll;
    public RectTransform lyricsContent;
    public TextMeshProUGUI lyricsLine;
    public GameObject lyricsLock;

    private void OnRectTransformDimensionsChange() => FitCabinet();

    public void FitCabinet()
    {
        if (cabinetContent == null) return;
        var size = ((RectTransform)transform).rect.size;
        var design = cabinetContent.rect.size;
        if (size.x <= 0 || size.y <= 0 || design.x <= 0 || design.y <= 0) return;
        var available=size-framingInsets*(2*size.y/1080f);
        float widthScale = Mathf.Max(1,available.x) / design.x;
        float heightScale = Mathf.Max(1,available.y) / design.y;
        float scale = Mathf.Min(widthScale, heightScale);
        // The authored 1740 x 980 proportions differ slightly from exact 16:9.
        // Absorb that small difference without separating the artwork from its controls.
        cabinetContent.localScale = Mathf.Max(widthScale, heightScale) <= scale * 1.01f
            ? new Vector3(widthScale, heightScale, 1)
            : new Vector3(scale, scale, 1);
        _fittedViewport = size;
        _fittedDesign = design;
    }

    private JukeboxTimeline _timeline;
    public float? TimelinePreview => _timeline != null && _timeline.Scrubbing ? _timeline.value : null;

    public void CancelTimelineScrub() => _timeline?.CancelScrub();

    public void BindTimeline(System.Action<float> seek)
    {
        var track = cabinetContent != null ? cabinetContent.Find("ProgressTrack") as RectTransform : null;
        if (track == null) return;
        var hit = new GameObject("Playback Timeline", typeof(RectTransform), typeof(Image), typeof(JukeboxTimeline));
        var rect = (RectTransform)hit.transform;
        rect.SetParent(track, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
        rect.pivot = new Vector2(0, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(track.rect.width, 48f);
        var graphic = hit.GetComponent<Image>();
        graphic.color = Color.clear;
        var range = new GameObject("Range", typeof(RectTransform)).GetComponent<RectTransform>();
        range.SetParent(rect, false);
        _timeline = hit.GetComponent<JukeboxTimeline>();
        _timeline.targetGraphic = graphic;
        _timeline.transition = Selectable.Transition.None;
        _timeline.fillRect = range;
        _timeline.interactable = false;
        _timeline.SeekRequested += seek;
    }

    public void SetProgress(float seconds, float total, bool canSeek = false)
    {
        if (progress == null) return;
        float fraction = total > 0 ? Mathf.Clamp01(seconds / total) : 0;
        if (_timeline != null)
        {
            _timeline.interactable = canSeek && total > 0;
            if (!_timeline.interactable) _timeline.CancelScrub();
            if (_timeline.Scrubbing)
            {
                fraction = _timeline.value;
                seconds = fraction * total;
            }
            else _timeline.SetValueWithoutNotify(fraction);
        }
        float width = 465 * fraction;
        var bar = progress.rectTransform;
        bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
        progress.color = JukeboxNeon.Tone(3);
        var knob = progressKnob.rectTransform;
        knob.anchoredPosition = new Vector2(bar.anchoredPosition.x + width - knob.rect.width * .5f, bar.anchoredPosition.y + 11);
        elapsed.text = Clock(seconds);
        duration.text = Clock(total);
    }

    private static string Clock(float time)
    {
        int seconds = Mathf.Max(0, Mathf.FloorToInt(time));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    public void ConfigurePlaybackHeader()
    {
        FitCabinet();
        JukeboxRowView.ApplyButtonFeedback(previous);
        JukeboxRowView.ApplyButtonFeedback(playPause);
        JukeboxRowView.ApplyButtonFeedback(next);
        JukeboxRowView.ApplyButtonFeedback(back);
        preview.gameObject.SetActive(false);
        full.gameObject.SetActive(false);
        if (detail != null)
        {
            ConfigureCabinetPresentation();
            return;
        }
        var titleRect = selected.rectTransform;
        var offerRect = (RectTransform)full.transform;
        offerRect.anchorMin = titleRect.anchorMin;
        offerRect.anchorMax = titleRect.anchorMax;
        offerRect.pivot = titleRect.pivot;
        offerRect.anchoredPosition = titleRect.anchoredPosition;
        offerRect.sizeDelta = new Vector2(titleRect.rect.width,
            titleRect.anchoredPosition.y - nowPlaying.rectTransform.anchoredPosition.y + nowPlaying.rectTransform.rect.height);
        full.gameObject.SetActive(false);
    }

    private void ConfigureCabinetPresentation()
    {
        if (_cabinetReframed || cabinetContent == null) return;
        var cabinet = cabinetContent.Find("Cabinet")?.GetComponent<Image>();
        var aperture = libraryPanel.GetComponent<ScrollRect>()?.viewport.GetComponent<Image>();
        if (cabinet == null || cabinet.sprite == null || aperture == null || aperture.sprite == null) return;
        _cabinetReframed = true;

        // Place the card before shifting its neighbours so all coordinates share the same origin.
        var offer = (RectTransform)full.transform;
        Place(offer, 905, 270, 672, 358);
        var plate = full.image;
        var buttonSprite = plate.sprite;
        plate.sprite = aperture.sprite;
        plate.type = Image.Type.Simple;
        plate.color = new Color(.67f, .42f, .19f);

        var inset = new GameObject("Offer Inset", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        inset.rectTransform.SetParent(offer, false);
        inset.transform.SetAsFirstSibling();
        Place(inset.rectTransform, 3, 3, 666, 352);
        inset.sprite = aperture.sprite;
        inset.color = new Color(.14f, .045f, .032f);
        inset.raycastTarget = false;

        var label = full.GetComponentInChildren<TextMeshProUGUI>(true);
        var action = new GameObject("Offer Action", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        action.rectTransform.SetParent(offer, false);
        action.transform.SetSiblingIndex(1);
        Place(action.rectTransform, 100, 193, 472, 73);
        action.sprite = buttonSprite;
        action.type = Image.Type.Sliced;
        action.pixelsPerUnitMultiplier = plate.pixelsPerUnitMultiplier;
        action.raycastTarget = false;

        var heading = new GameObject("Product Title", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        heading.rectTransform.SetParent(offer, false);
        heading.font = label.font;
        heading.alignment = TextAlignmentOptions.Center;
        heading.enableAutoSizing = true;
        heading.text = "Jukebox Premium";
        heading.fontStyle = FontStyles.Bold;
        heading.fontSizeMax = 49;
        heading.fontSizeMin = 28;
        heading.margin = Vector4.zero;
        heading.color = new Color(1f, .92f, .76f);
        heading.raycastTarget = false;
        Place(heading.rectTransform, 38, 92, 596, 78);
        label.margin = Vector4.zero;
        label.fontSizeMax = 29;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        Place(label.rectTransform, 137, 196, 398, 66);

        _sleeveBezel = cabinetContent.Find("SleeveBezel")?.gameObject;
        // The 21 added art pixels occupy 22 units in the 980-unit design.
        foreach (RectTransform child in cabinetContent)
            if (child != cabinet.rectTransform && child.name!="PaperSpillRoot" && child.name!="OwnerDisplayRoot" && child.name!="CabinetClearedTop")
                child.anchoredPosition += Vector2.down * CabinetDrop;
    }

    public void ShowPremiumOffer(bool show)
    {
        full.gameObject.SetActive(show);
        if (_sleeveBezel != null) _sleeveBezel.SetActive(!show);
        if (show) Loc.Set(full.GetComponentInChildren<TextMeshProUGUI>(true), "Jukebox.FullPremium");
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
