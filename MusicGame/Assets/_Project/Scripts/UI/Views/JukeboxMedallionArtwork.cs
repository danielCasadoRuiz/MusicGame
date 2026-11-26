using UnityEngine;
using UnityEngine.UI;

/// <summary>Cover cropping follows the selected sleeve's real aspect ratio, within the circular mask.</summary>
[ExecuteAlways,RequireComponent(typeof(Image),typeof(AspectRatioFitter))]
public sealed class JukeboxMedallionArtwork : MonoBehaviour
{
    Sprite previous;
    void OnEnable()=>Refresh();
    void LateUpdate(){var image=GetComponent<Image>();if(image.sprite!=previous)Refresh();}
    public void Refresh()
    {
        var image=GetComponent<Image>();previous=image.sprite;
        image.rectTransform.pivot=Vector2.one*.5f;
        var fitter=GetComponent<AspectRatioFitter>();fitter.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio=previous!=null&&previous.rect.height>0?previous.rect.width/previous.rect.height:1;
        image.rectTransform.anchoredPosition=Vector2.zero;
    }
}
