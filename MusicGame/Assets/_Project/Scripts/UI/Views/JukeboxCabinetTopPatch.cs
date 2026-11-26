using UnityEngine;
using UnityEngine.UI;

/// <summary>Only the cleared upper-right art is used; a short blend retains the original crest texture.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class JukeboxCabinetTopPatch : MaskableGraphic
{
    public Texture2D artwork;
    public override Texture mainTexture=>artwork!=null?artwork:Texture2D.whiteTexture;
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();if(artwork==null)return;
        var r=GetPixelAdjustedRect();
        void Strip(float left,float right,float a0,float a1)
        {
            int i=vertices.currentVertCount;
            var c0=color;c0.a*=a0;var c1=color;c1.a*=a1;
            float x0=Mathf.Lerp(r.xMin,r.xMax,left),x1=Mathf.Lerp(r.xMin,r.xMax,right);
            float u0=Mathf.Lerp(.6f,1,left),u1=Mathf.Lerp(.6f,1,right);
            vertices.AddVert(new Vector3(x0,r.yMin),c0,new Vector2(u0,.78f));
            vertices.AddVert(new Vector3(x0,r.yMax),c0,new Vector2(u0,1));
            vertices.AddVert(new Vector3(x1,r.yMax),c1,new Vector2(u1,1));
            vertices.AddVert(new Vector3(x1,r.yMin),c1,new Vector2(u1,.78f));
            vertices.AddTriangle(i,i+1,i+2);vertices.AddTriangle(i+2,i+3,i);
        }
        Strip(0,.0625f,0,1);Strip(.0625f,1,1,1);
    }
}
