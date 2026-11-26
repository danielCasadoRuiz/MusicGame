using TMPro;
using UnityEngine;
/// <summary>Colour-only binding for authored titles whose size/curvature are controlled separately.</summary>
[ExecuteAlways,RequireComponent(typeof(TMP_Text))]
public sealed class StyleTitleInk : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public bool inverseOutline;
    Material owned,original;
    void OnEnable()=>Apply();
    void LateUpdate()=>Apply();
    public void Apply(bool reviewMaterial=false)
    {
        if(library==null)return;var text=GetComponent<TMP_Text>();var ink=UIStylePalette.Ink(this,library);ink.a=text.color.a;text.color=ink;
        if(inverseOutline&&(Application.isPlaying||reviewMaterial))
        {
            if(owned==null){original=text.fontSharedMaterial;owned=new Material(original){name="Styled title inverse outline",hideFlags=HideFlags.DontSave};text.fontSharedMaterial=owned;}
            var style=UIStylePalette.ScreenStyle(this,library);var opposite=UIStylePalette.ForStyle(library,style,1-AppearancePreference.LightWeight);
            owned.SetColor("_OutlineColor",opposite);text.UpdateMeshPadding();
        }
    }
    void OnDestroy(){if(owned!=null){var text=GetComponent<TMP_Text>();if(text!=null&&text.fontSharedMaterial==owned)text.fontSharedMaterial=original;if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}}
}
