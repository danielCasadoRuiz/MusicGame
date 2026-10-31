using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The body skin shader "MusicGame/Avatar/Skin Visibility Lit" = URP Lit + the per-avatar skin visibility
/// mask in every color / GBuffer / depth / depth-normals / shadow pass (Shaders/Avatar/SkinVisibility*.hlsl).
///
///   * Apply: points the MakeHuman skin material at it (same property names as URP Lit, so every value is
///     kept). CanonicalAvatarSetup creates the skin material with it too.
///   * Regenerate: rebuilds SkinVisibilityLit.shader from the INSTALLED URP Lit.shader (run after a URP
///     upgrade; fails loudly if URP's pass layout changed so the patch points no longer exist).
/// </summary>
public static class SkinVisibilityShaderSetup
{
    public const string ShaderName = "MusicGame/Avatar/Skin Visibility Lit";
    public const string ShaderPath = "Assets/_Project/Shaders/Avatar/SkinVisibilityLit.shader";
    public const string SkinMaterialPath = "Assets/_Project/Avatar/MakeHuman/Generated/Materials/MakeHuman_Skin.mat";

    [MenuItem("Tools/MusicGame/Avatars/Garments/Apply Skin Visibility Shader To Body")]
    public static void ApplyMenu() => Debug.Log(Apply());

    public static void ApplyFromCommandLine()
    {
        string message = Apply();
        Debug.Log(message);
        if (Application.isBatchMode) EditorApplication.Exit(message.StartsWith("ERROR") ? 1 : 0);
    }

    public static string Apply()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) return $"ERROR: {ShaderPath} not found.";
        if (ShaderUtil.ShaderHasError(shader)) return $"ERROR: {ShaderName} has compile errors — see the shader inspector.";
        var material = AssetDatabase.LoadAssetAtPath<Material>(SkinMaterialPath);
        if (material == null) return $"ERROR: {SkinMaterialPath} not found.";
        if (material.shader == shader) return $"{SkinMaterialPath} already uses {ShaderName}.";
        string previous = material.shader != null ? material.shader.name : "none";
        material.shader = shader; // URP Lit property names are identical: every value carries over
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return $"{SkinMaterialPath}: {previous} → {ShaderName}.";
    }

    [MenuItem("Tools/MusicGame/Avatars/Garments/Regenerate Skin Visibility Shader")]
    public static void RegenerateMenu()
    {
        try { Regenerate(); Debug.Log($"[SkinVisibilityShaderSetup] {ShaderPath} regenerated from the installed URP Lit."); }
        catch (Exception e) { Debug.LogError("[SkinVisibilityShaderSetup] " + e.Message); }
    }

    public static void Regenerate()
    {
        var urp = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.universal/package.json");
        if (urp == null) throw new InvalidOperationException("URP package not found.");
        string s = File.ReadAllText(Path.Combine(urp.resolvedPath, "Shaders", "Lit.shader"));

        void Sub(string oldValue, string newValue)
        {
            if (!s.Contains(oldValue)) throw new InvalidOperationException($"URP {urp.version} Lit.shader no longer contains '{oldValue.Trim()}' — update the patch in SkinVisibilityShaderSetup.");
            int i = s.IndexOf(oldValue, StringComparison.Ordinal);
            s = s.Substring(0, i) + newValue + s.Substring(i + oldValue.Length);
        }
        const string I = "            ";
        Sub("Shader \"Universal Render Pipeline/Lit\"", $"Shader \"{ShaderName}\"");
        Sub("        [HideInInspector][NoScaleOffset]unity_Lightmaps",
            "        // Per-avatar skin visibility (white covered / black visible), set through a MaterialPropertyBlock.\n" +
            "        [HideInInspector][NoScaleOffset] _SkinVisibilityMask(\"Skin Visibility Mask\", 2D) = \"black\" {}\n\n" +
            "        [HideInInspector][NoScaleOffset]unity_Lightmaps");
        Sub("#pragma fragment LitPassFragment", "#pragma fragment SkinVisibilityLitPassFragment");
        Sub(I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl\"\n",
            I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl\"\n" + I + "#define MUSICGAME_SKIN_VISIBILITY_FORWARD\n" + I + "#include \"SkinVisibilityPasses.hlsl\"\n");
        Sub("#pragma vertex ShadowPassVertex", "#pragma vertex SkinVisibilityShadowPassVertex");
        Sub("#pragma fragment ShadowPassFragment", "#pragma fragment SkinVisibilityShadowPassFragment");
        Sub("#include \"Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl\"", "#include \"SkinVisibilityShadowCasterPass.hlsl\"");
        Sub("#pragma fragment LitGBufferPassFragment", "#pragma fragment SkinVisibilityGBufferPassFragment");
        Sub(I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl\"\n",
            I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl\"\n" + I + "#define MUSICGAME_SKIN_VISIBILITY_GBUFFER\n" + I + "#include \"SkinVisibilityPasses.hlsl\"\n");
        Sub("#pragma vertex DepthOnlyVertex", "#pragma vertex SkinVisibilityDepthOnlyVertex");
        Sub("#pragma fragment DepthOnlyFragment", "#pragma fragment SkinVisibilityDepthOnlyFragment");
        Sub("#include \"Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl\"", "#include \"SkinVisibilityDepthOnlyPass.hlsl\"");
        Sub("#pragma fragment DepthNormalsFragment", "#pragma fragment SkinVisibilityDepthNormalsFragment");
        Sub(I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl\"\n",
            I + "#define REQUIRES_UV_INTERPOLATOR\n" + I + "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl\"\n" +
            I + "#define MUSICGAME_SKIN_VISIBILITY_DEPTH_NORMALS\n" + I + "#include \"SkinVisibilityPasses.hlsl\"\n");

        int motion = s.IndexOf("        Pass\n        {\n            Name \"MotionVectors\"", StringComparison.Ordinal);
        int fallback = s.IndexOf("    FallBack", StringComparison.Ordinal);
        if (motion < 0 || fallback < motion) throw new InvalidOperationException("MotionVectors pass / FallBack not found in URP Lit.shader.");
        s = s.Substring(0, motion).TrimEnd() + "\n    }\n\n" + s.Substring(fallback);

        string header =
            $"// Generated from URP {urp.version} \"Universal Render Pipeline/Lit\" (Library/PackageCache/com.unity.render-pipelines.universal/Shaders/Lit.shader).\n" +
            "// Identical surface/lighting; adds the per-avatar skin visibility mask (SkinVisibility.hlsl) to every pass that\n" +
            "// writes color, GBuffer, depth, depth-normals or shadows. MotionVectors/XRMotionVectors passes are omitted (the\n" +
            "// body material already disables MOTIONVECTORS); without them covered skin cannot write motion vectors either.\n" +
            "// Regenerate (Tools > MusicGame > Avatars > Garments > Regenerate Skin Visibility Shader) after a URP upgrade.\n";
        File.WriteAllText(ShaderPath, header + s.Replace("\r\n", "\n"));
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
    }
}
