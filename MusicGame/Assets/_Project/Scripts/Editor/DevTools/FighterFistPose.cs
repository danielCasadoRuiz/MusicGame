#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The fighters' FIST pose, generated: a constant humanoid clip that only sets finger muscles, played on a
/// finger-masked override layer of FighterCombat.controller (CombatContentSetup). The mocap fight clips close
/// the hands harder than the MakeHuman hand allows (its rest fingers are not straight), so their fists
/// over-curl — fingers sink into the palm and look broken, very visible in first person. This pose replaces
/// only the fingers with a clean, moderate fist; arms and everything else keep the mocap.
/// Probe: -executeMethod FighterFistPose.ProbeFromCommandLine (with graphics) renders the canonical hand with
/// the guard clip alone and with several curls → Logs/FighterFistProbe.png.
/// </summary>
public static class FighterFistPose
{
    public const string Dir = "Assets/_Project/Configs/Fight/Combat/Animator/Generated";
    public const string ClipPath = Dir + "/FighterFist.anim";
    public const string MaskPath = Dir + "/FighterFingers.mask";
    private const string ProbeBody = "Assets/_Project/Avatar/MakeHuman/Generated/MakeHuman_Canonical.fbx";
    private const string GuardClip = "Assets/_Project/Animations/Processed/Combat/Fight/Segments/InPlace/Fight_Boxing_mixamo__GuardIdle_InPlace.anim";

    /// <summary>Finger curl (Humanoid "Stretched" muscle, −1 = fully curled, +1 = straight) of the four fingers.</summary>
    public const float FingerCurl = -0.55f;
    /// <summary>Thumb curl: it folds over the index, less than the fingers.</summary>
    public const float ThumbCurl = -0.25f;

    private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

    public static AnimationClip Build(StringBuilder log = null) => Build(FingerCurl, ThumbCurl, log);

    public static AnimationClip Build(float curl, float thumb, StringBuilder log)
    {
        EnsureFolder(Dir);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) { clip = new AnimationClip { name = "FighterFist" }; AssetDatabase.CreateAsset(clip, ClipPath); }
        Fill(clip, curl, thumb);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        log?.AppendLine($"  PASS  generated {ClipPath} (fingers {curl:0.00}, thumb {thumb:0.00})");
        return clip;
    }

    public static AvatarMask BuildMask()
    {
        EnsureFolder(Dir);
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static void Fill(AnimationClip clip, float curl, float thumb)
    {
        clip.ClearCurves();
        foreach (string side in new[] { "LeftHand", "RightHand" })
        {
            foreach (string f in Fingers)
            {
                for (int j = 1; j <= 3; j++) Set(clip, $"{side}.{f}.{j} Stretched", curl);
                Set(clip, $"{side}.{f}.Spread", 0f);
            }
            Set(clip, $"{side}.Thumb.1 Stretched", thumb);
            Set(clip, $"{side}.Thumb.2 Stretched", thumb);
            Set(clip, $"{side}.Thumb.3 Stretched", thumb);
            Set(clip, $"{side}.Thumb.Spread", 0f);
        }
    }

    private static void Set(AnimationClip clip, string property, float value) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), AnimationCurve.Constant(0f, 1f, value));

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ── Probe (renders) ─────────────────────────────────────────────────────

    public static void ProbeFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        var guard = AssetDatabase.LoadAssetAtPath<AnimationClip>(GuardClip);
        var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ProbeBody));
        var lightGo = new GameObject("ProbeLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        var camGo = new GameObject("ProbeCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.fieldOfView = 35f; cam.nearClipPlane = 0.01f;
        var mask = BuildMask();
        float[] curls = { float.NaN, -0.35f, -0.75f };
        float[] genders = { 0f, 1f }; // canonical (male) body, then the player's female morph (Gender 1, Weight 0.5)
        const int w = 400, h = 400;
        var sheet = new Texture2D(w * curls.Length, h * genders.Length, TextureFormat.RGB24, false);
        var rt = new RenderTexture(w, h, 24);
        try
        {
            var anim = go.GetComponent<Animator>();
            var morph = new AvatarBodyMorphController();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.sharedMesh = Object.Instantiate(smr.sharedMesh);
                morph.RegisterRenderer(smr, null, smr.name);
            }
            for (int g = 0; g < genders.Length; g++)
            {
                morph.Apply(new BodyMorphValues { Gender = genders[g], Weight = 0.5f, Muscle = 0f });
                for (int i = 0; i < curls.Length; i++)
                {
                    var fist = new AnimationClip { name = "fist" };
                    if (!float.IsNaN(curls[i])) Fill(fist, curls[i], curls[i] * 0.45f);
                    Sample(anim, guard, fist, mask, float.IsNaN(curls[i]) ? 0f : 1f, 0.3f);
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.enabled = false; smr.enabled = true; } // edit mode: force re-skin
                    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
                    var forearm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    Vector3 target = (hand.position * 2f + forearm.position) / 3f; // hand, wrist and forearm in frame
                    cam.transform.position = target + go.transform.forward * 0.5f + Vector3.up * 0.08f + go.transform.right * -0.12f;
                    cam.transform.LookAt(target);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), i * w, (genders.Length - 1 - g) * h);
                    RenderTexture.active = null;
                    Object.DestroyImmediate(fist);
                }
            }
            sheet.Apply();
            File.WriteAllBytes("Logs/FighterFistProbe.png", sheet.EncodeToPNG());
            Debug.Log("[FighterFistProbe] Logs/FighterFistProbe.png — rows: canonical, female morph; cols: mocap guard | curl -0.35 | -0.75");
        }
        finally
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(go);
        }
        EditorApplication.Exit(0);
    }

    private static void Sample(Animator anim, AnimationClip baseClip, AnimationClip fingers, AvatarMask mask, float weight, float time)
    {
        var graph = PlayableGraph.Create("FistProbe");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            var a = AnimationClipPlayable.Create(graph, baseClip);
            var b = AnimationClipPlayable.Create(graph, fingers);
            a.SetTime(time);
            graph.Connect(a, 0, mixer, 0);
            graph.Connect(b, 0, mixer, 1);
            mixer.SetLayerMaskFromAvatarMask(1, mask);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, weight);
            output.SetSourcePlayable(mixer);
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SamplePlayableGraph(graph, 0, time);
            AnimationMode.EndSampling();
        }
        finally { graph.Destroy(); }
    }
}
#endif
