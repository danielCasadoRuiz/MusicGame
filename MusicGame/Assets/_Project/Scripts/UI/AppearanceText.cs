using TMPro;
using UnityEngine;

/// <summary>Opt-in contrast for text directly over the backdrop. Text on cards/panels is left alone.</summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class AppearanceText : MonoBehaviour
{
    public Color night = new(.953f,.918f,.827f,1);
    public Color day = new(.11f,.13f,.17f,1);
    public FrontendStyleBackground background;
    public MainMenuView mainMenu;
    private TMP_Text text;
    private void OnEnable() { text=GetComponent<TMP_Text>(); Apply(); }
    private void LateUpdate() => Apply();
    public void Apply()
    {
        if (text==null) text=GetComponent<TMP_Text>();
        float weight = background != null ? background.InkLightWeight : mainMenu != null ? mainMenu.InkLightWeight : 0;
        text.color = Color.Lerp(night,day,weight);
    }
}
