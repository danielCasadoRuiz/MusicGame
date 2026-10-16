#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The runner's CROUCHED RUN, generated (no suitable mocap clip exists in the project): an ADDITIVE humanoid
/// clip in muscle space, played on RunnerHumanoid.controller's CrouchAdditive layer over the normal run
/// cycle — legs keep running, knees and hips bend more, torso leans forward, head keeps looking ahead,
/// body lowered. It is authored as two constant clips: Reference (all listed muscles 0, RootT.y = 1) and
/// the crouch (same curves + the deltas below), so the additive delta is EXACTLY those deltas — no root
/// X/Z offset (the old superhero-landing segment minus CMU Idle added a deep one-knee fold plus the offset
/// between the two recordings' origins: the avatar folded and drifted metres away).
/// Tune the deltas below (normalised Humanoid muscle units, −1..1) and re-run Setup Runner Animation.
/// Check: -executeMethod RunnerCrouchRunClip.ProbeFromCommandLine (edit mode, canonical MakeHuman body over
/// the CMU run: head/hips drop, lean, feet, horizontal drift) → Logs/RunnerCrouchRunProbe.txt.
/// </summary>
public static class RunnerCrouchRunClip
{
    public const string Dir = "Assets/_Project/Configs/Player/RunnerAnimation/Generated";
    public const string ClipPath = Dir + "/RunnerCrouchRun_Additive.anim";
    public const string ReferencePath = Dir + "/RunnerCrouchRun_Reference.anim";
    private const string ProbeBody = "Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx";
    private const string RunClipPath = "Assets/_Project/Avatar/AnimationTests/Clips/Derived/Run.anim";

    /// <summary>Muscle deltas added over the run cycle. Signs measured by the probe on the MakeHuman rig:
    /// Spine/Chest Front-Back + = bend BACK; Neck/Head Nod + = look UP; Upper Leg Front-Back + = thigh BACK;
    /// Lower Leg Stretch − = knee MORE bent.</summary>
    public static readonly (string muscle, float delta)[] Deltas =
    {
        ("Spine Front-Back", -0.25f),
        ("Chest Front-Back", -0.10f),
        ("Neck Nod Down-Up", 0.25f),
        ("Head Nod Down-Up", 0.30f),
        ("Left Upper Leg Front-Back", -0.85f),
        ("Right Upper Leg Front-Back", -0.85f),
        ("Left Lower Leg Stretch", -0.85f),
        ("Right Lower Leg Stretch", -0.85f),
    };

    /// <summary>Whole-body forward pitch (degrees, body orientation RootQ about its own X; sign per probe).</summary>
    public const float LeanDegrees = 24f;

    /// <summary>Initial guess of the body drop (RootT.y, normalised human units). Build() then FITS it on the
    /// canonical body over the run cycle so the feet keep touching the ground (bent legs are shorter: the body
    /// comes down by exactly that much) — retuning the deltas never buries or lifts the feet.</summary>
    public const float BodyDropGuess = 0.15f;

    /// <summary>Creates/overwrites both generated clips, sets the additive reference, returns the crouch clip.</summary>
    public static AnimationClip Build(StringBuilder log = null)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(Dir).Replace('\\', '/'), Path.GetFileName(Dir));
        var reference = Write(ReferencePath, "RunnerCrouchRun_Reference", 0f, 0f);
        float drop = BodyDropGuess;
        var crouch = Write(ClipPath, "RunnerCrouchRun_Additive", 1f, drop);
        SetAdditive(crouch, reference);
        drop = FitBodyDrop(crouch, reference, drop, log);
        AssetDatabase.SaveAssets();
        log?.AppendLine($"  PASS  generated {ClipPath} (additive over its own neutral reference; no root X/Z offset)");
        return crouch;
    }

    private static void SetAdditive(AnimationClip crouch, AnimationClip reference)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(crouch);
        settings.loopTime = true;
        settings.hasAdditiveReferencePose = true;
        settings.additiveReferencePoseClip = reference;
        settings.additiveReferencePoseTime = 0f;
        AnimationUtility.SetAnimationClipSettings(crouch, settings);
        EditorUtility.SetDirty(crouch);
    }

    /// <summary>Lowest foot over the run cycle, crouched vs standing, on the canonical body: the body drop is
    /// corrected by that difference (twice, the bent legs' geometry is nearly linear) — feet stay grounded.</summary>
    private static float FitBodyDrop(AnimationClip crouch, AnimationClip reference, float drop, StringBuilder log)
    {
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeBody);
        if (run == null || prefab == null) { log?.AppendLine($"  WARN  body drop not fitted (probe body / run clip missing) — {drop:0.000}"); return drop; }
        var go = (GameObject)Object.Instantiate(prefab);
        try
        {
            var anim = go.GetComponent<Animator>();
            anim.applyRootMotion = false;
            float scale = Mathf.Max(0.01f, anim.humanScale);
            for (int i = 0; i < 3; i++)
            {
                float gap = LowestFoot(anim, run, crouch, 1f) - LowestFoot(anim, run, crouch, 0f); // < 0: buried
                drop = Mathf.Max(0f, drop + gap / scale);
                Write(ClipPath, "RunnerCrouchRun_Additive", 1f, drop);
                SetAdditive(crouch, reference);
            }
            float final = LowestFoot(anim, run, crouch, 1f) - LowestFoot(anim, run, crouch, 0f);
            log?.AppendLine($"  PASS  body drop fitted to {drop:0.000} (human scale {scale:0.00}): lowest foot crouched vs standing {final:+0.000;-0.000} m");
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
        }
        return drop;
    }

    private static float LowestFoot(Animator anim, AnimationClip run, AnimationClip crouch, float weight)
    {
        float lowest = float.MaxValue;
        for (int k = 0; k < 12; k++)
        {
            Evaluate(anim, run, crouch, run.length * k / 12f, weight, out var p);
            lowest = Mathf.Min(lowest, p.footMin);
        }
        return lowest;
    }

    private static AnimationClip Write(string path, string name, float scale, float drop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = name }; AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        foreach (var (muscle, delta) in Deltas) Set(clip, muscle, delta * scale);
        Set(clip, "RootT.y", 1f - drop);
        SetRootQ(clip, LeanDegrees * scale);
        BakeRootIntoPose(clip);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void SetRootQ(AnimationClip clip, float pitchDegrees)
    {
        var q = Quaternion.AngleAxis(pitchDegrees, Vector3.right);
        Set(clip, "RootQ.x", q.x); Set(clip, "RootQ.y", q.y); Set(clip, "RootQ.z", q.z); Set(clip, "RootQ.w", q.w);
    }

    /// <summary>Root rotation / height / XZ baked into the pose: otherwise a code-made humanoid clip extracts them
    /// as root motion, which the runner discards (applyRootMotion off) — the lean would never show.</summary>
    private static void BakeRootIntoPose(AnimationClip clip)
    {
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopBlendOrientation = true; st.keepOriginalOrientation = true;
        st.loopBlendPositionY = true; st.keepOriginalPositionY = true;
        st.loopBlendPositionXZ = true; st.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, st);
    }

    private static void Set(AnimationClip clip, string property, float value) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), AnimationCurve.Constant(0f, 1f, value));

    // ── Probe ───────────────────────────────────────────────────────────────

    public static void ProbeFromCommandLine()
    {
        string report = Probe();
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/RunnerCrouchRunProbe.txt", report, Encoding.UTF8);
        EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
    }

    [MenuItem("Tools/MusicGame/Avatar/Probe Runner Crouch Run")]
    public static void ProbeMenu() => Debug.Log(Probe());

    public static string Probe()
    {
        var sb = new StringBuilder("[RunnerCrouchRunProbe] canonical MakeHuman body, CMU run + generated crouch (additive)\n");
        var crouch = Build(sb);
        var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath);
        var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ProbeBody));
        try
        {
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.applyRootMotion = false;
            sb.AppendLine($"probe body: human {anim.isHuman}, avatar {(anim.avatar != null ? anim.avatar.name : "none")}");
            // Muscle signs (each muscle +0.5 alone, over a neutral pose).
            Calibrate(anim);
            sb.AppendLine($"facing {s_facing}, head face axis (local) {s_headFaceAxisLocal}");
            sb.AppendLine("signs (alone over a neutral pose; lean + = forward, knee bend + = more bent, gaze + = up):");
            foreach (var (muscle, _) in Deltas.Where(d => !d.muscle.StartsWith("Right")))
                sb.AppendLine("  " + SignProbe(anim, muscle));
            sb.AppendLine("  " + SignProbe(anim, "RootQ", 15f));

            float[] times = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
            float headStand = 0, headCrouch = 0, hipsStand = 0, hipsCrouch = 0, maxDrift = 0, minFoot = float.MaxValue, lean = 0, gaze = 0;
            float runLen = run.length;
            foreach (float f in times)
            {
                float t = f * runLen;
                Evaluate(anim, run, crouch, t, 0f, out var s);
                Evaluate(anim, run, crouch, t, 1f, out var c);
                headStand += s.head.y; headCrouch += c.head.y; hipsStand += s.hips.y; hipsCrouch += c.hips.y;
                maxDrift = Mathf.Max(maxDrift, new Vector2(c.hips.x - s.hips.x, c.hips.z - s.hips.z).magnitude);
                minFoot = Mathf.Min(minFoot, c.footMin - s.footMin);
                lean += Vector3.Angle(Vector3.up, c.head - c.hips) - Vector3.Angle(Vector3.up, s.head - s.hips);
                gaze += c.headFwdY;
            }
            int n = times.Length;
            float headDrop = (headStand - headCrouch) / n, hipsDrop = (hipsStand - hipsCrouch) / n;
            lean /= n; gaze /= n;
            sb.AppendLine($"head height standing {headStand / n:0.00} m → crouched {headCrouch / n:0.00} m (drop {headDrop:0.00} m)");
            sb.AppendLine($"hips drop {hipsDrop:0.00} m, torso lean +{lean:0.0}°, head forward·y {gaze:0.00} (0 = looking level)");
            Check(sb, headDrop is > 0.25f and < 0.6f, $"crouched run lowers the head 0.25–0.6 m (a run, not a fold) — {headDrop:0.00} m");
            Check(sb, lean is > 5f and < 35f, $"torso leans forward moderately (5–35°) — {lean:0.0}°");
            Check(sb, maxDrift < 0.25f, $"no horizontal offset: hips stay over the runner (≤ 0.25 m; a deep crouch sits the hips back) — {maxDrift:0.00} m");
            float groundGap = LowestFoot(anim, run, crouch, 1f) - LowestFoot(anim, run, crouch, 0f);
            Check(sb, Mathf.Abs(groundGap) < 0.03f, $"feet keep touching the ground (lowest foot over the cycle, crouched vs standing) — {groundGap:+0.000;-0.000} m");
            Check(sb, Mathf.Abs(gaze) < 0.45f, $"head keeps looking ahead — forward·y {gaze:0.00}");
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
        }
        sb.AppendLine(sb.ToString().Contains("FAIL") ? "RESULT FAIL" : "RESULT PASS");
        return sb.ToString();
    }

    private static void Check(StringBuilder sb, bool ok, string what) => sb.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what);

    private struct Pose { public Vector3 head, hips; public float footMin, headFwdY; }

    private static void Evaluate(Animator anim, AnimationClip baseClip, AnimationClip additive, float time, float weight, out Pose pose)
    {
        var graph = PlayableGraph.Create("CrouchProbe");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            var a = AnimationClipPlayable.Create(graph, baseClip);
            var b = AnimationClipPlayable.Create(graph, additive);
            a.SetTime(time); b.SetTime(0.5f);
            a.SetApplyFootIK(false); b.SetApplyFootIK(false);
            graph.Connect(a, 0, mixer, 0);
            graph.Connect(b, 0, mixer, 1);
            mixer.SetLayerAdditive(1, true);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, weight);
            output.SetSourcePlayable(mixer);
            // Edit mode: a PlayableGraph only writes the pose through AnimationMode sampling.
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SamplePlayableGraph(graph, 0, time);
            AnimationMode.EndSampling();
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            pose.head = B(HumanBodyBones.Head).position;
            pose.hips = B(HumanBodyBones.Hips).position;
            pose.footMin = Mathf.Min(B(HumanBodyBones.LeftFoot).position.y, B(HumanBodyBones.RightFoot).position.y);
            pose.headFwdY = B(HumanBodyBones.Head).forward.y;
        }
        finally { graph.Destroy(); }
    }

    // Facing (feet → toes) and the head-bone axis that looks along it, from the neutral pose: every measure
    // below is axis-agnostic (MakeHuman bone axes are arbitrary).
    private static Vector3 s_facing;
    private static Vector3 s_headFaceAxisLocal;

    private static void Calibrate(Animator anim)
    {
        var neutral = new AnimationClip { name = "neutral" };
        Set(neutral, "RootT.y", 1f);
        Evaluate(anim, neutral, neutral, 0f, 0f, out _);
        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
        var f = B(HumanBodyBones.LeftToes).position - B(HumanBodyBones.LeftFoot).position; f.y = 0f;
        s_facing = f.normalized;
        var head = B(HumanBodyBones.Head);
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };
        s_headFaceAxisLocal = axes.OrderByDescending(ax => Vector3.Dot(head.rotation * ax, s_facing)).First();
        Object.DestroyImmediate(neutral);
    }

    private struct Measure { public float headFwd, headUp, kneeAngle, kneeFwd, gaze, lean; }

    private static Measure Take(Animator anim)
    {
        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
        Vector3 hips = B(HumanBodyBones.Hips).position, head = B(HumanBodyBones.Head).position;
        Vector3 thigh = B(HumanBodyBones.LeftUpperLeg).position, knee = B(HumanBodyBones.LeftLowerLeg).position, ankle = B(HumanBodyBones.LeftFoot).position;
        return new Measure
        {
            headFwd = Vector3.Dot(head - hips, s_facing),
            headUp = head.y - hips.y,
            kneeAngle = Vector3.Angle(thigh - knee, ankle - knee),
            kneeFwd = Vector3.Dot(knee - thigh, s_facing),
            gaze = (B(HumanBodyBones.Head).rotation * s_headFaceAxisLocal).y,
            lean = Vector3.SignedAngle(Vector3.up, head - hips, Vector3.Cross(Vector3.up, s_facing)),
        };
    }

    /// <summary>Effect of `property` = `value` alone over a neutral pose, relative to the hips and the facing.</summary>
    private static string SignProbe(Animator anim, string property, float value = 0.5f)
    {
        var neutral = new AnimationClip { name = "neutral" };
        var bent = new AnimationClip { name = "bent" };
        Set(neutral, "RootT.y", 1f); Set(bent, "RootT.y", 1f);
        BakeRootIntoPose(neutral); BakeRootIntoPose(bent);
        if (property.StartsWith("RootQ"))
        {
            var q = Quaternion.AngleAxis(value, Vector3.right); // value in degrees, about the body's own X
            Set(bent, "RootQ.x", q.x); Set(bent, "RootQ.y", q.y); Set(bent, "RootQ.z", q.z); Set(bent, "RootQ.w", q.w);
            Set(neutral, "RootQ.x", 0f); Set(neutral, "RootQ.y", 0f); Set(neutral, "RootQ.z", 0f); Set(neutral, "RootQ.w", 1f);
        }
        else { Set(neutral, property, 0f); Set(bent, property, value); }
        Evaluate(anim, neutral, neutral, 0f, 0f, out _);
        var m0 = Take(anim);
        Evaluate(anim, bent, bent, 0f, 0f, out _);
        var m1 = Take(anim);
        Object.DestroyImmediate(neutral); Object.DestroyImmediate(bent);
        return $"{property + " " + value,-34} lean {m1.lean - m0.lean:+0.0;-0.0}° | head fwd {m1.headFwd - m0.headFwd:+0.00;-0.00} up {m1.headUp - m0.headUp:+0.00;-0.00} | knee bend {m0.kneeAngle - m1.kneeAngle:+0;-0}° knee fwd {m1.kneeFwd - m0.kneeFwd:+0.00;-0.00} | gaze {m1.gaze - m0.gaze:+0.00;-0.00} (+ = up)";
    }
}
#endif
