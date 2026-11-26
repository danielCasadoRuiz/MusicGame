using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Finite skin choices wrap with the two arrows; the chosen menu skin persists.</summary>
public sealed class SettingsSkinSelector : MonoBehaviour
{
    public MainMenuView menu;
    public Button previous,next;
    public TMP_Text styleName;
    void Awake(){previous.onClick.AddListener(()=>Step(-1));next.onClick.AddListener(()=>Step(1));}
    void OnEnable()=>Refresh();
    public GameMusicStyle Adjacent(int direction)
    {
        var lib=menu.logo.library;
        var choices=lib.titles.Where(t=>t.style!=GameMusicStyle.Unknown&&t.style!=GameMusicStyle.Classical&&lib.Button(t.style)!=null).Select(t=>t.style).Distinct().ToArray();
        if(choices.Length==0)return GameMusicStyle.Unknown;
        var current=StyledTitleView.MenuStyle(lib);int i=System.Array.IndexOf(choices,current);
        return choices[(Mathf.Max(0,i)+direction%choices.Length+choices.Length)%choices.Length];
    }
    public void Step(int direction)
    {
        var style=Adjacent(direction);if(style==GameMusicStyle.Unknown)return;
        if(menu.SkinPicked!=null)menu.SkinPicked(style);
        else if(StyledTitleView.SetMenuSkin(menu.logo.library,style)){menu.logo.Show(style);menu.ShowBackground(style);menu.buttonStyle?.Apply(style,1);}
        Refresh();
    }
    public void Refresh(){if(menu!=null&&styleName!=null)styleName.text=StyledTitleView.MenuStyle(menu.logo.library).ToString();}
}
