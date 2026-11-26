using UnityEngine;
using UnityEngine.UI;
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SettingsSkinArrow : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {vh.Clear();var r=rectTransform.rect;vh.AddVert(new Vector2(r.xMin,r.yMin),color,Vector2.zero);vh.AddVert(new Vector2(r.xMin,r.yMax),color,Vector2.zero);vh.AddVert(new Vector2(r.xMax,r.center.y),color,Vector2.zero);vh.AddTriangle(0,1,2);}
}
