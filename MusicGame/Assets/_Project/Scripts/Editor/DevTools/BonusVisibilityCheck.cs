using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BonusVisibilityCheck
{
    [InitializeOnLoadMethod]
    static void OnceAfterImport()
    {
        EditorApplication.delayCall+=()=>
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool("BonusVisibilityCheck.v1",false))return;
            Run();ResetGameTool.Verify();SessionState.SetBool("BonusVisibilityCheck.v1",true);
        };
    }
    [MenuItem("Tools/MusicGame/Bonuses/Verify Glow")]
    public static void Run()
    {
        var config=AssetDatabase.LoadAssetAtPath<MusicRunnerCollectiblesConfig>("Assets/_Project/Configs/Gameplay/MusicRunner/MusicRunnerCollectiblesConfig.asset");
        if(config==null || config.ringGlowShader==null || ShaderUtil.ShaderHasError(config.ringGlowShader))throw new Exception("Bonus glow shader missing / compilation failed");
        int count=0;
        foreach(string file in Directory.GetFiles("Assets/_Project/Art/Bonuses","M_bonus_*.mat",SearchOption.AllDirectories))
        {
            var source=AssetDatabase.LoadAssetAtPath<Material>(file.Replace('\\','/'));var originalShader=source.shader;var texture=source.GetTexture("_BaseMap");
            var glow=BonusVisibilityMaterial.Create(source,config,Color.cyan);
            try
            {
                if(glow.shader!=config.ringGlowShader || glow.GetTexture("_BaseMap")!=texture || source.shader!=originalShader || glow.GetFloat("_GlowStrength")<=1 || glow.HasProperty("_GlowColor"))throw new Exception("Authored texture / glow regression: "+file);
                // Type colours must only affect textureless placeholders, never authored models.
                var other=BonusVisibilityMaterial.Create(source,config,Color.red);
                try
                {
                    if(other.GetColor("_BaseColor")!=glow.GetColor("_BaseColor") || other.GetTexture("_BaseMap")!=glow.GetTexture("_BaseMap"))throw new Exception("Fallback colour tinted authored bonus: "+file);
                }
                finally{UnityEngine.Object.DestroyImmediate(other);}
                count++;
            }
            finally{UnityEngine.Object.DestroyImmediate(glow);}
        }
        Directory.CreateDirectory("Logs");File.WriteAllText("Logs/BonusVisibilityCheck.txt","PASS: glow shader compiled; "+count+" authored bonus materials retain textures, source materials unchanged, fallback colour cannot tint authored textures, texture-derived HDR glow configured; direct shader reference retained for builds.");
        Debug.Log(File.ReadAllText("Logs/BonusVisibilityCheck.txt"));
    }
}
