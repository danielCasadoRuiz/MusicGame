using UnityEngine;
using UnityEngine.UI;

/// <summary>A full-screen interaction veil: soft light in day mode, dark in night mode.</summary>
[RequireComponent(typeof(Image))]
public sealed class ModalVeilTone : MonoBehaviour
{
    public Color night=new Color(.015f,.018f,.03f,.72f);
    public Color day=new Color(.97f,.965f,.94f,.58f);
    void OnEnable()=>Apply();
    void LateUpdate()=>Apply();
    public void Apply(){var image=GetComponent<Image>();image.color=Color.Lerp(night,day,AppearancePreference.LightWeight);image.raycastTarget=true;}
}
