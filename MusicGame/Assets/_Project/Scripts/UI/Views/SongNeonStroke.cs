using UnityEngine;
using UnityEngine.UI;

/// <summary>Analytic rounded light and soft sheared shadows; shared by all skins without glow textures.</summary>
[RequireComponent(typeof(Image))]
public sealed class SongNeonStroke : BaseMeshEffect
{
    public float padding=16;
    public float slant;
    protected override void OnEnable(){base.OnEnable();EnableChannels(this);}
    public static void EnableChannels(Component owner)
    {
        var canvas=owner.GetComponentInParent<Canvas>();
        if(canvas!=null)canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1;
    }
    public override void ModifyMesh(VertexHelper mesh)
    {
        if(!IsActive())return;mesh.Clear();
        Quad(mesh,((RectTransform)transform).rect,padding,slant,graphic.color);
    }
    public static void Quad(VertexHelper mesh,Rect core,float padding,float slant,Color colour)
    {
        float width=core.width+padding*2,height=core.height+padding*2;
        var data=new Vector4(width,height,Mathf.Max(.1f,(core.width-slant)*.5f),Mathf.Max(.1f,core.height*.5f));
        int n=mesh.currentVertCount;
        void Vertex(float x,float y,float u,float v)
        {
            var vertex=UIVertex.simpleVert;vertex.position=new Vector3(x,y,0);vertex.color=colour;vertex.uv0=new Vector2(u,v);vertex.uv1=data;mesh.AddVert(vertex);
        }
        Vertex(core.xMin-padding,core.yMin-padding,0,0);Vertex(core.xMax+padding,core.yMin-padding,1,0);
        Vertex(core.xMax+padding,core.yMax+padding,1,1);Vertex(core.xMin-padding,core.yMax+padding,0,1);
        mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
    }
}
