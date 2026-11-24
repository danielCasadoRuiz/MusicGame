using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits rectangular card art by cropping, preserving grain scale and trimming old selected rims.</summary>
[RequireComponent(typeof(Image))]
public sealed class SongCardSurfaceMesh : BaseMeshEffect, ICanvasRaycastFilter
{
    public bool trimFrame;
    public float slant;
    public void SetVerticesDirty(){if(graphic!=null)graphic.SetVerticesDirty();}
    public bool IsRaycastLocationValid(Vector2 screenPoint,Camera eventCamera)
    {
        if(!IsActive()||slant==0)return true;
        var rect=((Image)graphic).rectTransform;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screenPoint,eventCamera,out var p))return false;
        var r=rect.rect;float y=(p.y-r.yMin)/r.height;
        return p.x>=r.xMin+slant*y&&p.x<=r.xMax-slant+slant*y;
    }
    public override void ModifyMesh(VertexHelper mesh)
    {
        if(!IsActive())return;
        var image=(Image)graphic;var sprite=image.sprite;if(sprite==null)return;
        var rect=image.rectTransform.rect;if(rect.width<=0||rect.height<=0)return;
        float source=sprite.rect.width/sprite.rect.height,dest=rect.width/rect.height;
        float u=1,v=1;if(dest>source)v=source/dest;else u=dest/source;
        if(trimFrame){u*=.96f;v*=.91f;}
        var texture=sprite.texture;var sr=sprite.textureRect;
        var vertex=new UIVertex();
        for(int i=0;i<mesh.currentVertCount;i++)
        {
            mesh.PopulateUIVertex(ref vertex,i);
            if(slant!=0)
            {
                float px=(vertex.position.x-rect.xMin)/rect.width,py=(vertex.position.y-rect.yMin)/rect.height;
                vertex.position=new Vector3(rect.xMin+slant*py+px*(rect.width-slant),vertex.position.y,vertex.position.z);
            }
            float x=(vertex.uv0.x*texture.width-sr.x)/sr.width,y=(vertex.uv0.y*texture.height-sr.y)/sr.height;
            vertex.uv0=new Vector2((sr.x+sr.width*(.5f+(x-.5f)*u))/texture.width,(sr.y+sr.height*(.5f+(y-.5f)*v))/texture.height);
            mesh.SetUIVertex(vertex,i);
        }
    }
}
