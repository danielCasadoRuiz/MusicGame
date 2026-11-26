using UnityEngine;
using UnityEngine.UI;

/// <summary>Clips only the old floor strip in the UI mesh; the source artwork stays intact.</summary>
[RequireComponent(typeof(Graphic))]
public sealed class JukeboxCabinetBaseCrop : BaseMeshEffect
{
    [Tooltip("Hide only the old baked upper-right frames/envelopes; retain the original cabinet elsewhere.")]
    public bool clearUpperRight;
    public override void ModifyMesh(VertexHelper vertices)
    {
        if(!IsActive()||vertices.currentVertCount!=4)return;
        var original=new UIVertex[4];
        for(int i=0;i<4;i++)vertices.PopulateUIVertex(ref original[i],i);
        float x0=original[0].position.x,x1=original[2].position.x;
        float y0=original[0].position.y,y1=original[2].position.y;
        var uv0=original[0].uv0;var uv1=original[2].uv0;var color=original[0].color;
        vertices.Clear();
        void Quad(float left,float bottom,float right,float top)
        {
            int offset=vertices.currentVertCount;
            foreach(var p in new[]{new Vector2(left,bottom),new Vector2(left,top),new Vector2(right,top),new Vector2(right,bottom)})
            {
                var vertex=UIVertex.simpleVert;vertex.color=color;
                vertex.position=new Vector3(Mathf.Lerp(x0,x1,p.x),Mathf.Lerp(y0,y1,p.y));
                vertex.uv0=new Vector4(Mathf.Lerp(uv0.x,uv1.x,p.x),Mathf.Lerp(uv0.y,uv1.y,p.y),0,0);
                vertices.AddVert(vertex);
            }
            vertices.AddTriangle(offset,offset+1,offset+2);vertices.AddTriangle(offset+2,offset+3,offset);
        }
        if(clearUpperRight)
        {
            Quad(0,.085f,1,.78f);
            Quad(0,.78f,.625f,1);
        }
        else Quad(0,.085f,1,1);
        Quad(.015f,.035f,.985f,.085f);
    }
}
