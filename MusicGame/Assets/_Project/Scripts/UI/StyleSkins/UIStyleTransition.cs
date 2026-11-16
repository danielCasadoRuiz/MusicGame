using UnityEngine;
using UnityEngine.UI;

/// <summary>Maps each graphic into one shared curtain, independent of sprite UVs or fill amount.</summary>
public static class UIStyleTransition
{
    public static Material Create(StyleSkinLibrarySO library, string name)
    {
        return library != null && library.styleTransitionMaterial != null
            ? new Material(library.styleTransitionMaterial) { name = name } : null;
    }

    public static void Apply(Material material, Image image, RectTransform reference,
        StyleSkinLibrarySO library, float classicShare, Sprite classic, Color styleColor, Color classicColor, bool gold = false)
    {
        var rect = image.rectTransform;
        var referenceRect = reference != null ? reference : rect;
        var binding=image.GetComponent<UIStyleTransitionBinding>();
        if(binding==null)binding=image.gameObject.AddComponent<UIStyleTransitionBinding>();
        binding.Bind(image,material,referenceRect);
        material.SetFloat("_Edge",library.CurtainEdge(classicShare));
        material.SetFloat("_Softness",library.runnerCurtainSoftness);
        material.SetFloat("_FixedBlend",-1);
        material.SetFloat("_UseClassicTexture",classic != null ? 1 : 0);
        if(classic != null) material.SetTexture("_ClassicTex",classic.texture);
        material.SetColor("_StyleColor",styleColor);material.SetColor("_ClassicColor",classicColor);
        material.SetFloat("_GoldClassic",gold ? 1 : 0);
        image.material=material;image.color=Color.white;
    }

    public static void UpdateCoordinates(Image image,Material material,RectTransform referenceRect)
    {
        var canvas=image.canvas!=null?image.canvas.rootCanvas:image.GetComponentInParent<Canvas>();
        var camera=canvas!=null && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        float ViewportX(float x)
        {
            var world=referenceRect.TransformPoint(new Vector3(x,referenceRect.rect.center.y,0));
            return camera!=null?camera.WorldToViewportPoint(world).x:RectTransformUtility.WorldToScreenPoint(null,world).x/Mathf.Max(1,Screen.width);
        }
        float x0=ViewportX(referenceRect.rect.xMin),x1=ViewportX(referenceRect.rect.xMax);
        material.SetVector("_Mapping",new Vector4(x0,1/Mathf.Max(.000001f,x1-x0),0,0));
    }

    public static void Release(Material material)
    {
        if(material==null)return;
        if(Application.isPlaying)Object.Destroy(material);else Object.DestroyImmediate(material);
    }
}
