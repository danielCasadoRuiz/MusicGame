using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One pooled quad per pickup; shared mesh/material, no updates, lights or textures.</summary>
public static class BonusHalo
{
    private static readonly int HaloColorId = Shader.PropertyToID("_HaloColor");

    public static Color LightenedColor(Color bonusColor, Color highlight)
    {
        var color = Color.Lerp(bonusColor, highlight, .60f);
        color.a = highlight.a;
        return color;
    }

    public static Mesh CreateMesh()
    {
        var mesh=new Mesh {name="Bonus halo quad"};
        mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
        mesh.triangles=new[]{0,2,1,1,2,3};
        // Billboard rotations are shader-side: conservative bounds prevent edge-of-screen popping.
        mesh.bounds=new Bounds(Vector3.zero,Vector3.one*2);
        return mesh;
    }
    public static Renderer Attach(GameObject bonus,Mesh mesh,Material material,float multiplier,Color? tint = null)
    {
        var bounds=new Bounds();bool found=false;
        foreach(var renderer in bonus.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter=renderer.GetComponent<MeshFilter>();if(filter==null || filter.sharedMesh==null)continue;
            var b=filter.sharedMesh.bounds;var matrix=bonus.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            for(int n=0;n<8;n++)
            {
                var corner=b.center+Vector3.Scale(b.extents,new Vector3((n&1)==0?-1:1,(n&2)==0?-1:1,(n&4)==0?-1:1));
                var p=matrix.MultiplyPoint3x4(corner);if(!found){bounds=new Bounds(p,Vector3.zero);found=true;}else bounds.Encapsulate(p);
            }
        }
        if(!found)return null;
        var go=new GameObject("IvoryHalo",typeof(MeshFilter),typeof(MeshRenderer));go.layer=bonus.layer;
        go.transform.SetParent(bonus.transform,false);go.transform.localPosition=bounds.center;
        go.transform.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))*multiplier);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var halo=go.GetComponent<MeshRenderer>();halo.sharedMaterial=material;
        if (tint.HasValue)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor(HaloColorId, tint.Value);
            halo.SetPropertyBlock(properties);
        }
        halo.shadowCastingMode=ShadowCastingMode.Off;halo.receiveShadows=false;
        halo.lightProbeUsage=LightProbeUsage.Off;halo.reflectionProbeUsage=ReflectionProbeUsage.Off;
        return halo;
    }
}
