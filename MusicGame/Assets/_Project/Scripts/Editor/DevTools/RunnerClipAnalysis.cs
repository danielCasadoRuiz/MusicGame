using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev tool: samples Humanoid clips on the canonical MakeHuman avatar (edit mode) and reports, per
/// time step, hips height, body yaw and hand activity — used to pick segment windows (jump apex,
/// landing, active gesture stretches) out of long mocap takes without guessing.
/// Batch: -executeMethod RunnerClipAnalysis.AnalyzeFromCommandLine (Logs/RunnerClipAnalysis.txt).
/// </summary>
public static class RunnerClipAnalysis
{
    private const string BaseAvatarPath = "Assets/_Project/Avatar/MakeHuman/Content/BaseAvatar_MakeHuman.asset";

    private static readonly (string entry, float step)[] Targets =
    {
        ("Fight_RoundHouseKick_mixamo", 0.05f),
    };

    public static void AnalyzeFromCommandLine()
    {
        var sb = new StringBuilder("[RunnerClipAnalysis] t | hipsY | yaw | handsY(rel hips) | handSpeed | footSpeed\n");
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        var baseAvatar = AssetDatabase.LoadAssetAtPath<BaseAvatarDefinitionSO>(BaseAvatarPath);
        var go = Object.Instantiate(baseAvatar.baseAvatarPrefab.editorAsset);
        var animator = go.GetComponentInChildren<Animator>();
        try
        {
            foreach (var (name, step) in Targets)
            {
                var entry = library.entries.FirstOrDefault(e => e.name == name);
                var clip = entry?.inPlaceClip;
                if (clip == null) { sb.AppendLine($"== {name}: NOT FOUND"); continue; }
                sb.AppendLine($"== {name}  length {clip.length:0.00}s  loop {clip.isLooping}");
                float lastYaw = float.NaN, unwrapped = 0f;
                Vector3 lastL = Vector3.zero, lastR = Vector3.zero, lastLF = Vector3.zero, lastRF = Vector3.zero;
                for (float t = 0f; t <= clip.length + 1e-3f; t += step)
                {
                    clip.SampleAnimation(animator.gameObject, t);
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                    var rh = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                    var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                    var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    var fwd = hips.forward; fwd.y = 0f;
                    float yaw = Vector3.SignedAngle(Vector3.forward, fwd, Vector3.up);
                    if (!float.IsNaN(lastYaw)) unwrapped += Mathf.DeltaAngle(lastYaw, yaw);
                    lastYaw = yaw;
                    float handSpeed = t > 0f ? ((lh - lastL).magnitude + (rh - lastR).magnitude) / step : 0f;
                    float footSpeed = t > 0f ? ((lf - lastLF).magnitude + (rf - lastRF).magnitude) / step : 0f;
                    sb.AppendLine($"  {t,6:0.0} | {hips.position.y:0.00} | {yaw,5:0} | {Mathf.Max(lh.y, rh.y) - hips.position.y:+0.00;-0.00} | {handSpeed:0.00} | {footSpeed:0.00} | lf {lf.y:0.00} | cumYaw {unwrapped:0}");
                    lastL = lh; lastR = rh; lastLF = lf; lastRF = rf;
                }
            }
        }
        finally { Object.DestroyImmediate(go); }
        File.WriteAllText("Logs/RunnerClipAnalysis.txt", sb.ToString());
        EditorApplication.Exit(0);
    }
}
