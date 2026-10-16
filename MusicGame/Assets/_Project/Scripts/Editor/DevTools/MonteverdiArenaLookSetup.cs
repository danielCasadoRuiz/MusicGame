#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// First real look of an arena — Monteverdi_Arena_01 (Monteverdi's version-1 arena): polished Renaissance marble
/// mosaic floor + natural warm DAYLIGHT (reference: AssetsMaterial/Scenarios/monteverdi_reference.png).
///   Floor   URP Lit "MonteverdiMarble_Floor": base colour, subtle normal map (0.3), evenly polished (smoothness 0.9,
///           no metallic/smoothness map) and joint occlusion; the rosette centred on the fighters (2×2 tiling,
///           half-tile offset on the 20 m floor).
///   Sky     Skybox/Panoramic from Textures/Monteverdi_SkyPanorama.png = AssetsMaterial/Scenarios/Monteverdi_Skybox.png
///           with its content moved UP by 7 % of the height (bottom rows extended): the city's base now sits at eye
///           level — standing at a distance from the city, not flying over it.
///   Light   ArenaEnvironmentSettings on the Lighting node: procedural warm afternoon sky (the fight camera clears
///           to it), warm trilight ambient (no black shaded sides), the scene sun re-tinted warm with SOFT, light
///           shadows, a faint warm haze, and a realtime reflection probe (rendered once on load) so the polished
///           floor reflects sky and fighters. Everything is restored when the arena unloads.
///   Quality soft shadows enabled in every URP quality asset (otherwise "Soft" falls back to hard).
/// Also sets the TEMPORARY test force (AppConfig.testForceOpponentId = monteverdi, level 1) the user asked for.
/// Idempotent: re-running re-applies the same values (artistic edits to OTHER parts of the prefab are kept).
/// Menu: Tools > MusicGame > Content > Arenas > Monteverdi Arena 01 Look. Batch: -executeMethod MonteverdiArenaLookSetup.RunFromCommandLine
/// </summary>
public static class MonteverdiArenaLookSetup
{
    private const string Folder = "Assets/_Project/Art/Arenas/Monteverdi";
    private const string Tex = Folder + "/Textures";
    private const string PrefabPath = Folder + "/Monteverdi_Arena_01.prefab";
    private const string FloorMatPath = Folder + "/Materials/MonteverdiMarble_Floor.mat";
    private const string SkyMatPath = Folder + "/Materials/Monteverdi_DaySky.mat";
    private const string PanoramaPath = Tex + "/Monteverdi_SkyPanorama.png";
    private const string PanoramaMatPath = Folder + "/Materials/Monteverdi_SkyPanorama.mat";
    private const string PlazaMatPath = Folder + "/Materials/Monteverdi_FarPlaza.mat";
    // Panorama horizon colour (mean of its row at 50 % height): the haze the far plaza fades into, seamless with the sky.
    private static readonly Color HorizonHaze = new(0.66f, 0.47f, 0.39f);

    // The fight camera looks along +Z (player on the left, rival on the right — FightCameraController). The
    // panorama's painted sun sits at u ≈ 0.737 (azimuth ≈ 175° from +Z); the sky is rotated so that sun stands
    // just right of the view centre, behind the rival (like monteverdi_reference.png), and the scene sun light
    // comes from that same azimuth — shadows and backlight agree with the painted sky.
    private const float SunAzimuthInImage = 175.3f;   // degrees from +Z toward +X, unrotated panorama
    private const float SunAzimuthWanted = 20f;       // just right of the camera's view direction
    private const float SunLightElevation = 28f;      // higher than the painted sun: softer, longer-than-noon shadows

    [MenuItem("Tools/MusicGame/Content/Arenas/Monteverdi Arena 01 Look")]
    public static void Menu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        File.WriteAllText("Logs/MonteverdiArenaLookSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var sb = new StringBuilder("[MonteverdiArenaLookSetup]\n");
        if (!AssetDatabase.IsValidFolder(Folder + "/Materials")) AssetDatabase.CreateFolder(Folder, "Materials");
        AssetDatabase.Refresh();

        var baseColor = ImportTexture($"{Tex}/MonteverdiMarble_BaseColor.png", srgb: true, normal: false, sb);
        var normal = ImportTexture($"{Tex}/MonteverdiMarble_Normal.png", srgb: false, normal: true, sb);
        var metallic = ImportTexture($"{Tex}/MonteverdiMarble_MetallicSmoothness.png", srgb: false, normal: false, sb);
        var occlusion = ImportTexture($"{Tex}/MonteverdiMarble_Occlusion.png", srgb: false, normal: false, sb);
        if (baseColor == null || normal == null || metallic == null || occlusion == null) { sb.AppendLine("  FAIL  floor textures missing"); return sb.ToString(); }

        var floorMat = FloorMaterial(baseColor, normal, metallic, occlusion);
        var panorama = ImportPanorama(sb);
        var skyMat = panorama != null ? PanoramaMaterial(panorama) : SkyMaterial();
        sb.AppendLine($"  PASS  materials: {FloorMatPath}, {(panorama != null ? PanoramaMatPath + " (painted Monteverdi panorama)" : SkyMatPath + " (procedural fallback)")}");

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var floor = root.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "Floor");
            if (floor == null) sb.AppendLine("  FAIL  Monteverdi_Arena_01 has no Floor renderer");
            else { floor.sharedMaterial = floorMat; sb.AppendLine("  PASS  Floor → polished marble mosaic"); }

            var lighting = root.transform.Find("Lighting");
            if (lighting == null) lighting = root.transform;
            var env = lighting.GetComponent<ArenaEnvironmentSettings>();
            if (env == null) env = lighting.gameObject.AddComponent<ArenaEnvironmentSettings>();
            env.overrideSkybox = true; env.skybox = skyMat;
            // Ambient FROM THE SKY (spherical harmonics of the panorama, recomputed on load by
            // DynamicGI.UpdateEnvironment): warm from the sunset side, blue from the sky — the cheap URP "GI".
            env.overrideAmbient = true; env.ambientMode = panorama != null ? AmbientMode.Skybox : AmbientMode.Trilight;
            env.ambientIntensity = 1.1f;
            env.ambientColor = new Color(0.86f, 0.80f, 0.70f);        // trilight fallback only
            env.ambientEquatorColor = new Color(0.78f, 0.66f, 0.52f);
            env.ambientGroundColor = new Color(0.55f, 0.43f, 0.32f);
            env.overrideFog = true; env.fog = true; env.fogColor = HorizonHaze; env.fogDensity = 0.0045f; // the far plaza melts into the horizon
            env.overrideMainLight = true;
            env.mainLightColor = new Color(1f, 0.83f, 0.64f);         // late-afternoon sun
            env.mainLightIntensity = 1.4f;
            env.mainLightEuler = new Vector3(SunLightElevation, SunAzimuthWanted + 180f, 0f); // from the painted sun
            env.mainLightShadows = LightShadows.Soft;
            env.mainLightShadowStrength = 0.55f;
            env.cameraClearsToSkybox = true;
            sb.AppendLine("  PASS  Lighting: panorama sky, sky-based ambient, warm soft sun aligned with the painted sun, haze, camera clears to sky");

            // Fill from the camera side (no shadows): with the sun behind the rival, faces would go dark —
            // a soft warm bounce, like the light the marble and the terrace throw back.
            var fillTf = lighting.Find("CameraSideFill");
            if (fillTf == null) { fillTf = new GameObject("CameraSideFill").transform; fillTf.SetParent(lighting, false); }
            fillTf.rotation = Quaternion.Euler(18f, 10f, 0f); // shines toward +Z, from behind the camera
            var fill = fillTf.GetComponent<Light>();
            if (fill == null) fill = fillTf.gameObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(1f, 0.86f, 0.72f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            sb.AppendLine("  PASS  camera-side fill light (no shadows) so faces never go black against the sun");

            var probeTf = lighting.Find("FloorReflections");
            if (probeTf == null) { probeTf = new GameObject("FloorReflections").transform; probeTf.SetParent(lighting, false); }
            probeTf.localPosition = new Vector3(0f, 2f, 0f);
            var probe = probeTf.GetComponent<ReflectionProbe>();
            if (probe == null) probe = probeTf.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.boxProjection = true;
            probe.size = new Vector3(24f, 10f, 24f);
            probe.center = new Vector3(0f, 3f, 0f);
            probe.resolution = 256; // rendered once on load: sharp enough for the polished floor, still cheap
            probe.intensity = 1f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            sb.AppendLine("  PASS  realtime reflection probe (rendered once on load) for the polished floor");

            // FAR PLAZA: a big plain stone ground around the arena (no collider, just below the floor), fading into
            // the haze. It hides the panorama's lower half (painted from a high terrace: the city seen from above),
            // so we stand on a plaza with the city on the horizon instead of floating over it.
            var ground = root.transform.Find("Ground");
            if (ground == null) ground = root.transform;
            var plazaTf = ground.Find("FarPlaza");
            if (plazaTf == null)
            {
                var plaza = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plaza.name = "FarPlaza";
                Object.DestroyImmediate(plaza.GetComponent<Collider>());
                plazaTf = plaza.transform;
                plazaTf.SetParent(ground, false);
            }
            plazaTf.localPosition = new Vector3(0f, -0.02f, 0f);
            plazaTf.localRotation = Quaternion.identity;
            plazaTf.localScale = new Vector3(40f, 1f, 40f); // 400 m × 400 m
            var plazaRenderer = plazaTf.GetComponent<MeshRenderer>();
            plazaRenderer.sharedMaterial = PlazaMaterial(baseColor, normal, occlusion);
            plazaRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sb.AppendLine("  PASS  far plaza (400 m, fades into the horizon haze): standing at a distance from the city, not flying over it");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        int soft = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(asset);
            var prop = so.FindProperty("m_SoftShadowsSupported");
            if (prop != null && !prop.boolValue) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); soft++; }
        }
        sb.AppendLine($"  PASS  soft shadows supported in every URP quality asset ({soft} changed)");

        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app != null)
        {
            app.testForceOpponentId = "monteverdi";
            app.testForceOpponentLevel = 1;
            EditorUtility.SetDirty(app);
            sb.AppendLine("  PASS  TEST FORCE on: every normal fight vs Monteverdi v1 (arena 01) — clear AppConfig.testForceOpponentId for the real game");
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    private static Texture2D ImportTexture(string path, bool srgb, bool normal, StringBuilder sb)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { sb.AppendLine($"  FAIL  {path} not found"); return null; }
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8; // a floor seen at grazing angles
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material FloorMaterial(Texture2D baseColor, Texture2D normal, Texture2D metallic, Texture2D occlusion)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMatPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, FloorMatPath); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", baseColor);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 0.3f); // subtle relief: polished stone, not carved
        mat.EnableKeyword("_NORMALMAP");
        // Evenly polished: one smoothness for the whole floor (no per-joint map) — it reflects everywhere alike.
        mat.SetTexture("_MetallicGlossMap", null);
        mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetTexture("_OcclusionMap", occlusion);
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetFloat("_EnvironmentReflections", 1f);
        mat.SetFloat("_SpecularHighlights", 1f);
        // One rosette ≈ 10 m, centred on the fighters (floor = 20 m Unity plane).
        mat.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
        mat.SetTextureOffset("_BaseMap", new Vector2(0.5f, 0.5f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Texture2D ImportPanorama(StringBuilder sb)
    {
        var importer = AssetImporter.GetAtPath(PanoramaPath) as TextureImporter;
        if (importer == null) { sb.AppendLine($"  WARN  {PanoramaPath} not found — procedural sky used"); return null; }
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = true;
        importer.npotScale = TextureImporterNPOTScale.None; // keep the 2:1 equirectangular layout
        importer.mipmapEnabled = false;                     // no seam/blur artifacts at the wrap
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(PanoramaPath);
    }

    private static Material PanoramaMaterial(Texture2D panorama)
    {
        var shader = Shader.Find("Skybox/Panoramic");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PanoramaMatPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, PanoramaMatPath); }
        mat.shader = shader;
        mat.SetTexture("_MainTex", panorama);
        mat.SetFloat("_Mapping", 1f);   // latitude-longitude
        mat.SetFloat("_ImageType", 0f); // 360°
        mat.SetFloat("_MirrorOnBack", 0f);
        mat.SetFloat("_Layout", 0f);
        mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f)); // neutral
        mat.SetFloat("_Exposure", 1f);
        // Skybox/Panoramic shows image content at azimuth β − _Rotation (verified in a capture): put the painted
        // sun at SunAzimuthWanted.
        mat.SetFloat("_Rotation", Mathf.Repeat(SunAzimuthInImage - SunAzimuthWanted, 360f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>The same marble mosaic continuing as a vast plaza (one rosette every 10 m like the arena), slightly
    /// darker and less polished so the fighting floor still reads as the centre; it fades into the haze.</summary>
    private static Material PlazaMaterial(Texture2D baseColor, Texture2D normal, Texture2D occlusion)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PlazaMatPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, PlazaMatPath); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", baseColor);
        mat.SetColor("_BaseColor", new Color(0.82f, 0.80f, 0.78f));
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 0.3f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_OcclusionMap", occlusion);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetTextureScale("_BaseMap", new Vector2(40f, 40f));   // 400 m plane → one rosette per 10 m
        mat.SetTextureOffset("_BaseMap", new Vector2(0.5f, 0.5f));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.7f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material SkyMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        var shader = Shader.Find("Skybox/Procedural");
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, SkyMatPath); }
        mat.shader = shader;
        mat.SetFloat("_SunDisk", 2f);                              // high quality sun disk
        mat.SetFloat("_SunSize", 0.035f);
        mat.SetFloat("_SunSizeConvergence", 6f);
        mat.SetFloat("_AtmosphereThickness", 1.15f);
        mat.SetColor("_SkyTint", new Color(0.55f, 0.62f, 0.75f));
        mat.SetColor("_GroundColor", new Color(0.52f, 0.45f, 0.38f));
        mat.SetFloat("_Exposure", 1.25f);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
