using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Tools > MusicGame > Avatars > Setup Animation Test Clips — configures the three rig-validation
/// clips (CMU motion capture, converted by Source/cmu_bvh_to_fbx.py) as Unity Humanoid animations and
/// builds the AvatarDebugAnimator controller (states Idle / Walk / Run) used by the avatar debug scene.
///
/// Import configuration, all on the importer (the FBX files themselves are never edited):
///   - Rig: Humanoid, avatar created from the clip's own skeleton, whose rest pose IS the CMU T-pose
///     (see the .py). Retargeting onto the MakeHuman character then goes through Unity's normal
///     Humanoid path, onto MakeHuman_Canonical_Avatar — our skeleton is never touched.
///   - EXPLICIT bone mapping, deliberately WITHOUT fingers: CMU hands are a single noisy marker
///     ("LeftHandIndex1"/"LThumb" stubs), and letting Unity auto-map them produces garbage finger
///     curves. (Unity still emits constant finger muscles — handled by the derived clips below.)
///   - In place: root XZ translation goes to root motion (discarded — the debug Animator has Apply Root
///     Motion off), root rotation follows the body (so the avatar keeps facing forward even though the
///     capture subject veers), root height baked into the pose from the feet.
///   - Loop time + loop pose on segments picked for minimal loop error (see README.md).
///
/// Then a DERIVED copy of each imported clip is written (Clips/Derived/*.anim — the FBX stays
/// untouched) with two clip-level fixes, both found by inspecting the retargeted result:
///   - FINGERS: Unity writes all 40 finger muscles into every Humanoid clip even when no finger bone
///     is mapped — as constant 0, which on the MakeHuman Avatar is NOT a relaxed hand (it curls the
///     thumb onto the index: the "OK sign"). The derived clips replace them with the finger muscles
///     of the avatar's own relaxed rest hand (measured with HumanPoseHandler), so hands stay natural.
///   - GROUND: the clip's vertical offset ("level") is calibrated on the MakeHuman avatar so the
///     lowest sole over the cycle sits on y = 0 (CMU walk/run otherwise sank 7-9 cm, idle floated 3 cm),
///     and the controller states use Foot IK so feet track the source's recorded foot positions.
/// Neither touches the avatar's skeleton, Avatar or skinning.
/// </summary>
public static class MakeHumanAnimationTestSetup
{
    public const string Folder = "Assets/_Project/Avatar/AnimationTests";
    public const string ControllerPath = Folder + "/AvatarDebugAnimator.controller";
    public const string DerivedFolder = Folder + "/Clips/Derived";

    public static readonly (DebugAnimation animation, string fbx)[] Clips =
    {
        (DebugAnimation.Idle, Folder + "/Clips/CMU_Idle_111_28.fbx"),
        (DebugAnimation.Walk, Folder + "/Clips/CMU_Walk_07_09.fbx"),
        (DebugAnimation.Run,  Folder + "/Clips/CMU_Run_09_05.fbx"),
    };

    /// <summary>Unity human bone -> CMU (cgspeed MotionBuilder-friendly) joint. No fingers, no jaw/eyes.</summary>
    private static readonly (string human, string bone)[] CmuBoneMap =
    {
        ("Hips", "Hips"),
        ("Spine", "LowerBack"), ("Chest", "Spine"), ("UpperChest", "Spine1"),
        ("Neck", "Neck"), ("Head", "Head"),
        ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftArm"), ("LeftLowerArm", "LeftForeArm"), ("LeftHand", "LeftHand"),
        ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightArm"), ("RightLowerArm", "RightForeArm"), ("RightHand", "RightHand"),
        ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"), ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToeBase"),
        ("RightUpperLeg", "RightUpLeg"), ("RightLowerLeg", "RightLeg"), ("RightFoot", "RightFoot"), ("RightToes", "RightToeBase"),
    };

    [MenuItem("Tools/MusicGame/Avatars/Setup Animation Test Clips")]
    public static void SetupMenu() => Debug.Log(Setup(out _));

    /// <summary>True when all three FBX clips exist (they're what's needed; everything else is generated).</summary>
    public static bool ClipsPresent()
    {
        foreach (var (_, fbx) in Clips) if (AssetDatabase.LoadMainAssetAtPath(fbx) == null) return false;
        return true;
    }

    public static string Setup(out bool success)
    {
        var report = new List<string>();
        var clipsByAnimation = new Dictionary<DebugAnimation, AnimationClip>();
        success = true;

        foreach (var (animation, fbx) in Clips)
        {
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null)
            {
                success = false;
                report.Add($"MISSING {animation}: {fbx} — run Source/cmu_bvh_to_fbx.py (see README.md) or drop a Humanoid clip there.");
                continue;
            }

            // Pass 1: Humanoid with Unity's own avatar so the importer fills in the skeleton description.
            // Always round-trip through Generic first: the importer caches humanDescription.skeleton (the
            // T-pose reference) in the .meta, so after re-exporting the FBX with a recalibrated rest pose
            // Unity would otherwise keep retargeting against the OLD T-pose (verified: identical output).
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.SaveAndReimport();
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            // Pass 2: explicit, finger-free mapping on the importer's own skeleton (rest = CMU T-pose).
            var description = importer.humanDescription;
            var human = new List<HumanBone>();
            foreach (var (humanName, bone) in CmuBoneMap)
            {
                var hb = new HumanBone { humanName = humanName, boneName = bone };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            description.human = human.ToArray();
            string tposeReport = EnforceSourceTPose(ref description, fbx);
            description.upperArmTwist = 0.5f;
            description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f;
            description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f;
            description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            description.hasTranslationDoF = false;
            importer.humanDescription = description;

            var clip = importer.defaultClipAnimations.Length > 0 ? importer.defaultClipAnimations[0] : new ModelImporterClipAnimation();
            clip.name = animation.ToString();
            clip.loopTime = true;
            clip.loopPose = true;
            clip.lockRootRotation = false;       // root yaw follows the body: always faces forward in place
            clip.lockRootHeightY = true;         // height baked into pose...
            clip.heightFromFeet = true;          // ...from the feet: grounded
            clip.keepOriginalPositionY = false;
            clip.lockRootPositionXZ = false;     // XZ travel -> root motion (discarded: Apply Root Motion off)
            clip.maskType = ClipAnimationMaskType.None;
            importer.clipAnimations = new[] { clip };
            importer.importAnimation = true;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(fbx);
            AnimationClip imported = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) imported = c;

            if (avatar == null || !avatar.isValid || !avatar.isHuman || imported == null || !imported.isHumanMotion)
            {
                success = false;
                report.Add($"FAILED {animation}: {fbx} — Humanoid import invalid (avatar valid={avatar != null && avatar.isValid}, clip humanoid={imported != null && imported.isHumanMotion}).");
                continue;
            }

            int fingerCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(imported))
                if (binding.propertyName.Contains("Thumb") || binding.propertyName.Contains("Index") || binding.propertyName.Contains("Middle") ||
                    binding.propertyName.Contains("Ring") || binding.propertyName.Contains("Little")) fingerCurves++;
            clipsByAnimation[animation] = imported;
            report.Add($"OK {animation}: {Path.GetFileName(fbx)} — Humanoid, {imported.length:0.00} s loop, {fingerCurves} finger curves; {tposeReport}.");
        }

        if (clipsByAnimation.Count == Clips.Length)
        {
            var derived = BuildDerivedClips(clipsByAnimation, report, out bool derivedOk);
            if (derivedOk) BuildController(derived);
            success &= derivedOk;
        }
        else success = false;

        AssetDatabase.SaveAssets();
        return "[MakeHumanAnimationTestSetup] " + (success ? "Done" : "INCOMPLETE") + ":\n  " + string.Join("\n  ", report);
    }

    /// <summary>T-pose chains for the CMU skeleton: bone -> child it points at -> desired direction
    /// ("down" for legs, "lateral" for arms — resolved per side from the skeleton itself).</summary>
    private static readonly (string bone, string child, string direction)[] CmuTPoseChains =
    {
        // Only MAPPED bones are rotated (Unity rejects an in-between bone whose T-pose differs from the
        // file: "Inbetween bone rotation ... does not match"), so Neck aims straight at Head past Neck1.
        ("LowerBack", "Spine", "up"), ("Spine", "Spine1", "up"), ("Spine1", "Neck", "up"), ("Neck", "Head", "up"),
        ("LeftUpLeg", "LeftLeg", "down"),   ("LeftLeg", "LeftFoot", "down"),
        ("RightUpLeg", "RightLeg", "down"), ("RightLeg", "RightFoot", "down"),
        ("LeftArm", "LeftForeArm", "left"),   ("LeftForeArm", "LeftHand", "left"),
        ("RightArm", "RightForeArm", "right"), ("RightForeArm", "RightHand", "right"),
    };

    /// <summary>
    /// "Enforce T-Pose" for the SOURCE avatar (the same thing MakeHumanHumanoid does for ours): the
    /// cgspeed frame-0 T-pose is synthetic and not exact (limbs off by up to ~40 deg), and Unity takes
    /// it as the neutral reference. Non-vertical legs made Unity misjudge the source's leg length (the
    /// retargeted body rode too high: feet floated 5-10 cm mid-stride); a tilted spine/neck made a
    /// straight-standing subject lean back and look up. This straightens spine/neck (up), legs (down)
    /// and arms (horizontal) in the source HumanDescription only — clip data and our avatar are untouched.
    /// </summary>
    private static string EnforceSourceTPose(ref HumanDescription description, string fbx)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (model == null) return "T-pose: no model";

        // Pose a real instance of the model (Unity handles the FBX node hierarchy/scale), starting from
        // the description's own skeleton, then read the corrected local rotations back.
        var skeleton = description.skeleton;
        var go = Object.Instantiate(model);
        try
        {
            var transforms = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) transforms[t.name] = t;
            foreach (var bone in skeleton)
                if (transforms.TryGetValue(bone.name, out var t) && t != go.transform)
                {
                    t.localPosition = bone.position;
                    t.localRotation = bone.rotation;
                    t.localScale = bone.scale;
                }

            foreach (var name in new[] { "LeftArm", "RightArm" })
                if (!transforms.ContainsKey(name)) return $"T-pose: missing {name}";
            var span = transforms["LeftArm"].position - transforms["RightArm"].position;
            var left = new Vector3(span.x, 0f, span.z).normalized;

            float worst = 0f;
            foreach (var (bone, child, direction) in CmuTPoseChains)
            {
                if (!transforms.TryGetValue(bone, out var b) || !transforms.TryGetValue(child, out var c)) continue;
                var target = direction == "down" ? Vector3.down : direction == "up" ? Vector3.up : direction == "left" ? left : -left;
                var current = (c.position - b.position).normalized;
                worst = Mathf.Max(worst, Vector3.Angle(current, target));
                b.rotation = Quaternion.FromToRotation(current, target) * b.rotation;
            }

            for (int i = 0; i < skeleton.Length; i++)
                if (transforms.TryGetValue(skeleton[i].name, out var t) && t != go.transform)
                    skeleton[i].rotation = t.localRotation;
            description.skeleton = skeleton;
            return $"source T-pose enforced (largest limb correction {worst:0.0} deg)";
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private static readonly string[] FingerWords = { "Thumb", "Index", "Middle", "Ring", "Little" };

    internal static bool IsFingerCurve(string attribute)
    {
        foreach (var word in FingerWords) if (attribute.Contains(word)) return true;
        return false;
    }

    /// <summary>Animator curve attribute ("LeftHand.Thumb.1 Stretched") -> HumanTrait muscle name
    /// ("Left Thumb 1 Stretched").</summary>
    internal static string MuscleNameOf(string attribute) =>
        attribute.Replace("LeftHand.", "Left ").Replace("RightHand.", "Right ").Replace('.', ' ');

    private static Dictionary<DebugAnimation, AnimationClip> BuildDerivedClips(Dictionary<DebugAnimation, AnimationClip> source, List<string> report, out bool ok)
    {
        ok = true;
        var derived = new Dictionary<DebugAnimation, AnimationClip>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        if (prefab == null) { ok = false; report.Add("FAILED: MakeHuman prefab missing — bake the body first."); return derived; }

        if (!AssetDatabase.IsValidFolder(DerivedFolder)) AssetDatabase.CreateFolder(Folder + "/Clips", "Derived");

        var avatarGO = Object.Instantiate(prefab);
        try
        {
            var animator = avatarGO.GetComponent<Animator>();
            var restMuscles = MeasureRestMuscles(avatarGO, animator.avatar);

            foreach (var (animation, clip) in source)
            {
                string path = $"{DerivedFolder}/{animation}.anim";
                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (copy == null)
                {
                    copy = new AnimationClip();
                    AssetDatabase.CreateAsset(copy, path);
                }
                copy.ClearCurves();
                copy.name = animation.ToString();
                copy.frameRate = clip.frameRate;

                int fingerCurves = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (binding.type == typeof(Animator) && IsFingerCurve(binding.propertyName) &&
                        restMuscles.TryGetValue(MuscleNameOf(binding.propertyName), out float rest))
                    {
                        curve = AnimationCurve.Constant(0f, clip.length, rest);
                        fingerCurves++;
                    }
                    AnimationUtility.SetEditorCurve(copy, binding, curve);
                }

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.level = 0f;
                AnimationUtility.SetAnimationClipSettings(copy, settings);

                float yaw = CalibrateFacing(copy, avatarGO);
                float offset = CalibrateGround(copy, avatarGO);
                report.Add($"   derived {animation}.anim — {fingerCurves} finger curves pinned to the relaxed rest hand, ground offset {offset:+0.0000;-0.0000}, facing offset {yaw:+0.0;-0.0} deg.");
                EditorUtility.SetDirty(copy);
                derived[animation] = copy;
            }
        }
        finally
        {
            Object.DestroyImmediate(avatarGO);
        }
        return derived;
    }

    /// <summary>Humanoid muscle values of the avatar's own rest pose, by HumanTrait muscle name.</summary>
    internal static Dictionary<string, float> MeasureRestMuscles(GameObject avatarGO, Avatar avatar)
    {
        var handler = new HumanPoseHandler(avatar, avatarGO.transform);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);
        handler.Dispose();

        var result = new Dictionary<string, float>();
        for (int i = 0; i < HumanTrait.MuscleCount; i++) result[HumanTrait.MuscleName[i]] = pose.muscles[i];
        return result;
    }

    /// <summary>Rotates the clip (orientationOffsetY) so the avatar's hips face the root's forward on
    /// average — an in-place debug clip should face the camera it's inspected from, whatever direction the
    /// capture subject happened to face (a few degrees on these clips).</summary>
    private static float CalibrateFacing(AnimationClip clip, GameObject avatarGO)
    {
        var animator = avatarGO.GetComponent<Animator>();
        animator.applyRootMotion = false;
        Transform left = null, right = null;
        foreach (var t in avatarGO.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "thigh_l") left = t;
            if (t.name == "thigh_r") right = t;
        }
        if (left == null || right == null) return 0f;

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.orientationOffsetY = 0f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var graph = PlayableGraph.Create("CalibrateFacing");
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimationClipPlayable.Create(graph, clip);
        output.SetSourcePlayable(playable);
        var sum = Vector3.zero;
        for (int i = 0; i < 24; i++)
        {
            playable.SetTime(clip.length * i / 24f);
            graph.Evaluate(0f);
            var across = avatarGO.transform.InverseTransformDirection(right.position - left.position); // character's right
            sum += Vector3.Cross(across, Vector3.up).normalized;                                      // character's forward
        }
        graph.Destroy();

        float yaw = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg; // how far the body faces off +Z
        settings.orientationOffsetY = -yaw;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return -yaw;
    }

    /// <summary>Sets the clip's vertical offset ("level") so the lowest sole over the whole cycle lands
    /// on y = 0 for the MakeHuman avatar. Evaluated through a PlayableGraph with Foot IK on — the same
    /// evaluation the debug Animator states use (AnimationClip.SampleAnimation ignores both `level` and
    /// Foot IK, so it can't be used here). level -> height is linear, so two probes solve it.</summary>
    internal static float CalibrateGround(AnimationClip clip, GameObject avatarGO)
    {
        var animator = avatarGO.GetComponent<Animator>();
        animator.applyRootMotion = false;
        var body = avatarGO.GetComponentInChildren<SkinnedMeshRenderer>();

        float LowestSole(float level)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.level = level;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var graph = PlayableGraph.Create("CalibrateGround");
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(true);
            output.SetSourcePlayable(playable);

            var mesh = new Mesh();
            float lowest = float.MaxValue;
            for (int i = 0; i < 60; i++)
            {
                playable.SetTime(clip.length * i / 60f);
                graph.Evaluate(0f);
                body.BakeMesh(mesh, true);
                var toRoot = avatarGO.transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) lowest = Mathf.Min(lowest, toRoot.MultiplyPoint3x4(v).y);
            }
            graph.Destroy();
            Object.DestroyImmediate(mesh);
            return lowest;
        }

        float at0 = LowestSole(0f);
        float at1 = LowestSole(0.1f);
        float slope = (at1 - at0) / 0.1f;
        float level = Mathf.Abs(slope) > 1e-4f ? -at0 / slope : 0f;
        LowestSole(level); // leaves the clip with the solved level applied
        return level;
    }

    /// <summary>AvatarDebugAnimator: one state per DebugAnimation (named exactly after it), no
    /// parameters — AvatarDebugPreview.SetDebugAnimation cross-fades by state name.</summary>
    private static void BuildController(Dictionary<DebugAnimation, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var machine = controller.layers[0].stateMachine;
        foreach (var state in machine.states) machine.RemoveState(state.state);

        AnimatorState idle = null;
        foreach (var (animation, _) in Clips)
        {
            var state = machine.AddState(animation.ToString());
            state.motion = clips[animation];
            state.writeDefaultValues = true;
            state.iKOnFeet = true; // feet follow the source's recorded foot goals (better contact after retargeting)
            if (animation == DebugAnimation.Idle) idle = state;
        }
        machine.defaultState = idle;
        EditorUtility.SetDirty(controller);
    }
}
