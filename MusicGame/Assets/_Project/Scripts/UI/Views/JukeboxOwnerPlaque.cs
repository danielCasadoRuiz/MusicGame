using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>Owner changes once per visit; the plate grows around independently rendered engraved text.</summary>
public sealed class JukeboxOwnerPlaque : MonoBehaviour
{
    public JukeboxPlaqueSurface plate;
    public TextMeshProUGUI nameText;
    public float minimumWidth=260,maximumWidth=470,plateHeight=38;
    public string OwnerName{get;private set;}
    public string OwnerId{get;private set;}
    void OnEnable(){if(nameText!=null)SetOwnerName(string.IsNullOrEmpty(OwnerName)?nameText.text:OwnerName);}
    public void ChooseOwner(OpponentRosterSO roster)
    {
        var candidates=roster?.opponents?.Where(o=>o!=null&&!string.IsNullOrWhiteSpace(o.displayName)).ToArray();
        if(candidates==null||candidates.Length==0){OwnerId=null;SetOwnerName("Mozart");return;}
        var choices=candidates.Where(o=>o.id!=OwnerId).ToArray();if(choices.Length==0)choices=candidates;
        var owner=choices[Random.Range(0,choices.Length)];OwnerId=owner.id;SetOwnerName(owner.displayName);
    }
    public void SetOwnerName(string value)
    {
        if(plate==null||nameText==null)return;
        OwnerName=string.IsNullOrWhiteSpace(value)?"Mozart":value.Trim();
        nameText.richText=false;
        nameText.text=OwnerName.ToUpperInvariant();
        nameText.textWrappingMode=TextWrappingModes.NoWrap;
        nameText.enableAutoSizing=true;nameText.fontSizeMax=28;nameText.fontSizeMin=18;nameText.fontSize=28;
        plate.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,plateHeight);
        float padding=plate.EndWidth+9;
        var preferred=nameText.GetPreferredValues(nameText.text,float.PositiveInfinity,1000);
        float width=Mathf.Clamp(preferred.x+padding*2,minimumWidth,maximumWidth);
        plate.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        nameText.rectTransform.anchorMin=Vector2.zero;nameText.rectTransform.anchorMax=Vector2.one;
        nameText.rectTransform.offsetMin=new Vector2(padding,2);nameText.rectTransform.offsetMax=new Vector2(-padding,-2);
        plate.SetVerticesDirty();
    }
}
