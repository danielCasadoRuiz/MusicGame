using UnityEngine;
using UnityEngine.UI;

/// <summary>Three horizontal slices: the two shaped riveted ends keep their native proportions.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class JukeboxPlaqueSurface : MaskableGraphic
{
    public Texture2D artwork;
    [Tooltip("Visible artwork bounds in source pixels, bottom-left origin.")]
    public Rect pixelRect;
    [Range(.05f,.3f)] public float endFraction=.16f;
    public override Texture mainTexture=>artwork!=null?artwork:Texture2D.whiteTexture;
    public float EndWidth=>pixelRect.height>0?rectTransform.rect.height*pixelRect.width*endFraction/pixelRect.height:0;
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();if(artwork==null||pixelRect.height<=0)return;
        var r=GetPixelAdjustedRect();float cap=Mathf.Min(EndWidth,r.width*.45f);
        var uv=new Rect(pixelRect.x/artwork.width,pixelRect.y/artwork.height,pixelRect.width/artwork.width,pixelRect.height/artwork.height);
        void Strip(float x0,float x1,float u0,float u1)
        {
            int i=vertices.currentVertCount;
            vertices.AddVert(new Vector3(x0,r.yMin),color,new Vector2(u0,uv.yMin));
            vertices.AddVert(new Vector3(x0,r.yMax),color,new Vector2(u0,uv.yMax));
            vertices.AddVert(new Vector3(x1,r.yMax),color,new Vector2(u1,uv.yMax));
            vertices.AddVert(new Vector3(x1,r.yMin),color,new Vector2(u1,uv.yMin));
            vertices.AddTriangle(i,i+1,i+2);vertices.AddTriangle(i+2,i+3,i);
        }
        Strip(r.xMin,r.xMin+cap,uv.xMin,uv.xMin+uv.width*endFraction);
        Strip(r.xMin+cap,r.xMax-cap,uv.xMin+uv.width*endFraction,uv.xMax-uv.width*endFraction);
        Strip(r.xMax-cap,r.xMax,uv.xMax-uv.width*endFraction,uv.xMax);
    }
}
