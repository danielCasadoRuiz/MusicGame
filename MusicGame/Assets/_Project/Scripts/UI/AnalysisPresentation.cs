using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only: real progress and preparation lifetime remain owned by the controller.</summary>
public sealed class AnalysisPresentation : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Texture2D barAtlas;
    public Texture2D motifAtlas;
    public Shader styleFlashShader;
    public AudioClip styleRevealClip;
    [Range(0,1)] public float motifOpacity = .72f;
    private Image backdrop;
    private Image[] motifs, incomingMotifs;
    private Sprite[] motifSprites;
    private Color fromBackground, toBackground;
    private static readonly GameMusicStyle[] MotifOrder = {
        GameMusicStyle.Electronic, GameMusicStyle.Dance, GameMusicStyle.Rock, GameMusicStyle.Heavy,
        GameMusicStyle.Acoustic, GameMusicStyle.Cinematic, GameMusicStyle.Groove, GameMusicStyle.Funky,
        GameMusicStyle.Soulful, GameMusicStyle.Jazzy, GameMusicStyle.Catchy, GameMusicStyle.Energetic,
        GameMusicStyle.Chill, GameMusicStyle.Dreamy, GameMusicStyle.Dark, GameMusicStyle.Experimental
    };
    public Color provisionalBackground = new Color(.018f, .024f, .035f, 1);
    private AnalyzingScreenView view;
    private TextMeshProUGUI song, artist;
    private Image frame, nextFrame, nextFill, sheen;
    private RectTransform block;
    private Sprite[] frames, fills;
    private GameMusicStyle current;
    private float blend = 1;
    private Color fromColor, toColor, displayedAccent;
    private Material titleMaterial;
    private Color Ivory => library != null ? library.titleColor : new Color(.953f,.918f,.827f,1);
    private static readonly string[] Hex = { "53BFFF", "E8BC65", "C99650", "DCA45C", "ED3944", "AD293D", "208FFF", "AC59FF", "42B9B0", "F166AD", "BE7B86", "D6AE63", "FF7EAB", "FFAA30", "78C9CE", "B4A4FF", "8776B8", "A9E883" };
    public static Color Accent(GameMusicStyle style) { if(style==GameMusicStyle.Unknown) return new Color(.65f,.64f,.60f); ColorUtility.TryParseHtmlString("#" + Hex[Mathf.Clamp((int)style, 0, 17)], out var c); return c; }

    public void Build(AnalyzingScreenView target)
    {
        view = target;
        foreach (var receiver in GetComponentsInChildren<ThemeElementReceiver>(true)) receiver.enabled = false;
        backdrop = transform.Find("Dim")?.GetComponent<Image>();
        if (backdrop != null) backdrop.color = provisionalBackground;
        if (motifAtlas != null)
        {
            motifSprites = new Sprite[18];
            float w=motifAtlas.width/4f, h=motifAtlas.height/4f;
            for(int n=0;n<MotifOrder.Length;n++)
                motifSprites[(int)MotifOrder[n]]=Sprite.Create(motifAtlas,
                    new Rect(n%4*w+.5f,(3-n/4)*h+.5f,w-1,h-1),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
            motifs = CreateMotifs("StyleMotif");
            incomingMotifs = CreateMotifs("IncomingMotif");
            int sibling = backdrop != null ? backdrop.transform.GetSiblingIndex()+1 : 0;
            foreach(var image in motifs) image.transform.SetSiblingIndex(sibling++);
            foreach(var image in incomingMotifs) image.transform.SetSiblingIndex(sibling++);
        }
        if (view.titleText.TryGetComponent<TitleText>(out var title)) title.enabled = false;
        block = UIFactory.CreateRect("AnalysisContent", (RectTransform)transform);
        Box(block, 0, 0, 1100, 420);
        view.titleText.transform.SetParent(block, false);
        Box(view.titleText.rectTransform, 0, 125, 1060, 115);
        if (library != null && library.titleFont != null) { view.titleText.font = library.titleFont; view.titleText.fontSharedMaterial = library.titleFont.material; }
        // Own a material instance: never change the font asset used by other screens.
        titleMaterial = new Material(view.titleText.fontSharedMaterial);
        titleMaterial.name = "Analysis Title · Ivory / Ink";
        titleMaterial.SetColor("_FaceColor", Color.white);
        titleMaterial.SetColor("_OutlineColor", new Color(.012f,.014f,.022f,1));
        titleMaterial.SetFloat("_OutlineWidth", .18f);
        titleMaterial.SetFloat("_OutlineSoftness", .015f);
        titleMaterial.EnableKeyword("UNDERLAY_ON");
        titleMaterial.SetColor("_UnderlayColor", Ivory);
        titleMaterial.SetFloat("_UnderlayOffsetX", 0);
        titleMaterial.SetFloat("_UnderlayOffsetY", 0);
        titleMaterial.SetFloat("_UnderlayDilate", .42f);
        titleMaterial.SetFloat("_UnderlaySoftness", .025f);
        view.titleText.fontSharedMaterial = titleMaterial;
        view.titleText.UpdateMeshPadding();
        view.titleText.colorGradientPreset = null;
        view.titleText.enableVertexGradient = true;
        view.titleText.fontStyle = FontStyles.Normal;
        view.titleText.enableAutoSizing = true;
        view.titleText.fontSizeMin = 36; view.titleText.fontSizeMax = 78;
        view.titleText.textWrappingMode = TextWrappingModes.NoWrap;
        song = Text("Song", 0, 38, 34, Ivory);
        artist = Text("Artist", 0, -3, 23, new Color(.7f,.74f,.8f));
        view.tipText.transform.SetParent(block, false);
        Box(view.tipText.rectTransform, 0, -140, 1000, 35);
        view.tipText.fontSize = 21; view.tipText.color = new Color(.68f,.72f,.79f);
        var oldBar = view.progressFill.transform.parent.gameObject;
        view.progressFill.transform.SetParent(block, false);
        oldBar.SetActive(false);
        frames = new Sprite[18]; fills = new Sprite[18];
        for (int i = 0; i < 18; i++)
        {
            // Atlas rows have an empty gutter; upper half is frame, lower half is fill.
            frames[i] = Sprite.Create(barAtlas, new Rect(0, (i * 128 + 64) * barAtlas.height / 2304f, barAtlas.width, 64 * barAtlas.height / 2304f), new Vector2(.5f,.5f), 100);
            fills[i] = Sprite.Create(barAtlas, new Rect(0, i * 128 * barAtlas.height / 2304f, barAtlas.width, 64 * barAtlas.height / 2304f), new Vector2(.5f,.5f), 100);
        }
        frame = Image("BarFrame"); nextFrame = Image("IncomingFrame");
        view.progressFill.transform.SetAsLastSibling();
        Box(view.progressFill.rectTransform, 0, -80, 980, 24);
        view.progressFill.color = Color.white;
        nextFill = Image("IncomingFill"); nextFill.type = UnityEngine.UI.Image.Type.Filled; nextFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        sheen = Image("LoadingLight"); Box(sheen.rectTransform, 0, -80, 2, 12);
        current = GameMusicStyle.Unknown;
        ApplyImmediate(current);
    }
    private Image[] CreateMotifs(string name)
    {
        var images=new Image[2];
        for(int n=0;n<2;n++)
        {
            images[n]=UIFactory.CreatePanel(name+n,(RectTransform)transform,Color.clear);
            images[n].raycastTarget=false;
            images[n].preserveAspect=true;
            var rt=images[n].rectTransform;
            rt.anchorMin=rt.anchorMax=rt.pivot=n==0 ? new Vector2(0,1) : new Vector2(1,0);
            if(n==1) rt.localRotation=Quaternion.Euler(0,0,180);
            // Rotation is about the centre; both corners keep the same crop on all aspect ratios.
            rt.pivot=new Vector2(.5f,.5f);
        }
        return images;
    }
    private Color BackgroundColor(GameMusicStyle style) =>
        Color.Lerp(provisionalBackground, style==GameMusicStyle.Classical || style==GameMusicStyle.Unknown ? provisionalBackground : Accent(style), .035f);
    private void SetMotifs(Image[] images, GameMusicStyle style, float alpha)
    {
        if(images==null) return;
        Sprite sprite=motifSprites[Mathf.Clamp((int)style,0,17)];
        foreach(var image in images) { image.sprite=sprite; image.enabled=sprite!=null; image.color=new Color(1,1,1,alpha*motifOpacity); }
    }
    private void LayoutMotifs(Image[] images)
    {
        if(images==null) return;
        var rect=((RectTransform)transform).rect;
        float size=Mathf.Min(620,Mathf.Min(rect.width*.32f,rect.height*.58f));
        for(int n=0;n<2;n++)
        {
            images[n].rectTransform.sizeDelta=new Vector2(size,size);
            images[n].rectTransform.anchoredPosition=n==0 ? new Vector2(size*.26f,-size*.26f) : new Vector2(-size*.26f,size*.26f);
        }
    }
    private Image Image(string name) { var image = UIFactory.CreatePanel(name, block, Color.white); Box(image.rectTransform, 0,-80,980,24); image.raycastTarget = false; return image; }
    private TextMeshProUGUI Text(string name, float x, float y, int size, Color color)
    {
        var t = UIFactory.CreateText(name, block, "", size, color, TextAlignmentOptions.Center);
        Box(t.rectTransform, x,y,1000,45); t.raycastTarget = false; t.richText = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = size*.65f; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }
    private static void Box(RectTransform rt,float x,float y,float w,float h) => UIFactory.SetBox(rt,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(x,y),new Vector2(w,h));
    private void ApplyImmediate(GameMusicStyle style)
    {
        int i = Mathf.Clamp((int)style,0,17); frame.sprite=frames[i]; view.progressFill.sprite=fills[i];
        nextFrame.color=nextFill.color=Color.clear;
        fromColor=toColor=Accent(style); ApplyTitleGradient(toColor); blend=1;
        SetMotifs(motifs,style,1); SetMotifs(incomingMotifs,style,0);
        fromBackground=toBackground=BackgroundColor(style);
        if(backdrop!=null) backdrop.color=toBackground;
    }
    private void ApplyTitleGradient(Color accent)
    {
        displayedAccent = accent;
        view.titleText.color = Color.white; // neutral multiplier; actual face colours are the gradient
        view.titleText.colorGradient = new VertexGradient(Ivory, Ivory, Ivory, Ivory);
    }
    public void Begin()
    {
        if (view == null) return;
        DetectedStyleLabel.Cancel();
        current = GameMusicStyle.Unknown; ApplyImmediate(current);
        var selected = GameSession.Instance != null ? GameSession.Instance.SelectedSong : null;
        string name = selected?.DisplayName ?? "";
        if (string.IsNullOrWhiteSpace(name) && selected.HasValue && !string.IsNullOrWhiteSpace(selected.Value.LocalFilePath)) name=System.IO.Path.GetFileNameWithoutExtension(selected.Value.LocalFilePath);
        var match = System.Text.RegularExpressions.Regex.Match(name,@"\s+[—–-]\s+");
        song.text=match.Success ? name.Substring(match.Index+match.Length).Trim() : name;
        artist.text=match.Success ? name.Substring(0,match.Index).Trim() : "";
    }
    public void Reveal(GameMusicStyle style)
    {
        if(view==null) return;
        if(styleRevealClip!=null && GameAudio.Instance!=null) GameAudio.Instance.PlayClip(styleRevealClip,.65f,GameAudioScope.UI);
        DetectedStyleLabel.Show(view.titleText,style,(RectTransform)transform.parent,styleFlashShader);
        int i=Mathf.Clamp((int)style,0,17);
        nextFrame.sprite=frames[i]; nextFill.sprite=fills[i];
        fromColor=displayedAccent; toColor=Accent(style); current=style; blend=0;
        SetMotifs(incomingMotifs,style,0);
        // Never show the previous song's motifs. Only the detected background fades in.
        SetMotifs(motifs,GameMusicStyle.Unknown,0);
        fromBackground=provisionalBackground;
        if(backdrop!=null) backdrop.color=provisionalBackground;
        toBackground=BackgroundColor(style);
    }
    private void Update()
    {
        if(view==null) return;
        if(blend<1)
        {
            blend=Mathf.Min(1,blend+Time.unscaledDeltaTime/1.1f);
            float t=Mathf.SmoothStep(0,1,blend);
            nextFrame.color=nextFill.color=new Color(1,1,1,t);
            SetMotifs(incomingMotifs,current,t);
            if(motifs!=null) foreach(var image in motifs) image.color=new Color(1,1,1,(1-t)*motifOpacity);
            if(backdrop!=null) backdrop.color=Color.Lerp(fromBackground,toBackground,t);
            ApplyTitleGradient(Color.Lerp(fromColor,toColor,t));
            if(blend>=1) ApplyImmediate(current);
        }
        LayoutMotifs(motifs); LayoutMotifs(incomingMotifs);
        float p=view.progressFill.fillAmount; nextFill.fillAmount=p;
        // A light travels only inside the completed portion; it never invents progress.
        sheen.enabled=p>.02f && p<1;
        sheen.rectTransform.anchoredPosition=new Vector2(-474+Mathf.Repeat(Time.unscaledTime*.55f,1)*948*p,-80);
        sheen.color=new Color(1,1,1,.35f+.3f*Mathf.Sin(Time.unscaledTime*3));
        float scale=Mathf.Min(1,( ((RectTransform)transform).rect.width-80)/1100f);
        block.localScale=Vector3.one*Mathf.Max(.2f,scale);
    }
    private void OnDestroy()
    {
        if(motifSprites!=null) foreach(var sprite in motifSprites) if(sprite!=null) { if(Application.isPlaying) Destroy(sprite); else DestroyImmediate(sprite); }
        if(titleMaterial!=null) { if(Application.isPlaying) Destroy(titleMaterial); else DestroyImmediate(titleMaterial); }
        if(frames!=null) foreach(var s in frames) if(s!=null) { if(Application.isPlaying) Destroy(s); else DestroyImmediate(s); }
        if(fills!=null) foreach(var s in fills) if(s!=null) { if(Application.isPlaying) Destroy(s); else DestroyImmediate(s); }
    }
}
