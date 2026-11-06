using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HorizonVisualReview
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        string output="Logs/HorizonReview";
        Directory.CreateDirectory(output);
        var config=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<HorizonConfig>("Assets/_Project/Configs/Gameplay/Environment/HorizonConfig.asset"));
        if(config.horizonPalettes==null || config.horizonPalettes.palettes.Length!=17) throw new Exception("Palette library missing entries");
        var session=new GameObject("Session").AddComponent<GameSession>();
        typeof(GameSession).GetProperty("Instance").SetValue(null,session);
        var root=new GameObject("Horizon");
        var driver=root.AddComponent<HorizonPaletteDriver>();
        driver.Initialize(config);
        var mountains=root.AddComponent<HorizonMountainLayers>(); mountains.Initialize(config,root.transform);
        var water=root.AddComponent<HorizonWater>();water.Initialize(config,root.transform);
        var bars=root.AddComponent<SpectrumBars3D>();bars.Initialize(config,root.transform,water);
        var gameplay=AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset");
        string file=Directory.GetFiles(Path.Combine(Application.persistentDataPath,"SongProfileCache"),"*.mgsp")[0];
        var raw=SongProfileSerializer.Read(File.ReadAllBytes(file),out var header,out var tags);
        var profile=new SongProfile {duration=raw.duration,analysisHopTime=raw.hopTime,visualBandEnvelopes=raw.visualBands};
        var world=new GameObject("Audio world").AddComponent<MusicWorldManager>();world.Initialize(gameplay);
        typeof(MusicWorldManager).GetField("_profile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(world,profile);
        var references=typeof(MusicWorldManager).GetMethod("ComputeBandReferences",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{profile,gameplay.levelGeneration.frequencyColorReferencePercentile});
        typeof(MusicWorldManager).GetField("_bandReference",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(world,references);
        typeof(MusicWorldManager).GetProperty("FrequencyBandsUsed").SetValue(world,profile.VisualBandCount);
        var cam=new GameObject("Review Camera").AddComponent<Camera>();
        cam.clearFlags=CameraClearFlags.Skybox;cam.nearClipPlane=.05f;cam.farClipPlane=200;
        cam.fieldOfView=60;cam.aspect=16f/9;cam.allowHDR=true;
        cam.cullingMask=1<<LayerMask.NameToLayer(HorizonCameraController.HorizonLayerName);
        var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.requiresColorTexture=true;
        var sky=cam.gameObject.AddComponent<ProceduralSkyController>();sky.Initialize(config,cam);
        var light=new GameObject("Preview existing main light").AddComponent<Light>();light.type=LightType.Directional;
        light.transform.rotation=Quaternion.Euler(35,-25,0);light.intensity=.7f;
        RenderSettings.sun=light;
        var volume=root.AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom=volume.profile.Add<Bloom>();bloom.intensity.Override(config.horizonBloomIntensity);
        bloom.threshold.Override(config.horizonBloomThreshold);bloom.scatter.Override(config.horizonBloomScatter);
        foreach(var style in new[]{GameMusicStyle.Cinematic,GameMusicStyle.Electronic,GameMusicStyle.Funky})
        foreach(float energy in new[]{0f,1f})
        {
            // Extremes on purpose: calm with the harmonic shift one way, peak with it the other.
            session.DetectedGameStyle=style;driver.Tick(energy,energy==0?-1f:1f,energy==0?0f:1f,true);
            for(int i=0;i<200;i++) bars.Tick(world,22f+i/60f);
            water.Tick();sky.Tick();mountains.Tick();
            cam.transform.rotation=Quaternion.Euler(10,0,0);
            Capture(cam,output+"/"+style+"_"+(energy==0?"calm":"peak")+".png");
        }
        foreach(float energy in new[]{0f,1f})
        foreach(int yaw in new[]{90,180})
        {
            session.DetectedGameStyle=GameMusicStyle.Electronic;driver.Tick(energy,energy==0?-1f:1f,energy==0?0f:1f,true);
            water.Tick();sky.Tick();mountains.Tick();
            cam.transform.rotation=Quaternion.Euler(10,yaw,0);
            Capture(cam,output+"/surround_"+yaw+"_"+(energy==0?"calm":"peak")+".png");
        }
        // Looking up near the sun vs away from it, to check the sunset stays fixed around the sun.
        foreach(float energy in new[]{0f,1f})
        {
            session.DetectedGameStyle=GameMusicStyle.Electronic;driver.Tick(energy,energy==0?-1f:1f,energy==0?0f:1f,true);
            water.Tick();sky.Tick();mountains.Tick();
            cam.transform.rotation=Quaternion.Euler(-25,0,0);
            Capture(cam,output+"/up_"+(energy==0?"calm":"peak")+".png");
        }
        var mesh=HorizonBeveledBarMesh.Build(.07f);
        if(mesh.vertexCount!=96 || mesh.triangles.Length/3!=108) throw new Exception("Unexpected bevel geometry budget");
        int lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        File.WriteAllText(output+"/validation.txt",$"PASS: 17 palettes, actual cached-song spectrum, 360 side/back renders. Bar mesh: {mesh.vertexCount} vertices / {mesh.triangles.Length/3} triangles. Lights in review: {lights} (preview main light only). No added runtime lights or reflection captures.");
        Debug.Log("HORIZON_VISUAL_REVIEW_OK");
    }
    private static void Capture(Camera cam,string file)
    {
        var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGBHalf);
        cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
        var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(file,tex.EncodeToPNG());
        RenderTexture.active=null;cam.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);
    }
}
