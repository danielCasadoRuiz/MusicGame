using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent gradient surface; slanted tabs retain constant-width metallic edges.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SongStyleSurface : MaskableGraphic
{
    public bool diagonal;
    public float slantWidth=18;
    private Color right=Color.white,edge=Color.white;
    public Color EndColor=>right;
    public void Apply(Color left,Color end,Color rim)
    {
        if(color==left&&right==end&&edge==rim)return;
        color=left;right=end;edge=rim;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper m)
    {
        m.Clear();var r=GetPixelAdjustedRect();float slant=diagonal?slantWidth:0;
        Vector2 a=new(r.xMin,r.yMin),b=new(r.xMax-slant,r.yMin),c=new(r.xMax,r.yMax),d=new(r.xMin+slant,r.yMax);
        Quad(m,a,b,c,d,edge,edge);
        Quad(m,a+new Vector2(2,2),b+new Vector2(-2,2),c+new Vector2(-2,-2),d+new Vector2(2,-2),color,right);
        Quad(m,d+new Vector2(2,-3),c+new Vector2(-2,-3),c+new Vector2(-2,-2),d+new Vector2(2,-2),new Color(1,1,1,.8f),new Color(1,1,1,.8f));
    }
    static void Quad(VertexHelper m,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color left,Color right)
    {
        int n=m.currentVertCount;m.AddVert(a,left,Vector2.zero);m.AddVert(b,right,Vector2.zero);m.AddVert(c,right,Vector2.zero);m.AddVert(d,left,Vector2.zero);
        m.AddTriangle(n,n+1,n+2);m.AddTriangle(n,n+2,n+3);
    }
    public static void Palette(StyleSkinLibrarySO lib,GameMusicStyle style,out Color left,out Color right)
    {
        Color accent=lib.HudAccent(style);
        left=Color.Lerp(accent,Color.white,.22f);
        right=Color.Lerp(accent,Color.black,.16f);
        // Endpoints follow the two characteristic colours in the existing style-button artwork.
        switch(style)
        {
            case GameMusicStyle.Acoustic:left=new Color(.27f,.39f,.20f);right=new Color(.88f,.66f,.31f);break;
            case GameMusicStyle.Catchy:left=new Color(.08f,.77f,.98f);right=new Color(.96f,.18f,.66f);break;
            case GameMusicStyle.Chill:left=new Color(.19f,.55f,.68f);right=new Color(.49f,.86f,.91f);break;
            case GameMusicStyle.Cinematic:left=new Color(.28f,.19f,.30f);right=new Color(.88f,.66f,.32f);break;
            case GameMusicStyle.Dance:left=new Color(.24f,.07f,.58f);right=new Color(.87f,.12f,.77f);break;
            case GameMusicStyle.Dark:left=new Color(.12f,.12f,.18f);right=new Color(.46f,.24f,.66f);break;
            case GameMusicStyle.Dreamy:left=new Color(.42f,.36f,.78f);right=new Color(.74f,.61f,.88f);break;
            case GameMusicStyle.Electronic:left=new Color(.035f,.16f,.44f);right=new Color(.04f,.73f,.96f);break;
            case GameMusicStyle.Energetic:left=new Color(.19f,.20f,.15f);right=new Color(.94f,.94f,.09f);break;
            case GameMusicStyle.Experimental:left=new Color(.16f,.18f,.15f);right=new Color(.67f,.88f,.035f);break;
            case GameMusicStyle.Funky:left=new Color(.39f,.08f,.36f);right=new Color(1,.42f,.08f);break;
            case GameMusicStyle.Groove:left=new Color(.32f,.10f,.23f);right=new Color(1,.66f,.08f);break;
            case GameMusicStyle.Heavy:left=new Color(.13f,.11f,.15f);right=new Color(.66f,.045f,.13f);break;
            case GameMusicStyle.Jazzy:left=new Color(.10f,.16f,.23f);right=new Color(.86f,.63f,.27f);break;
            case GameMusicStyle.Rock:left=new Color(.17f,.10f,.14f);right=new Color(.90f,.075f,.13f);break;
            case GameMusicStyle.Soulful:left=new Color(.055f,.35f,.39f);right=new Color(.83f,.60f,.31f);break;
        }
    }
}
