using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Actual Unity shader validation, runtime sharing tests and deterministic studio captures.</summary>
[InitializeOnLoad]
public static class AvatarSkinToneReview
{
    const string Generated="Assets/_Project/Avatar/MakeHuman/Generated/";
    const string MaterialPath=Generated+"Materials/MakeHuman_Skin_v7.mat";
    const string PrefabPath=Generated+"MakeHumanAvatar_SkinV7.prefab";
    const string ScenePath="Assets/_Project/Scenes/Debug/AvatarSkinToneLab.unity";
    static readonly string Output=Path.GetFullPath("../VisualReviews/Skin_2026-11-13/UnityToneV7");
    static readonly List<string> Checks=new();
    static Camera camera;static GameObject avatar;static AvatarSkinToneController controller;
    static Vector3 up,side,front,center,face,eye;static float height,width;
    static double nextPoll;
    static bool running;
    static bool nailsOnly;
    static int captureCount;
    public static void RunNailsOnly(){nailsOnly=true;Run();}
    static Scene reviewScene;
    const string ReviewRevision="tone-review-13";
    static AvatarSkinToneReview()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"ready_revision.txt"),ReviewRevision);
        EditorApplication.update+=PollRequest;
    }
    static void PollRequest()
    {
        if(running||EditorApplication.timeSinceStartup<nextPoll)return;
        nextPoll=EditorApplication.timeSinceStartup+1;
        string request=Path.Combine(Output,"run.request");
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(File.ReadAllText(request).Trim()!=ReviewRevision)return;
        File.Delete(request);Run();
    }

    [MenuItem("Tools/MusicGame/Avatars/Surface/Build And Review Skin Tone Lab")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);int exit=0;running=true;Checks.Clear();captureCount=0;
        if(File.Exists(Path.Combine(Output,"failure.txt")))File.Delete(Path.Combine(Output,"failure.txt"));
        bool previousAsync=ShaderUtil.allowAsyncCompilation;
        var originalScene=SceneManager.GetActiveScene();bool originalDirty=originalScene.isDirty;int originalRoots=originalScene.rootCount;
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AvatarSkinShaderSetup.CalibrateIrisTextures();AvatarSkinShaderSetup.Regenerate();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(AvatarSkinShaderSetup.ShaderPath);
            Check(shader!=null,"Unified skin shader imported");
            ShaderUtil.allowAsyncCompilation=false;
            CreateMaterialAndPrefab(shader);
            reviewScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
            SceneManager.SetActiveScene(reviewScene);
            avatar=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),reviewScene);
            controller=avatar.GetComponent<AvatarSkinToneController>();
            ValidateRecipeIntegration();
            Measure();SetupStudio();
            Check(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Graphics device available: "+SystemInfo.graphicsDeviceType);
            var material=avatar.GetComponentsInChildren<Renderer>(true).First(r=>r.sharedMaterial!=null).sharedMaterial;
            var baseMap=material.GetTexture("_BaseMap");var textures=Resources.LoadAll<Texture2D>("Avatar/Iris");
            Check(textures.Length>=4,"Four source iris textures available");
            var settings=AuthoredIris(textures,0);
            AvatarIrisAppearance.Apply(avatar,null,settings);
            CheckShared("After runtime iris setup");
            var runtime=avatar.GetComponentsInChildren<Renderer>(true).First(r=>r.sharedMaterial!=null).sharedMaterial;
            Check(runtime.GetTexture("_BaseMap")==baseMap,"Runtime iris reuses shared albedo; no baked atlas copy");
            AvatarIrisAppearance.Apply(avatar,null,settings);
            Check(avatar.GetComponentsInChildren<Renderer>(true).First(r=>r.sharedMaterial!=null).sharedMaterial==runtime,"Repeated iris setup reuses the same owned material");
            var block=new MaterialPropertyBlock();var renderer=avatar.GetComponentsInChildren<Renderer>(true).First();
            renderer.GetPropertyBlock(block);block.SetFloat("_ReviewSentinel",.731f);renderer.SetPropertyBlock(block);
            controller.SetTone(.71f);renderer.GetPropertyBlock(block);Check(Mathf.Abs(block.GetFloat("_ReviewSentinel")-.731f)<.0001f,"Tone control preserves existing property-block fields");
            CheckShared("After tone slider update");
            foreach(var renderer2 in avatar.GetComponentsInChildren<Renderer>(true))
            {renderer2.GetPropertyBlock(block);Check(Mathf.Abs(block.GetFloat("_SkinTone")-.71f)<.0001f,renderer2.name+" receives same tone");}
            var sibling=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),reviewScene);
            sibling.SetActive(false);var siblingRenderer=sibling.GetComponentInChildren<Renderer>(true);
            siblingRenderer.GetPropertyBlock(block);Check(Mathf.Abs(block.GetFloat("_SkinTone")-.71f)>.01f,"Changing one avatar does not tint another");Object.DestroyImmediate(sibling);
            if(!nailsOnly)
            {
            for(int index=0;index<13;index++)
            {
                controller.SetUndertone(0);controller.SetTone(index/12f);
                Capture("tone_"+index.ToString("00")+"_head","head");
                Capture("tone_"+index.ToString("00")+"_front","front");
                Capture("tone_"+index.ToString("00")+"_back","back");
                Debug.Log("[SkinToneReview] Captured tone "+index+"/12");
            }
            controller.SetTone(.5f);
            foreach(int value in new[]{-1,0,1}){controller.SetUndertone(value);Capture("undertone_"+(value+1),"head");}
            controller.SetUndertone(0);controller.SetTone(1f/3f);
            for(int index=0;index<textures.Length;index++)
            {
                AvatarIrisAppearance.Apply(avatar,null,AuthoredIris(textures,index));
                CheckShared("Iris variant "+index);Capture("iris_"+index,"eye");
            }
            AvatarIrisAppearance.Apply(avatar,null,settings);
            var clone=avatar.GetComponentsInChildren<Renderer>(true).First(r=>r.sharedMaterial!=null).sharedMaterial;
            clone.SetFloat("_SkinSoftness",0);Capture("lighting_pbr","head");
            clone.SetFloat("_SkinSoftness",.25f);Capture("lighting_skin","head");
            }
            Check(!ShaderUtil.ShaderHasError(shader),"Shader has no compilation errors after all variants rendered");
            foreach(var message in ShaderUtil.GetShaderMessages(shader))Debug.Log("[SkinToneReview] Shader: "+message.message);
            Object.DestroyImmediate(avatar);
            avatar=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),reviewScene);
            controller=avatar.GetComponent<AvatarSkinToneController>();controller.SetTone(1f/3f);controller.SetUndertone(0);
            controller.UseNaturalNails();
            Capture("nails_natural_hand","nailhand");Capture("nails_natural_right_hand","nailhandright");
            Capture("nails_natural_thumb","nailthumb");Capture("nails_natural_right_thumb","nailthumbright");
            controller.PaintAll(new Color(.65f,.015f,.025f,1));
            Capture("nails_hand","nailhand");Capture("nails_foot","nailfoot");
            for(int i=0;i<20;i++)controller.SetNailColor(i,Color.HSVToRGB(i/20f,.85f,.75f));
            Capture("nails_individual_hand","nailhand");Capture("nails_individual_foot","nailfoot");
            Capture("nails_right_hand","nailhandright");Capture("nails_right_foot","nailfootright");
            Capture("nails_individual_thumb","nailthumb");Capture("nails_individual_right_thumb","nailthumbright");
            controller.PaintAll(Color.clear);
            AddSliders();Frame("front");camera.targetTexture=null;camera.aspect=16f/9;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Output,nailsOnly?"nails_validation.txt":"validation.txt"),string.Join("\n",Checks));
            File.WriteAllText(Path.Combine(Output,nailsOnly?"nails_complete.json":"complete.json"),JsonUtility.ToJson(new ReviewSummary {success=true,revision=ReviewRevision,tones=nailsOnly?0:13,viewsPerTone=nailsOnly?0:3,sharedMaterial=true,captures=captureCount}));
            Debug.Log("[SkinToneReview] COMPLETE "+Output);
        }
        catch(Exception e)
        {
            exit=1;var shader=AssetDatabase.LoadAssetAtPath<Shader>(AvatarSkinShaderSetup.ShaderPath);
            string messages=shader==null?"":string.Join("\n",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message));
            File.WriteAllText(Path.Combine(Output,"failure.txt"),e+"\n"+messages+"\n"+string.Join("\n",Checks));Debug.LogException(e);
        }
        finally
        {
            if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
            if(reviewScene.IsValid()&&reviewScene.isLoaded)EditorSceneManager.CloseScene(reviewScene,true);
            ShaderUtil.allowAsyncCompilation=previousAsync;
            if(originalScene.IsValid())File.WriteAllText(Path.Combine(Output,"scene_preservation.json"),
                "{\"rootCountUnchanged\":"+(originalScene.rootCount==originalRoots?"true":"false")+",\"dirtyFlagUnchanged\":"+(originalScene.isDirty==originalDirty?"true":"false")+"}");
            running=false;nailsOnly=false;
        }
        if(Application.isBatchMode)EditorApplication.Exit(exit);
    }
    public static void ValidateCurrentAssets()
    {
        int exit=0;GameObject root=null;AvatarInstance instance=null;AvatarRecipeSO recipe=null;
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null || material.shader.name!=AvatarSkinShaderSetup.ShaderName)throw new Exception("Current material/shader missing");
            string[] properties={"_BaseMap","_BumpMap","_MetallicGlossMap","_OcclusionMap","_SkinRegionMap"};
            foreach(var property in properties)if(material.GetTexture(property)==null)throw new Exception("Missing skin texture: "+property);
            var basis=AssetDatabase.LoadAssetAtPath<BaseAvatarDefinitionSO>("Assets/_Project/Avatar/MakeHuman/Content/BaseAvatar_MakeHuman.asset");
            if(basis==null || basis.skinMaterial!=material)throw new Exception("Production base material mismatch");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(basis.baseAvatarPrefab.AssetGUID));
            if(prefab==null)throw new Exception("Production prefab missing");
            foreach(var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(renderer.sharedMaterial!=material)throw new Exception("Production prefab retains old skin material");
            root=Object.Instantiate(prefab);recipe=ScriptableObject.CreateInstance<AvatarRecipeSO>();recipe.baseAvatar=basis;
            var runtime=recipe.ToRuntime();instance=new AvatarInstance{Recipe=runtime,FinalIdentity=runtime.Identity};
            AvatarFactory.AssembleBody(instance,root,runtime);
            instance.SetSkinTone(.7f);instance.SkinToneController.PaintGroup(0,Color.red);
            var skin=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if(skin.Length<2 || skin.Any(r=>r.sharedMaterial!=skin[0].sharedMaterial))throw new Exception("Body/eyes no longer share material");
            var dependencies=AssetDatabase.GetDependencies(new[]{MaterialPath,PrefabPath,AssetDatabase.GUIDToAssetPath(basis.baseAvatarPrefab.AssetGUID)},true);
            if(dependencies.Any(p=>p.EndsWith("skinTest_NailRegions.png")))throw new Exception("Editor-only nail source still included in runtime dependencies");
            if(material.HasProperty("_NailRegionMap"))throw new Exception("Shader still exposes runtime nail mask");
            if(Resources.LoadAll<Texture2D>("Avatar/Iris").Length<4)throw new Exception("Iris sources missing");
            Debug.Log("[SkinCleanup] PASS: production prefab, current material, five texture slots; precalculated nails without runtime mask, shared body/eyes, live skin tone, nail painting and iris sources.");
        }
        catch(Exception error){exit=1;Debug.LogException(error);}
        finally {instance?.Dispose();if(root!=null)Object.DestroyImmediate(root);if(recipe!=null)Object.DestroyImmediate(recipe);}
        if(Application.isBatchMode)EditorApplication.Exit(exit);
    }

    static void Check(bool condition,string label)
    {Checks.Add((condition?"PASS ":"FAIL ")+label);if(!condition)throw new InvalidOperationException(label);}
    static void CheckShared(string label)
    {
        var values=avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).ToArray();
        Check(values.Length>=2&&values.All(m=>ReferenceEquals(m,values[0])),label+": body and eyes share one material instance");
    }
    static void ValidateRecipeIntegration()
    {
        var source=ScriptableObject.CreateInstance<AvatarRecipeSO>();
        source.baseAvatar=AssetDatabase.LoadAssetAtPath<BaseAvatarDefinitionSO>("Assets/_Project/Avatar/MakeHuman/Content/BaseAvatar_MakeHuman.asset");
        source.skinTone=.82f;source.skinUndertone=-.4f;
        var recipe=source.ToRuntime();
        Check(recipe.SkinTone==.82f && recipe.SkinUndertone==-.4f,"Recipe serializes and resolves authored skin values");
        Check(source.baseAvatar.skinMaterial!=null && source.baseAvatar.skinMaterial.HasProperty("_SkinTone"),"Production base references unified skin material");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(source.baseAvatar.baseAvatarPrefab.AssetGUID));
        Check(prefab!=null,"Production Addressable base prefab resolves");
        var body=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(body,reviewScene);
        var instance=new AvatarInstance {Recipe=recipe,FinalIdentity=recipe.Identity};
        try
        {
            AvatarFactory.AssembleBody(instance,body,recipe);
            Check(instance.SkinToneController!=null && Mathf.Approximately(instance.SkinToneController.tone,.82f),"Factory applies recipe tone at avatar creation");
            var renderers=body.GetComponentsInChildren<SkinnedMeshRenderer>();
            var skin=renderers.Where(r=>r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_SkinTone")).ToArray();
            Check(skin.Length>=2 && skin.All(r=>r.sharedMaterial==skin[0].sharedMaterial),"Production factory shares one body/eyes material");
            var block=new MaterialPropertyBlock();skin[0].GetPropertyBlock(block);block.SetFloat("_ReviewSentinel",.731f);skin[0].SetPropertyBlock(block);
            instance.SetSkinTone(.21f);instance.SetSkinUndertone(.6f);
            instance.SkinToneController.PaintGroup(0,new Color(.65f,.02f,.04f,1));
            skin[0].GetPropertyBlock(block);
            Check(block.GetFloat("_NailMode")==1 && block.GetFloat("_NailGroup0Enabled")==1 && block.GetFloat("_NailGroup1Enabled")==0,"Group mode paints only the selected hand or foot");
            instance.SkinToneController.SetNailColor(6,new Color(.02f,.1f,.65f,1));
            skin[0].GetPropertyBlock(block);
            Check(block.GetColor("_NailColor0").a==1 && block.GetColor("_NailColor5").a==0 && block.GetColor("_NailColor6").b>.6f,"Independent nail and group painting reaches shared skin shader");
            Check(block.GetFloat("_NailMode")==2,"Individual mode is driven by per-nail recipe colors");
            instance.SkinToneController.UseNaturalNails();skin[0].GetPropertyBlock(block);
            Check(block.GetFloat("_NailMode")==0 && block.GetColor("_NailColor0").a==1,"Natural nails bypass polish without deleting configured colors");
            Check(recipe.Nails.colors[0].a==1 && source.nails.colors[0].a==0,"Nail edits update runtime recipe without mutating source");
            Check(recipe.SkinTone==.21f && recipe.SkinUndertone==.6f,"Live controls update runtime recipe");
            Check(source.skinTone==.82f && source.skinUndertone==-.4f,"Live instance edits do not mutate authored recipe asset");
            skin[0].GetPropertyBlock(block);
            Check(Mathf.Approximately(block.GetFloat("_SkinTone"),.21f) && Mathf.Approximately(block.GetFloat("_ReviewSentinel"),.731f),"Factory live tone preserves garment/property fields");
        }
        finally { instance.Dispose();if(body!=null)Object.DestroyImmediate(body);Object.DestroyImmediate(source); }
    }

    static AvatarIrisSettings AuthoredIris(Texture2D[] sources,int index)=>new AvatarIrisSettings
    {baseTextures=sources,textureIndex=index,randomizeOnSpawn=false,useGradient=true,inner=new Color(.38f,.30f,.16f),middle=new Color(.16f,.38f,.53f),outer=new Color(.075f,.14f,.19f),offset=Vector2.zero,distortion=0};
    static void CreateMaterialAndPrefab(Shader shader)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Check(material!=null,"Current production skin material available");
        material.name="MakeHuman_Skin_v7";material.shader=shader;
        var regions=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/skinTest_Regions_v7.png");Check(regions!=null,"Semantic tone regions imported");
        material.SetTexture("_SkinRegionMap",regions);material.SetFloat("_SkinToneEnabled",1);material.SetFloat("_SkinTone",1f/3f);material.SetFloat("_SkinUndertone",0);
        material.SetFloat("_SkinSoftness",.25f);material.SetFloat("_SkinWrap",.3f);material.SetColor("_SkinScatterColor",new Color(1,.55f,.38f));
        material.SetVector("_IrisRightUV",new Vector4(.7760985f,.9250188f,.02527f,0));material.SetVector("_IrisLeftUV",new Vector4(.9239943f,.8990489f,.02527f,0));
        material.SetFloat("_IrisPupilRadius",.34f);material.SetFloat("_IrisEdgeFeather",.035f);
        AvatarIrisAppearance.Configure(material,AuthoredIris(Resources.LoadAll<Texture2D>("Avatar/Iris"),0));
        EditorUtility.SetDirty(material);
        var contents=PrefabUtility.LoadPrefabContents(Generated+"MakeHumanAvatar.prefab");
        try
        {
            contents.name="MakeHumanAvatar_SkinV7";
            foreach(var renderer in contents.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterial=material;
            var control=contents.GetComponent<AvatarSkinToneController>();if(control==null)control=contents.AddComponent<AvatarSkinToneController>();control.tone=1f/3f;control.undertone=0;
            PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(contents);}
        AssetDatabase.SaveAssets();
    }
    static Transform Bone(string name)=>avatar.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
    static void Measure()
    {
        up=(Bone("head").position-(Bone("foot_l").position+Bone("foot_r").position)*.5f).normalized;
        side=(Bone("hand_l").position-Bone("hand_r").position).normalized;
        side=Vector3.ProjectOnPlane(side,up).normalized;front=Vector3.Cross(side,up).normalized;
        var points=new List<Vector3>();var eyePoints=new List<Vector3>();
        foreach(var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.updateWhenOffscreen=true;var mesh=new Mesh();renderer.BakeMesh(mesh);
            var world=mesh.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();points.AddRange(world);
            if(renderer.name.IndexOf("eye",StringComparison.OrdinalIgnoreCase)>=0)eyePoints.AddRange(world);
            Object.DestroyImmediate(mesh);
        }
        Check(points.Count>10000&&eyePoints.Count>0,"Body and eye geometry measured");
        eye=eyePoints.Aggregate(Vector3.zero,(a,b)=>a+b)/eyePoints.Count;
        if(Vector3.Dot(eye-Bone("head").position,front)<0)front=-front;
        float min=points.Min(v=>Vector3.Dot(v,up)),max=points.Max(v=>Vector3.Dot(v,up));height=max-min;
        float sx=points.Min(v=>Vector3.Dot(v,side)),ex=points.Max(v=>Vector3.Dot(v,side));width=ex-sx;
        center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Count;center+=up*((min+max)*.5f-Vector3.Dot(center,up));
        face=Bone("head").position+up*height*.015f+front*height*.025f;
    }
    static void SetupStudio()
    {
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.35f,.35f);RenderSettings.skybox=null;RenderSettings.fog=false;
        var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.35f,.35f,.35f));RenderSettings.ambientProbe=ambient;
        Light Key(string name,Vector3 direction,float intensity,Color color)
        {var go=new GameObject(name);var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.color=color;go.transform.rotation=Quaternion.LookRotation(-direction,up);light.shadows=LightShadows.None;return light;}
        RenderSettings.sun=Key("Skin studio key",(front+side*.65f+up*.65f).normalized,1.3f,new Color(1,.95f,.90f));
        Key("Skin studio fill",(front-side*.8f+up*.2f).normalized,.65f,new Color(.88f,.94f,1));
        Key("Skin studio rim",(-front+up*.7f-side*.3f).normalized,.8f,new Color(1,.96f,.92f));
        var go=new GameObject("Skin tone camera");camera=go.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.22f,.24f,.27f);camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.allowHDR=true;
        camera.scene=reviewScene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(reviewScene);
        var data=camera.GetUniversalAdditionalCameraData();data.allowXRRendering=false;data.renderPostProcessing=true;data.volumeLayerMask=1<<31;
        const string profilePath="Assets/_Project/Scenes/Debug/AvatarSkinToneLabVolume.asset";
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
        if(!profile.TryGet<Tonemapping>(out var tone)){tone=profile.Add<Tonemapping>(true);AssetDatabase.AddObjectToAsset(tone,profile);}
        tone.mode.Override(TonemappingMode.ACES);EditorUtility.SetDirty(profile);
        if(!profile.TryGet<ColorAdjustments>(out var grading)){grading=profile.Add<ColorAdjustments>(true);AssetDatabase.AddObjectToAsset(grading,profile);}
        grading.postExposure.Override(0);grading.contrast.Override(0);grading.saturation.Override(0);grading.hueShift.Override(0);grading.colorFilter.Override(Color.white);
        var volume=new GameObject("Skin tone studio grading").AddComponent<Volume>();volume.gameObject.layer=31;volume.priority=1000;volume.isGlobal=true;volume.sharedProfile=profile;
    }
    static void Frame(string view)
    {
        Vector3 target;float size;Vector3 direction=front;
        if(view=="head"){target=face;size=height*.14f;}
        else if(view.StartsWith("nailthumb")){target=avatar.transform.TransformPoint(new Vector3(view.EndsWith("right")?-.773f:.773f,1.35f,.101f));size=.028f;direction=(front+up*.35f).normalized;}
        else if(view.StartsWith("nailhand")){target=avatar.transform.TransformPoint(new Vector3(view.EndsWith("right")?-.81f:.81f,1.38f,.035f));size=.085f;direction=up;}
        else if(view.StartsWith("nailfoot")){target=avatar.transform.TransformPoint(new Vector3(view.EndsWith("right")?-.12f:.12f,.025f,.172f));size=.075f;direction=up;}
        else if(view=="eye"){target=eye+side*height*.019f;size=height*.031f;}
        else {target=center;size=Mathf.Max(height*.57f,width/(camera.aspect*2)*1.07f);if(view=="back")direction=-front;}
        camera.orthographicSize=size;camera.transform.position=target+direction*height*3;
        camera.transform.rotation=Quaternion.LookRotation(target-camera.transform.position,view.StartsWith("nailthumb")?up:(view.StartsWith("nail")?front:up));
    }
    [Serializable] class ReviewSummary { public bool success; public string revision; public int tones,viewsPerTone,captures; public bool sharedMaterial; }
    [Serializable] class NailUVProjection {public Vector3[] screen;public Vector2[] uv;public int[] triangles;}
    static void Capture(string name,string view)
    {
        int w=720,h=900;camera.aspect=(float)w/h;Frame(view);
        if(view.StartsWith("nail"))
        {
            var body=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Body");
            var baked=new Mesh();body.BakeMesh(baked);var vertices=baked.vertices;var projected=new Vector3[vertices.Length];
            for(int i=0;i<vertices.Length;i++){var v=camera.WorldToViewportPoint(body.transform.TransformPoint(vertices[i]));projected[i]=new Vector3(v.x*w,(1-v.y)*h,v.z);}
            File.WriteAllText(Path.Combine(Output,view+"_projection.json"),JsonUtility.ToJson(new NailUVProjection{screen=projected,uv=baked.uv,triangles=baked.triangles}));Object.DestroyImmediate(baked);
        }
        var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();var old=RenderTexture.active;
        Texture2D image=null;
        try
        {
            camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();
            var pixels=image.GetPixels32();Check(pixels.Count(c=>c.r>140&&c.b>140&&c.g<60)<pixels.Length*.03,"No error-shader pixels: "+name);
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());captureCount++;
        }
        finally{RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);}
    }
    static void AddSliders()
    {
        var canvas=new GameObject("Skin tone controls",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
        var panel=new GameObject("Controls",typeof(RectTransform),typeof(Image));panel.transform.SetParent(canvas.transform,false);panel.GetComponent<Image>().color=new Color(.04f,.055f,.075f,.9f);
        var rect=panel.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.one;rect.pivot=Vector2.one;rect.anchoredPosition=new Vector2(-20,-20);rect.sizeDelta=new Vector2(320,190);
        void Slider(string label,float min,float max,float value,float y,UnityAction<float> action)
        {
            var text=new GameObject(label,typeof(RectTransform),typeof(Text));text.transform.SetParent(panel.transform,false);var tr=text.GetComponent<RectTransform>();tr.anchorMin=tr.anchorMax=new Vector2(.5f,1);tr.anchoredPosition=new Vector2(0,y);tr.sizeDelta=new Vector2(285,28);
            var content=text.GetComponent<Text>();content.text=label;content.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");content.fontSize=18;content.color=Color.white;
            var go=DefaultControls.CreateSlider(new DefaultControls.Resources());go.name=label+" slider";go.transform.SetParent(panel.transform,false);var sr=go.GetComponent<RectTransform>();sr.anchorMin=sr.anchorMax=new Vector2(.5f,1);sr.anchoredPosition=new Vector2(0,y-35);sr.sizeDelta=new Vector2(275,24);
            var slider=go.GetComponent<UnityEngine.UI.Slider>();slider.minValue=min;slider.maxValue=max;slider.value=value;UnityEventTools.AddPersistentListener(slider.onValueChanged,action);
        }
        Slider("To de pell: clar — fosc",0,1,1f/3f,-22,controller.SetTone);
        Slider("Subtò: fred — càlid",-1,1,0,-103,controller.SetUndertone);
        var events=new GameObject("Skin tone event system",typeof(EventSystem));
        var type=Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if(type!=null){var module=events.AddComponent(type);type.GetMethod("AssignDefaultActions")?.Invoke(module,null);}else events.AddComponent<StandaloneInputModule>();
    }
}

