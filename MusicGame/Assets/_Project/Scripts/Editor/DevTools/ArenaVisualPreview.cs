#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.SceneManagement;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using Object=UnityEngine.Object;
public static class ArenaVisualPreview
{
 const string W=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\arena_visual_refine";
 const string Shared="Assets/_Project/Art/Arenas/_Shared/BakedPresentation";
 [Serializable] public class Job{public string path,id,composer,quality;public int variant;public Vector3 position,target;public float fov;}
 [Serializable] class Queue{public Job[] jobs;}
 [Serializable] class Result{public string id,path,composer,quality,status,error;public int variant,lightmaps,probes,reflections,lightmapReceivers,probeReceivers;public double seconds;public bool ao;public Vector3 sunEuler;}
 static Queue queue;static int index,phase;static Job job;static GameObject root;static Camera cam;static ArenaEnvironmentSettings env;static Light sun;static ArenaCombatLook look;static double ready,started;static bool bakeEnded;static RenderPipelineAsset originalPipeline;static UniversalRenderPipelineAsset pipeline;static int lmReceivers,prReceivers;static int failures;static int processed;
 public static void Start(){try{EditorApplication.LockReloadAssemblies();Directory.CreateDirectory(W+"/results");Directory.CreateDirectory(W+"/captures");Directory.CreateDirectory(Shared);AssetDatabase.Refresh();foreach(string quality in new[]{"Low","Mid","HighMid","High","Ultra"}){var preset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Shared+"/ArenaAA_"+quality+".asset");if(preset)UseBakedAOOnly(preset);}queue=JsonUtility.FromJson<Queue>(File.ReadAllText(W+"/queue.json"));originalPipeline=QualitySettings.renderPipeline;if(AssetDatabase.GetAssetPath(originalPipeline).StartsWith(Shared+"/"))originalPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Quality/URP_"+QualitySettings.names[QualitySettings.GetQualityLevel()]+".asset");if(File.Exists(W+"/original_pipeline.guid"))originalPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(File.ReadAllText(W+"/original_pipeline.guid").Trim()));Lightmapping.bakeCompleted+=BakeCompleted;EditorApplication.update+=Tick;Next();}catch(Exception e){Fatal(e);}}
 static void BakeCompleted(){bakeEnded=true;}
 static void Status(string state){File.WriteAllText(W+"/status.json",JsonUtility.ToJson(new Result{id=job?.id,path=job?.path,composer=job?.composer,quality=job?.quality,variant=job?.variant??0,status=state,seconds=EditorApplication.timeSinceStartup-started},true));}
 static void Next(){
  if(cam)Object.DestroyImmediate(cam.gameObject);
  while(index<queue.jobs.Length&&File.Exists(W+"/results/"+queue.jobs[index].id+".json"))index++;
  if(index>=queue.jobs.Length){QualitySettings.renderPipeline=originalPipeline;Lightmapping.bakeCompleted-=BakeCompleted;EditorApplication.update-=Tick;if(failures==0)File.WriteAllText(W+"/complete.txt","Queue complete. Failures this run="+failures);EditorApplication.Exit(failures>0?2:0);return;}
  if(processed>=36){EditorApplication.update-=Tick;Lightmapping.bakeCompleted-=BakeCompleted;QualitySettings.renderPipeline=originalPipeline;AssetDatabase.SaveAssets();EditorApplication.Exit(0);return;}
  job=queue.jobs[index++];started=EditorApplication.timeSinceStartup;Status("opening");Backup(job.path);
  EditorSceneManager.OpenScene(job.path,OpenSceneMode.Single);root=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.GetComponentInChildren<ArenaEnvironmentSettings>(true));env=root.GetComponentInChildren<ArenaEnvironmentSettings>(true);sun=root.GetComponentInChildren<ArenaSceneLighting>(true).mainLight;
  QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Quality/URP_"+job.quality+".asset");
  cam=new GameObject("BakeReviewCamera").AddComponent<Camera>();cam.transform.position=job.position;cam.transform.LookAt(job.target);cam.fieldOfView=job.fov;cam.nearClipPlane=.05f;cam.farClipPlane=250;cam.allowHDR=true;cam.allowMSAA=true;cam.clearFlags=CameraClearFlags.Skybox;cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
  env.Apply(sun,cam);look=root.GetComponentInChildren<ArenaCombatLook>(true);if(look){look.AttachCamera(cam);look.SetTreatment(true);}FreezeParticles();phase=0;ready=EditorApplication.timeSinceStartup+2;
 }
 static void Backup(string p){string b=W+"/backup/"+p;if(File.Exists(b))return;Directory.CreateDirectory(Path.GetDirectoryName(b));File.Copy(p,b);}
 static void Tick(){try{
  EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup<ready||ShaderUtil.anythingCompiling)return;
  if(phase==0){
   var presentation=root.GetComponent<ArenaBakedPresentation>();if(presentation&&presentation.pipeline)QualitySettings.renderPipeline=presentation.pipeline;
   foreach(var shader in root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Select(m=>m.shader).Distinct())if(ShaderUtil.ShaderHasError(shader))throw new Exception("Shader error: "+shader.name);
   Capture("shader_fixed");File.WriteAllText(W+"/results/"+job.id+".json","{\"status\":\"complete\"}");processed++;Next();
  }

 }catch(Exception e){failures++;processed=6;Debug.LogException(e);if(job!=null){File.WriteAllText(W+"/results/"+job.id+".json",JsonUtility.ToJson(new Result{id=job.id,path=job.path,composer=job.composer,variant=job.variant,quality=job.quality,status="failed",error=e.ToString()},true));try{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);File.Copy(W+"/backup/"+job.path,job.path,true);AssetDatabase.ImportAsset(job.path);}catch(Exception restore){Debug.LogException(restore);}Next();}else Fatal(e);}}
 static void Prepare(){
  string folder=Path.GetDirectoryName(job.path).Replace('\\','/')+"/../BakedPresentation";folder="Assets/_Project/Art/Arenas/"+job.composer+"/BakedPresentation/"+job.id;Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  if(job.composer=="Monteverdi"){
   var sky=env.skybox;float az=39.190271f+85.809729f; // image centroid's unrotated azimuth + selected rotation below
   az=sky.GetFloat("_Rotation")+85.809729f;float el=(.09803712f+sky.GetFloat("_VerticalOffset"))*180f;
   env.mainLightEuler=new Vector3(el,Mathf.Repeat(az+180,360),0);sun.transform.rotation=Quaternion.Euler(env.mainLightEuler);
   var grounding=root.GetComponentInChildren<ArenaCharacterGrounding>(true);if(grounding&&grounding.shadowKey){var key=grounding.shadowKey;key.transform.position=-sun.transform.forward*80;key.transform.LookAt(Vector3.zero);}
  }
  env.Apply(sun,cam);EditorUtility.SetDirty(env);
  string pp=Shared+"/ArenaAA_"+job.quality+".asset";pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pp);if(!pipeline){pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Quality/URP_"+job.quality+".asset"));AssetDatabase.CreateAsset(pipeline,pp);}
  UseBakedAOOnly(pipeline);pipeline.msaaSampleCount=job.quality=="Low"?1:job.quality=="Mid"?2:4;pipeline.renderScale=job.quality=="Low"?.7f:job.quality=="Mid"?.85f:1f;EditorUtility.SetDirty(pipeline);QualitySettings.renderPipeline=pipeline;
  var presentation=root.GetComponent<ArenaBakedPresentation>();if(!presentation)presentation=root.AddComponent<ArenaBakedPresentation>();presentation.pipeline=pipeline;
  look=root.GetComponentInChildren<ArenaCombatLook>(true);if(!look){var node=new GameObject("BakedCombatPresentation");node.transform.SetParent(root.transform,false);look=node.AddComponent<ArenaCombatLook>();var volume=node.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Art/Arenas/Monteverdi/LightingPilot/CombatLook/MonteverdiLook_"+job.quality+".asset");if(!volume.sharedProfile)throw new Exception("Missing presentation profile");look.volume=volume;var lights=new List<Light>();foreach(int i in new[]{0,1}){var go=new GameObject(i==0?"CharacterKey":"CharacterRim");go.transform.SetParent(node.transform,false);go.transform.position=i==0?new Vector3(-2.8f,4,-3.6f):new Vector3(2.8f,3.8f,2.2f);go.transform.LookAt(new Vector3(0,1,0));var l=go.AddComponent<Light>();l.type=LightType.Spot;l.range=12;l.spotAngle=75;l.innerSpotAngle=50;l.intensity=i==0?22:18;l.shadows=LightShadows.None;l.GetUniversalAdditionalLightData().renderingLayers=ArenaCombatLook.CharacterMask;lights.Add(l);}look.characterLights=lights.ToArray();}
  look.AttachCamera(cam);look.SetTreatment(true);
  lmReceivers=prReceivers=0;
  foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true)){
   var filter=renderer.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;var mesh=filter.sharedMesh;
   if(renderer.sharedMaterials.Any(m=>m&&m.renderQueue>=3000)){renderer.receiveGI=ReceiveGI.LightProbes;renderer.lightProbeUsage=LightProbeUsage.BlendProbes;continue;}
   GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)|StaticEditorFlags.ContributeGI);
   renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
   if(renderer.name=="Floor"){
    string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh))+"_"+mesh.name.Replace('/','_');string mp=Shared+"/Floor_"+key+".asset";var cached=AssetDatabase.LoadAssetAtPath<Mesh>(mp);
    if(!mesh.HasVertexAttribute(VertexAttribute.TexCoord1)){if(!cached){cached=Object.Instantiate(mesh);using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh)){using(var uv=new Unity.Collections.NativeArray<Vector2>(mesh.vertexCount,Unity.Collections.Allocator.Temp)){data[0].GetUVs(0,uv);cached.uv2=uv.ToArray();}}AssetDatabase.CreateAsset(cached,mp);}filter.sharedMesh=cached;}
    renderer.receiveGI=ReceiveGI.Lightmaps;renderer.scaleInLightmap=1;lmReceivers++;
   }else if(mesh.name=="Cube"&&mesh.HasVertexAttribute(VertexAttribute.TexCoord1)){renderer.receiveGI=ReceiveGI.Lightmaps;renderer.scaleInLightmap=.35f;lmReceivers++;}
   else{renderer.receiveGI=ReceiveGI.LightProbes;prReceivers++;}
  }
  var existing=root.GetComponentsInChildren<LightProbeGroup>(true);LightProbeGroup group;if(existing.Length>0)group=existing[0];else{var go=new GameObject("BakedArenaLightProbes");go.transform.SetParent(root.transform,false);group=go.AddComponent<LightProbeGroup>();}
  var positions=new List<Vector3>();foreach(float x in new[]{-7.5f,-5,-2.5f,0,2.5f,5,7.5f})foreach(float z in new[]{-7f,-3.5f,0,3.5f,7,10.5f})foreach(float y in new[]{.5f,1.5f,3,5.5f,7.5f})positions.Add(root.transform.InverseTransformPoint(new Vector3(x,y,z)));foreach(float x in new[]{-4f,-2,0,2,4})foreach(float z in new[]{-2f,0,2})foreach(float y in new[]{.4f,1.3f,2.2f})positions.Add(root.transform.InverseTransformPoint(new Vector3(x,y,z)));group.probePositions=positions.ToArray();
  foreach(var l in root.GetComponentsInChildren<Light>(true)){if(look.characterLights.Contains(l)||l.name=="CharacterGroundShadowKey"){l.lightmapBakeType=LightmapBakeType.Realtime;continue;}l.lightmapBakeType=l==sun?LightmapBakeType.Mixed:LightmapBakeType.Baked;}
  var settings=new LightingSettings{name=job.id+"_Lighting",bakedGI=true,realtimeGI=false,lightmapper=File.Exists(W+"/cpu/"+job.id+".flag")?LightingSettings.Lightmapper.ProgressiveCPU:LightingSettings.Lightmapper.ProgressiveGPU,lightmapResolution=job.quality=="Low"?10:job.quality=="Mid"?12:16,lightmapMaxSize=1024,directionalityMode=LightmapsMode.NonDirectional,mixedBakeMode=MixedLightingMode.IndirectOnly,directSampleCount=64,indirectSampleCount=256,environmentSampleCount=128,maxBounces=2,ao=true,aoMaxDistance=1.2f};
  string sp=folder+"/"+job.id+"_LightingSettings.asset";var prior=AssetDatabase.LoadAssetAtPath<LightingSettings>(sp);if(prior){EditorUtility.CopySerialized(settings,prior);Object.DestroyImmediate(settings);settings=prior;}else AssetDatabase.CreateAsset(settings,sp);Lightmapping.lightingSettings=settings;
  EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());SaveWithoutCamera();AssetDatabase.SaveAssets();FreezeParticles();Status("prepared");
 }
 public static void UseBakedAOOnly(UniversalRenderPipelineAsset profile){var data=new SerializedObject(profile);var list=data.FindProperty("m_RendererDataList");for(int i=0;i<list.arraySize;i++){var source=list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;if(!source||!source.rendererFeatures.Any(f=>f&&f.GetType().Name.Contains("AmbientOcclusion")))continue;string path=Shared+"/BakedRenderer_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+".asset";var copy=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);if(!copy){copy=Object.Instantiate(source);AssetDatabase.CreateAsset(copy,path);}var serialized=new SerializedObject(copy);var features=serialized.FindProperty("m_RendererFeatures");var map=serialized.FindProperty("m_RendererFeatureMap");for(int n=features.arraySize-1;n>=0;n--){var feature=features.GetArrayElementAtIndex(n).objectReferenceValue as ScriptableRendererFeature;if(!feature||!feature.GetType().Name.Contains("AmbientOcclusion"))continue;features.GetArrayElementAtIndex(n).objectReferenceValue=null;features.DeleteArrayElementAtIndex(n);if(map!=null&&n<map.arraySize)map.DeleteArrayElementAtIndex(n);}serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(copy);list.GetArrayElementAtIndex(i).objectReferenceValue=copy;}data.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(profile);}
 static void FreezeParticles(){foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true)){p.Simulate(1.2f,true,true);p.Pause(true);}}
 static void FinishBake(){
  Status("reflection");foreach(var probe in root.GetComponentsInChildren<ReflectionProbe>(true)){probe.mode=ReflectionProbeMode.Baked;probe.resolution=128;string rp="Assets/_Project/Art/Arenas/"+job.composer+"/BakedPresentation/"+job.id+"/"+job.id+"_Reflection_"+probe.GetInstanceID()+".exr";if(!Lightmapping.BakeReflectionProbe(probe,rp))throw new Exception("Reflection probe bake failed");AssetDatabase.ImportAsset(rp,ImportAssetOptions.ForceSynchronousImport);var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(rp);if(!cube)throw new Exception("Reflection cubemap missing");probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=cube;}
  SaveWithoutCamera();AssetDatabase.SaveAssets();Status("capturing");
 }
 static void SaveWithoutCamera(){var flags=cam.gameObject.hideFlags;cam.gameObject.hideFlags=HideFlags.DontSaveInEditor;EditorSceneManager.SaveScene(SceneManager.GetActiveScene());cam.gameObject.hideFlags=flags;}
 static void Capture(string suffix){const int w=1280,h=720;cam.aspect=(float)w/h;var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=(QualitySettings.renderPipeline as UniversalRenderPipelineAsset)?.msaaSampleCount??1};rt.Create();cam.targetTexture=rt;for(int i=0;i<3;i++)cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(W+"/captures/"+job.id+"_"+suffix+".png",t.EncodeToPNG());RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(t);}
 static void Fatal(Exception e){Directory.CreateDirectory(W);File.WriteAllText(W+"/fatal.txt",e.ToString());Debug.LogException(e);QualitySettings.renderPipeline=originalPipeline;EditorApplication.Exit(1);}
}
#endif
