using UnityEngine;
using UnityEngine.UI;
/// <summary>Extends only the middle of the existing button art; diagonal ends keep their native scale.</summary>
[RequireComponent(typeof(Image))]
public sealed class StyleButtonStretchMesh : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper mesh)
    {
        if(!IsActive()||mesh.currentVertCount!=4)return;
        var a=UIVertex.simpleVert;var c=UIVertex.simpleVert;mesh.PopulateUIVertex(ref a,0);mesh.PopulateUIVertex(ref c,2);
        var r=graphic.rectTransform.rect;float cap=Mathf.Min(r.width*.45f,r.height*256/320);
        float[] x={r.xMin,r.xMin+cap,r.xMax-cap,r.xMax};float[] u={0,.2f,.8f,1};mesh.Clear();
        for(int i=0;i<3;i++)
        {
            int n=mesh.currentVertCount;
            void V(float px,float py,float tx,float ty){var v=a;v.position=new Vector3(px,py);v.uv0=new Vector2(Mathf.Lerp(a.uv0.x,c.uv0.x,tx),Mathf.Lerp(a.uv0.y,c.uv0.y,ty));mesh.AddVert(v);}
            V(x[i],r.yMin,u[i],0);V(x[i],r.yMax,u[i],1);V(x[i+1],r.yMax,u[i+1],1);V(x[i+1],r.yMin,u[i+1],0);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
    }
}
