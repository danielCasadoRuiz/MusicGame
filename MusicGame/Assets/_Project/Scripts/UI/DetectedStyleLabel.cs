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
    private bool ownsHudSlot;
    private UnityEngine.UI.Image flash;
    private Material flashMaterial;

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
    public static void Show(TextMeshProUGUI template,GameMusicStyle style,RectTransform canvas,Shader flashShader = null)
    {
        if(instance==null)
        {
            var text=Instantiate(template,canvas,false);text.name="DetectedStyle";
            instance=text.gameObject.AddComponent<DetectedStyleLabel>();instance.label=text;
            instance.ownedMaterial=new Material(template.fontSharedMaterial);text.fontSharedMaterial=instance.ownedMaterial;
            text.raycastTarget=false;
        }
        var self=instance;
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
        self.label.fontSizeMin=40;self.label.fontSizeMax=self.label.fontSize=120;
        var rt=self.label.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
        rt.anchoredPosition=new Vector2(0,-230);rt.sizeDelta=new Vector2(Mathf.Min(1100,canvas.rect.width-80),170);
        rt.localScale=Vector3.one;
        self.motion=self.StartCoroutine(self.Entrance());
    }
    private IEnumerator Entrance()
    {
        label.ForceMeshUpdate();int count=label.textInfo.characterCount;
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
    public static void Dock()
    {
        if(instance==null||!instance.showing)return;
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
        Vector2 startBox=new Vector2(label.preferredWidth+6,140);rt.sizeDelta=startBox;
        label.alignment=TextAlignmentOptions.MidlineRight;
        Vector3 parentScale=rt.parent.lossyScale;
        Vector3 endScale=new Vector3(destination.transform.lossyScale.x/parentScale.x,destination.transform.lossyScale.y/parentScale.y,1);
        float startSize=label.fontSize;float elapsed=0;
        while(elapsed<.8f)
        {
            float t=Mathf.SmoothStep(0,1,elapsed/.8f);
            rt.position=Vector3.Lerp(start,destination.rectTransform.TransformPoint(destination.rectTransform.rect.center),t);
            rt.sizeDelta=Vector2.Lerp(startBox,destination.rectTransform.rect.size,t);
            rt.localScale=Vector3.Lerp(startScale,endScale,t);
            label.enableAutoSizing=false;label.fontSize=Mathf.Lerp(startSize,30,t);
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        var target=destination.rectTransform;
        rt.SetParent(target.parent,false);rt.anchorMin=target.anchorMin;rt.anchorMax=target.anchorMax;rt.pivot=target.pivot;
        rt.anchoredPosition=target.anchoredPosition;rt.sizeDelta=target.sizeDelta;rt.localScale=Vector3.one;
        label.enableAutoSizing=true;label.fontSizeMax=30;label.fontSizeMin=14;label.fontSize=30;
        showing=false;motion=null;
    }
    public static void Cancel()
    {
        if(instance==null)return;
        if(instance.motion!=null)instance.StopCoroutine(instance.motion);
        instance.motion=null;instance.showing=false;instance.ownsHudSlot=false;instance.gameObject.SetActive(false);
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
