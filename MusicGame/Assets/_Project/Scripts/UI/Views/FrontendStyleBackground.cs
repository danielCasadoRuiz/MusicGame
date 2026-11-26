using UnityEngine;
using UnityEngine.UI;

/// <summary>Home/detected music-style blend, independently blended between night and day.</summary>
public sealed class FrontendStyleBackground : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public RawImage backdrop;
    public RawImage previousBackdrop;
    public StyleBlendGroup controls;
    private Material nightMaterial, previousMaterial;
    private GameMusicStyle shown, prior;
    private float styleBlend=1;
    public GameMusicStyle HomeStyle => UIStylePalette.ScreenStyle(this,library);
    public GameMusicStyle DisplayedStyle=>Effective(shown);
    public GameMusicStyle PreviousStyle=>Effective(prior);
    public float StyleBlend=>styleBlend;
    public Texture2D HomeTexture => library != null ? library.MenuBackground(HomeStyle) : null;
    private GameMusicStyle Effective(GameMusicStyle style) => library != null && library.MenuBackground(style) != null ? style : HomeStyle;
    public float InkLightWeight => library == null ? 0 : AppearancePreference.LightWeight * Mathf.Lerp(library.DayUsesDarkInk(Effective(prior))?1:0,library.DayUsesDarkInk(Effective(shown))?1:0,styleBlend);
    private void OnEnable() { AppearancePreference.Updated+=Render; ShowHome(); }
    private void OnDisable() => AppearancePreference.Updated-=Render;
    private void OnDestroy() { MenuBackgroundRenderer.Release(nightMaterial); MenuBackgroundRenderer.Release(previousMaterial); }
    public void ShowHome() => Show(HomeStyle,HomeStyle,1);
    public void Show(GameMusicStyle style, GameMusicStyle from, float blend)
    {
        shown=style;prior=from;styleBlend=Mathf.Clamp01(blend);Render();
        if(controls!=null)controls.Apply(style==GameMusicStyle.Unknown?HomeStyle:style,blend,from);
    }
    private void Render()
    {
        if(library==null)return;
        var target=Effective(shown);var from=Effective(prior);
        MenuBackgroundRenderer.Apply(backdrop,library.MenuBackground(target),library.DayBackground(target),1,library.menuAppearanceMaterial,ref nightMaterial);
        MenuBackgroundRenderer.Apply(previousBackdrop,library.MenuBackground(from),library.DayBackground(from),target==from?0:1-styleBlend,library.menuAppearanceMaterial,ref previousMaterial);
    }
    private void OnRectTransformDimensionsChange() { MenuBackgroundRenderer.Crop(backdrop);MenuBackgroundRenderer.Crop(previousBackdrop); }
}
