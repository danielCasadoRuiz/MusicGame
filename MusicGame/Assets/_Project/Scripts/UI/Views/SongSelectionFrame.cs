using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared metallic double-rivet selection with diagonal inset accents, without extra textures.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SongSelectionFrame : MaskableGraphic
{
    public bool hero;
    public bool simple;
    public float slant;
    public bool luminous;
    public bool cleanCorners;
    private Color metallic;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=GetPixelAdjustedRect();
        metallic=color;
        if(luminous)
        {
            metallic=new Color(color.r,color.g,color.b,.1f*color.a);Ring(mesh,r,0,12);
            metallic=new Color(color.r,color.g,color.b,.28f*color.a);Ring(mesh,r,3,6);metallic=color;
        }
        if(cleanCorners)
        {
            metallic=new Color(1,.79f,.28f,color.a);Ring(mesh,r,2,4);
            metallic=new Color(.025f,.02f,.04f,color.a);Ring(mesh,r,6,4);
            metallic=new Color(1,.97f,.80f,color.a);Ring(mesh,r,10,2);return;
        }
        if(simple)
        {
            metallic=new Color(.03f,.04f,.06f,1);Ring(mesh,r,0,6);
            metallic=color;Ring(mesh,r,1,4);return;
        }
        if(hero){Ring(mesh,r,0,2);Ring(mesh,r,6,1);}
        else
        {
            metallic=new Color(.025f,.035f,.06f,1);Ring(mesh,r,0,11);
            metallic=new Color(1,.96f,.81f,1);Ring(mesh,r,2,5);
            metallic=color;Ring(mesh,r,8,3);
        }
        metallic=color;
        if(!cleanCorners){Cut(mesh,new Vector2(r.xMin+3,r.yMax-3),false);Cut(mesh,new Vector2(r.xMax-3,r.yMin+3),true);}
    }
    void Ring(VertexHelper m,Rect r,float inset,float width)
    {
        float l=r.xMin+inset,b=r.yMin+inset,t=r.yMax-inset,x=r.xMax-inset;
        Quad(m,new Vector2(l,b),new Vector2(x,b),new Vector2(x,b+width),new Vector2(l,b+width));
        Quad(m,new Vector2(l,t-width),new Vector2(x,t-width),new Vector2(x,t),new Vector2(l,t));
        Quad(m,new Vector2(l,b),new Vector2(l+width,b),new Vector2(l+width,t),new Vector2(l,t));
        Quad(m,new Vector2(x-width,b),new Vector2(x,b),new Vector2(x,t),new Vector2(x-width,t));
    }
    void Cut(VertexHelper m,Vector2 p,bool reverse)
    {
        float s=reverse?-1:1;
        Quad(m,p+new Vector2(4,-57)*s,p+new Vector2(4,-39)*s,p+new Vector2(39,-4)*s,p+new Vector2(57,-4)*s);
        Quad(m,p+new Vector2(4,-67)*s,p+new Vector2(4,-63)*s,p+new Vector2(63,-4)*s,p+new Vector2(67,-4)*s);
    }
    void Quad(VertexHelper m,Vector2 a,Vector2 b,Vector2 c,Vector2 d)
    {
        if(slant!=0){a=Shear(a);b=Shear(b);c=Shear(c);d=Shear(d);}
        int i=m.currentVertCount;
        var dark=Color.Lerp(metallic,Color.black,.38f);var light=Color.Lerp(metallic,Color.white,.7f);
        m.AddVert(a,dark,Vector2.zero);m.AddVert(b,metallic,Vector2.zero);m.AddVert(c,light,Vector2.zero);m.AddVert(d,metallic,Vector2.zero);
        m.AddTriangle(i,i+1,i+2);m.AddTriangle(i,i+2,i+3);
    }
    Vector2 Shear(Vector2 p)
    {
        var r=GetPixelAdjustedRect();
        return new Vector2(r.xMin+slant*(p.y-r.yMin)/r.height+(p.x-r.xMin)*(r.width-slant)/r.width,p.y);
    }
}
