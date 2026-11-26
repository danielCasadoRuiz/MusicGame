using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored Settings row. No runtime UI construction.</summary>
public sealed class AppearanceSelectorView : MonoBehaviour
{
    public Button automatic, lightButton, dark;
    private void Awake()
    {
        if(automatic!=null)automatic.onClick.AddListener(()=>AppearancePreference.SetMode(AppearancePreference.Auto));
        lightButton.onClick.AddListener(()=>AppearancePreference.SetMode(AppearancePreference.Light));
        dark.onClick.AddListener(()=>AppearancePreference.SetMode(AppearancePreference.Dark));
    }
    private void OnEnable() { AppearancePreference.ModeChanged+=Refresh; Refresh(); }
    private void OnDisable() => AppearancePreference.ModeChanged-=Refresh;
    public void Refresh()
    {
        var buttons=new[]{automatic,lightButton,dark};
        var modes=new[]{AppearancePreference.Auto,AppearancePreference.Light,AppearancePreference.Dark};
        for(int i=0;i<buttons.Length;i++)
        {
            if(buttons[i]==null)continue;
            bool selected=AppearancePreference.Mode==modes[i];
            buttons[i].image.color=selected?new Color(.70f,.52f,.26f):new Color(.16f,.17f,.20f);
            var label=buttons[i].GetComponentInChildren<TMP_Text>();
            if(label!=null)label.color=selected?Color.black:new Color(.95f,.92f,.83f);
        }
    }
}
