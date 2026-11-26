using UnityEngine;
using UnityEngine.UI;

/// <summary>Angular modal panel sharing the song-card artwork, cropped instead of stretched.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SettingsPanelSurface : MaskableGraphic
{
    public Sprite artwork;
    public bool borderOnly;
    public float slant=45,rimWidth=2;
    public Color rim=Color.white;
    public override Texture mainTexture=>!borderOnly&&artwork!=null?artwork.texture:Texture2D.whiteTexture;
    public void Refresh(){SetMaterialDirty();SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var r=rectTransform.rect;
        if(borderOnly)
        {
            var outer=new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMin+slant,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax-slant,r.yMin)};
            var inner=new[]{outer[0]+new Vector2(2,2),outer[1]+new Vector2(2,-2),outer[2]+new Vector2(-2,-2),outer[3]+new Vector2(-2,2)};
            for(int i=0;i<4;i++){int j=(i+1)%4,n=vh.currentVertCount;vh.AddVert(outer[i],rim,Vector2.zero);vh.AddVert(outer[j],rim,Vector2.zero);vh.AddVert(inner[j],rim,Vector2.zero);vh.AddVert(inner[i],rim,Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);}return;
        }
        Draw(vh,r,slant,rim);
        Draw(vh,new Rect(r.x+rimWidth,r.y+rimWidth,r.width-2*rimWidth,r.height-2*rimWidth),slant,color);
    }
    void Draw(VertexHelper vh,Rect r,float skew,Color tint)
    {
        int n=vh.currentVertCount;
        var uv=artwork!=null?artwork.textureRect:new Rect(0,0,1,1);
        if(artwork!=null){uv=new Rect(uv.x+uv.width*.06f,uv.y+uv.height*.10f,uv.width*.88f,uv.height*.80f);}
        float tw=mainTexture.width,th=mainTexture.height;
        float crop=artwork!=null?Mathf.Min(1,(uv.width/uv.height)/(r.width/r.height)):1;
        var points=new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMin+skew,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax-skew,r.yMin)};
        var tex=new[]{new Vector2(0,.5f-crop/2),new Vector2(0,.5f+crop/2),new Vector2(1,.5f+crop/2),new Vector2(1,.5f-crop/2)};
        for(int i=0;i<4;i++)vh.AddVert(points[i],tint,new Vector2((uv.x+tex[i].x*uv.width)/tw,(uv.y+tex[i].y*uv.height)/th));
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
    }
}
