using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the Mixamo fight pack (CombatAnimationImporter) and measures every Mixamo_* clip on the canonical
/// MakeHuman avatar: length, and per limb the moment of its farthest forward extension past the hips (+ that
/// limb's height) — the numbers FightFeelSetup's Mixamo windows are cut from. Edit mode, no save touched.
/// Batch: -executeMethod MixamoStrikeScan.RunFromCommandLine  (Logs/MixamoStrikeScan.txt)
/// </summary>
public static class MixamoStrikeScan
{
    private const string AvatarPath = "Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx";

    public static void RunFromCommandLine()
    {
        string report;
        try { report = Run(); } catch (System.Exception e) { report = "FAIL " + e; }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/MixamoStrikeScan.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.StartsWith("FAIL") ? 1 : 0);
    }

    public static string Run()
    {
        var log = new StringBuilder("[MixamoStrikeScan]\n");
        log.AppendLine(CombatAnimationImporter.ImportAll(out bool ok).Split('\n').Where(l => l.Contains("Mixamo") || l.Contains("FAIL") || l.Contains("ERROR")).Aggregate("", (a, b) => a + "  " + b.Trim() + "\n"));
        log.AppendLine($"  import ok {ok}");
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath);
        if (library == null || model == null) return "FAIL library / avatar";
        var go = (GameObject)Object.Instantiate(model);
        go.hideFlags = HideFlags.DontSave;
        var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        a.applyRootMotion = false;
        try
        {
            // Also the clips the fight uses today (animation set), to compare their strike direction.
            var animSet = AssetDatabase.LoadAssetAtPath<FighterAnimationSetSO>(CombatContentSetup.AnimationSetPath);
            var used = animSet != null ? animSet.clips.Where(c => c != null && c.clip != null).Select(c => (c.role.ToString(), c.clip)) : Enumerable.Empty<(string, AnimationClip)>();
            var items = library.entries.Where(x => x != null && x.name.StartsWith("Mixamo_")).OrderBy(x => x.name)
                               .Select(x => (x.name, x.inPlaceClip != null ? x.inPlaceClip : x.clip)).Concat(used.Select(u => ("[set] " + u.Item1, u.Item2)));
            foreach (var (label, clipItem) in items)
            {
                var e = new { name = label };
                var clip = clipItem;
                if (clip == null) { log.AppendLine($"  {e.name}: no clip"); continue; }
                Sample(a, clip, 0f);
                Vector3 hips0 = Bone(a, HumanBodyBones.Hips);
                Vector3 hands0 = (Bone(a, HumanBodyBones.LeftHand) + Bone(a, HumanBodyBones.RightHand)) * 0.5f;
                Vector3 guard = hands0 - hips0; guard.y = 0f;
                float guardYaw = Mathf.Atan2(guard.x, guard.z) * Mathf.Rad2Deg; // where the guard hands point (0 = straight at the opponent)
                Vector3 fwd = go.transform.forward;                                // the GAME's forward: the root's +Z
                var limbs = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                var best = new float[4]; var bestT = new float[4]; var bestY = new float[4]; var start = new float[4]; var bestYaw = new float[4];
                for (int i = 0; i < 4; i++) best[i] = float.MinValue;
                float maxTravel = 0f;
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 30f)
                {
                    Sample(a, clip, t);
                    Vector3 hips = Bone(a, HumanBodyBones.Hips);
                    maxTravel = Mathf.Max(maxTravel, Vector3.Dot(hips - hips0, fwd));
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = Bone(a, limbs[i]);
                        float ext = Vector3.Dot(p - hips0, fwd);   // from the STARTING hips: a step-in counts
                        if (t == 0f) start[i] = ext;
                        if (ext > best[i]) { best[i] = ext; bestT[i] = t; bestY[i] = p.y; Vector3 d = p - hips0; bestYaw[i] = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
                    }
                }
                int k = 0;
                for (int i = 1; i < 4; i++) if (best[i] - start[i] > best[k] - start[k]) k = i;
                log.AppendLine($"  {e.name,-34} len {clip.length:0.00}s  strike {limbs[k],-9} peak {bestT[k]:0.00}s ext {best[k]:0.00} m (from {start[k]:0.00}) height {bestY[k]:0.00} m  STRIKE YAW {bestYaw[k]:+0;-0}°  guard yaw {guardYaw:+0;-0}°  hips travel {maxTravel:0.00} m" +
                               $"  | L hand {bestT[0]:0.00}/{best[0]:0.00} R hand {bestT[1]:0.00}/{best[1]:0.00} L foot {bestT[2]:0.00}/{best[2]:0.00} R foot {bestT[3]:0.00}/{best[3]:0.00}");
            }
        }
        finally { Object.DestroyImmediate(go); }
        return log.ToString();
    }

    private static Vector3 Bone(Animator a, HumanBodyBones b) { var t = a.GetBoneTransform(b); return t != null ? t.position : Vector3.zero; }

    private static void Sample(Animator a, AnimationClip clip, float t)
    {
        var root = a.transform;
        Vector3 p = root.position; Quaternion r = root.rotation;
        clip.SampleAnimation(a.gameObject, Mathf.Min(t, clip.length));
        root.SetPositionAndRotation(p, r);
    }
}
