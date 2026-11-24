using UnityEngine;
using UnityEngine.UI;

/// <summary>Passes the row's aspect to the shared material; no material or texture per row.</summary>
[RequireComponent(typeof(Graphic))]
public sealed class RectangularButtonMesh : BaseMeshEffect
{
    public bool diagonal;
    protected override void OnEnable()
    {
        base.OnEnable();
        if (graphic.canvas != null) graphic.canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }
    protected override void OnCanvasHierarchyChanged()
    {
        base.OnCanvasHierarchyChanged();
        if (graphic.canvas != null) graphic.canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }
    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsActive()) return;
        var size=((RectTransform)transform).rect.size;
        float aspect=size.y>0?size.x/size.y:6;
        var vertex=new UIVertex();
        for(int i=0;i<vertices.currentVertCount;i++)
        {
            vertices.PopulateUIVertex(ref vertex,i);vertex.uv1.x=aspect;
            if(diagonal)vertex.position.x+=18*(vertex.uv0.y-vertex.uv0.x);
            vertices.SetUIVertex(vertex,i);
        }
    }
}
