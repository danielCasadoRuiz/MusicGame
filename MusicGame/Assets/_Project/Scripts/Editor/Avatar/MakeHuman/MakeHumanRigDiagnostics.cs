using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Rig-calibration diagnostics that separate the TARGET avatar from any animation source:
///   - Procedural Humanoid poses driven purely through Unity muscles (HumanPoseHandler): if a normal
///     muscle moves a limb the wrong way here, the target Avatar calibration is wrong, whatever clip plays.
///   - Per-clip muscle statistics (left vs right) straight from the Humanoid clip curves: shows whether
///     the SOURCE motion itself is asymmetric/odd, independent of our avatar.
/// Output: Logs/RigDiag_*.png + Logs/RigDiag.txt.
/// </summary>
public static class MakeHumanRigDiagnostics
{
    public static readonly (string name, (string muscle, float value)[] muscles)[] Poses =
    {
        ("Muscles zero", new (string, float)[0]),
        ("Arms down", new[] { ("Left Arm Down-Up", -1f), ("Right Arm Down-Up", -1f) }),
        ("Arms up", new[] { ("Left Arm Down-Up", 1f), ("Right Arm Down-Up", 1f) }),
        ("Arms forward", new[] { ("Left Arm Front-Back", 1f), ("Right Arm Front-Back", 1f) }),
        ("L fwd / R back", new[] { ("Left Arm Front-Back", 0.8f), ("Right Arm Front-Back", -0.8f), ("Left Arm Down-Up", -0.6f), ("Right Arm Down-Up", -0.6f) }),
        ("Elbows bent", new[] { ("Left Forearm Stretch", -1f), ("Right Forearm Stretch", -1f), ("Left Arm Down-Up", -0.6f), ("Right Arm Down-Up", -0.6f) }),
        ("Arm twist +", new[] { ("Left Arm Twist In-Out", 1f), ("Right Arm Twist In-Out", 1f), ("Left Forearm Stretch", -0.6f), ("Right Forearm Stretch", -0.6f) }),
        ("Knee lift L", new[] { ("Left Upper Leg Front-Back", 1f), ("Left Lower Leg Stretch", -1f) }),
        ("Spine fwd", new[] { ("Spine Front-Back", 1f), ("Chest Front-Back", 1f) }),
    };

    /// <summary>Sets `muscles` (all others 0) on top of `rest`'s body position/rotation — pass the pose
    /// captured ONCE at rest, never one read back after a previous pose (body rotation depends on the pose).</summary>
    public static void ApplyPose(HumanPoseHandler handler, HumanPose rest, (string muscle, float value)[] muscles)
    {
        var pose = new HumanPose { bodyPosition = rest.bodyPosition, bodyRotation = rest.bodyRotation, muscles = (float[])rest.muscles.Clone() };
        for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
        foreach (var (muscle, value) in muscles)
        {
            int index = System.Array.IndexOf(HumanTrait.MuscleName, muscle);
            if (index >= 0) pose.muscles[index] = value;
            else Debug.LogWarning($"[MakeHumanRigDiagnostics] Unknown muscle '{muscle}'.");
        }
        // Body position/rotation stay as measured at rest, so every pose is expressed relative to the
        // avatar's own rest orientation (whatever its root node's orientation is).
        handler.SetHumanPose(ref pose);
    }

    [MenuItem("Tools/MusicGame/Avatars/Rig Diagnostics (poses + clip muscles)")]
    public static void Run()
    {
        var sb = new StringBuilder();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);

        // ---- 1. Target avatar alone: the rig-test poses (Humanoid muscles on top of the T-pose rest).
        var light = new GameObject("Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.orthographic = true;
        cam.orthographicSize = 1.05f;
        foreach (AvatarTestPose testPose in System.Enum.GetValues(typeof(AvatarTestPose)))
        {
            var go = Object.Instantiate(prefab);
            AvatarPoseTests.ApplyFromRest(go.GetComponent<Animator>(), go.transform, testPose);
            var t = new Dictionary<string, Transform>();
            foreach (var x in go.GetComponentsInChildren<Transform>()) t[x.name] = x;
            string Dir(string a, string b) { var d = (t[b].position - t[a].position).normalized; return $"({d.x:+0.00;-0.00},{d.y:+0.00;-0.00},{d.z:+0.00;-0.00})"; }
            sb.AppendLine($"POSE {testPose,-12} upperarm L{Dir("upperarm_l", "lowerarm_l")} R{Dir("upperarm_r", "lowerarm_r")}  forearm L{Dir("lowerarm_l", "hand_l")} R{Dir("lowerarm_r", "hand_r")}  thigh L{Dir("thigh_l", "calf_l")} shin L{Dir("calf_l", "foot_l")}");
            Shot(cam, $"Logs/RigDiag_{testPose}_front.png", new Vector3(0f, 1f, 10f), Vector3.back, 700, 700);
            Shot(cam, $"Logs/RigDiag_{testPose}_side.png", new Vector3(10f, 1f, 0f), Vector3.left, 700, 700);
            Object.DestroyImmediate(go);
        }

        // ---- 1b. A/B against an independent reference Humanoid (the CMU skeleton): the SAME muscle values
        // must point every limb the same way in each body's own frame, or our Avatar calibration is off.
        sb.AppendLine(CompareWithReference(prefab, MakeHumanAnimationTestSetup.Clips[1].fbx));

        // ---- 2. Animation source alone: Humanoid muscle curves per clip, left vs right.
        foreach (var (animation, fbx) in MakeHumanAnimationTestSetup.Clips)
        {
            AnimationClip clip = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{MakeHumanAnimationTestSetup.DerivedFolder}/{animation}.anim"))
                if (asset is AnimationClip c) clip = c;
            if (clip == null) continue;
            foreach (var muscle in new[] { "Arm Front-Back", "Arm Down-Up", "Arm Twist In-Out", "Forearm Stretch", "Shoulder Down-Up", "Shoulder Front-Back", "Upper Leg Front-Back" })
                sb.AppendLine($"CLIP {animation,-5} {muscle,-20} L {Stats(clip, "Left " + muscle)}   R {Stats(clip, "Right " + muscle)}");
        }

        // ---- 3. Retargeted result on OUR avatar, same geometric metrics as Source/bvh_arm_metrics.py.
        foreach (var (animation, fbx) in MakeHumanAnimationTestSetup.Clips)
        {
            AnimationClip clip = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{MakeHumanAnimationTestSetup.DerivedFolder}/{animation}.anim"))
                if (asset is AnimationClip c) clip = c;
            if (clip == null) continue;
            foreach (var line in RetargetedArmMetrics(prefab, clip)) sb.AppendLine($"DSTARM {animation,-5} {line}");
        }

        File.WriteAllText("Logs/RigDiag.txt", sb.ToString());
        Debug.Log("[MakeHumanRigDiagnostics]\n" + sb);
    }

    /// <summary>Upper-arm flexion / abduction and elbow bend (degrees) of the retargeted avatar over the
    /// clip, in the avatar's own body frame — directly comparable with bvh_arm_metrics.py's source numbers.</summary>
    public static List<string> RetargetedArmMetrics(GameObject prefab, AnimationClip clip)
    {
        var go = Object.Instantiate(prefab);
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        var t = new Dictionary<string, Transform>();
        foreach (var x in go.GetComponentsInChildren<Transform>()) t[x.name] = x;

        var graph = UnityEngine.Playables.PlayableGraph.Create("ArmMetrics");
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
        UnityEngine.Playables.PlayableExtensions.SetTime(playable, 0);
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);

        var stats = new SortedDictionary<string, List<float>>();
        void Add(string k, float v) { if (!stats.TryGetValue(k, out var l)) stats[k] = l = new List<float>(); l.Add(v); }
        for (int i = 0; i < 60; i++)
        {
            UnityEngine.Playables.PlayableExtensions.SetTime(playable, clip.length * i / 60f);
            graph.Evaluate(0f);
            var up = Vector3.up;
            var right = t["thigh_r"].position - t["thigh_l"].position;
            right.y = 0f;
            right.Normalize();
            var forward = Vector3.Cross(right, up); // Unity is left-handed: right x up = forward
            foreach (var (side, s, sign) in new[] { ("Left", "l", -1f), ("Right", "r", 1f) })
            {
                var d = (t[$"lowerarm_{s}"].position - t[$"upperarm_{s}"].position).normalized;
                Add($"{side} flex", Mathf.Atan2(Vector3.Dot(d, forward), -Vector3.Dot(d, up)) * Mathf.Rad2Deg);
                Add($"{side} abd", Mathf.Atan2(sign * Vector3.Dot(d, right), -Vector3.Dot(d, up)) * Mathf.Rad2Deg);
                var e = (t[$"hand_{s}"].position - t[$"lowerarm_{s}"].position).normalized;
                Add($"{side} elbow", Vector3.Angle(d, e));
            }
        }
        graph.Destroy();
        Object.DestroyImmediate(go);

        var lines = new List<string>();
        foreach (var kv in stats)
        {
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            foreach (var v in kv.Value) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v; }
            lines.Add($"{kv.Key,-12} min {min:+0.0;-0.0} mean {sum / kv.Value.Count:+0.0;-0.0} max {max:+0.0;-0.0}");
        }
        return lines;
    }

    private static readonly (string label, string ours, string oursChild, string cmu, string cmuChild)[] Limbs =
    {
        ("L upper arm", "upperarm_l", "lowerarm_l", "LeftArm", "LeftForeArm"),
        ("R upper arm", "upperarm_r", "lowerarm_r", "RightArm", "RightForeArm"),
        ("L forearm", "lowerarm_l", "hand_l", "LeftForeArm", "LeftHand"),
        ("R forearm", "lowerarm_r", "hand_r", "RightForeArm", "RightHand"),
        ("L thigh", "thigh_l", "calf_l", "LeftUpLeg", "LeftLeg"),
        ("R thigh", "thigh_r", "calf_r", "RightUpLeg", "RightLeg"),
        ("L shin", "calf_l", "foot_l", "LeftLeg", "LeftFoot"),
        ("L foot", "foot_l", "ball_l", "LeftFoot", "LeftToeBase"),
        ("spine", "spine_01", "neck_01", "LowerBack", "Neck"),
    };

    /// <summary>Applies each diagnostic muscle pose to our avatar AND to the reference model, and reports
    /// the angle between corresponding limb directions expressed in each body's own frame
    /// (right = right hip - left hip, up = world up, forward = right x up).</summary>
    public static string CompareWithReference(GameObject ours, string referenceFbx)
    {
        var sb = new StringBuilder("A/B muscle poses — our avatar vs reference Humanoid (degrees between limb directions, body frame):\n");
        var referenceModel = AssetDatabase.LoadAssetAtPath<GameObject>(referenceFbx);
        var referenceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(referenceFbx);
        if (referenceModel == null || referenceAvatar == null) return "A/B: reference model missing";

        var a = Object.Instantiate(ours);
        var b = Object.Instantiate(referenceModel);
        var aHandler = new HumanPoseHandler(a.GetComponent<Animator>().avatar, a.transform);
        var bHandler = new HumanPoseHandler(referenceAvatar, b.transform);
        var ta = Index(a); var tb = Index(b);
        // Each body's frame from its OWN rest skeleton (the reference FBX has a rotated root node, so world
        // axes mean nothing there): right = left hip -> right hip, up = hips -> head, forward = right x up.
        var aRest = new HumanPose(); aHandler.GetHumanPose(ref aRest);
        var bRest = new HumanPose(); bHandler.GetHumanPose(ref bRest);
        float worstAll = 0f;
        foreach (var (name, muscles) in Poses)
        {
            ApplyPose(aHandler, aRest, muscles);
            ApplyPose(bHandler, bRest, muscles);
            // Frame from the POSED pelvis: right = left hip -> right hip, up = pelvis -> shoulders midpoint.
            var fa = PelvisFrame(ta, "thigh_l", "thigh_r", "upperarm_l", "upperarm_r");
            var fb = PelvisFrame(tb, "LeftUpLeg", "RightUpLeg", "LeftArm", "RightArm");
            var line = new StringBuilder($"  {name,-15}");
            float worst = 0f;
            foreach (var limb in Limbs)
            {
                var da = InFrame(fa, ta[limb.oursChild].position - ta[limb.ours].position);
                var db = InFrame(fb, tb[limb.cmuChild].position - tb[limb.cmu].position);
                float angle = Vector3.Angle(da, db);
                worst = Mathf.Max(worst, angle);
                line.Append($" {limb.label} {angle,5:0.0}");
            }
            worstAll = Mathf.Max(worstAll, worst);
            sb.AppendLine(line.Append($"   | worst {worst:0.0}").ToString());
        }
        aHandler.Dispose(); bHandler.Dispose();
        Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        sb.AppendLine($"A/B worst limb disagreement over all poses: {worstAll:0.0} deg");
        return sb.ToString();
    }

    private static Dictionary<string, Transform> Index(GameObject go)
    {
        var t = new Dictionary<string, Transform>();
        foreach (var x in go.GetComponentsInChildren<Transform>(true)) t[x.name] = x;
        return t;
    }

    private static (Vector3 right, Vector3 up, Vector3 forward) PelvisFrame(Dictionary<string, Transform> t, string leftHip, string rightHip, string leftShoulder, string rightShoulder)
    {
        var hipsMid = (t[leftHip].position + t[rightHip].position) * 0.5f;
        var shouldersMid = (t[leftShoulder].position + t[rightShoulder].position) * 0.5f;
        var right = (t[rightHip].position - t[leftHip].position).normalized;
        var up = Vector3.ProjectOnPlane(shouldersMid - hipsMid, right).normalized;
        return (right, up, Vector3.Cross(right, up));
    }

    private static Vector3 InFrame((Vector3 right, Vector3 up, Vector3 forward) f, Vector3 world)
    {
        var d = world.normalized;
        return new Vector3(Vector3.Dot(d, f.right), Vector3.Dot(d, f.up), Vector3.Dot(d, f.forward));
    }

    private static string Stats(AnimationClip clip, string muscle)
    {
        var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), muscle);
        var curve = AnimationUtility.GetEditorCurve(clip, binding);
        if (curve == null) return "(no curve)";
        float min = float.MaxValue, max = float.MinValue, sum = 0f;
        int n = 60;
        for (int i = 0; i < n; i++)
        {
            float v = curve.Evaluate(clip.length * i / n);
            min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v;
        }
        return $"min {min:+0.00;-0.00} max {max:+0.00;-0.00} mean {sum / n:+0.00;-0.00} range {max - min:0.00}";
    }

    public static void Shot(Camera cam, string file, Vector3 position, Vector3 forward, int width, int height)
    {
        cam.transform.position = position;
        cam.transform.rotation = Quaternion.LookRotation(forward);
        var rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
