#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using Object=UnityEngine.Object;
public static class ArenaRefinedFinalReview
{
 const string W=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\arena_visual_refine\bakes";
 [Serializable] class Queue{public ArenaBakeAll.Job[] jobs;}
 static Queue queue;static int index;static Camera camera;static ArenaBakeAll.Job job;static double ready;static RenderPipelineAsset original;static List<string> report=new();
 public static void Start(){try{EditorApplication.LockReloadAssemblies();original=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(File.ReadAllText(W+"/original_pipeline.guid").Trim()));queue=JsonUtility.FromJson<Queue>(File.ReadAllText(W+"/queue.json"));foreach(string quality in new[]{"Low","Mid","HighMid","High","Ultra"}){var preset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/_Project/Art/Arenas/_Shared/BakedPresentation/ArenaAA_"+quality+".asset");if(preset){preset.renderScale=quality=="Low"?.7f:quality=="Mid"?.85f:1; preset.msaaSampleCount=quality=="Low"?1:quality=="Mid"?2:4;ArenaBakeAll.UseBakedAOOnly(preset);EditorUtility.SetDirty(preset);}}OrganizeBakeResources();ArenaBundleValidation.Register();EditorApplication.update+=Tick;OpenNext();}catch(Exception e){Fail(e);}}
 static void OrganizeBakeResources(){foreach(var j in queue.jobs){string folder="Assets/_Project/Art/Arenas/"+j.composer+"/BakedPresentation";string dest=folder+"/"+j.id;if(!Directory.Exists(dest))Directory.CreateDirectory(dest);AssetDatabase.Refresh();foreach(string path in Directory.GetFiles(folder,j.id+"_*")){if(path.EndsWith(".meta"))continue;string from=path.Replace('\\','/');string target=dest+"/"+Path.GetFileName(path);if(File.Exists(target))target=dest+"/"+Path.GetFileNameWithoutExtension(path)+"_legacy_"+AssetDatabase.AssetPathToGUID(from).Substring(0,8)+Path.GetExtension(path);string error=AssetDatabase.MoveAsset(from,target);if(!string.IsNullOrEmpty(error))throw new Exception(error);}}AssetDatabase.SaveAssets();}
 static void OpenNext(){
  if(camera)Object.DestroyImmediate(camera.gameObject);camera=null;
  if(index>=queue.jobs.Length){QualitySettings.renderPipeline=original;AssetDatabase.SaveAssets();EditorApplication.update-=Tick;File.WriteAllLines(W+"/native_verification.txt",report);EditorApplication.Exit(0);return;}
  job=queue.jobs[index++];File.WriteAllText(W+"/review_status.txt",index+"/"+queue.jobs.Length+" "+job.id);EditorSceneManager.OpenScene(job.path,OpenSceneMode.Single);
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true));var env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);var presentation=root.GetComponent<ArenaBakedPresentation>();var look=root.GetComponentInChildren<ArenaCombatLook>(true);
  if(!presentation||!presentation.pipeline||!look||!look.volume||!look.volume.sharedProfile)throw new Exception("Presentation missing "+job.id);
  if(job.composer!="Monteverdi"){var contact=look.GetComponent<ArenaCharacterGrounding>();if(!contact)contact=look.gameObject.AddComponent<ArenaCharacterGrounding>();contact.look=look;contact.contactOnly=true;contact.lowContactMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Arenas/Monteverdi/LightingPilot/CombatShadows/LowContact.mat");if(!contact.lowContactMaterial)throw new Exception("Contact shadow material missing");EditorUtility.SetDirty(contact);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
  if(!Lightmapping.lightingDataAsset||!Lightmapping.lightingSettings||!Lightmapping.lightingSettings.ao)throw new Exception("Bake or AO missing "+job.id);
  int maps=LightmapSettings.lightmaps.Length;int probes=LightmapSettings.lightProbes?LightmapSettings.lightProbes.count:0;
  if(maps==0||probes<255)throw new Exception("GI missing "+job.id);
  bool adjusted=false;foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.receiveGI==ReceiveGI.Lightmaps)){if(r.lightmapIndex>=0&&r.lightmapIndex<maps)continue;var filter=r.GetComponent<MeshFilter>();var size=r.bounds.size;bool cube=filter&&filter.sharedMesh&&(filter.sharedMesh.name=="Cube"||(filter.sharedMesh.name.StartsWith("Box_")||filter.sharedMesh.name.StartsWith("WindowWall_"))||filter.sharedMesh.name.StartsWith("RefinedBox_"));bool opaque=r.sharedMaterials.All(m=>!m||m.renderQueue<3000);if(r.name=="Floor"||(cube&&opaque&&(size.x*size.y+size.y*size.z+size.z*size.x)>1))throw new Exception("Invalid architectural lightmap binding "+job.id+" / "+r.name+" = "+r.lightmapIndex);r.receiveGI=ReceiveGI.LightProbes;r.lightProbeUsage=LightProbeUsage.BlendProbes;EditorUtility.SetDirty(r);if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);adjusted=true;report.Add("PROBES "+job.id+" / "+r.name+" | inherited unused lightmap setting replaced by baked probes");}if(adjusted){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}


  foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true)){
   var filter=renderer.GetComponent<MeshFilter>();
   if(filter&&filter.sharedMesh&&ArenaVisualAuthoring.IsArchitecturalBox(filter.sharedMesh)&&filter.sharedMesh.name!="Cube"){
    if((filter.transform.localScale-Vector3.one).sqrMagnitude>.000001f)throw new Exception("Box scale not applied: "+job.id+" / "+filter.name);
    if(filter.sharedMesh.uv2.Length!=filter.sharedMesh.vertexCount)throw new Exception("Box lightmap UV missing: "+job.id+" / "+filter.name);
    if(filter.sharedMesh.uv.Length!=filter.sharedMesh.vertexCount)throw new Exception("Box texture UV missing: "+job.id+" / "+filter.name);
   }
   foreach(var material in renderer.sharedMaterials.Where(m=>m)){
    if(!material.shader||ShaderUtil.ShaderHasError(material.shader))throw new Exception("Shader invalid: "+job.id+" / "+material.name);
    if(material.shader.name=="MusicGame/Art/ScenarioVariantFloor"){
     var text=File.ReadAllText(AssetDatabase.GetAssetPath(material.shader));
     if(!text.Contains("SAMPLE_GI(input.lightmapUV")||!text.Contains("VariantMetaFragment"))throw new Exception("Variant floor not consuming/exporting GI: "+job.id);
    }
    if(renderer.receiveGI==ReceiveGI.Lightmaps&&material.shader.name.StartsWith("MusicGame/Art/")){
     var source=File.ReadAllText(AssetDatabase.GetAssetPath(material.shader));
     if(!source.Contains("SAMPLE_GI(")&&!source.Contains("SampleLightmap("))throw new Exception("Lightmapped custom shader ignores GI: "+job.id+" / "+material.shader.name);
    }
   }
  }
  int accentCount=root.GetComponentsInChildren<Light>(true).Count(l=>l.name.StartsWith("SpecularAccent_")&&l.lightmapBakeType!=LightmapBakeType.Baked);
  if(accentCount>1||(job.quality=="Low"&&accentCount!=0))throw new Exception("Accent light budget exceeded: "+job.id);
  LightProbes.Tetrahedralize();
  if(job.composer=="Bach")ArenaVisualAuthoring.AlignExistingBeams(root);
  if(job.composer=="Wagner")ArenaWagnerBakeWalls.Verify(root);
  if(job.composer=="Beethoven"&&!root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ReviewedFrontWallBacking"))throw new Exception("Rear-view wall completion missing: "+job.id);
  if(job.composer=="Chopin"&&job.variant==1&&!root.transform.Find("ReviewedSalonInfill"))throw new Exception("Window surround completion missing: "+job.id);
  long triangles=0;foreach(var filter in root.GetComponentsInChildren<MeshFilter>(false)){if(!filter.sharedMesh)continue;for(int part=0;part<filter.sharedMesh.subMeshCount;part++)if(filter.sharedMesh.GetTopology(part)==MeshTopology.Triangles)triangles+=(long)filter.sharedMesh.GetIndexCount(part)/3;}
  int runtimeLights=root.GetComponentsInChildren<Light>(true).Count(l=>l.enabled&&l.gameObject.activeInHierarchy&&l.intensity>0&&l.lightmapBakeType!=LightmapBakeType.Baked);
  long mapBytes=LightmapSettings.lightmaps.Where(l=>l.lightmapColor).Sum(l=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(l.lightmapColor));
  string metrics=W+"/refined_scene_metrics.csv";if(index==1)File.WriteAllText(metrics,"id,triangles,runtimeLights,lightmaps,editorLightmapBytes\n");File.AppendAllText(metrics,job.id+","+triangles+","+runtimeLights+","+maps+","+mapBytes+"\n");

  foreach(var probe in root.GetComponentsInChildren<ReflectionProbe>(true))if(probe.mode!=ReflectionProbeMode.Custom||!probe.customBakedTexture)throw new Exception("Reflection missing "+job.id);
  var registry=UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
  foreach(string dep in AssetDatabase.GetDependencies(job.path,true).Where(d=>d.StartsWith("Assets/_Project/Art/Arenas/")&&(d.Contains("/BakedPresentation/")||d.Contains("/VisualRefinement/")||d.Contains("/Scenes/"))&&!d.EndsWith(".unity")))if(registry.FindAssetEntry(AssetDatabase.AssetPathToGUID(dep))==null)throw new Exception("Unregistered baked dependency "+dep);
  float wanted=job.quality=="Low"?.7f:job.quality=="Mid"?.85f:1;
  if(Mathf.Abs(presentation.pipeline.renderScale-wanted)>.001f)throw new Exception("Render scale mismatch "+job.id);
  var pipelineData=new SerializedObject(presentation.pipeline);var renderers=pipelineData.FindProperty("m_RendererDataList");for(int n=0;n<renderers.arraySize;n++){var renderer=renderers.GetArrayElementAtIndex(n).objectReferenceValue as ScriptableRendererData;if(renderer&&renderer.rendererFeatures.Any(f=>f&&f.isActive&&f.GetType().Name.Contains("AmbientOcclusion")))throw new Exception("Realtime AO still active "+job.id);}
  if(look.characterLights.Any(l=>l&&l.lightmapBakeType!=LightmapBakeType.Realtime))throw new Exception("Character fill was baked "+job.id);
  report.Add("PASS "+job.id+" | maps="+maps+" probes="+probes+" | lightmap bindings + shader GI consumption + Meta, box UV0/UV1 + applied scale, accent budget, baked AO, reflection, AA and dependencies OK");

  QualitySettings.renderPipeline=presentation.pipeline;camera=new GameObject("FinalReviewCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.Skybox;camera.transform.position=job.position;camera.transform.LookAt(job.target);camera.fieldOfView=job.fov;camera.nearClipPlane=.05f;camera.farClipPlane=250;camera.allowHDR=true;camera.allowMSAA=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;env.Apply(root.GetComponentInChildren<ArenaSceneLighting>(true).mainLight,camera);look.AttachCamera(camera);look.SetTreatment(true);
  foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.Simulate(1.2f,true,true);ps.Pause(true);}ready=EditorApplication.timeSinceStartup+.5;
 }
 static void Tick(){try{EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup<ready||ShaderUtil.anythingCompiling)return;if(camera){Capture();if(job.quality=="High"){var position=camera.transform.position;var rotation=camera.transform.rotation;camera.transform.position=new Vector3(-3.8f,2,-1.7f);camera.transform.LookAt(new Vector3(4,3,7));Capture("after_side");camera.transform.position=job.composer=="Haydn"||job.composer=="Pachelbel"?new Vector3(0,2,1.8f):new Vector3(0,2,7);camera.transform.LookAt(new Vector3(0,3,-6));Capture("after_rear");camera.transform.SetPositionAndRotation(position,rotation);}}OpenNext();}catch(Exception e){Fail(e);}}
 static void Capture(string suffix="after"){const int w=1280,h=720;camera.aspect=(float)w/h;var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=(QualitySettings.renderPipeline as UniversalRenderPipelineAsset).msaaSampleCount};rt.Create();camera.targetTexture=rt;for(int i=0;i<3;i++)camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(W+"/captures/"+job.id+"_"+suffix+".png",t.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(t);}
 static void Fail(Exception e){File.WriteAllText(W+"/native_verification_error.txt",e.ToString());Debug.LogException(e);EditorApplication.update-=Tick;if(original)QualitySettings.renderPipeline=original;AssetDatabase.SaveAssets();EditorApplication.Exit(1);}
}
#endif
