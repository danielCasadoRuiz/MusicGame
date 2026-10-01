using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev diagnostic for the Runner ground-window rebuild ("segment") seam: builds the real world from a
/// cached SongProfile, rebuilds the window at the same trigger points gameplay does, and compares the
/// ground mesh vertices of the previous window vs the new one at the SAME global rows (behind, under
/// and ahead of the player). Also times the main-thread cost of each rebuild (collider bake excluded:
/// it runs on a worker).
/// Batch: -executeMethod GroundSeamDiagnostic.RunFromCommandLine (Logs/GroundSeamDiagnostic.txt).
/// </summary>
public static class GroundSeamDiagnostic
{
    private const string ConfigPath = "Assets/_Project/Configs/Gameplay/MusicRunner/_MusicRunnerGameplayConfig.asset";
    private const float Behind = 20f; // MusicWorldManager.GroundWindowBehind

    public static void RunFromCommandLine()
    {
        var sb = new StringBuilder("[GroundSeamDiagnostic]\n");
        try { Run(sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText("Logs/GroundSeamDiagnostic.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    // Mirrors MusicWorldManager.RebuildGroundWindow's window placement (constant 80 m span).
    private static int Row0(float center, float total, float rowSpacing) =>
        Mathf.FloorToInt(Mathf.Clamp(center - Behind, 0f, Mathf.Max(0f, total - 80f)) / rowSpacing);

    private static void Run(StringBuilder sb)
    {
        string cacheDir = Path.Combine(Application.persistentDataPath, "SongCache");
        string file = Directory.GetFiles(cacheDir, "BillieJean*.json")[0];
        var profile = JsonUtility.FromJson<SongProfileData>(File.ReadAllText(file)).ToProfile();
        var config = AssetDatabase.LoadAssetAtPath<MusicRunnerGameplayConfig>(ConfigPath);
        sb.AppendLine($"profile {Path.GetFileName(file)}, duration {profile.duration:0.0}s");

        var go = new GameObject("WorldDiag");
        var world = go.AddComponent<MusicWorldManager>();
        world.Initialize(config);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var build = System.Diagnostics.Stopwatch.StartNew();
        typeof(MusicWorldManager).GetMethod("BuildWorld", flags).Invoke(world, new object[] { profile });
        build.Stop();
        sb.AppendLine($"BuildWorld (incl. whole-path height grid): {build.Elapsed.TotalMilliseconds:0.0} ms");
        var gridSw = System.Diagnostics.Stopwatch.StartNew();
        typeof(MusicWorldManager).GetMethod("ComputeGlobalGrid", flags).Invoke(world, null);
        gridSw.Stop();
        sb.AppendLine($"  of which ComputeGlobalGrid (whole {world.Path.TotalLength:0} m path): {gridSw.Elapsed.TotalMilliseconds:0.0} ms");
        var rebuild = typeof(MusicWorldManager).GetMethod("RebuildGroundWindow", flags);
        var applyPending = typeof(MusicWorldManager).GetMethod("ApplyPendingCollider", flags);
        var mesh = ((GameObject)typeof(MusicWorldManager).GetField("_groundGO", flags).GetValue(world)).GetComponent<MeshFilter>().sharedMesh;
        float rowSpacing = 1f / config.levelGeneration.longitudinalSegmentsPerMeter;
        int cols = config.levelGeneration.crossMeshSegments + 1;

        float worst = 0f, worstMs = 0f, worstAfterWarmup = 0f, totalMs = 0f; int n = 0;
        float prevCenter = 0f;
        for (int k = 1; k <= 40; k++)
        {
            var oldVerts = mesh.vertices;
            int oldRow0 = Row0(prevCenter, world.Path.TotalLength, rowSpacing);
            float center = k * 15.0001f; // first distance at which Update() triggers a rebuild

            applyPending.Invoke(world, null); // previous bake finished (a real frame gap is ~1.5 s)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rebuild.Invoke(world, new object[] { center, false });
            sw.Stop();
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            totalMs += ms; n++; worstMs = Mathf.Max(worstMs, ms);
            if (k > 2) worstAfterWarmup = Mathf.Max(worstAfterWarmup, ms);

            var newVerts = mesh.vertices;
            int newRow0 = Row0(center, world.Path.TotalLength, rowSpacing);
            float maxDelta = 0f;
            int newRows = newVerts.Length / cols, oldRows = oldVerts.Length / cols;
            for (int r = 0; r < newRows; r++)
            {
                int ro = newRow0 + r - oldRow0;
                if (ro < 0 || ro >= oldRows) continue;
                for (int c = 0; c < cols; c++)
                    maxDelta = Mathf.Max(maxDelta, (newVerts[r * cols + c] - oldVerts[ro * cols + c]).magnitude);
            }
            worst = Mathf.Max(worst, maxDelta);
            if (k <= 6) sb.AppendLine($"  rebuild @{center,7:0.0} m: max vertex displacement in overlap {maxDelta:0.000000} m, main thread {ms:0.0} ms");
            prevCenter = center;
        }
        applyPending.Invoke(world, null);
        sb.AppendLine($"WORST vertex displacement across {n} rebuilds: {worst:0.000000} m; main thread mean {totalMs / n:0.0} ms, worst {worstMs:0.0} ms (after the first 2 / JIT warm-up: {worstAfterWarmup:0.0} ms)");
        Object.DestroyImmediate(go);
    }
}
