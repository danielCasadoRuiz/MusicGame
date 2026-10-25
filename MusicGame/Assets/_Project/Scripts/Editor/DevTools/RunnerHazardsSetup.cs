#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds (or refreshes) the hazard prefabs — six block obstacles and the chaser — in Assets/_Project/Prefabs/Hazards,
/// their placeholder materials, and RunnerHazardsConfig.asset, and assigns that config to the root
/// _MusicRunnerGameplayConfig. Re-running keeps the dimension values already edited on existing prefabs (only
/// the preview parts are rebuilt) and the values of an existing config asset.
/// Menu: Tools > MusicGame > Runner > Build Hazard Prefabs. Batch: -executeMethod RunnerHazardsSetup.RunFromCommandLine
/// </summary>
public static class RunnerHazardsSetup
{
    private const string Folder = "Assets/_Project/Prefabs/Hazards";
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/RunnerHazardsConfig.asset";
    private const string RootConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";

    [MenuItem("Tools/MusicGame/Runner/Build Hazard Prefabs")]
    public static void Run() => Debug.Log(Build());

    public static void RunFromCommandLine()
    {
        int code = 0;
        try { File.WriteAllText("Logs/RunnerHazardsSetup.txt", Build()); }
        catch (System.Exception e) { File.WriteAllText("Logs/RunnerHazardsSetup.txt", e.ToString()); Debug.LogException(e); code = 1; }
        EditorApplication.Exit(code);
    }

    public static string Build()
    {
        Directory.CreateDirectory(Folder);
        var obstacleMat = Mat("Hazard_Obstacle", new Color(0.42f, 0.44f, 0.52f), Color.black, 0.35f);
        var chaserMat   = Mat("Hazard_ChaserBody", new Color(0.02f, 0.02f, 0.025f), Color.black, 0.85f);
        var glowMat     = Mat("Hazard_ChaserGlow", new Color(0.5f, 0.02f, 0.02f), new Color(2f, 0.05f, 0.02f), 0.5f);

        var cfg = AssetDatabase.LoadAssetAtPath<RunnerHazardsConfig>(ConfigPath);
        if (cfg == null) { cfg = ScriptableObject.CreateInstance<RunnerHazardsConfig>(); AssetDatabase.CreateAsset(cfg, ConfigPath); }

        cfg.lowBlockPrefab   = Obstacle("Obstacle_LowBlock",   RunnerObstacleKind.LowBlock,   obstacleMat);
        cfg.highBlockPrefab  = Obstacle("Obstacle_HighBlock",  RunnerObstacleKind.HighBlock,  obstacleMat);
        cfg.portalPrefab     = Obstacle("Obstacle_Portal",     RunnerObstacleKind.Portal,     obstacleMat);
        cfg.windowWallPrefab = Obstacle("Obstacle_WindowWall", RunnerObstacleKind.WindowWall, obstacleMat);
        cfg.crawlWallPrefab  = Obstacle("Obstacle_CrawlWall",  RunnerObstacleKind.CrawlWall,  obstacleMat);
        cfg.uShapePrefab     = Obstacle("Obstacle_UShape",     RunnerObstacleKind.UShape,     obstacleMat);
        cfg.chaserPrefab     = Chaser(chaserMat, glowMat);
        EditorUtility.SetDirty(cfg);

        var root = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(RootConfigPath);
        if (root != null) { root.hazards = cfg; EditorUtility.SetDirty(root); }
        AssetDatabase.SaveAssets();
        return $"RunnerHazardsSetup: 6 obstacle prefabs + chaser in {Folder}; config {ConfigPath}; root config {(root != null ? "assigned" : "NOT FOUND")}.";
    }

    private static Material Mat(string name, Color baseColor, Color emission, float smoothness)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", baseColor);
        m.SetColor("_Color", baseColor);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static RunnerObstacle Obstacle(string name, RunnerObstacleKind kind, Material mat)
    {
        string path = $"{Folder}/{name}.prefab";
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        GameObject go = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);
        var o = go.GetComponent<RunnerObstacle>() ?? go.AddComponent<RunnerObstacle>();
        o.kind = kind;
        var so = new SerializedObject(o);
        so.FindProperty("material").objectReferenceValue = mat;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Preview parts for a 7 m track (rebuilt for the real width at runtime).
        for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        o.AdoptExistingParts();
        o.Build(7f, kind == RunnerObstacleKind.LowBlock || kind == RunnerObstacleKind.HighBlock ? -1.5f : 0f);
        var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        if (exists) PrefabUtility.UnloadPrefabContents(go); else Object.DestroyImmediate(go);
        return saved.GetComponent<RunnerObstacle>();
    }

    private static RunnerChaser Chaser(Material body, Material glow)
    {
        string path = $"{Folder}/Hazard_Chaser.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<RunnerChaser>(path);
        if (existing != null) return existing; // keep hand edits; delete the prefab to rebuild it
        var go = new GameObject("Hazard_Chaser");
        var c = go.AddComponent<RunnerChaser>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Body";
        Object.DestroyImmediate(cube.GetComponent<Collider>()); // contact is resolved by distance, never physics
        cube.GetComponent<MeshRenderer>().sharedMaterial = body;
        cube.transform.SetParent(go.transform, false);
        var glows = new System.Collections.Generic.List<Renderer>();
        // A red slit "eye" on each side face — reads as a menace, never as a bonus.
        for (int side = 0; side < 4; side++)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eye.name = "Eye" + side;
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.GetComponent<MeshRenderer>().sharedMaterial = glow;
            eye.transform.SetParent(go.transform, false);
            var rot = Quaternion.Euler(0f, side * 90f, 0f);
            eye.transform.localRotation = rot;
            eye.transform.localPosition = rot * new Vector3(0f, 0.12f, 0.505f);
            eye.transform.localScale = new Vector3(0.6f, 0.08f, 0.02f);
            glows.Add(eye.GetComponent<MeshRenderer>());
        }
        var so = new SerializedObject(c);
        var arr = so.FindProperty("glowRenderers");
        arr.arraySize = glows.Count;
        for (int i = 0; i < glows.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = glows[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return saved.GetComponent<RunnerChaser>();
    }
}
#endif
