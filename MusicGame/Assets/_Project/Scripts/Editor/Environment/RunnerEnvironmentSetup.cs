using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the Runner environment module content (idempotent; existing assets are reused, prefab
/// settings refreshed):
///   Prefabs/Environment/PF_HorizonMountains_Default  (HorizonMountainLayers, uses the Runner's HorizonConfig)
///   Prefabs/Environment/PF_SpectrumBars_Default      (SpectrumBars3D, uses the Runner's HorizonConfig)
///   Prefabs/Environment/PF_HorizonMountains_Alt      (same system, own HorizonConfig_AltMountains)
///   Prefabs/Environment/PF_SpectrumBars_Alt          (same system, own HorizonConfig_AltSpectrum)
///   Configs/Environment/RunnerEnvironment_Base       (default modules) → BaseTheme.runnerEnvironment
///   Configs/Environment/Styles/RunnerEnvStyle_TestA_Inherit | _TestB_NoSpectrum | _TestC_Override
/// Menu: Tools > MusicGame > Environment > Setup Runner Environment Modules.
/// Batch: RunnerEnvironmentSetup.SetupFromCommandLine.
/// </summary>
public static class RunnerEnvironmentSetup
{
    private const string PrefabDir = "Assets/_Project/Prefabs/Environment";
    private const string ConfigDir = "Assets/_Project/Configs/Environment";
    private const string StyleDir  = ConfigDir + "/Styles";
    private const string BaseHorizonConfigPath = "Assets/_Project/Configs/Gameplay/Environment/HorizonConfig.asset";
    private const string BaseThemePath = "Assets/_Project/Configs/Theme/Base/BaseTheme.asset";

    [MenuItem("Tools/MusicGame/Environment/Setup Runner Environment Modules")]
    public static void SetupMenu() => Debug.Log(Setup());

    public static void SetupFromCommandLine()
    {
        string report = Setup();
        File.WriteAllText("Logs/RunnerEnvironmentSetup.txt", report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup()
    {
        var sb = new StringBuilder("[RunnerEnvironmentSetup]\n");
        EnsureFolder(PrefabDir); EnsureFolder(ConfigDir); EnsureFolder(StyleDir);

        var baseHorizon = AssetDatabase.LoadAssetAtPath<HorizonConfig>(BaseHorizonConfigPath);
        if (baseHorizon == null) return sb.AppendLine($"  ERROR  {BaseHorizonConfigPath} not found").ToString();

        // Alternative settings = copies of the real HorizonConfig with a few visible changes.
        var altMountains = CopyConfig(baseHorizon, "HorizonConfig_AltMountains", c =>
        {
            c.horizonMountainFarScale  = new Vector2(c.horizonMountainFarScale.x * 1.15f, c.horizonMountainFarScale.y * 2.2f);
            c.horizonMountainNearScale = new Vector2(c.horizonMountainNearScale.x * 1.15f, c.horizonMountainNearScale.y * 2.0f);
            c.horizonMountainFarVerticalOffset  = c.horizonMountainFarVerticalOffset + 2.5f;
            c.horizonMountainNearVerticalOffset = c.horizonMountainNearVerticalOffset + 1.0f;
            c.horizonMountainFarTint  = new Color(0.95f, 0.45f, 0.20f, 1f);
            c.horizonMountainNearTint = new Color(0.30f, 0.07f, 0.03f, 1f);
            c.horizonMountainFarTintIntense  = new Color(1.00f, 0.60f, 0.25f, 1f);
            c.horizonMountainNearTintIntense = new Color(0.45f, 0.10f, 0.04f, 1f);
        }, sb);
        var altSpectrum = CopyConfig(baseHorizon, "HorizonConfig_AltSpectrum", c =>
        {
            c.horizonBarCount = 20;
            c.horizonArcSpanDegrees = 110f;
            c.horizonArcRadius = c.horizonArcRadius * 0.75f;
            c.horizonBarWidth = 1.3f;
            c.horizonBarDepth = 1.3f;
            c.horizonBarMaxHeight = c.horizonBarMaxHeight * 1.8f;
            c.horizonBarPlasticColor = new Color(1f, 0.25f, 0.65f, 1f);
        }, sb);

        var mountainsDefault = SavePrefab<HorizonMountainLayers>("PF_HorizonMountains_Default", null, sb);
        var barsDefault      = SavePrefab<SpectrumBars3D>("PF_SpectrumBars_Default", null, sb);
        var mountainsAlt     = SavePrefab<HorizonMountainLayers>("PF_HorizonMountains_Alt", altMountains, sb);
        var barsAlt          = SavePrefab<SpectrumBars3D>("PF_SpectrumBars_Alt", altSpectrum, sb);

        var baseEnv = LoadOrCreate<RunnerEnvironmentBaseSO>($"{ConfigDir}/RunnerEnvironment_Base.asset");
        baseEnv.horizonPrefab = mountainsDefault;
        baseEnv.musicReactivePrefab = barsDefault;
        EditorUtility.SetDirty(baseEnv);

        Style("RunnerEnvStyle_TestA_Inherit", EnvironmentOverrideMode.Inherit, null, EnvironmentOverrideMode.Inherit, null, sb);
        Style("RunnerEnvStyle_TestB_NoSpectrum", EnvironmentOverrideMode.Inherit, null, EnvironmentOverrideMode.Disabled, null, sb);
        Style("RunnerEnvStyle_TestC_Override", EnvironmentOverrideMode.Override, mountainsAlt, EnvironmentOverrideMode.Override, barsAlt, sb);

        var baseTheme = AssetDatabase.LoadAssetAtPath<BaseThemeSO>(BaseThemePath);
        if (baseTheme == null) sb.AppendLine($"  ERROR  {BaseThemePath} not found");
        else
        {
            baseTheme.runnerEnvironment = baseEnv;
            EditorUtility.SetDirty(baseTheme);
            sb.AppendLine($"  PASS  BaseTheme.runnerEnvironment = {baseEnv.name} (Horizon {mountainsDefault.name}, MusicReactive {barsDefault.name})");
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine($"  test style selector: {baseEnv.name} → Test Style Override (currently {(baseEnv.testStyleOverride != null ? baseEnv.testStyleOverride.name : "none = base")})");
        return sb.ToString();
    }

    private static HorizonConfig CopyConfig(HorizonConfig source, string name, System.Action<HorizonConfig> tweak, StringBuilder sb)
    {
        string path = $"{ConfigDir}/{name}.asset";
        var copy = AssetDatabase.LoadAssetAtPath<HorizonConfig>(path);
        if (copy == null)
        {
            copy = Object.Instantiate(source);
            AssetDatabase.CreateAsset(copy, path);
        }
        else EditorUtility.CopySerialized(source, copy); // stay in sync with the real config, then re-tweak
        tweak(copy);
        EditorUtility.SetDirty(copy);
        sb.AppendLine($"  {name}: copy of HorizonConfig + visible tweaks");
        return copy;
    }

    private static GameObject SavePrefab<T>(string name, HorizonConfig settings, StringBuilder sb) where T : Component
    {
        var go = new GameObject(name);
        var module = go.AddComponent<T>();
        if (settings != null)
        {
            var so = new SerializedObject(module);
            so.FindProperty("settingsOverride").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        sb.AppendLine($"  PASS  {path} ({typeof(T).Name}{(settings != null ? $", settings {settings.name}" : ", Runner's HorizonConfig")})");
        return prefab;
    }

    private static void Style(string name, EnvironmentOverrideMode horizonMode, GameObject horizon,
                              EnvironmentOverrideMode reactiveMode, GameObject reactive, StringBuilder sb)
    {
        var style = LoadOrCreate<RunnerEnvironmentStyleSO>($"{StyleDir}/{name}.asset");
        style.horizon = new EnvironmentModuleOverride { mode = horizonMode, prefab = horizon };
        style.musicReactive = new EnvironmentModuleOverride { mode = reactiveMode, prefab = reactive };
        EditorUtility.SetDirty(style);
        sb.AppendLine($"  PASS  {name}: Horizon {horizonMode}{(horizon != null ? " " + horizon.name : "")}, MusicReactive {reactiveMode}{(reactive != null ? " " + reactive.name : "")}");
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
        return asset;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
