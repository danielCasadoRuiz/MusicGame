using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolve the curtain after canvas layout, including pause opening and window resizing.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class UIStyleTransitionBinding : MonoBehaviour
{
    private Image image;
    private Material material;
    private RectTransform reference;

    public void Bind(Image target,Material ownedMaterial,RectTransform curtainReference)
    {
        image=target;material=ownedMaterial;reference=curtainReference;Refresh();
    }

    private void OnEnable() => Canvas.willRenderCanvases+=Refresh;
    private void OnDisable() => Canvas.willRenderCanvases-=Refresh;
    private void Refresh()
    {
        if(image!=null&&material!=null&&reference!=null)
            UIStyleTransition.UpdateCoordinates(image,material,reference);
    }
}
