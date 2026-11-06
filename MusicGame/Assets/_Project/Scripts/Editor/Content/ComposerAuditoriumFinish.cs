#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class ComposerAuditoriumFinish
{
 sealed class Geometry
 {
  readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> indices=new List<int>();
  public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int mapping=0){int n=vertices.Count;foreach(var p in new[]{a,b,c,d}){vertices.Add(p);uv.Add(mapping==0?new Vector2(p.x/20+.5f,p.z/20+.5f):mapping==1?new Vector2(p.x/20+.5f,p.y/20+.5f):new Vector2(p.z/20+.5f,p.y/20+.5f));}indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
  public Mesh Mesh(string name){var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;}
 }
 static Mesh Save(Mesh mesh,string composer,string name)
 {
  string folder="Assets/_Project/Art/Arenas/"+composer+"/Architecture";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/_Project/Art/Arenas/"+composer,"Architecture");string path=folder+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;
 }
 static void Add(Transform parent,string name,Mesh mesh,Material material){var old=parent.Find(name);if(old)Object.DestroyImmediate(old.gameObject);var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;}
 public static void Apply(GameObject arena,string composer)
 {
  if(composer!="Brahms"&&composer!="Beethoven")return;var parent=arena.transform.Find("ComposerScenery");var floor=arena.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Floor").sharedMaterial;
  if(composer=="Beethoven"){
   var g=new Geometry();g.Quad(new Vector3(-4,1.7f,16),new Vector3(-4,1.7f,22),new Vector3(4,1.7f,22),new Vector3(4,1.7f,16));Add(parent,"RecedingHallFloor",Save(g.Mesh("RecedingHallFloor"),composer,"RecedingHallFloor"),floor);return;
  }
  var wall=new Geometry();for(int i=0;i<128;i++){float a=i*Mathf.PI*2/128,b=(i+1)*Mathf.PI*2/128;Vector3 p=new Vector3(Mathf.Sin(a)*10.4f,0,Mathf.Cos(a)*10.4f),q=new Vector3(Mathf.Sin(b)*10.4f,0,Mathf.Cos(b)*10.4f);wall.Quad(p,p+Vector3.up*9.3f,q+Vector3.up*9.3f,q,1);}
  var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Arenas/Brahms/Materials/Brahms_RearWall.mat");Add(parent,"CircularAuditoriumBacking",Save(wall.Mesh("CircularAuditoriumBacking"),composer,"CircularAuditoriumBacking"),mat);
  var steps=new Geometry();for(int row=0;row<6;row++)for(int col=0;col<52;col++){
   float x=-6.5f+col*.25f,xx=x+.25f,z=3.6f+row*.8f-.04f*x*x,zz=3.6f+row*.8f-.04f*xx*xx,h=row*.15f,previous=row==0?-.08f:(row-1)*.15f;
   steps.Quad(new Vector3(x,h,z),new Vector3(x,h,z+.8f),new Vector3(xx,h,zz+.8f),new Vector3(xx,h,zz));
   steps.Quad(new Vector3(x,previous,z),new Vector3(x,h,z),new Vector3(xx,h,zz),new Vector3(xx,previous,zz),1);
   if(col==0)steps.Quad(new Vector3(x,-.08f,z),new Vector3(x,-.08f,z+.8f),new Vector3(x,h,z+.8f),new Vector3(x,h,z),2);
   if(col==51)steps.Quad(new Vector3(xx,-.08f,zz),new Vector3(xx,h,zz),new Vector3(xx,h,zz+.8f),new Vector3(xx,-.08f,zz+.8f),2);
  }
  Add(parent,"CurvedAudienceRisers",Save(steps.Mesh("CurvedAudienceRisers"),composer,"CurvedAudienceRisers"),floor);
 }
}
#endif
