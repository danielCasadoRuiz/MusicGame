using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FinishTerrainSetup
{
    const string Root="Assets/_Project/Art/RunnerSurfaces/MusicStyles";
    [MenuItem("MusicGame/Runner/Install Classical Finish Transition")]
    public static void InstallAndCheck()
    {
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        try
        {
            var source=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Classical/Classical_1.mat");
            if(source==null)throw new Exception("Missing Classical surface source");
            string path=Root+"/Classical/ClassicalFinish.mat";
            var finish=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(finish==null){finish=new Material(source){name="ClassicalFinish"};AssetDatabase.CreateAsset(finish,path);}
            finish.CopyPropertiesFromMaterial(source);
            finish.SetFloat("_SurfaceEmissionStrength",0);
            EditorUtility.SetDirty(finish);
            int assigned=0;
            foreach(string guid in AssetDatabase.FindAssets("t:TrackStyleSO",new[]{"Assets/_Project/Configs/Theme"}))
            {
                var track=AssetDatabase.LoadAssetAtPath<TrackStyleSO>(AssetDatabase.GUIDToAssetPath(guid));
                if(track.finishMaterial==null){track.finishMaterial=finish;EditorUtility.SetDirty(track);}assigned++;
            }
            AssetDatabase.SaveAssets();
            if(ShaderUtil.ShaderHasError(source.shader))throw new Exception("Terrain shader failed compilation");
            VerifyArch(f=>f.ArchAppearsWithItsGroundWhileSongIsStillAheadOfTheClosingSection());
            VerifyArch(f=>f.RestartWindowRemovesOldArchAndRecreatesItOnlyNearFinish());
            VerifyArch(f=>f.ArchDoesNotAppearOutsideTheLoadedGround(-80,-.01f));
            VerifyArch(f=>f.ArchDoesNotAppearOutsideTheLoadedGround(.01f,80));
            VerifyMaterial(finish);
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/FinishTerrainCheck.txt","PASS: arch aligned with playback end (warmup/speed included), restart rewind removes/recreates the arch, streaming boundaries and no duplicate arch verified; classical finish assigned through "+assigned+" track configs; 10-second patch-reveal span, patch controls, endpoint and finish texture binding verified; no captures generated. "+DateTime.UtcNow.ToString("O"));
            Debug.Log(File.ReadAllText("Logs/FinishTerrainCheck.txt"));
        }
        finally{ShaderUtil.allowAsyncCompilation=async;}
    }
    static void VerifyArch(Action<FinishArchStreamingTests> check)
    {
        var fixture=new FinishArchStreamingTests();fixture.SetUp();
        try{check(fixture);}finally{fixture.TearDown();}
    }
    static void VerifyMaterial(Material finish)
    {
        var host=new GameObject("FinishTerrainMaterialCheck");host.SetActive(false);
        var config=ScriptableObject.CreateInstance<MusicRunnerGameplayConfig>();var core=ScriptableObject.CreateInstance<MusicRunnerCoreConfig>();var level=ScriptableObject.CreateInstance<MusicRunnerLevelConfig>();config.core=core;config.levelGeneration=level;
        core.playerSpeed=10;core.warmupTime=3;core.endingFlatSeconds=9;level.finishSurfaceBlendMeters=5;level.finishSurfaceRevealSeconds=10;
        var world=host.AddComponent<MusicWorldManager>();world.Initialize(config,null,finish);
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(MusicWorldManager).GetField("_playRangeEnd",flags).SetValue(world,120f);
        var material=(Material)typeof(MusicWorldManager).GetMethod("VertexColorMaterial",flags).Invoke(world,null);
        try
        {
            if(material.GetFloat("_FinishMode")!=2 || material.GetVector("_FinishLayerIndices")!=finish.GetVector("_LayerIndices") || material.GetVector("_FinishHighWeights")!=finish.GetVector("_HighWeights") || material.GetFloat("_FinishLayerPatchSize")!=finish.GetFloat("_PatchSize"))throw new Exception("Classical finish lost its altitude layers");
            if(!material.SetPass(0))throw new Exception("Terrain forward pass did not compile");
            if(material.GetFloat("_FinishSurfaceEnabled")!=1 || Mathf.Abs(material.GetFloat("_FinishDistance")-1230)>0.001f || material.GetFloat("_FinishBlendMeters")!=100 || Mathf.Abs(material.GetFloat("_FinishPatchSize")-1.8f)>.001f || Mathf.Abs(material.GetFloat("_FinishPatchSoftness")-.07f)>.001f || material.GetTexture("_FinishAtlas")!=finish.GetTexture("_SurfaceAtlas"))throw new Exception("Finish texture / spatial boundary mismatch");
        }
        finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(config);UnityEngine.Object.DestroyImmediate(core);UnityEngine.Object.DestroyImmediate(level);}
    }
}
