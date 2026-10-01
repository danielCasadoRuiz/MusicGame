using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode check of the Runner environment modules on the REAL Horizon World pieces
/// (HorizonCameraController + HorizonWater + RunnerEnvironmentController) with a real cached song:
/// base, Test A (inherit), Test B (no spectrum), Test C (override) — module presence, no leftovers
/// across rebuilds, bars reacting to the song, one Horizon-camera capture per case
/// (Logs/RunnerEnv_*.png). Batch: RunnerEnvironmentCheck.RunFromCommandLine (no -quit).
/// </summary>
[InitializeOnLoad]
public static class RunnerEnvironmentCheck
{
    private const string PendingKey = "RunnerEnvironmentCheck.Pending";
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private const string StyleDir = "Assets/_Project/Configs/Environment/Styles/";
    private static readonly StringBuilder Log = new();
    private static int _fails;

    static RunnerEnvironmentCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            _ = Run();
        };
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static async Task Run()
    {
        Log.Clear(); _fails = 0;
        try { await Body(); }
        catch (System.Exception e) { Check("Exception", false, e.ToString()); }
        string report = (_fails == 0 ? "[RunnerEnvironmentCheck] ALL PASSED\n" : $"[RunnerEnvironmentCheck] {_fails} FAILED\n") + Log;
        File.WriteAllText("Logs/RunnerEnvironmentCheck.txt", report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    private static async Task Body()
    {
        foreach (var p in Object.FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var gameplay = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        var horizon = gameplay.environment.horizon;
        var appConfig = Resources.Load<AppConfigSO>("AppConfig");
        var baseEnv = appConfig.theme.baseTheme.runnerEnvironment;
        Check("BaseTheme.runnerEnvironment assigned", baseEnv != null && baseEnv.horizonPrefab != null && baseEnv.musicReactivePrefab != null);

        // Real song world (band energies the bars read) — MusicClock absent, song time driven here.
        string file = Directory.GetFiles(Path.Combine(Application.persistentDataPath, "SongCache"), "BillieJean*.json")[0];
        var profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file)).ToProfile();
        var worldGO = new GameObject("TestWorld");
        var world = worldGO.AddComponent<MusicWorldManager>();
        world.Initialize(gameplay);
        typeof(MusicWorldManager).GetMethod("BuildWorld", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world, new object[] { profile });

        var cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>(); }
        cam.transform.SetPositionAndRotation(new Vector3(0f, 2f, -6f), Quaternion.identity);
        var host = new GameObject("HorizonHost");
        var camCtl = host.AddComponent<HorizonCameraController>();
        camCtl.Initialize(horizon);
        Check("Horizon camera active (layer + main camera)", camCtl.IsActive);
        var water = host.AddComponent<HorizonWater>();
        water.Initialize(horizon, camCtl.HorizonRoot);
        var env = host.AddComponent<RunnerEnvironmentController>();

        var cases = new (string label, RunnerEnvironmentStyleSO style, bool mountains, bool bars)[]
        {
            ("Base", null, true, true),
            ("TestA_Inherit", Load("RunnerEnvStyle_TestA_Inherit"), true, true),
            ("TestB_NoSpectrum", Load("RunnerEnvStyle_TestB_NoSpectrum"), true, false),
            ("TestC_Override", Load("RunnerEnvStyle_TestC_Override"), true, true),
        };
        foreach (var c in cases)
        {
            env.Build(baseEnv, c.style, camCtl.HorizonRoot, horizon, water);
            await Task.Yield(); // let the previous build's Destroy() complete

            int roots = camCtl.HorizonRoot.Cast<Transform>().Count(t => t.name == "[Runner Environment]");
            var mountains = camCtl.HorizonRoot.GetComponentsInChildren<HorizonMountainLayers>(true);
            var bars = camCtl.HorizonRoot.GetComponentsInChildren<SpectrumBars3D>(true);
            int barObjects = bars.Sum(b => b.transform.Find("SpectrumBarsRoot")?.childCount ?? 0);
            Check($"{c.label}: one environment root, mountains {(c.mountains ? "present" : "absent")}, bars {(c.bars ? "present" : "absent")}",
                  roots == 1 && (mountains.Length == 1) == c.mountains && (bars.Length == 1) == c.bars && (barObjects > 0) == c.bars,
                  $"roots {roots}, mountains {mountains.Length}, bar modules {bars.Length}, bar objects {barObjects}; {env.Summary}");

            // Music reaction: bar heights must follow the song (vary over song time).
            float minH = float.MaxValue, maxH = 0f;
            for (int f = 0; f < 90; f++)
            {
                float songTime = 20f + f * (1f / 30f);
                camCtl.Tick(); water.Tick();
                env.Tick(RunnerEnvironmentSlot.MusicReactive, world, songTime);
                env.Tick(RunnerEnvironmentSlot.Horizon, world, songTime);
                if (bars.Length > 0)
                {
                    var bar = bars[0].transform.Find("SpectrumBarsRoot").GetChild(4);
                    minH = Mathf.Min(minH, bar.localScale.y); maxH = Mathf.Max(maxH, bar.localScale.y);
                }
                await Task.Yield();
            }
            if (c.bars) Check($"{c.label}: bars react to the song", maxH - minH > 0.05f, $"bar #4 height {minH:0.00}..{maxH:0.00}");
            Capture(camCtl.HorizonCamera, cam, $"RunnerEnv_{c.label}");
        }

        // Override with a missing prefab → warning + base module (graceful).
        var broken = ScriptableObject.CreateInstance<RunnerEnvironmentStyleSO>();
        broken.name = "BrokenOverride";
        broken.horizon = new EnvironmentModuleOverride { mode = EnvironmentOverrideMode.Override, prefab = null };
        broken.musicReactive = new EnvironmentModuleOverride { mode = EnvironmentOverrideMode.Disabled };
        env.Build(baseEnv, broken, camCtl.HorizonRoot, horizon, water);
        await Task.Yield();
        Check("Override without prefab falls back to the base module; Disabled builds nothing",
              env.HasModule(RunnerEnvironmentSlot.Horizon) && !env.HasModule(RunnerEnvironmentSlot.MusicReactive), env.Summary);
        Object.Destroy(host);
        await Task.Yield();
        Check("destroying the controller removes every module", Object.FindObjectsByType<SpectrumBars3D>(FindObjectsSortMode.None).Length == 0 &&
                                                                Object.FindObjectsByType<HorizonMountainLayers>(FindObjectsSortMode.None).Length == 0);
    }

    private static RunnerEnvironmentStyleSO Load(string name) => AssetDatabase.LoadAssetAtPath<RunnerEnvironmentStyleSO>(StyleDir + name + ".asset");

    private static void Capture(Camera horizonCam, Camera main, string file)
    {
        int mask = main.cullingMask;
        main.cullingMask = 0; // only the Horizon World in the shot
        var rt = new RenderTexture(960, 400, 24);
        horizonCam.targetTexture = rt;
        horizonCam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 400, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 400), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        horizonCam.targetTexture = null;
        main.cullingMask = mask;
        File.WriteAllBytes($"Logs/{file}.png", tex.EncodeToPNG());
        Object.Destroy(rt); Object.Destroy(tex);
    }
}
