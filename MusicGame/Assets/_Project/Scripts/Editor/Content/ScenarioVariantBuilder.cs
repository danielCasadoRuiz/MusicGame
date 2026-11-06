#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;using Object=UnityEngine.Object;
public static class ScenarioVariantBuilder
{
 public const string Config="Assets/_Project/Configs/Editor/scenario_layout_variants.json";
 const string Root="Assets/_Project/Art/Arenas/";
 [Serializable] public class Document{public Layout[] layouts;}
 [Serializable] public class Layout{public string composer,title;public int version;public Edit[] edits;public Floor floor;}
 [Serializable] public class Edit{public string name,cloneFrom;public Vector3 position;public float yaw;public bool remove,rotation;}
 [Serializable] public class Floor{public Color tint;public float tiling,smoothness,pattern,angle,tileSize;public Color alternate;}
 public static Layout Get(string c,int v)=>JsonUtility.FromJson<Document>(File.ReadAllText(Config)).layouts.Single(l=>l.composer==c&&l.version==v);
 [MenuItem("Tools/MusicGame/Content/Arenas/Build Independent Variants (02-03)")]
 public static void BuildAll(){if(!Application.isBatchMode&&SceneManager.GetActiveScene().isDirty)throw new Exception("Save the open scene first.");foreach(var c in JsonUtility.FromJson<Document>(File.ReadAllText(Config)).layouts.Select(l=>l.composer).Distinct())BuildOne(c);ArenaBundleValidation.Register();}
 public static void Apply(GameObject root,Layout layout)
 {
  var nodes=root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&&new[]{"North","South","East","West"}.Contains(t.parent.name)).ToList();
  foreach(var e in layout.edits){var t=nodes.FirstOrDefault(t=>t&&t.name==e.name);
   if(!string.IsNullOrEmpty(e.cloneFrom)){var source=nodes.FirstOrDefault(t=>t&&t.name==e.cloneFrom);if(!source)throw new Exception("Missing template "+layout.composer+" "+e.cloneFrom);var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source.gameObject));if(!asset)throw new Exception("Template prefab missing "+source.name);var clone=(GameObject)PrefabUtility.InstantiatePrefab(asset,root.scene);clone.transform.SetParent(source.parent,false);clone.transform.localScale=source.localScale;clone.transform.rotation=source.rotation;clone.name=e.name;t=clone.transform;nodes.Add(t);}
   if(!t)throw new Exception("Missing placement "+layout.composer+" "+e.name);
   if(e.remove){Object.DestroyImmediate(t.gameObject);continue;}
   t.position=e.position;if(e.rotation){var angles=t.eulerAngles;angles.y=e.yaw;t.eulerAngles=angles;}
  }
  foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="GeneratedFrom_Arena_01").ToArray())Object.DestroyImmediate(t.gameObject);
  root.name=layout.composer+"_Arena_"+layout.version.ToString("00");
  if(layout.composer=="Mozart"&&layout.version==2){var basis=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Mozart_Upper_North");AddBox(basis.parent,"VariantMirrorWallBacking",new Vector3(0,4.2f,9.35f),new Vector3(20,8.4f,.3f),basis.GetComponent<Renderer>().sharedMaterial);}
  if(layout.composer=="Monteverdi"&&layout.version==2){var basis=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="West_PaintingSupport");AddBox(basis.parent,"VariantSolidGalleryBacking",new Vector3(-9.25f,4.2f,0),new Vector3(.3f,8.4f,18),basis.GetComponent<Renderer>().sharedMaterial);}
  if(layout.composer=="Tchaikovsky"){
   string ceilingPath=Root+layout.composer+"/Materials/VariantCeiling.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(ceilingPath);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.5f,.49f,.46f));material.SetFloat("_Smoothness",.25f);AssetDatabase.CreateAsset(material,ceilingPath);}var parent=root.transform.Find("ComposerScenery");AddBox(parent,"VariantSalonCeiling",new Vector3(0,8.25f,0),new Vector3(21,.35f,21),material);
  }
  if(layout.composer=="Monteverdi"){
   var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);string skyPath=Root+layout.composer+"/Materials/Monteverdi_Sky_"+layout.version+".mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);if(!sky){sky=new Material(env.skybox);AssetDatabase.CreateAsset(sky,skyPath);}else sky.CopyPropertiesFromMaterial(env.skybox);sky.shader=Shader.Find("MusicGame/Art/ScenarioVariantBackdrop");env.skybox=sky;EditorUtility.SetDirty(sky);
  }
  if(layout.composer=="Haydn"){
   var basis=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Haydn_RearLintel");var material=basis.GetComponent<Renderer>().sharedMaterial;
   AddBox(basis.parent,"VariantSouthLintel",new Vector3(0,7.4f,-9.2f),new Vector3(18,2,.3f),material);
   AddBox(basis.parent,"VariantSouthBase",new Vector3(0,.2f,-9.2f),new Vector3(18,.4f,.3f),material);
   foreach(float x in new[]{-7f,0,7f})AddBox(basis.parent,"VariantSouthInfill",new Vector3(x,3.7f,-9.2f),new Vector3(x==0?3.6f:2.2f,7.4f,.3f),material);
  }
  if(layout.composer=="Chopin"){
   var basis=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Chopin_RearSupport");AddBox(basis.parent,"VariantSouthSupport",new Vector3(0,4,-9.8f),new Vector3(18,8,.3f),basis.GetComponent<Renderer>().sharedMaterial);
  }
  if(layout.composer=="Brahms"){
   foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="CurvedAudienceRisers").ToArray())Object.DestroyImmediate(t.gameObject);
   if(layout.version==3){var backing=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="CircularAuditoriumBacking");if(backing){var material=backing.GetComponent<Renderer>().sharedMaterial;var parent=backing.parent;Object.DestroyImmediate(backing.gameObject);
    foreach(float side in new[]{-1f,1f}){AddBox(parent,"VariantRectangularBacking",new Vector3(side*8.2f,4.65f,0),new Vector3(.3f,9.3f,16.7f),material);AddBox(parent,"VariantRectangularBacking",new Vector3(0,4.65f,side*8.2f),new Vector3(16.7f,9.3f,.3f),material);}
   }}
  }
  if(layout.composer=="Beethoven"){
   var rear=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Beethoven_RearWall");AddBox(rear.parent,"VariantRearBacking",new Vector3(0,4,12.2f),new Vector3(18,8,.3f),rear.GetComponent<Renderer>().sharedMaterial);
   float side=layout.version==2?1:-1;var turn=Quaternion.Euler(0,side*90,0);var pivot=new Vector3(0,0,7.8f);
   foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>new[]{"RearStairLanding","RecedingHallFloor","RecedingHall_Left","RecedingHall_Right"}.Contains(t.name)).ToArray()){
    t.position=turn*(t.position-pivot)+pivot+Vector3.right*side*4.4f;t.rotation=turn*t.rotation;
   }
   // The passage now exits through a side wall. Keep solid wall above and around it.
   var wall=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Beethoven_SideWall"&&t.position.x*side>0);
   if(wall){var material=wall.GetComponent<Renderer>().sharedMaterial;var parent=wall.parent;Object.DestroyImmediate(wall.gameObject);
    AddBox(parent,"VariantSideWallFront",new Vector3(side*9,4,-4.1f),new Vector3(.4f,8,17.8f),material);
    AddBox(parent,"VariantSideWallBack",new Vector3(side*9,4,14),new Vector3(.4f,8,2),material);
    AddBox(parent,"VariantPassageHeader",new Vector3(side*9,8,9),new Vector3(.4f,1,8.2f),material);
   }
   foreach(string name in layout.version==2?new[]{"ornate_wall_panel_34","theatre_balcony_module_35","wall_candle_sconce_36"}:new[]{"ornate_wall_panel_16","theatre_balcony_module_17","wall_candle_sconce_18"}){
    var t=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);if(t)Object.DestroyImmediate(t.gameObject);
   }
  }
  // Materials are independent: the reference floor and shared textures remain untouched.
  var renderer=root.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Floor");
  string dir=Root+layout.composer+"/Materials";string path=dir+"/"+layout.composer+"_Floor_"+layout.version.ToString("00")+".mat";
  var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(renderer.sharedMaterial);AssetDatabase.CreateAsset(mat,path);}else mat.CopyPropertiesFromMaterial(renderer.sharedMaterial);
  mat.shader=Shader.Find("MusicGame/Art/ScenarioVariantFloor");if(!mat.shader)throw new Exception("Missing variant floor shader");
  var f=layout.floor;mat.SetColor("_BaseColor",f.tint);mat.SetColor("_AlternateColor",f.alternate);mat.SetFloat("_Pattern",f.pattern);mat.SetFloat("_FloorAngle",f.angle*Mathf.Deg2Rad);mat.SetFloat("_TileSize",f.tileSize);mat.SetFloat("_Smoothness",f.smoothness);mat.SetFloat("_EnvironmentReflectionStrength",.24f);mat.SetTextureScale("_BaseMap",Vector2.one*f.tiling);mat.SetFloat("_BumpScale",.12f);renderer.sharedMaterial=mat;EditorUtility.SetDirty(mat);
  // Shift nearby practical lights with a moved candle/brazier when their source is identifiable.
  foreach(var e in layout.edits.Where(e=>!e.remove&&e.cloneFrom==null&&e.name.Contains("brazier"))){var lamp=root.GetComponentsInChildren<Light>(true).Where(l=>l.type==LightType.Point&&Vector3.Distance(l.transform.position,e.position)<2).FirstOrDefault();if(lamp)lamp.transform.position=new Vector3(e.position.x,1.2f,e.position.z);}
 }
 static void AddBox(Transform parent,string name,Vector3 position,Vector3 size,Material material){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.position=position;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(g.GetComponent<Collider>());}
 public static void AuditAll(){var rows=new List<string>{"composer,version,quality,props,triangles,clearanceIssues"};foreach(var c in JsonUtility.FromJson<Document>(File.ReadAllText(Config)).layouts.Select(l=>l.composer).Distinct())foreach(int version in new[]{2,3}){string signature=null;foreach(var q in new[]{"Low","Mid","HighMid","High","Ultra"}){
  var scene=EditorSceneManager.OpenScene(Root+c+"/Scenes/"+c+"_Arena_"+version.ToString("00")+"_"+q+".unity",OpenSceneMode.Single);var root=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true));
  if(root.GetComponentsInChildren<MonteverdiPropQuality>(true).Length>0||root.GetComponentsInChildren<LODGroup>(true).Length>0)throw new Exception("Runtime selectors "+c);
  var props=root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&&new[]{"North","South","East","West"}.Contains(t.parent.name)).ToArray();
  string shape=string.Join("\n",props.Select(t=>t.name+"|"+t.position.ToString("F3")+"|"+t.eulerAngles.ToString("F3")+"|"+t.localScale.ToString("F3")).OrderBy(s=>s));if(signature==null)signature=shape;else if(signature!=shape)throw new Exception("Placement drift "+c+version+q);
  int issues=0;foreach(var p in props){var rs=p.GetComponentsInChildren<MeshRenderer>(true);if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);if(b.min.y<2.5f&&b.min.x<4.5f&&b.max.x>-4.5f&&b.min.z<2.2f&&b.max.z>-2.2f){issues++;Debug.LogWarning("VARIANT_CLEARANCE "+c+" "+version+" "+q+" "+p.name+" "+b);}}
  foreach(var m in root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials))if(!m||!m.shader||m.shader.name.Contains("Error"))throw new Exception("Missing material "+c);
  var floor=root.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Floor").sharedMaterial;if(!AssetDatabase.GetAssetPath(floor).EndsWith("_Floor_"+version.ToString("00")+".mat"))throw new Exception("Shared base floor "+c);
  long triangles=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh).Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(long)f.sharedMesh.GetIndexCount(i)/3));rows.Add(c+","+version+","+q+","+props.Length+","+triangles+","+issues);
 }}Directory.CreateDirectory("Logs/ScenarioVariants");File.WriteAllLines("Logs/ScenarioVariants/validation.csv",rows);ArenaBundleValidation.Register();}
 public static void BuildOne(string composer)
 {
  if(composer=="Wagner"){WagnerAuthoringTools.BuildIndependentVariants();return;}
  var manifest=JsonUtility.FromJson<ComposerSceneryAssembly.Manifest>(File.ReadAllText("Assets/_Project/Configs/Editor/composer_scenery_manifest.json"));var spec=manifest.scenes.Single(s=>s.composer==composer);
  foreach(int version in new[]{2,3}){var layout=Get(composer,version);var source=Root+composer+"/"+composer+"_Arena_01.prefab";var target=Root+composer+"/"+composer+"_Arena_"+version.ToString("00")+".prefab";var root=PrefabUtility.LoadPrefabContents(source);try{Apply(root,layout);PrefabUtility.SaveAsPrefabAsset(root,target);}finally{PrefabUtility.UnloadPrefabContents(root);}typeof(ComposerSceneryAssembly).GetMethod("CompileScenes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{spec,version});}
  AssetDatabase.SaveAssets();
 }
 public static void Run(){try{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-variantComposer");BuildOne(args[i+1]);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
public static partial class WagnerAuthoringTools
{
 public static void BuildIndependentVariants()
 {
  foreach(int version in new[]{2,3})foreach(string quality in new[]{"High","Low","Mid","HighMid","Ultra"}){
   var scene=EditorSceneManager.OpenScene(Canonical,OpenSceneMode.Single);var arena=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true));ScenarioVariantBuilder.Apply(arena,ScenarioVariantBuilder.Get("Wagner",version));
   var bindings=Bindings(scene);foreach(var b in bindings){var selected=Select(b,Array.IndexOf(Tiers,quality));Retarget(b,selected);
    string dir=Generated+"/Variants/"+version+"/"+Key(b.path)+"/"+selected;Directory.CreateDirectory(dir);var parent=b.root.transform.parent;var pos=b.root.transform.localPosition;var rot=b.root.transform.localRotation;var scale=b.root.transform.localScale;var name=b.root.name;
    var prefab=PrefabUtility.SaveAsPrefabAsset(b.root,dir+"/Prop.prefab");var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);instance.name=name;instance.transform.SetParent(parent,false);instance.transform.SetLocalPositionAndRotation(pos,rot);instance.transform.localScale=scale;Object.DestroyImmediate(b.root);b.root=instance;b.tier=selected;
   }
   BeginWindows();ApplyWindows(scene,bindings,quality);
   foreach(var light in arena.GetComponentsInChildren<Light>(true).Where(l=>l.type==LightType.Point&&l.name.StartsWith("Reference_")))if(Array.IndexOf(Tiers,quality)<3)light.shadows=LightShadows.None;
   string output=Folder+"/Scenes/Wagner_Arena_"+version.ToString("00")+"_"+quality+".unity";EditorSceneManager.SaveScene(scene,output);Register(version,quality,output);if(quality=="High")PrefabUtility.SaveAsPrefabAsset(arena,Folder+"/Wagner_Arena_"+version.ToString("00")+".prefab");
  }AssetDatabase.SaveAssets();
 }
}
#endif
