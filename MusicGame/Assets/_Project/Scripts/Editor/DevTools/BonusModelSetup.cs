#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the Life / Special pickups from the 3D pipeline the same way as the other bonuses (Art/Bonuses/bonus_beat…):
///   AssetsMaterial/3D_pipeline/Output/&lt;name&gt;/&lt;name&gt;_{LOW_5k,MEDIUM_10k,MIDHIGH_20k,HIGH_30k,ULTRA_50k}.fbx (+ LOW textures)
///   → Art/Bonuses/&lt;name&gt;/ : the 5 qualities, Textures/, M_&lt;name&gt;.mat (copy of M_bonus_beat with these maps) and
///     &lt;name&gt;_LOW.asset (the LOW mesh, node transform baked, centred, largest side = 1 m, as the other bonuses)
///   → Prefabs/&lt;Kind&gt;Prefab.prefab from the placeholder (its RingController settings kept), Visual child like BeatPrefab,
///     assigned to MusicRunnerCollectiblesConfig.ringLife / ringSpecial.
/// Idempotent (copies only changed files; prefab rebuilt). Menu: Tools > MusicGame > Bonuses > Import Life + Special Models.
/// Batch: -executeMethod BonusModelSetup.RunFromCommandLine   (Logs/BonusModelSetup.txt)
/// </summary>
public static class BonusModelSetup
{
    private static readonly string[] Qualities = { "LOW_5k", "MEDIUM_10k", "MIDHIGH_20k", "HIGH_30k", "ULTRA_50k" };
    private const string ArtRoot = "Assets/_Project/Art/Bonuses";
    private const string TemplateMaterial = ArtRoot + "/bonus_beat/M_bonus_beat.mat";
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/MusicRunnerCollectiblesConfig.asset";
    private const float TargetDepth = 0.25f;   // flat pickups are thickened to this depth (as the other bonus prefabs)

    private static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../AssetsMaterial/3D_pipeline/Output"));

    private static readonly (string name, RingType ring, string prefab, string placeholder)[] Models =
    {
        ("bonus_special", RingType.Special, "SpecialPrefab", "SpecialPickup_Placeholder"),
        ("bonus_heart_2", RingType.Life, "LifePrefab", "LifePickup_Placeholder"),
    };

    [MenuItem("Tools/MusicGame/Bonuses/Import Life + Special Models")]
    public static void Menu() => Debug.Log(Run());

    public static void RunFromCommandLine()
    {
        string report = Run();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/BonusModelSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var log = new StringBuilder("[BonusModelSetup]\n");
        var config = AssetDatabase.LoadAssetAtPath<MusicRunnerCollectiblesConfig>(ConfigPath);
        if (config == null) { log.AppendLine($"  FAIL  {ConfigPath} missing"); return log.ToString(); }
        Probe(ArtRoot + "/bonus_beat/bonus_beat_LOW_5k.fbx", ArtRoot + "/bonus_beat/bonus_beat_LOW.asset", log);
        foreach (var m in Models)
        {
            try
            {
                var prefab = Import(m.name, m.prefab, m.placeholder, log);
                if (prefab == null) continue;
                if (m.ring == RingType.Life) config.ringLife = prefab; else config.ringSpecial = prefab;
                log.AppendLine($"  PASS  {m.ring}: {AssetDatabase.GetAssetPath(prefab)}");
            }
            catch (System.Exception e) { log.AppendLine($"  FAIL  {m.name}: {e}"); }
        }
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        log.Append(TextureSourceShrinker.Run(ArtRoot));   // sources down to their import size
        return log.ToString();
    }

    private static GameObject Import(string name, string prefabName, string placeholderName, StringBuilder log)
    {
        string src = Path.Combine(OutputRoot, name), dst = $"{ArtRoot}/{name}";
        if (!Directory.Exists(src)) { log.AppendLine($"  FAIL  missing {src}"); return null; }
        Directory.CreateDirectory(Path.GetFullPath(dst + "/Textures"));

        foreach (var q in Qualities)
        {
            string fbx = $"{dst}/{name}_{q}.fbx";
            if (!Copy(Path.Combine(src, $"{name}_{q}.fbx"), fbx, log)) continue;
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation || importer.importCameras || importer.importLights)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
        }
        string texSrc = Path.Combine(src, "Textures", "LOW");                       // shared set (texture_share.py)
        if (!Directory.Exists(texSrc)) texSrc = Path.Combine(src, $"{name}_LOW_5k_Textures");
        foreach (var t in new[] { "BaseColor", "MetallicSmoothness", "Normal" })
        {
            string path = $"{dst}/Textures/{t}.png";
            if (!Copy(Path.Combine(texSrc, t + ".png"), path, log)) return null;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = t == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = t == "BaseColor";
            if (ti.textureType != type || ti.sRGBTexture != srgb || ti.maxTextureSize != 2048)
            {
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
        }

        // Material: the other bonuses' material with this model's maps.
        string matPath = $"{dst}/M_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            AssetDatabase.CopyAsset(TemplateMaterial, matPath);
            mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/Textures/BaseColor.png"));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/Textures/Normal.png"));
        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/Textures/MetallicSmoothness.png"));
        EditorUtility.SetDirty(mat);

        // Mesh: LOW, baked and normalised like bonus_beat_LOW.asset.
        var mesh = BakeNormalised($"{dst}/{name}_LOW_5k.fbx", log);
        if (mesh == null) return null;
        mesh.name = $"{name}_LOW";
        string meshPath = $"{dst}/{name}_LOW.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
        else AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();
        var ext = mesh.bounds.extents;
        log.AppendLine($"  mesh  {meshPath}: extents {ext.x:0.###} × {ext.y:0.###} × {ext.z:0.###}");

        // Prefab: the placeholder (RingController settings, collider) with a Visual child like BeatPrefab.
        string placeholder = $"Assets/_Project/Prefabs/Placeholders/{placeholderName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(placeholder) == null) { log.AppendLine($"  FAIL  missing {placeholder}"); return null; }
        var root = PrefabUtility.LoadPrefabContents(placeholder);
        try
        {
            root.name = prefabName;
            Object.DestroyImmediate(root.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(root.GetComponent<MeshFilter>());
            var old = root.transform.Find("Visual");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var visual = new GameObject("Visual", typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            float zScale = Mathf.Max(1f, TargetDepth / Mathf.Max(ext.z * 2f, 1e-4f));
            visual.transform.localScale = new Vector3(1f, 1f, zScale);
            visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var box = root.GetComponent<BoxCollider>();
            if (box != null) { box.center = Vector3.zero; box.size = new Vector3(ext.z * 2f * zScale, ext.y * 2f, ext.x * 2f); }
            string path = $"Assets/_Project/Prefabs/{prefabName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            return saved;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>The fbx's meshes in its root space (node transforms baked), centred, largest side = 1.</summary>
    private static Mesh BakeNormalised(string fbxPath, StringBuilder log)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) { log.AppendLine($"  FAIL  cannot load {fbxPath}"); return null; }
        var filters = model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
        if (filters.Length == 0) { log.AppendLine($"  FAIL  {fbxPath} has no mesh"); return null; }
        var combine = filters.Select(f => new CombineInstance
        {
            mesh = f.sharedMesh,
            transform = model.transform.worldToLocalMatrix * f.transform.localToWorldMatrix,
        }).ToArray();
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine, true, true);
        mesh.RecalculateBounds();
        var b = mesh.bounds;
        float scale = 0.5f / Mathf.Max(b.extents.x, b.extents.y, b.extents.z, 1e-5f);   // flat ones: width / height; a solid one (the Special knot): any side
        var v = mesh.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) * scale;
        mesh.vertices = v;
        mesh.RecalculateBounds();
        return mesh;
    }

    // The existing bonus as a reference: its fbx baked the same way vs the hand-made LOW mesh.
    private static void Probe(string fbx, string asset, StringBuilder log)
    {
        var baked = BakeNormalised(fbx, log);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(asset);
        if (baked != null && existing != null)
            log.AppendLine($"  check bonus_beat: baked extents {baked.bounds.extents} vs existing {existing.bounds.extents}");
        if (baked != null) Object.DestroyImmediate(baked);
    }

    private static bool Copy(string source, string assetPath, StringBuilder log)
    {
        if (!File.Exists(source)) { log.AppendLine($"  FAIL  missing {source}"); return false; }
        string full = Path.GetFullPath(assetPath);
        if (!File.Exists(full) || new FileInfo(full).Length != new FileInfo(source).Length)
        {
            File.Copy(source, full, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        return true;
    }
}
#endif
