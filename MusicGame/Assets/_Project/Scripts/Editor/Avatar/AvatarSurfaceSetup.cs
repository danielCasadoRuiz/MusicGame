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
        [HideInInspector] _IrisBaked(""Baked iris"", Float) = 0
        [Header(Iris gradient)]
        [Toggle] _IrisEnabled(""Tint iris by UV"", Float) = 1
        _IrisInnerColor(""Iris center"", Color) = (0.42,0.32,0.14,1)
        _IrisMainColor(""Iris body"", Color) = (0.28,0.49,0.64,1)
        _IrisOuterColor(""Iris rim"", Color) = (0.08,0.16,0.21,1)
        _IrisInnerRadius(""Center extent"", Range(0.36,0.85)) = 0.55
        _IrisOuterRadius(""Rim start"", Range(0.65,0.98)) = 0.87
        _IrisDetailGain(""Fiber brightness"", Range(0.5,4)) = 2.2
        _EyeSmoothness(""Eyeball smoothness"", Range(0,1)) = 0.8
        [HideInInspector] _IrisRightUV(""Right iris UV"", Vector) = (0.7760985,0.9250188,0.02527,0)
        [HideInInspector] _IrisLeftUV(""Left iris UV"", Vector) = (0.9239943,0.8990489,0.02527,0)
        [HideInInspector] _IrisUseGradient(""Use gradient"", Float) = 1
        [HideInInspector] _IrisDefaultColor(""Uniform iris"", Color) = (0.28,0.49,0.64,1)
        [HideInInspector] _IrisDetailEnabled(""Use iris image"", Float) = 0
        [NoScaleOffset] _IrisDetailMap(""Iris fibers (RGBA)"", 2D) = ""white"" {}
        [HideInInspector] _IrisDetailOuter(""Source outer annulus"", Vector) = (0.5,0.5,0.44,0)
        [HideInInspector] _IrisDetailPupil(""Source pupil"", Vector) = (0.5,0.5,0.14,0)
        [HideInInspector] _IrisVariation(""Gradient offset and seed"", Vector) = (0,0,1,0)
        [HideInInspector] _IrisDistortion(""Gradient distortion"", Float) = 0
        _IrisPupilRadius(""Pupil fraction"", Range(0.2,0.45)) = 0.34
        _IrisEdgeFeather(""Iris edge feather"", Range(0.01,0.1)) = 0.035
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
