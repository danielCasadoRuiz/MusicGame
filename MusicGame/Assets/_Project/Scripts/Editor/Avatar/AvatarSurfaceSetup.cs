#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Preserves the unified iris extension when the URP-derived skin shader is regenerated.</summary>
public static class AvatarSurfaceSetup
{
    public const string ShaderFolder = "Assets/_Project/Shaders/Avatar/";
    public const string IrisProperties = @"
        [Header(Iris gradient)]
        [Toggle] _IrisEnabled(""Tint iris by UV"", Float) = 1
        _IrisInnerColor(""Iris center"", Color) = (0.42,0.32,0.14,1)
        _IrisMainColor(""Iris body"", Color) = (0.28,0.49,0.64,1)
        _IrisOuterColor(""Iris rim"", Color) = (0.08,0.16,0.21,1)
        _IrisInnerRadius(""Center extent"", Range(0.36,0.85)) = 0.55
        _IrisOuterRadius(""Rim start"", Range(0.65,0.98)) = 0.87
        _IrisDetailGain(""Fiber brightness"", Range(0.5,4)) = 2.2
        _EyeSmoothness(""Eyeball smoothness"", Range(0,1)) = 0.8
        [HideInInspector] _IrisRightUV(""Right iris UV"", Vector) = (0.7759774,0.9251543,0.02494856,0)
        [HideInInspector] _IrisLeftUV(""Left iris UV"", Vector) = (0.9238683,0.8991770,0.02494856,0)
";
    public static string PatchGeneratedShader(string source)
    {
        RebuildInput();
        if (!source.Contains("_IrisEnabled("))
            source = source.Replace("        // Specular vs Metallic workflow", IrisProperties + "\n        // Specular vs Metallic workflow");
        return source.Replace("\"Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl\"", "\"AvatarSurfaceInput.hlsl\"")
            .Replace("CustomEditor \"UnityEditor.Rendering.Universal.ShaderGUI.LitShader\"", "CustomEditor \"AvatarSurfaceShaderGUI\"");
    }
    public static void RebuildInput()
    {
        var urp = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.universal/package.json");
        string source = File.ReadAllText(Path.Combine(urp.resolvedPath,"Shaders/LitInput.hlsl")).Replace("\r\n","\n");
        if (!source.Contains("inline void InitializeStandardLitSurfaceData(")) throw new InvalidOperationException("URP LitInput layout changed.");
        source = source.Replace("CBUFFER_END", "#include \"AvatarIrisParameters.hlsl\"\nCBUFFER_END")
            .Replace("inline void InitializeStandardLitSurfaceData(", "inline void InitializeAvatarBaseSurfaceData(");
        int end = source.LastIndexOf("#endif",StringComparison.Ordinal);
        source = source.Insert(end,"#include \"AvatarIrisSurface.hlsl\"\n");
        string path = ShaderFolder + "AvatarSurfaceInput.hlsl";
        if (!File.Exists(path) || File.ReadAllText(path) != source) File.WriteAllText(path,source);
    }
}
#endif
