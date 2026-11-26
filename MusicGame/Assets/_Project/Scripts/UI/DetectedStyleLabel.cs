using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>The actual detected-style TMP object travels from analysis into the HUD.</summary>
public sealed class DetectedStyleLabel : MonoBehaviour
{
    private static DetectedStyleLabel instance;
    private static TextMeshProUGUI destination;
    private TextMeshProUGUI label;
    private Material ownedMaterial;
    private Coroutine motion;
    private bool showing;
    private StyleSkinLibrarySO styleLibrary;
    private GameMusicStyle detectedStyle;
    private bool ownsHudSlot;
    private UnityEngine.UI.Image flash;
    private Material flashMaterial;
    private TextMeshProUGUI inlineSlot;
    private bool impactEntrance;
    private static readonly Color Ivory = new(.953f, .918f, .827f, 1);
    public static bool IsDocked => instance != null && instance.ownsHudSlot && !instance.showing;
    public static bool HasDestination => destination != null && destination.gameObject.activeInHierarchy;

    public static string Caption(GameMusicStyle style)
    {
        string key="GameStyle."+style;
        string value=Loc.Get(key);
        return (string.IsNullOrWhiteSpace(value)||value==key ? style.ToString() : value).ToUpperInvariant();
    }
    public static void RegisterHud(TextMeshProUGUI target)
    {
        destination=target;
        // Registration can happen before Dock/GameStarted. Hide the fallback before
        // the HUD's first rendered frame, and keep it hidden after the moving text lands.
        if (destination != null) destination.enabled = instance == null || !instance.ownsHudSlot;
    }
    public static void Show(TextMeshProUGUI template,GameMusicStyle style,RectTransform canvas,Shader flashShader = null, bool inline = false, bool impact = false)
    {
        if(instance==null)
        {
            var text=Instantiate(template,canvas,false);text.name="DetectedStyle";
            instance=text.gameObject.AddComponent<DetectedStyleLabel>();instance.label=text;
            instance.ownedMaterial=new Material(template.fontSharedMaterial);text.fontSharedMaterial=instance.ownedMaterial;
            text.raycastTarget=false;
        }
        var self=instance;
        self.styleLibrary=template.GetComponentInParent<AnalysisPresentation>()?.library??template.GetComponentInParent<FrontendStyleBackground>()?.library;self.detectedStyle=style;
        self.impactEntrance = impact;
        self.inlineSlot = inline ? template : null;
        if (self.flash != null) self.flash.gameObject.SetActive(false);
        if(self.motion!=null)self.StopCoroutine(self.motion);
        self.transform.SetParent(canvas,false);self.gameObject.SetActive(true);self.transform.SetAsLastSibling();
        self.showing=true;
        self.ownsHudSlot=true;
        if(destination!=null)destination.enabled=false;
        if(flashShader!=null)
        {
            if(self.flash==null)
            {
                self.flash=UIFactory.CreatePanel("StyleRevealFlash",canvas,Color.white);
                self.flash.raycastTarget=false;
                self.flashMaterial=new Material(flashShader);self.flash.material=self.flashMaterial;
            }
            self.flash.transform.SetParent(canvas,false);self.flash.gameObject.SetActive(true);
            self.flash.transform.SetSiblingIndex(self.transform.GetSiblingIndex());
            var fx=self.flash.rectTransform;fx.anchorMin=fx.anchorMax=fx.pivot=new Vector2(.5f,.5f);
            fx.anchoredPosition=new Vector2(0,-230);fx.sizeDelta=new Vector2(Mathf.Min(1200,canvas.rect.width-40),350);
            self.flashMaterial.SetColor("_Color",AnalysisPresentation.Accent(style));self.flashMaterial.SetFloat("_Progress",0);
        }
        self.label.text=Caption(style);
        self.label.color=Color.white;self.label.enableVertexGradient=true;
        var accent=AnalysisPresentation.Accent(style);
        var highlight=Color.Lerp(new Color(.953f,.918f,.827f),accent,.35f);
        self.label.colorGradient=new VertexGradient(highlight,highlight,accent,accent);
        self.label.alignment=TextAlignmentOptions.Center;self.label.enableAutoSizing=true;
        self.label.fontSizeMin=8;self.label.fontSizeMax=self.label.fontSize=120;
        self.label.textWrappingMode = TextWrappingModes.NoWrap;
        self.label.overflowMode = TextOverflowModes.Overflow;
        self.label.wordSpacing = AnalysisPresentation.BrushWordSpacing;
        var rt=self.label.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
        rt.anchoredPosition=new Vector2(0,-230);rt.sizeDelta=new Vector2(Mathf.Min(1100,canvas.rect.width-80),170);
        rt.localScale=Vector3.one;
        if (inline) self.PlaceInline();
        if (impact)
        {
            self.label.colorGradient = new VertexGradient(Ivory, Ivory, Color.Lerp(Ivory, accent, .18f), Color.Lerp(Ivory, accent, .18f));
            self.ownedMaterial.SetColor("_FaceColor", Color.white);
            self.ownedMaterial.SetColor("_OutlineColor", new Color(.02f, .015f, .03f, 1));
            self.ownedMaterial.SetFloat("_OutlineWidth", .13f);
            self.ownedMaterial.EnableKeyword("UNDERLAY_ON");
            self.ownedMaterial.SetColor("_UnderlayColor", accent);
            self.ownedMaterial.SetFloat("_UnderlayOffsetX", .5f);
            self.ownedMaterial.SetFloat("_UnderlayOffsetY", -.5f);
            self.ownedMaterial.SetFloat("_UnderlayDilate", .15f);
            self.label.UpdateMeshPadding();
            if (self.flash != null)
            {
                self.flash.rectTransform.position = rt.position;
                self.flash.rectTransform.sizeDelta = rt.sizeDelta * new Vector2(1.3f, 1.7f);
            }
        }
        self.label.enableVertexGradient=false;
        if(self.styleLibrary!=null)self.label.color=UIStylePalette.ForStyle(self.styleLibrary,style,AppearancePreference.LightWeight);
        self.motion=self.StartCoroutine(self.Entrance());
    }
    private void PlaceInline()
    {
        if (inlineSlot == null) return;
        var canvas = (RectTransform)transform.parent;
        float scale = inlineSlot.transform.lossyScale.x / canvas.lossyScale.x;
        label.rectTransform.position = inlineSlot.rectTransform.position;
        label.rectTransform.sizeDelta = inlineSlot.rectTransform.rect.size * scale;
        label.fontSizeMin = inlineSlot.fontSizeMin * scale;
        label.fontSizeMax = inlineSlot.fontSizeMax * scale;
        // Keep TMP's fitted size. Resetting fontSize to the maximum every LateUpdate undid
        // autosizing while the letter animation held its mesh, clipping longer style names.
        label.wordSpacing = inlineSlot.wordSpacing;
    }
    private void LateUpdate() { if(styleLibrary!=null&&showing&&label!=null)label.color=UIStylePalette.ForStyle(styleLibrary,detectedStyle,AppearancePreference.LightWeight); if (inlineSlot != null && showing) PlaceInline(); }
    private IEnumerator Entrance()
    {
        label.ForceMeshUpdate();int count=label.textInfo.characterCount;
        if (impactEntrance)
        {
            yield return LetterImpact(); motion = null; yield break;
        }
        if (inlineSlot != null)
        {
            label.maxVisibleCharacters = int.MaxValue;
            for (float time = 0; time < .25f; time += Time.unscaledDeltaTime)
            { label.alpha = Mathf.Clamp01(time / .25f); yield return null; }
            label.alpha = 1; motion = null; yield break;
        }
        float elapsed=0;
        while(elapsed<.30f)
        {
            float t=Mathf.Clamp01(elapsed/.14f);label.maxVisibleCharacters=Mathf.CeilToInt(count*t);
            float pop=elapsed/.30f;
            if(flashMaterial!=null)flashMaterial.SetFloat("_Progress",elapsed/.55f);
            // Fast impact: characters land in a short burst, with a pronounced overshoot.
            float scale=pop<.42f?Mathf.Lerp(.52f,1.24f,1-Mathf.Pow(1-pop/.42f,3)):Mathf.Lerp(1.24f,1,Mathf.SmoothStep(0,1,(pop-.42f)/.58f));
            transform.localScale=Vector3.one*scale;
            label.rectTransform.anchoredPosition=new Vector2(0,-230-32*(1-Mathf.SmoothStep(0,1,Mathf.Min(1,pop/.42f))));
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        label.maxVisibleCharacters=int.MaxValue;transform.localScale=Vector3.one;
        while(elapsed<.55f){if(flashMaterial!=null)flashMaterial.SetFloat("_Progress",elapsed/.55f);elapsed+=Time.unscaledDeltaTime;yield return null;}
        if(flash!=null)flash.gameObject.SetActive(false);motion=null;
    }
    private IEnumerator LetterImpact()
    {
        label.alpha = 1; label.maxVisibleCharacters = int.MaxValue;
        label.ForceMeshUpdate();
        float fittedSize = label.fontSize;
        label.enableAutoSizing = false;
        // TMP's fontSize setter ignores an unchanged value; commit the fitted size as its
        // fixed-size base too, otherwise its next mesh rebuild restores the old larger size.
        label.fontSize = fittedSize + 1f; label.fontSize = fittedSize; label.ForceMeshUpdate();
        var original = label.textInfo.CopyMeshInfoVertexData();
        int count = label.textInfo.characterCount;
        float duration = .36f + Mathf.Max(0, count - 1) * .055f;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            for (int i = 0; i < count; i++)
            {
                var character = label.textInfo.characterInfo[i]; if (!character.isVisible) continue;
                float t = Mathf.Clamp01((elapsed - i * .055f) / .36f);
                float scale = t < .6f ? Mathf.Lerp(.55f, 1.13f, 1 - Mathf.Pow(1 - t / .6f, 3))
                    : Mathf.Lerp(1.13f, 1, Mathf.SmoothStep(0, 1, (t - .6f) / .4f));
                int material = character.materialReferenceIndex, vertex = character.vertexIndex;
                Vector3 center = (original[material].vertices[vertex] + original[material].vertices[vertex + 2]) * .5f;
                for (int n = 0; n < 4; n++)
                {
                    label.textInfo.meshInfo[material].vertices[vertex + n] = center + (original[material].vertices[vertex + n] - center) * scale
                        + Vector3.down * (1 - t) * 35;
                    var color = original[material].colors32[vertex + n];
                    color.a = (byte)(color.a * Mathf.Clamp01(t * 4));
                    label.textInfo.meshInfo[material].colors32[vertex + n] = color;
                }
            }
            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
            if (flashMaterial != null) flashMaterial.SetFloat("_Progress", Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        label.enableAutoSizing = true; label.ForceMeshUpdate();
        if (flash != null) flash.gameObject.SetActive(false);
    }
    public static void Dock()
    {
        if(instance==null||!instance.showing)return;
        instance.inlineSlot = null;
        instance.transform.SetAsLastSibling();
        if(instance.motion!=null)instance.StopCoroutine(instance.motion);
        instance.motion=instance.StartCoroutine(instance.FlyToHud());
    }
    private IEnumerator FlyToHud()
    {
        // HUD may be activated in the same frame as GameStartedEvent.
        float waitUntil=Time.unscaledTime+1.5f;
        while((destination==null||!destination.gameObject.activeInHierarchy)&&Time.unscaledTime<waitUntil)yield return null;
        if(destination==null||!destination.gameObject.activeInHierarchy){Cancel();yield break;}
        destination.enabled=false;Canvas.ForceUpdateCanvases();
        label.maxVisibleCharacters=int.MaxValue;label.ForceMeshUpdate();
        var rt=label.rectTransform;Vector3 start=rt.position;Vector3 startScale=rt.localScale;
        Vector2 startBox=new Vector2(label.GetRenderedValues(false).x+12,rt.rect.height);rt.sizeDelta=startBox;
        label.alignment=TextAlignmentOptions.MidlineRight;
        Vector3 parentScale=rt.parent.lossyScale;
        Vector3 endScale=new Vector3(destination.transform.lossyScale.x/parentScale.x,destination.transform.lossyScale.y/parentScale.y,1);
        destination.ForceMeshUpdate(true, true);
        float endSize = destination.fontSize;
        float startSize=label.fontSize;float elapsed=0;
        VertexGradient startGradient = label.colorGradient;
        Color startShadow = ownedMaterial.GetColor("_UnderlayColor");
        while(elapsed<.8f)
        {
            float t=Mathf.SmoothStep(0,1,elapsed/.8f);
            rt.position=Vector3.Lerp(start,destination.rectTransform.TransformPoint(destination.rectTransform.rect.center),t);
            rt.sizeDelta=Vector2.Lerp(startBox,destination.rectTransform.rect.size,t);
            rt.localScale=Vector3.Lerp(startScale,endScale,t);
            label.enableAutoSizing=false;label.fontSize=Mathf.Lerp(startSize,endSize,t);
            label.colorGradient = new VertexGradient(Color.Lerp(startGradient.topLeft, Ivory, t), Color.Lerp(startGradient.topRight, Ivory, t),
                Color.Lerp(startGradient.bottomLeft, Ivory, t), Color.Lerp(startGradient.bottomRight, Ivory, t));
            ownedMaterial.SetColor("_UnderlayColor", Color.Lerp(startShadow, new Color(.01f, .01f, .02f, 1), t));
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        var target=destination.rectTransform;
        rt.SetParent(target.parent,false);rt.anchorMin=target.anchorMin;rt.anchorMax=target.anchorMax;rt.pivot=target.pivot;
        rt.anchoredPosition=target.anchoredPosition;rt.sizeDelta=target.sizeDelta;rt.localScale=Vector3.one;
        label.enableAutoSizing=true;label.fontSizeMax=destination.fontSizeMax;
        label.fontSizeMin=destination.fontSizeMin;label.fontSize=endSize;
        label.enableVertexGradient = false; label.color = Ivory;
        ownedMaterial.SetFloat("_OutlineWidth", .15f);
        showing=false;motion=null;
    }
    public static void Cancel()
    {
        if(instance==null)return;
        if(instance.motion!=null)instance.StopCoroutine(instance.motion);
        instance.motion=null;instance.showing=false;instance.ownsHudSlot=false;instance.gameObject.SetActive(false);
        instance.inlineSlot = null;
        if(instance.flash!=null)instance.flash.gameObject.SetActive(false);
        if(destination!=null)destination.enabled=true;
    }
    private void OnDestroy()
    {
        if(ownedMaterial!=null){if(Application.isPlaying)Destroy(ownedMaterial);else DestroyImmediate(ownedMaterial);}
        if(flashMaterial!=null){if(Application.isPlaying)Destroy(flashMaterial);else DestroyImmediate(flashMaterial);}
        if(flash!=null){if(Application.isPlaying)Destroy(flash.gameObject);else DestroyImmediate(flash.gameObject);}
        if(instance==this)
        {
            instance=null;
            if(destination!=null)destination.enabled=true;
        }
    }
}
