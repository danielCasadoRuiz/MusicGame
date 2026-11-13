using UnityEngine;
public enum AvatarNailMode { Natural = 0, Groups = 1, Individual = 2 }
public enum AvatarNailGroup { LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3 }
[System.Serializable]
public class AvatarNailGroupSettings
{
    public bool painted;
    public Color color = Color.white;
    public AvatarNailGroupSettings Copy()=>new AvatarNailGroupSettings {painted=painted,color=color};
}
[System.Serializable]
public class AvatarNailSettings
{
    public AvatarNailMode mode = AvatarNailMode.Natural;
    public AvatarNailGroupSettings[] groups = {new(),new(),new(),new()};
    // Thumb/big toe to little finger/toe, for each of the four groups.
    public Color[] colors = new Color[20];
    public AvatarNailSettings Copy()
    {
        var copy=new AvatarNailSettings {mode=mode};
        if(colors!=null) System.Array.Copy(colors,copy.colors,System.Math.Min(20,colors.Length));
        for(int i=0;i<4;i++)if(groups!=null && i<groups.Length && groups[i]!=null)copy.groups[i]=groups[i].Copy();
        return copy;
    }
    public Color Resolve(int index)
    {
        if(index<0 || index>=20 || mode==AvatarNailMode.Natural)return Color.clear;
        if(mode==AvatarNailMode.Groups)
        {
            var g=groups!=null && index/5<groups.Length?groups[index/5]:null;
            return g!=null && g.painted?g.color:Color.clear;
        }
        return colors!=null && index<colors.Length?colors[index]:Color.clear;
    }
    public void Set(int index,Color color)
    {
        if(index<0 || index>=20)return;
        if(mode!=AvatarNailMode.Individual)
        {
            var resolved=new Color[20];for(int i=0;i<20;i++)resolved[i]=Resolve(i);colors=resolved;
        }
        else if(colors==null || colors.Length!=20)
        {var old=colors;colors=new Color[20];if(old!=null)System.Array.Copy(old,colors,System.Math.Min(20,old.Length));}
        mode=AvatarNailMode.Individual;colors[index]=color;
    }
    public void SetGroup(int index,Color color)
    {
        if(index<0 || index>=4)return;
        if(groups==null || groups.Length!=4) {var old=groups;groups=new AvatarNailGroupSettings[4];for(int i=0;i<4;i++)groups[i]=old!=null && i<old.Length && old[i]!=null?old[i]:new AvatarNailGroupSettings();}
        groups[index] ??=new AvatarNailGroupSettings();
        groups[index].painted=color.a>0;groups[index].color=color;mode=AvatarNailMode.Groups;
    }
}
