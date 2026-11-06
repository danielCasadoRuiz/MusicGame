using System.Collections.Generic;
using UnityEngine;

/// <summary>96 vertices / 108 triangles, built once per spectrum module and shared by all bars.</summary>
public static class HorizonBeveledBarMesh
{
    public static Mesh Build(float bevel)
    {
        bevel=Mathf.Clamp(bevel,.005f,.2f);
        float inner=.5f-bevel;
        float[] grid={-.5f,-inner,inner,.5f};
        var vertices=new List<Vector3>(96); var normals=new List<Vector3>(96);
        var uv=new List<Vector2>(96); var triangles=new List<int>(324);
        Vector3[] axes={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
        foreach(var normal in axes)
        {
            Vector3 u=Mathf.Abs(normal.y)>.5f?Vector3.right:Vector3.up;
            Vector3 v=Vector3.Cross(normal,u);
            int start=vertices.Count;
            for(int y=0;y<4;y++) for(int x=0;x<4;x++)
            {
                Vector3 p=normal*.5f+u*grid[x]+v*grid[y];
                Vector3 q=new(Mathf.Clamp(p.x,-inner,inner),Mathf.Clamp(p.y,-inner,inner),Mathf.Clamp(p.z,-inner,inner));
                Vector3 n=(p-q).normalized;
                vertices.Add(q+n*bevel); normals.Add(n); uv.Add(new Vector2(grid[x]+.5f,grid[y]+.5f));
            }
            for(int y=0;y<3;y++) for(int x=0;x<3;x++)
            {
                int a=start+y*4+x; triangles.AddRange(new[]{a,a+1,a+4,a+1,a+5,a+4});
            }
        }
        var mesh=new Mesh{name="Horizon LED — shared beveled shell"};
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
        mesh.RecalculateBounds(); return mesh;
    }
}
