#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEditor;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using Object=UnityEngine.Object;

public static class ArenaVisualAuthoring
{
 const string Folder="Assets/_Project/Art/Arenas/_Shared/VisualRefinement";
 static Color Warm=new Color(1,.88f,.73f),Neutral=new Color(.88f,.92f,1);
 public static void Apply(GameObject root,string composer,int variant,string quality)
 {
  Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
  if(composer=="Beethoven")CompletePassage(root,quality,variant);
  if(composer=="Chopin"&&variant==1)CompleteSalonInfill(root);
  var plasterImporter=AssetImporter.GetAtPath(Folder+"/Textures/PalacePlaster.png") as TextureImporter;
  if(plasterImporter&&(plasterImporter.maxTextureSize!=1024||plasterImporter.wrapMode!=TextureWrapMode.Repeat)){plasterImporter.maxTextureSize=1024;plasterImporter.wrapMode=TextureWrapMode.Repeat;plasterImporter.mipmapEnabled=true;plasterImporter.anisoLevel=2;plasterImporter.textureCompression=TextureImporterCompression.CompressedHQ;plasterImporter.SaveAndReimport();}
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true)){
   if(composer=="Wagner"&&filter.sharedMesh&&filter.sharedMesh.name.StartsWith("Box_")&&IsArchitecturalBox(filter.sharedMesh)){
    var existingRenderer=filter.GetComponent<MeshRenderer>();if(existingRenderer&&ArenaWagnerBakeWalls.Masked(existingRenderer)){existingRenderer.sharedMaterial=IntegratedInfill(existingRenderer.sharedMaterial,composer);ArenaWagnerBakeWalls.Cut(filter);}continue;
   }
   if(!filter.sharedMesh||filter.sharedMesh.name!="Cube"||!IsArchitecturalBox(filter.sharedMesh))continue;
   var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||filter.transform.childCount!=0)continue;
   var scale=filter.transform.localScale;if(Mathf.Abs(scale.x*scale.y*scale.z)<.000001f)continue;
   string key=string.Join("_",new[]{scale.x,scale.y,scale.z}.Select(v=>v.ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture))).Replace('-','n').Replace('.','p');
   string path=Folder+"/Meshes/Box_"+key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(!mesh){
    mesh=Object.Instantiate(filter.sharedMesh);mesh.name="RefinedBox_"+key;
    var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;var uv=new Vector2[vertices.Length];
    for(int i=0;i<vertices.Length;i++){
     vertices[i]=Vector3.Scale(vertices[i],scale);
     normals[i]=new Vector3(normals[i].x/scale.x,normals[i].y/scale.y,normals[i].z/scale.z).normalized;
     var n=normals[i];var v=vertices[i];
     // Two metres per repeat on every face, irrespective of the original box dimensions.
     uv[i]=Mathf.Abs(n.y)>.5f?new Vector2(v.x,v.z)*.5f:Mathf.Abs(n.x)>.5f?new Vector2(v.z,v.y)*.5f:new Vector2(v.x,v.y)*.5f;
     if(tangents.Length==vertices.Length){var t=Vector3.Scale(new Vector3(tangents[i].x,tangents[i].y,tangents[i].z),scale).normalized;tangents[i]=new Vector4(t.x,t.y,t.z,tangents[i].w*Mathf.Sign(scale.x*scale.y*scale.z));}
    }
    mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;if(tangents.Length==vertices.Length)mesh.tangents=tangents;
    if(scale.x*scale.y*scale.z<0){var triangles=mesh.triangles;for(int i=0;i<triangles.Length;i+=3){int t=triangles[i];triangles[i]=triangles[i+1];triangles[i+1]=t;}mesh.triangles=triangles;}
    mesh.RecalculateTangents();mesh.RecalculateBounds();UnwrapParam.SetDefaults(out var unwrap);unwrap.packMargin=4f/1024;unwrap.angleError=5;unwrap.areaError=5;Unwrapping.GenerateSecondaryUVSet(mesh,unwrap);AssetDatabase.CreateAsset(mesh,path);
   }
   filter.sharedMesh=mesh;filter.transform.localScale=Vector3.one;
   // Keep decorative/stone/floor materials already carrying artwork. Upgrade only plain infill.
   var original=renderer.sharedMaterial;
   renderer.sharedMaterial=IntegratedInfill(original,composer);
   renderer.receiveGI=ReceiveGI.Lightmaps;renderer.scaleInLightmap=.7f;
   if(composer=="Wagner"&&ArenaWagnerBakeWalls.Masked(renderer))ArenaWagnerBakeWalls.Cut(filter);
  }
  var prior=root.transform.Find("ReviewedLighting");if(prior)Object.DestroyImmediate(prior.gameObject);
  var node=new GameObject("ReviewedLighting");node.transform.SetParent(root.transform,false);
  float strength=1;
  if(composer=="Brahms"){
   Add(node,"ChandelierDiffuse",new Vector3(0,4.7f,3),new Vector3(0,1,0),new Color(1,.94f,.85f),62,24,LightType.Point);
   Add(node,"ChandelierRearBounce",new Vector3(0,5.5f,8),Vector3.zero,new Color(1,.94f,.85f),26,22,LightType.Point);
   Add(node,"FrontRoomBounce",new Vector3(0,5,-4),Vector3.zero,new Color(1,.96f,.9f),26,22,LightType.Point);
   Add(node,"ChandelierLeftSpread",new Vector3(-2,5,2),new Vector3(-8,4.5f,4),new Color(1,.94f,.85f),58,20,LightType.Spot);
   Add(node,"ChandelierRightSpread",new Vector3(2,5,2),new Vector3(8,4.5f,4),new Color(1,.94f,.85f),58,20,LightType.Spot);
   if(quality!="Low")Add(node,"SpecularAccent_Chandelier",new Vector3(0,5,3),Vector3.zero,new Color(1,.94f,.85f),12,18,LightType.Point);
   else Add(node,"LowBakedChandelierCompensation",new Vector3(0,5,3),Vector3.zero,new Color(1,.94f,.85f),12,18,LightType.Point);
  }else if(composer=="Beethoven"){
   Add(node,"ChandelierDiffuse",new Vector3(0,5.2f,3),Vector3.zero,Warm,32,25,LightType.Point);
   Add(node,"HallReflectedLight",new Vector3(0,4,11),new Vector3(0,1,7),Warm,14,20,LightType.Point);
  }else if(composer=="Handel"){
   Add(node,"WindowDiffuse",new Vector3(7.2f,4,0),new Vector3(-3,1.5f,0),new Color(1,.96f,.88f),65,27,LightType.Spot);
   Add(node,"ChandelierBounce",new Vector3(0,5.3f,3),Vector3.zero,Warm,26,25,LightType.Point);
  }else if(composer=="Wagner"){
   Add(node,"OpenSkyReflectedLight",new Vector3(0,7,2),new Vector3(0,1,0),new Color(.72f,.82f,1),24,26,LightType.Point);
   Add(node,quality=="Low"?"LowBakedOpenSkyCompensation":"SpecularAccent_OpenSky",new Vector3(0,4,-3),Vector3.zero,new Color(.82f,.88f,1),14,18,LightType.Point);
   node.GetComponentsInChildren<Light>().First(l=>l.name=="SpecularAccent_OpenSky"||l.name=="LowBakedOpenSkyCompensation").GetUniversalAdditionalLightData().renderingLayers=8;
   foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name!="Floor"&&r.sharedMaterials.All(m=>!m||m.renderQueue<3000))){renderer.renderingLayerMask|=8;EditorUtility.SetDirty(renderer);if(PrefabUtility.IsPartOfPrefabInstance(renderer))PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);}
   foreach(var pair in new[]{new[]{"Reference_Fixture_29","ornate_brazier_30"},new[]{"Reference_Fixture_33","ornate_brazier_34"}}){
    var lamp=root.GetComponentsInChildren<Light>(true).FirstOrDefault(l=>l.name==pair[0]);var brazier=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==pair[1]);
    if(lamp&&brazier){var meshes=brazier.GetComponentsInChildren<MeshRenderer>(false);var position=brazier.position+Vector3.up*1.78f;if(meshes.Length>0){var bounds=meshes[0].bounds;foreach(var mesh in meshes.Skip(1))bounds.Encapsulate(mesh.bounds);position=new Vector3(bounds.center.x,bounds.max.y+.03f,bounds.center.z);}lamp.transform.position=position;EditorUtility.SetDirty(lamp.transform);if(PrefabUtility.IsPartOfPrefabInstance(lamp.transform))PrefabUtility.RecordPrefabInstancePropertyModifications(lamp.transform);}
   }
  }else if(composer=="Monteverdi"||composer=="Vivaldi"||composer=="Pachelbel"){
   strength=composer=="Monteverdi"?12:composer=="Pachelbel"?9:6;
   Add(node,"OpenArcadeSkyBounce",new Vector3(-4,5,0),Vector3.zero,new Color(.9f,.94f,1),strength,24,LightType.Point);
   Add(node,"OppositeArcadeBounce",new Vector3(4,5,2),Vector3.zero,new Color(1,.95f,.87f),strength*.65f,24,LightType.Point);
  }else{
   strength=composer=="Bach"?22:composer=="Chopin"?12:composer=="Tchaikovsky"?14:composer=="Mozart"?6:4;
   if(composer=="Tchaikovsky"&&variant==1)strength*=1.8f;
   var windows=root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&&new[]{"North","South","East","West"}.Contains(t.parent.name)&&t.name.ToLowerInvariant().Contains("window")).ToList();
   // Use actual window placements in each layout, not fixed lights left behind after moving scenery.
   foreach(var group in windows.GroupBy(t=>Mathf.Abs(t.position.x)>Mathf.Abs(t.position.z)?(t.position.x<0?"W":"E"):(t.position.z<0?"S":"N"))){
    foreach(var window in group.OrderBy(t=>Mathf.Abs(t.position.x)+Mathf.Abs(t.position.z)).Take(2)){
     var pos=window.position;var inward=new Vector3(-pos.x,0,-pos.z).normalized;pos+=inward*.8f;pos.y=composer=="Bach"?6:4;
     var color=composer=="Tchaikovsky"?Neutral:composer=="Bach"?new Color(1,.93f,.82f):new Color(1,.97f,.9f);
     Add(node,"WindowTransmission_"+window.name,pos,new Vector3(pos.x*.15f,1.2f,pos.z*.15f),color,strength,28,LightType.Spot);
    }
   }
   if(composer=="Bach"||composer=="Chopin")Add(node,"UpperRoomReflectedLight",new Vector3(0,5.5f,1),Vector3.zero,new Color(1,.95f,.86f),composer=="Bach"?12:9,25,LightType.Point);
   if(composer=="Bach"){
    int count=0;
    foreach(var light in node.GetComponentsInChildren<Light>().Where(l=>l.name.StartsWith("WindowTransmission_")&&l.transform.position.x>5).OrderBy(l=>Mathf.Abs(l.transform.position.z))){
     if(count++>=(quality=="Low"||quality=="Mid"?1:2))break;
     AddBeam(node,light.transform.position,new Vector3(-2,.15f,light.transform.position.z*.3f),composer+"_"+variant+"_"+count);
    }
   }
  }
  if(composer=="Bach")AlignExistingBeams(root);
  AssetDatabase.SaveAssets();
 }
 public static bool IsArchitecturalBox(Mesh mesh){if(!mesh)return false;string path=AssetDatabase.GetAssetPath(mesh);return (mesh.name=="Cube"&&!path.StartsWith("Assets/"))||(path.StartsWith(Folder+"/Meshes/")&&(mesh.name.StartsWith("Box_")||mesh.name.StartsWith("RefinedBox_")||mesh.name.StartsWith("WindowWall_")));}
 static Material IntegratedInfill(Material original,string composer){
  if(!original||(original.shader.name!="Universal Render Pipeline/Lit"&&original.shader.name!="MusicGame/Art/GothicWindowCutout")||original.GetTexture("_BaseMap"))return original;
  string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));string path=Folder+"/Materials/"+composer+"_"+guid+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!material){material=new Material(original);material.name=composer+"_Integrated_"+original.name;AssetDatabase.CreateAsset(material,path);}
  var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/PalacePlaster.png");if(texture){material.SetTexture("_BaseMap",texture);material.SetTextureScale("_BaseMap",Vector2.one);}
  var tint=original.GetColor("_BaseColor");float minimum=composer=="Wagner"?.16f:composer=="Brahms"||composer=="Beethoven"?.20f:composer=="Bach"?.36f:.32f;float largest=Mathf.Max(tint.r,Mathf.Max(tint.g,tint.b));if(largest<minimum)tint*=minimum/Mathf.Max(largest,.01f);
  material.SetColor("_BaseColor",tint);material.SetFloat("_Smoothness",.22f);EditorUtility.SetDirty(material);return material;
 }
 public static void AlignExistingBeams(GameObject root){
  var lighting=root.GetComponentInChildren<ArenaSceneLighting>(true);if(!lighting||!lighting.mainLight)return;
  var direction=lighting.mainLight.transform.forward;if(direction.y>=-.1f)return;
  foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("SubtleWindowBeam_")&&f.sharedMesh)){
   var mesh=filter.sharedMesh;var vertices=mesh.vertices;if(vertices.Length!=50)continue;
   var start=filter.transform.TransformPoint(vertices[2]);float distance=(.15f-start.y)/direction.y;
   foreach(int axis in new[]{0,2}){float d=direction[axis];if(Mathf.Abs(d)>.001f){float boundary=d>0?8.7f:-8.7f;float hit=(boundary-start[axis])/d;if(hit>0)distance=Mathf.Min(distance,hit);}}
   var end=start+direction*distance;var across=Vector3.Cross(direction,Vector3.up).normalized;var second=Vector3.Cross(direction,across).normalized;
   for(int plane=0;plane<2;plane++)for(int y=0;y<5;y++)for(int x=0;x<5;x++){
    float t=y/4f,u=x/4f;var world=Vector3.Lerp(start,end,t)+(plane==0?across:second)*((u-.5f)*Mathf.Lerp(.7f,2,t));vertices[plane*25+y*5+x]=filter.transform.InverseTransformPoint(world);
   }
   mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
  }
 }
 static void CompletePassage(GameObject root,string quality,int variant){
  var prior=root.transform.Find("ReviewedPassageArchitecture");if(prior)Object.DestroyImmediate(prior.gameObject);
  var portal=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="GrandHall_Portal_Header");
  var candles=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("RecedingHall_Candles_")).ToArray();
  if(!portal||candles.Length==0)return;
  var center=Vector3.zero;foreach(var t in candles)center+=t.position;center/=candles.Length;
  var direction=center-portal.position;direction.y=0;direction.Normalize();
  var material=root.GetComponentsInChildren<Renderer>(true).First(r=>r.name=="Beethoven_RearWall").sharedMaterial;
  var group=new GameObject("ReviewedPassageArchitecture");group.transform.SetParent(root.transform,false);
  foreach(bool roof in new[]{true,false}){
   var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=roof?"ReviewedPassageCeiling":"ReviewedPassageEndBacking";obj.transform.SetParent(group.transform,false);
   var pos=portal.position+direction*(roof?4.5f:9.5f);pos.y=roof?10:5.8f;obj.transform.position=pos;obj.transform.rotation=Quaternion.LookRotation(direction);
   obj.transform.localScale=roof?new Vector3(8.4f,.3f,10):new Vector3(8.4f,8.4f,.35f);obj.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(obj.GetComponent<Collider>());
  }
  string[] tiers={"Low","Mid","HighMid","High","Ultra"};string tier=tiers[Mathf.Max(0,Array.IndexOf(tiers,quality)-3)];
  string prefabPath="Assets/_Project/Art/Arenas/Beethoven/Props/Beethoven_scenario_ornate_wall_panel/"+tier+"/Beethoven_scenario_ornate_wall_panel_"+tier+".prefab";
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
  if(prefab){var panel=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.scene);panel.name="ReviewedPassageEndPanel";panel.transform.SetParent(group.transform,false);var position=portal.position+direction*8.4f;position.y=1.7f;panel.transform.position=position;panel.transform.rotation=Quaternion.LookRotation(-direction);}
  var bounce=portal.position+direction*4.5f;bounce.y=4.5f;Add(group,"PassageCandleBounce",bounce,portal.position,Warm,12,16,LightType.Point);
  // The original 8m wall infill stopped below the 8.5m roof: seal the narrow exposed strip.
  foreach(float side in new[]{-1f,1f}){
   bool passageSide=(variant==2&&side>0)||(variant==3&&side<0);
   if(passageSide){PassageBox(group,"ReviewedUpperWallFront",new Vector3(side*9,8.25f,-4.1f),new Vector3(.5f,.8f,17.8f),material);PassageBox(group,"ReviewedUpperWallBack",new Vector3(side*9,8.25f,14),new Vector3(.5f,.8f,2),material);}
   else PassageBox(group,"ReviewedUpperSideWall",new Vector3(side*9,8.25f,1),new Vector3(.5f,.8f,28),material);
  }
  PassageBox(group,"ReviewedFrontWallBacking",new Vector3(0,4.4f,-9.9f),new Vector3(18.5f,8.8f,.4f),material);
  if(variant>1)PassageBox(group,"ReviewedUpperRearWall",new Vector3(0,8.25f,12.2f),new Vector3(18,.8f,.35f),material);
  if(variant>1){var floor=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Floor");PassageBox(group,"ReviewedNorthFloorExtension",new Vector3(0,floor.bounds.max.y-.075f,10.8f),new Vector3(18,.12f,5),floor.sharedMaterial);}
 }
 static void CompleteSalonInfill(GameObject root){
  var old=root.transform.Find("ReviewedSalonInfill");if(old)Object.DestroyImmediate(old.gameObject);
  var windows=root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&&t.parent.name=="South"&&t.name.Contains("arched_window")).ToArray();if(windows.Length==0)return;
  var bounds=new List<Bounds>();foreach(var window in windows){var renderers=window.GetComponentsInChildren<MeshRenderer>(false);if(renderers.Length==0)continue;var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);bounds.Add(b);}if(bounds.Count==0)return;
  var group=new GameObject("ReviewedSalonInfill");group.transform.SetParent(root.transform,false);var floor=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Floor");var support=root.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Chopin_RearSupport");var roof=root.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Chopin_Ceiling");
  float bottom=floor.bounds.max.y,top=roof.bounds.min.y+.15f,z=bounds.Min(b=>b.min.z)-.15f;float sill=Mathf.Max(bottom+.45f,bounds.Min(b=>b.min.y)+.1f);float header=Mathf.Min(bounds.Max(b=>b.max.y)-.25f,top-.45f);
  PassageBox(group,"ReviewedWindowSillBacking",new Vector3(0,(bottom+sill)*.5f,z),new Vector3(18,sill-bottom,.2f),support.sharedMaterial);
  PassageBox(group,"ReviewedWindowHeadBacking",new Vector3(0,(header+top)*.5f,z),new Vector3(18,top-header,.2f),support.sharedMaterial);
  float cursor=-9;foreach(var b in bounds.OrderBy(b=>b.min.x)){float edge=Mathf.Clamp(b.min.x,-9,9);if(edge-cursor>.01f)PassageBox(group,"ReviewedWindowPierBacking",new Vector3((cursor+edge)*.5f,(bottom+top)*.5f,z),new Vector3(edge-cursor,top-bottom,.2f),support.sharedMaterial);cursor=Mathf.Max(cursor,b.max.x);}
  if(9-cursor>.01f)PassageBox(group,"ReviewedWindowPierBacking",new Vector3((cursor+9)*.5f,(bottom+top)*.5f,z),new Vector3(9-cursor,top-bottom,.2f),support.sharedMaterial);
  PassageBox(group,"ReviewedSouthFloorExtension",new Vector3(0,bottom-.075f,z+.6f),new Vector3(18,.12f,2),floor.sharedMaterial);
 }
 static void PassageBox(GameObject group,string name,Vector3 position,Vector3 size,Material material){var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(group.transform,false);obj.transform.position=position;obj.transform.localScale=size;obj.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(obj.GetComponent<Collider>());}
 static void AddBeam(GameObject parent,Vector3 start,Vector3 end,string key){
  var shader=Shader.Find("MusicGame/Art/ReviewedWindowBeam");if(!shader)return;
  string materialPath=Folder+"/Materials/Bach_WindowBeam.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
  if(!material){material=new Material(shader);material.SetColor("_Tint",new Color(1,.88f,.68f,.025f));AssetDatabase.CreateAsset(material,materialPath);}
  string path=Folder+"/Meshes/WindowBeam_"+key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
  if(!mesh){
   var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();var direction=(end-start).normalized;
   var side=Vector3.Cross(direction,Vector3.up).normalized;var other=Vector3.Cross(direction,side).normalized;
   foreach(var across in new[]{side,other}){
    int first=vertices.Count;
    for(int y=0;y<5;y++)for(int x=0;x<5;x++){
     float t=y/4f,u=x/4f;vertices.Add(Vector3.Lerp(start,end,t)+across*((u-.5f)*Mathf.Lerp(.7f,2,t)));
     float alpha=Mathf.Sin(u*Mathf.PI)*Mathf.Sin(t*Mathf.PI);colors.Add(new Color(1,1,1,alpha));
    }
    for(int y=0;y<4;y++)for(int x=0;x<4;x++){int a=first+y*5+x;triangles.AddRange(new[]{a,a+5,a+1,a+1,a+5,a+6});}
   }
   mesh=new Mesh();mesh.name="WindowBeam_"+key;mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
  }
  var go=new GameObject("SubtleWindowBeam_"+key);go.transform.SetParent(parent.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
 }
 static void Add(GameObject parent,string name,Vector3 position,Vector3 target,Color color,float intensity,float range,LightType type){
  var obj=new GameObject(name);obj.transform.SetParent(parent.transform,false);obj.transform.position=position;obj.transform.LookAt(target);var light=obj.AddComponent<Light>();light.type=type;light.color=color;light.intensity=intensity;light.range=range;light.spotAngle=115;light.innerSpotAngle=70;light.shadows=LightShadows.Soft;light.shadowRadius=1;light.bounceIntensity=1.3f;light.lightmapBakeType=LightmapBakeType.Baked;light.GetUniversalAdditionalLightData().renderingLayers=1;
 }
}
#endif
