using UnityEngine;
using UnityEngine.UI;

/// <summary>Blends day/night within each existing style layer, preserving the separate music-style fade.</summary>
public static class MenuBackgroundRenderer
{
    public static void Apply(RawImage image, Texture night, Texture day, float alpha, Material template, ref Material runtime)
    {
        if (image == null) return;
        if (runtime == null && template != null)
            runtime = new Material(template) { name = "Menu appearance blend" };
        if (runtime != null)
        {
            runtime.SetTexture("_DayTex", day != null ? day : night);
            runtime.SetFloat("_Light", AppearancePreference.LightWeight);
            image.material = runtime;
        }
        image.texture = night;
        image.enabled = night != null && alpha > 0f;
        image.color = new Color(1, 1, 1, Mathf.Clamp01(alpha));
        Crop(image);
    }

    public static void Crop(RawImage image)
    {
        if (image == null || image.texture == null) return;
        var size = image.rectTransform.rect.size;
        if (size.x <= 0 || size.y <= 0) return;
        float ratio = (size.x / size.y) / ((float)image.texture.width / image.texture.height);
        image.uvRect = ratio < 1 ? new Rect((1-ratio)*.5f,0,ratio,1) : new Rect(0,(1-1/ratio)*.5f,1,1/ratio);
    }

    public static void Release(Material material)
    {
        if (material == null) return;
        if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
    }
}
