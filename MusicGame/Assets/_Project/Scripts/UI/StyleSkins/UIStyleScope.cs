using UnityEngine;

/// <summary>The menu skin survives recognition; song-owned screens read the detected song instead.</summary>
public sealed class UIStyleScope : MonoBehaviour
{
    public enum Domain { Automatic, Menu, Song }
    public Domain domain;
    void OnEnable()
    {
        foreach(var group in GetComponentsInChildren<StyleBlendGroup>(true))
            if(group.library!=null)group.Apply(UIStylePalette.ScreenStyle(this,group.library),1);
    }
    public bool FollowsSong=>domain==Domain.Song||(domain==Domain.Automatic&&UIStylePalette.IsSongFlow);
}
public static class UIStylePalette
{
    public static bool IsSongFlow
    {
        get
        {
            var flow=AppFlowController.Instance;if(flow==null)return false;
            return flow.CurrentState==GameFlowState.Gameplay||flow.CurrentState==GameFlowState.Fight||flow.CurrentState==GameFlowState.Results;
        }
    }
    public static GameMusicStyle SongOrMenu(StyleSkinLibrarySO library)
    {var style=StyleSkinLibrarySO.CurrentStyle();return style!=GameMusicStyle.Unknown?style:StyledTitleView.MenuStyle(library);}
    public static GameMusicStyle ScreenStyle(Component owner,StyleSkinLibrarySO library)
    {
        var scope=owner.GetComponentInParent<UIStyleScope>(true);
        return (scope!=null?scope.FollowsSong:IsSongFlow)?SongOrMenu(library):StyledTitleView.MenuStyle(library);
    }
    public static Color ForStyle(StyleSkinLibrarySO library,GameMusicStyle style,float day)
    {return Color.Lerp(library.SongHeadingColor(style,false),library.SongHeadingColor(style,true),day);}
    public static Color Ink(Component owner,StyleSkinLibrarySO library,bool forceSong=false)
    {
        float day=AppearancePreference.LightWeight;
        var scope=owner.GetComponentInParent<UIStyleScope>(true);
        if(forceSong||(scope!=null&&scope.FollowsSong))return ForStyle(library,SongOrMenu(library),day);
        var menu=owner.GetComponentInParent<MainMenuView>(true);
        if(menu!=null)return Color.Lerp(ForStyle(library,menu.PreviousStyle,day),ForStyle(library,menu.DisplayedStyle,day),menu.StyleBlend);
        var bg=owner.GetComponentInParent<FrontendStyleBackground>(true);
        if(bg!=null)return Color.Lerp(ForStyle(library,bg.PreviousStyle,day),ForStyle(library,bg.DisplayedStyle,day),bg.StyleBlend);
        return ForStyle(library,ScreenStyle(owner,library),day);
    }
}
