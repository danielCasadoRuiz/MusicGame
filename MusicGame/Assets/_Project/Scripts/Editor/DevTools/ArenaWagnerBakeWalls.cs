#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text;using System.Security.Cryptography;
using UnityEditor;using UnityEngine;

// Use the simple masonry backing as the physical GI wall. Decorative imported panels keep probes,
// their existing visible cutout and realtime shadows; they do not seal the baked aperture again.
public static class ArenaWagnerBakeWalls
{
 const string Folder="Assets/_Project/Art/Arenas/_Shared/VisualRefinement/Meshes";
 struct Halfspace{public Vector3 normal;public float offset;public float Distance(Vector3 v)=>Vector3.Dot(normal,v)+offset;}
 public static bool Masked(Renderer r)=>r.sharedMaterials.Any(m=>m&&m.shader.name=="MusicGame/Art/GothicWindowCutout");
 public static bool IsDecoration(MeshRenderer r){var f=r.GetComponent<MeshFilter>();return f&&f.sharedMesh&&Masked(r)&&!ArenaVisualAuthoring.IsArchitecturalBox(f.sharedMesh);}
 public static void Cut(MeshFilter filter){
  var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!Masked(renderer)||filter.sharedMesh.name.StartsWith("WindowWall_"))return;
  var material=renderer.sharedMaterials.First(m=>m&&m.shader.name=="MusicGame/Art/GothicWindowCutout");
  var masks=new List<List<Halfspace>>();var signature=new StringBuilder(filter.sharedMesh.name+filter.transform.localToWorldMatrix.ToString("F5"));
  for(int index=0;index<4;index++){
   var center=material.GetVector("_WindowCenter"+index);if(center.w<.5f)continue;
   var right=material.GetVector("_WindowRight"+index);var up=material.GetVector("_WindowUp"+index);var normal=material.GetVector("_WindowNormal"+index);var shape=material.GetVector("_WindowShape"+index);
   signature.Append(center.ToString("F5")).Append(right.ToString("F5")).Append(up.ToString("F5")).Append(normal.ToString("F5")).Append(shape.ToString("F5"));
   var polygon=new List<Vector2>{new Vector2(-right.w,0),new Vector2(right.w,0),new Vector2(right.w,up.w)};
   for(int step=1;step<=64;step++){float x=1-step/32f;float arc=Mathf.Sqrt(Mathf.Clamp01(1-(Mathf.Abs(x)+1)*(Mathf.Abs(x)+1)*.25f))/Mathf.Sqrt(.75f);polygon.Add(new Vector2(x*right.w,up.w+shape.x*arc));}
   var planes=new List<Halfspace>();Vector3 origin=center;
   for(int i=0;i<polygon.Count;i++){var a=polygon[i];var edge=polygon[(i+1)%polygon.Count]-a;var n2=new Vector2(-edge.y,edge.x).normalized;var n=(Vector3)right*n2.x+(Vector3)up*n2.y;n.Normalize();planes.Add(new Halfspace{normal=n,offset=-Vector3.Dot(n,origin+(Vector3)right*a.x+(Vector3)up*a.y)});}
   var depth=(Vector3)normal;planes.Add(new Halfspace{normal=depth,offset=normal.w-Vector3.Dot(depth,origin)});planes.Add(new Halfspace{normal=-depth,offset=normal.w+Vector3.Dot(depth,origin)});masks.Add(planes);
  }
  if(masks.Count==0)return;
  string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-","").Substring(0,20);
  string path=Folder+"/WindowWall_"+key+".asset";var cached=AssetDatabase.LoadAssetAtPath<Mesh>(path);
  if(!cached){
   var source=filter.sharedMesh;var original=source.vertices;var indices=source.triangles;var sourceNormals=source.normals;
   var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var welded=new Dictionary<string,int>();
   for(int t=0;t<indices.Length;t+=3){
    var pieces=new List<List<Vector3>>{new List<Vector3>{filter.transform.TransformPoint(original[indices[t]]),filter.transform.TransformPoint(original[indices[t+1]]),filter.transform.TransformPoint(original[indices[t+2]])}};
    foreach(var planes in masks){var result=new List<List<Vector3>>();foreach(var polygon in pieces){
     if(planes.Any(plane=>polygon.All(v=>plane.Distance(v)<-.00001f))){result.Add(polygon);continue;}
     var inside=polygon;foreach(var plane in planes){var outside=Clip(inside,plane,false);if(outside.Count>=3)result.Add(outside);inside=Clip(inside,plane,true);if(inside.Count<3)break;}
    }pieces=result;if(pieces.Count==0)break;}
    Vector3 normal=sourceNormals.Length==original.Length?sourceNormals[indices[t]]:Vector3.Cross(original[indices[t+1]]-original[indices[t]],original[indices[t+2]]-original[indices[t]]).normalized;
    foreach(var polygon in pieces){var face=new List<int>();foreach(var world in polygon){
     var v=filter.transform.InverseTransformPoint(world);var tex=Mathf.Abs(normal.y)>.5f?new Vector2(v.x,v.z)*.5f:Mathf.Abs(normal.x)>.5f?new Vector2(v.z,v.y)*.5f:new Vector2(v.x,v.y)*.5f;
     string id=v.ToString("F4")+normal.ToString("F3");if(!welded.TryGetValue(id,out int vertex)){vertex=vertices.Count;welded[id]=vertex;vertices.Add(v);normals.Add(normal);uv.Add(tex);}face.Add(vertex);
    }for(int i=1;i+1<face.Count;i++){int a=face[0],b=face[i],c=face[i+1];if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-12f)continue;triangles.AddRange(new[]{a,b,c});}}
   }
   cached=new Mesh{name="WindowWall_"+key};if(vertices.Count>65535)cached.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;cached.SetVertices(vertices);cached.SetNormals(normals);cached.SetUVs(0,uv);cached.SetTriangles(triangles,0);cached.RecalculateTangents();cached.RecalculateBounds();UnwrapParam.SetDefaults(out var settings);settings.packMargin=4f/1024;Unwrapping.GenerateSecondaryUVSet(cached,settings);AssetDatabase.CreateAsset(cached,path);
  }
  filter.sharedMesh=cached;
 }
 static List<Vector3> Clip(List<Vector3> polygon,Halfspace plane,bool keepInside){
  var result=new List<Vector3>();if(polygon.Count==0)return result;var previous=polygon[polygon.Count-1];float previousD=plane.Distance(previous)*(keepInside?1:-1);bool previousIn=previousD>=0;
  foreach(var current in polygon){float currentD=plane.Distance(current)*(keepInside?1:-1);bool currentIn=currentD>=0;if(currentIn!=previousIn){float t=previousD/(previousD-currentD);result.Add(Vector3.LerpUnclamped(previous,current,t));}if(currentIn)result.Add(current);previous=current;previousD=currentD;previousIn=currentIn;}
  return result;
 }
 public static void Verify(GameObject root){
  int walls=0,holes=0;
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("WindowWall_"))){
   walls++;var material=filter.GetComponent<Renderer>().sharedMaterials.First(m=>m&&m.shader.name=="MusicGame/Art/GothicWindowCutout");
   var vertices=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();var triangles=filter.sharedMesh.triangles;
   for(int i=0;i<4;i++){
    Vector4 c=material.GetVector("_WindowCenter"+i);if(c.w<.5f)continue;Vector4 u=material.GetVector("_WindowUp"+i),r=material.GetVector("_WindowRight"+i),n=material.GetVector("_WindowNormal"+i);
    Vector3 direction=n;var origin=(Vector3)c+(Vector3)u*(u.w*.5f)+(Vector3)r*(r.w*.45f)-direction*8;
    if(Hits(origin,direction,vertices,triangles))throw new Exception("Physical window still sealed: "+filter.name+" aperture "+i);
    var solid=(Vector3)c+(Vector3)u*(u.w*.5f)+(Vector3)r*(r.w*1.3f)-direction*8;
    if(!Hits(solid,direction,vertices,triangles))throw new Exception("Physical wall missing beside window: "+filter.name+" aperture "+i);
    holes++;
   }
  }
  if(walls==0||holes==0)throw new Exception("Wagner physical GI wall openings missing");
  foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true).Where(IsDecoration))if((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&StaticEditorFlags.ContributeGI)!=0)throw new Exception("Decorative wall seals baked aperture: "+renderer.name);
  Debug.Log("WAGNER_GI_APERTURES PASS walls="+walls+" holes="+holes+"; rays pass through apertures and hit adjacent masonry");
 }
 static bool Hits(Vector3 origin,Vector3 direction,Vector3[] vertices,int[] triangles){
  for(int i=0;i<triangles.Length;i+=3){
   var a=vertices[triangles[i]];var e1=vertices[triangles[i+1]]-a;var e2=vertices[triangles[i+2]]-a;var p=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<.000001f)continue;float inv=1/det;var t=origin-a;float u=Vector3.Dot(t,p)*inv;if(u<-.00001f||u>1.00001f)continue;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)*inv;if(v<-.00001f||u+v>1.00001f)continue;float distance=Vector3.Dot(e2,q)*inv;if(distance>.0001f&&distance<16)return true;
  }
  return false;
 }
}
#endif
