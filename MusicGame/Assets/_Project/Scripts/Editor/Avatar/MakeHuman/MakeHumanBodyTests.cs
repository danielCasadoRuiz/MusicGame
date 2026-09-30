using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Automated acceptance tests for the MakeHuman body — every check drives the REAL runtime path
/// (AvatarFactory.AssembleBody / AvatarInstance.ApplyBody / AvatarBodyTransition), never a test-only
/// deformation. Expected geometry comes from an independent fresh bake (MakeHumanBodyBaker) combined
/// with the documented formula
///   V = lerp(MaleBase + maleMorphs, FemaleBase + femaleMorphs, Gender)
/// so the tests prove both the math and that the saved assets still match their source data.
///
///   Edit mode  (Tools > MusicGame > Avatars > Run MakeHuman Body Tests): geometry, height, ground,
///              Gender interpolation, determinism, shared skeleton, animation sampling, instances.
///   Play mode  (batch: -executeMethod MakeHumanBodyTests.RunPlayModeFromCommandLine): the real
///              Addressables AvatarFactory.CreateAsync path, a Gender transition on a live walking
///              Animator, and the real Fight scene opponent path.
/// </summary>
[InitializeOnLoad]
public static partial class MakeHumanBodyTests
{
    private const float Tolerance = 1e-4f;       // 0.1 mm — rest skinning reproduces positions to float precision
    private const float HeightTolerance = 1e-4f; // baked states must hit the canonical height this closely
    private const string PendingKey = "MakeHumanBodyTests.PlayModePending";
    private const string ResultFile = "Logs/MakeHumanBodyTests.txt";

    // ── Edit-mode suite ──────────────────────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Avatars/Run MakeHuman Body Tests")]
    public static void RunEditModeTestsMenu() => RunEditModeTests(out _);

    public static string RunEditModeTests(out bool passed)
    {
        var log = new TestLog();
        var errors = new List<string>();
        var bake = MakeHumanBodyBaker.Bake(errors);
        var maleRecipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MakeHumanBodyBuilder.MaleRecipePath);
        var femaleRecipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MakeHumanBodyBuilder.FemaleRecipePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);

        if (bake == null || maleRecipe == null || femaleRecipe == null || prefab == null)
        {
            log.Fail("Setup", $"missing bake/recipes/prefab — run the MakeHuman Body Builder first. {string.Join("; ", errors)}");
            return log.Finish(out passed);
        }

        // The FBX importer orders vertices its own way: map every imported vertex to its bake vertex
        // (exact position + UV match), so the bake can serve as the independent expectation.
        s_bodyOrder = MapImportedToBake(bake, 0, log);
        s_eyesOrder = MapImportedToBake(bake, 1, log);
        if (s_bodyOrder == null || s_eyesOrder == null) return log.Finish(out passed);

        var assetMeshes = new List<(Mesh mesh, Vector3[] snapshot)>();
        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            assetMeshes.Add((smr.sharedMesh, smr.sharedMesh.vertices));

        var created = new List<AvatarInstance>();
        try
        {
            CheckAssets(log, bake, maleRecipe.baseAvatar, prefab);
            CheckBakedHeightsAndGround(log, bake);

            var a = Assemble(prefab, maleRecipe, Vector3.zero);
            created.Add(a);
            log.Check("Recipes map to Gender endpoints (Male -> 0, Female -> 1)",
                      a.BodyMorphValues.Gender == 0f && femaleRecipe.ToRuntime().Identity.Body.Gender == 1f, "wrong Gender from recipe");

            // Snapshot the shared skeleton/bindposes: NOTHING below may change them.
            var skeleton = SnapshotSkeleton(a);
            var bindposes = a.BodyRenderers[0].sharedMesh.bindposes;
            var avatar = a.Animator.avatar;

            // Gender endpoints are exact.
            ExpectBody(log, "Gender 0 == MaleBase", a, Body(0f, 0.5f, 0f), bake);
            ExpectBody(log, "Gender 1 == FemaleBase", a, Body(1f, 0.5f, 0f), bake);

            // Each body channel alone, at its own gender's endpoint, reproduces its evaluated MakeHuman shape.
            foreach (var channel in MakeHumanBodyShapes.AllChannels)
            {
                float g = MakeHumanBodyShapes.BaseOf(channel) == BodyBaseType.Male ? 0f : 1f;
                var values = channel switch
                {
                    MorphChannel.MaleSlim or MorphChannel.FemaleSlim => Body(g, 0f, 0f),
                    MorphChannel.MaleHeavy or MorphChannel.FemaleHeavy => Body(g, 1f, 0f),
                    _ => Body(g, 0.5f, 1f),
                };
                a.ApplyBody(values);
                float error = MaxError(Fbx(bake.Morphs[channel].Parts[0].Vertices), AvatarBodyChecks.BakeBody(a));
                log.Check($"{channel} alone == evaluated {channel} (max error {Mm(error)})", error <= Tolerance, $"max error {Mm(error)}");
            }

            // Combinations + Gender interpolation, all against the closed-form expectation.
            ExpectBody(log, "Female, weight +0.4, muscle 0.7 == FemaleBase + 0.4 dHeavy + 0.7 dMuscle", a, Body(1f, 0.7f, 0.7f), bake);
            ExpectBody(log, "Gender 0.5 neutral == 0.5 MaleBase + 0.5 FemaleBase", a, Body(0.5f, 0.5f, 0f), bake);
            ExpectBody(log, "Gender 0.25 neutral == lerp(MaleBase, FemaleBase, 0.25)", a, Body(0.25f, 0.5f, 0f), bake);
            ExpectBody(log, "Heavy 0.8 + muscle 0.7 at Gender 0 == Male version", a, Body(0f, 0.9f, 0.7f), bake);
            ExpectBody(log, "Heavy 0.8 + muscle 0.7 at Gender 1 == Female version", a, Body(1f, 0.9f, 0.7f), bake);
            ExpectBody(log, "Heavy 0.8 + muscle 0.7 at Gender 0.5 == midpoint of Male/Female versions", a, Body(0.5f, 0.9f, 0.7f), bake);
            ExpectBody(log, "Slim 0.6 at Gender 0.3 == lerp(Male slim state, Female slim state, 0.3)", a, Body(0.3f, 0.2f, 0f), bake);
            ExpectBody(log, "Strong at Gender 0.75 == lerp(MaleMuscle, FemaleMuscle, 0.75)", a, Body(0.75f, 0.5f, 1f), bake);

            // Weight/Muscle survive a Gender change: the midpoint of a heavy male->female morph is the
            // midpoint of MaleHeavy and FemaleHeavy — never a neutralized body.
            {
                var heavyMid = Expected(bake, 0.5f, 1f, 0f);
                var maleHeavy = Fbx(bake.Morphs[MorphChannel.MaleHeavy].Parts[0].Vertices);
                var femaleHeavy = Fbx(bake.Morphs[MorphChannel.FemaleHeavy].Parts[0].Vertices);
                var neutralMid = Expected(bake, 0.5f, 0.5f, 0f);
                float fromLerp = 0f, fromNeutral = 0f;
                for (int i = 0; i < heavyMid.Length; i++)
                {
                    fromLerp = Mathf.Max(fromLerp, (heavyMid[i] - Vector3.Lerp(maleHeavy[i], femaleHeavy[i], 0.5f)).magnitude);
                    fromNeutral = Mathf.Max(fromNeutral, (heavyMid[i] - neutralMid[i]).magnitude);
                }
                log.Check($"Heavy stays heavy mid-transition (== lerp(MaleHeavy, FemaleHeavy) within {Mm(fromLerp)}, {fromNeutral * 100f:0.0} cm away from neutral)",
                          fromLerp <= Tolerance && fromNeutral > 0.01f, "Gender transition neutralizes weight");
            }

            // Determinism: ping-pong + the requested abuse sequence + random states; always lands exactly.
            {
                var target = Body(0.72f, 0.2f, 0.3f);
                a.ApplyBody(target);
                var reference = AvatarBodyChecks.BakeBody(a);
                var sequence = new[]
                {
                    Body(0f, 0.5f, 0f), Body(1f, 0.5f, 0f), Body(0f, 0.5f, 0f), Body(1f, 0.5f, 0f), Body(0.34f, 0.5f, 0f),
                    Body(0.34f, 1f, 0f), Body(0.34f, 1f, 1f), Body(0f, 1f, 1f), Body(1f, 1f, 1f), Body(0.72f, 1f, 1f), Body(0.72f, 0f, 0f),
                    Body(0f, 0.5f, 0f), Body(0.37f, 0.8f, 0.2f), Body(0.81f, 0.1f, 0.9f), Body(0.12f, 0.6f, 0.4f), Body(1f, 0.3f, 0f), Body(0.5f, 1f, 1f),
                };
                var random = new System.Random(7);
                for (int round = 0; round < 300; round++)
                {
                    foreach (var step in sequence) a.ApplyBody(step);
                    a.ApplyBody(Body((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));
                }
                for (int i = 0; i < 200; i++) a.ApplyBody(Body(i % 2, 0.5f, 0f));
                a.ApplyBody(target);
                int diff = AvatarBodyChecks.CountDifferent(reference, AvatarBodyChecks.BakeBody(a));
                log.Check("Gender stress: 300 rounds of the Male/Female/0.34/Heavy/Strong/0.72/Thin sequence + random states + 200 ping-pongs — bit-exact return",
                          diff == 0, $"{diff} rendered vertices drifted");
                ExpectBody(log, "  ...and still equals the closed-form expectation", a, target, bake);
            }

            // One skeleton: nothing above touched bones, bindposes, the Humanoid Avatar or scale.
            log.Check("Body changes never touch the skeleton rest pose", SkeletonUnchanged(skeleton), "a bone moved");
            log.Check("Body changes never touch bindposes", SameMatrices(bindposes, a.BodyRenderers[0].sharedMesh.bindposes), "bindposes changed");
            log.Check("Body changes never swap the Humanoid Avatar", a.Animator.avatar == avatar, "Animator.avatar changed");
            log.Check("Body changes never scale the avatar", a.Root.localScale == Vector3.one, "localScale changed");

            CheckRenderedHeightAndGround(log, a);
            CheckAnimation(log, a);

            // Two instances never share/affect each other's meshes.
            {
                var b = Assemble(prefab, femaleRecipe, new Vector3(1.2f, 0f, 0f));
                created.Add(b);
                a.ApplyBody(Body(0.2f, 0f, 0f));
                b.ApplyBody(Body(0.8f, 1f, 1f));
                var bBefore = AvatarBodyChecks.BakeBody(b);
                for (int i = 0; i <= 50; i++) a.ApplyBody(Body(Mathf.PingPong(i * 0.07f, 1f), i / 50f, 1f - i / 50f));
                int diff = AvatarBodyChecks.CountDifferent(bBefore, AvatarBodyChecks.BakeBody(b));
                log.Check("Two avatars have separate runtime meshes", a.BodyRenderers[0].sharedMesh != b.BodyRenderers[0].sharedMesh, "instances share a Mesh");
                log.Check("Morphing avatar A (Gender 0.2 thin) leaves avatar B (Gender 0.8 heavy strong) bit-identical", diff == 0, $"{diff} of B's vertices changed");
                ExpectBody(log, "  ...and B equals its closed-form body", b, Body(0.8f, 1f, 1f), bake);
                log.Check("Both avatars share the same Humanoid Avatar asset", a.Animator.avatar == b.Animator.avatar, "different Avatars");
            }

            // Instance safety — nothing above may have written into the shared baked asset.
            log.Check("Runtime mesh is a clone (not the asset)", !AssetDatabase.Contains(a.BodyRenderers[0].sharedMesh), "renderer is using the shared asset mesh");
            foreach (var (mesh, snapshot) in assetMeshes)
                log.Check($"Shared asset mesh '{mesh.name}' unchanged", AvatarBodyChecks.CountDifferent(snapshot, mesh.vertices) == 0, "the asset mesh was mutated");
        }
        catch (System.Exception e)
        {
            log.Fail("Exception", e.ToString());
        }
        finally
        {
            foreach (var instance in created) instance.Dispose();
        }

        return log.Finish(out passed);
    }

    private static void CheckAssets(TestLog log, MakeHumanBakeResult bake, BaseAvatarDefinitionSO baseAvatar, GameObject prefab)
    {
        log.Check("BaseAvatarDefinitionSO wired", baseAvatar != null && baseAvatar.maleBase != null && baseAvatar.femaleBase != null &&
                  baseAvatar.baseAvatarPrefab != null && baseAvatar.baseAvatarPrefab.editorAsset == prefab, "baseAvatar/presets/prefab reference missing");
        if (baseAvatar == null) return;

        foreach (var type in new[] { BodyBaseType.Male, BodyBaseType.Female })
        {
            var preset = baseAvatar.GetBasePreset(type);
            var fresh = bake.GetBase(type);
            for (int p = 0; p < bake.Source.Parts.Length; p++)
            {
                var data = preset != null ? preset.FindRenderer(bake.Source.Parts[p].Name) : null;
                float error = data == null ? float.MaxValue : MaxError(Fbx(fresh.Parts[p].Vertices, p), data.vertices);
                log.Check($"{type}Base '{bake.Source.Parts[p].Name}' through the FBX == fresh bake (max error {Mm(error)})", error <= RoundTripTolerance,
                          data == null ? "renderer entry missing" : $"max error {Mm(error)} — rebake needed");
            }
        }

        var animator = prefab.GetComponent<Animator>();
        var humanoid = MakeHumanFbxPipeline.LoadAvatar();
        var importer = AssetImporter.GetAtPath(MakeHumanFbxPipeline.FbxPath) as ModelImporter;
        log.Check("MakeHuman_Canonical.fbx imports as Humanoid / Create From This Model", importer != null &&
                  importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel,
                  "wrong import settings");
        log.Check($"ONE canonical Humanoid Avatar from the FBX ('{(humanoid != null ? humanoid.name : "-")}'), valid, used by the prefab",
                  humanoid != null && humanoid.isValid && humanoid.isHuman && animator != null && animator.avatar == humanoid, "missing/invalid/unassigned");
        log.Check("No other Avatar assets remain in Generated/", AssetDatabase.FindAssets("t:Avatar", new[] { MakeHumanBodyBuilder.GeneratedFolder }).Length == 1,
                  "stale Avatar assets in Generated/");
        log.Check("Avatar prefab is a variant of the canonical FBX model", PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant &&
                  PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab) == MakeHumanFbxPipeline.LoadModel(), "prefab not based on the FBX");
        CheckFbxRoundTrip(log, bake, prefab);

        var channels = new List<MorphChannel>(MakeHumanBodyShapes.AllChannels) { MorphChannel.Gender };
        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int shapes = 0;
            foreach (var channel in channels) if (smr.sharedMesh.GetBlendShapeIndex(channel.ToString()) >= 0) shapes++;
            log.Check($"'{smr.name}' has Gender + all six body blendshapes", shapes == 7, $"only {shapes}/7");
            log.Check($"'{smr.name}' skinned to the one {bake.Rig.Bones.Count}-bone skeleton", smr.bones.Length == bake.Rig.Bones.Count && smr.sharedMesh.bindposeCount == smr.bones.Length,
                      $"{smr.bones.Length} bones / {smr.sharedMesh.bindposeCount} bindposes");
            var male = baseAvatar.maleBase.FindRenderer(smr.name);
            var restVertices = smr.sharedMesh.vertices;
            log.Check($"'{smr.name}' rest vertices ARE MaleBase", male != null && AvatarBodyChecks.CountDifferent(male.vertices, restVertices) == 0,
                      male == null ? "no preset entry" : $"{AvatarBodyChecks.CountDifferent(male.vertices, restVertices)} of {restVertices.Length} differ");
        }

        string guid = AssetDatabase.AssetPathToGUID(MakeHumanBodyBuilder.PrefabPath);
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        log.Check("Prefab is Addressable", settings != null && settings.FindAssetEntry(guid) != null, "no Addressables entry");
    }

    /// <summary>The eight baked source states: same canonical height, same ground plane.</summary>
    private static void CheckBakedHeightsAndGround(TestLog log, MakeHumanBakeResult bake)
    {
        var sb = new StringBuilder();
        float minH = float.MaxValue, maxH = float.MinValue, maxGround = 0f;
        foreach (var state in bake.AllStates())
        {
            sb.Append($"{state.Name} {state.Height:0.00000} m / ground {Mm(state.GroundY)}; ");
            minH = Mathf.Min(minH, state.Height);
            maxH = Mathf.Max(maxH, state.Height);
            maxGround = Mathf.Max(maxGround, Mathf.Abs(state.GroundY));
        }
        log.Info($"Baked heights: {sb}");
        log.Check($"All 8 baked states are {MakeHumanBodyBaker.CanonicalHeight} m tall (range {minH:0.00000}..{maxH:0.00000} m)",
                  Mathf.Abs(minH - MakeHumanBodyBaker.CanonicalHeight) <= HeightTolerance && Mathf.Abs(maxH - MakeHumanBodyBaker.CanonicalHeight) <= HeightTolerance,
                  "height mismatch");
        log.Check($"MaleBase height == FemaleBase height ({bake.Male.Height:0.00000} vs {bake.Female.Height:0.00000} m)",
                  Mathf.Abs(bake.Male.Height - bake.Female.Height) <= HeightTolerance, "different heights");
        log.Check($"All 8 baked states stand on the same ground plane (max |ground| {Mm(maxGround)})", maxGround <= HeightTolerance, "ground mismatch");
        log.Check("MakeHuman's own height targets did the work (final uniform scale within 0.1%)",
                  AllStatesScaleWithin(bake, 0.001f), "normalization had to rescale by more than 0.1%");
    }

    /// <summary>Rendered (skinned) body along the Gender axis and for Heavy/Strong transitions: same
    /// height and grounded at every intermediate value.</summary>
    private static void CheckRenderedHeightAndGround(TestLog log, AvatarInstance a)
    {
        var sb = new StringBuilder();
        float worstHeight = 0f, worstGround = 0f;
        foreach (var (weight, muscle, label) in new[] { (0.5f, 0f, "neutral"), (1f, 0f, "heavy"), (0.5f, 1f, "strong"), (0f, 0f, "thin") })
        {
            sb.Append($"{label}: ");
            for (int step = 0; step <= 10; step++)
            {
                float g = step / 10f;
                a.ApplyBody(Body(g, weight, muscle));
                var vertices = AvatarBodyChecks.BakeBody(a);
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var v in vertices) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
                worstHeight = Mathf.Max(worstHeight, Mathf.Abs(maxY - minY - MakeHumanBodyBaker.CanonicalHeight));
                worstGround = Mathf.Max(worstGround, Mathf.Abs(minY));
                if (step % 5 == 0) sb.Append($"g{g:0.0}={maxY - minY:0.0000}m ");
            }
            sb.Append("| ");
        }
        log.Info($"Rendered heights along Gender: {sb}");
        log.Check($"Rendered body keeps canonical height across Gender 0..1 (neutral/heavy/strong/thin, worst deviation {Mm(worstHeight)})",
                  worstHeight <= 0.005f, $"height drifts {Mm(worstHeight)}");
        log.Check($"Rendered body stays grounded across Gender 0..1 (worst |minY| {Mm(worstGround)})", worstGround <= 0.005f, $"feet off the ground by {Mm(worstGround)}");
    }

    /// <summary>The three rig-validation clips exactly as the debug scene plays them — the motions of
    /// the AvatarDebugAnimator states Idle / Walk / Run.</summary>
    private static Dictionary<DebugAnimation, AnimationClip> LoadValidationClips()
    {
        var clips = new Dictionary<DebugAnimation, AnimationClip>();
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(MakeHumanAnimationTestSetup.ControllerPath);
        if (controller == null) return clips;
        foreach (var child in controller.layers[0].stateMachine.states)
            if (System.Enum.TryParse(child.state.name, out DebugAnimation animation) && child.state.motion is AnimationClip clip)
                clips[animation] = clip;
        return clips;
    }

    private static readonly (string label, BodyMorphValues body)[] AnimationBodies =
    {
        ("Male neutral",   new BodyMorphValues { Gender = 0f,   Weight = 0.5f, Muscle = 0f }),
        ("Female neutral", new BodyMorphValues { Gender = 1f,   Weight = 0.5f, Muscle = 0f }),
        ("Gender 0.5",     new BodyMorphValues { Gender = 0.5f, Weight = 0.5f, Muscle = 0f }),
        ("Male heavy",     new BodyMorphValues { Gender = 0f,   Weight = 1f,   Muscle = 0f }),
        ("Female heavy",   new BodyMorphValues { Gender = 1f,   Weight = 1f,   Muscle = 0f }),
        ("Male strong",    new BodyMorphValues { Gender = 0f,   Weight = 0.5f, Muscle = 1f }),
        ("Female strong",  new BodyMorphValues { Gender = 1f,   Weight = 0.5f, Muscle = 1f }),
        ("Female thin",    new BodyMorphValues { Gender = 1f,   Weight = 0f,   Muscle = 0f }),
    };

    private static readonly string[] FingerBones =
    {
        "thumb_01_l", "thumb_02_l", "thumb_03_l", "index_01_l", "index_02_l", "index_03_l", "middle_01_l", "middle_02_l", "middle_03_l",
        "ring_01_l", "ring_02_l", "ring_03_l", "pinky_01_l", "pinky_02_l", "pinky_03_l",
        "thumb_01_r", "thumb_02_r", "thumb_03_r", "index_01_r", "index_02_r", "index_03_r", "middle_01_r", "middle_02_r", "middle_03_r",
        "ring_01_r", "ring_02_r", "ring_03_r", "pinky_01_r", "pinky_02_r", "pinky_03_r",
    };

    /// <summary>
    /// Idle / Walk / Run x eight bodies, each evaluated at several clip times through Unity's real Humanoid
    /// retargeting, exactly like the debug Animator states (EvaluateLikeRuntime: PlayableGraph + Foot IK):
    ///   - the skeleton actually moves;
    ///   - skin follows the bones at both hands and both feet (a vertex bound near each keeps its
    ///     rest distance to the bone — no detaching/collapsing);
    ///   - finger curves are constant, pinned to the avatar's relaxed rest hand, so fingers stay (within
    ///     the Humanoid finger-muscle residual) in their rest pose — the old generated clip's hands were
    ///     the complaint;
    ///   - feet: never sink below the floor, and do touch it during the cycle;
    ///   - the same clip time gives the identical bone pose for every body (one skeleton).
    /// </summary>
    private static void CheckAnimation(TestLog log, AvatarInstance a)
    {
        var clips = LoadValidationClips();
        log.Check("Idle / Walk / Run validation clips present", clips.Count == 3, $"only {clips.Count}/3 — run Tools > MusicGame > Avatars > Setup Animation Test Clips");
        if (clips.Count != 3 || a.Animator == null) return;

        var tracked = new[] { "hand_l", "hand_r", "foot_l", "foot_r" };
        foreach (var (animation, clip) in clips)
        {
            int fingerCurves = 0, animatedFingerCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                    if (binding.propertyName.Contains(finger))
                    {
                        fingerCurves++;
                        var keys = AnimationUtility.GetEditorCurve(clip, binding).keys;
                        foreach (var k in keys) if (Mathf.Abs(k.value - keys[0].value) > 1e-6f) { animatedFingerCurves++; break; }
                        break;
                    }
            log.Check($"{animation}: Humanoid, looping, finger curves held constant ({fingerCurves} constant, {animatedFingerCurves} animated)",
                      clip.isHumanMotion && clip.isLooping && animatedFingerCurves == 0, "not humanoid/looping or animates fingers");

            float worstFollow = 0f, worstSink = 0f, lowestSole = float.MaxValue, maxBoneMove = 0f, worstFinger = 0f;
            string worstFingerName = "";
            bool fingersRest = true, sameSkeleton = true;
            string worstWhere = "";
            var times = new[] { 0.1f, 0.35f, 0.6f, 0.85f };
            var bonePoseAtTime = new Dictionary<float, Vector3>();

            foreach (var (label, body) in AnimationBodies)
            {
                a.ResetToRestPose();
                a.ApplyBody(body);
                var restMesh = AvatarBodyChecks.BakeBody(a);
                var restFingers = new Dictionary<string, Quaternion>();
                foreach (var f in FingerBones) restFingers[f] = a.SkeletonMapper.FindBone(f).localRotation;

                var nearest = new int[tracked.Length];
                var restOffset = new float[tracked.Length];
                var restBone = new Vector3[tracked.Length];
                for (int t = 0; t < tracked.Length; t++)
                {
                    var bone = a.SkeletonMapper.FindBone(tracked[t]);
                    restBone[t] = bone.position;
                    var local = a.Root.InverseTransformPoint(bone.position);
                    nearest[t] = NearestVertex(restMesh, local);
                    restOffset[t] = (restMesh[nearest[t]] - local).magnitude;
                }

                float bodyLowest = float.MaxValue;
                foreach (float time in times)
                {
                    EvaluateLikeRuntime(a, clip, clip.length * time);
                    var posed = AvatarBodyChecks.BakeBody(a);
                    foreach (var v in posed) { bodyLowest = Mathf.Min(bodyLowest, v.y); worstSink = Mathf.Min(worstSink, v.y); }

                    for (int t = 0; t < tracked.Length; t++)
                    {
                        var bone = a.SkeletonMapper.FindBone(tracked[t]);
                        var local = a.Root.InverseTransformPoint(bone.position);
                        float offset = (posed[nearest[t]] - local).magnitude;
                        float follow = Mathf.Abs(offset - restOffset[t]);
                        if (follow > worstFollow) { worstFollow = follow; worstWhere = $"{label} {tracked[t]} t={time}"; }
                        maxBoneMove = Mathf.Max(maxBoneMove, (bone.position - restBone[t]).magnitude);
                    }
                    foreach (var f in FingerBones)
                    {
                        float angle = Quaternion.Angle(a.SkeletonMapper.FindBone(f).localRotation, restFingers[f]);
                        if (angle > worstFinger) { worstFinger = angle; worstFingerName = f; }
                        if (angle > 15f) fingersRest = false;
                    }

                    var handPos = a.SkeletonMapper.FindBone("hand_l").position;
                    if (!bonePoseAtTime.TryGetValue(time, out var reference)) bonePoseAtTime[time] = handPos;
                    else if ((handPos - reference).magnitude > 1e-4f) sameSkeleton = false;
                }
                lowestSole = Mathf.Min(lowestSole, bodyLowest);
            }

            log.Check($"{animation}: skeleton animates (max hand/foot travel {maxBoneMove * 100f:0} cm)", maxBoneMove > 0.02f, "no motion");
            log.Check($"{animation} x {AnimationBodies.Length} bodies: skin follows hands and feet (worst {worstFollow * 100f:0.0} cm — {worstWhere})", worstFollow < 0.02f, $"skin detached {worstFollow * 100f:0.0} cm at {worstWhere}");
            log.Check($"{animation}: fingers stay at the relaxed rest hand (worst joint {worstFingerName} {worstFinger:0.0} deg — residual of Humanoid finger muscle space)", fingersRest, $"{worstFingerName} rotated {worstFinger:0.0} deg");
            log.Check($"{animation}: feet never sink below the floor (lowest {Mm(worstSink)})", worstSink > -0.015f, $"sinks {Mm(-worstSink)}");
            log.Check($"{animation}: feet touch the floor during the cycle (lowest sole {Mm(lowestSole)})", lowestSole < 0.015f, "floating");
            log.Check($"{animation}: identical bone pose for every body at the same clip time", sameSkeleton, "skeleton pose depends on the body");
        }
        a.ResetToRestPose();
    }

    // ── Play-mode suite (batch) ──────────────────────────────────────────────────

    /// <summary>Unity.exe -batchmode -projectPath ... -executeMethod MakeHumanBodyTests.RunPlayModeFromCommandLine
    /// (no -quit: the harness exits itself once play-mode checks finish).</summary>
    public static void RunPlayModeFromCommandLine()
    {
        string editReport = RunEditModeTests(out bool editPassed);
        File.WriteAllText(ResultFile, editReport + "\n");
        if (!editPassed) { EditorApplication.Exit(1); return; }

        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    static MakeHumanBodyTests()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            _ = RunPlayModeChecks();
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(CombatPendingKey, false)) return;
            SessionState.SetBool(CombatPendingKey, false);
            _ = RunCombatPlayChecks();
        };
    }

    private static async Task RunPlayModeChecks()
    {
        var log = new TestLog();
        try
        {
            // A — the real Addressables AvatarFactory path (what AvatarDebugPreview uses).
            var preview = Object.FindFirstObjectByType<AvatarDebugPreview>();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (preview != null && (preview.IsBuilding || preview.Instances.Count < 2) && Time.realtimeSinceStartup < deadline) await Task.Yield();
            log.Check("P1 AvatarDebugPreview built 2 avatars via Addressables", preview != null && preview.Instances.Count == 2 &&
                      preview.Instances[0].Root != null && preview.Instances[1].Root != null, "preview did not build");
            if (preview != null && preview.Instances.Count == 2)
            {
                var male = preview.Instances[0];
                var female = preview.Instances[1];
                var baseAvatar = male.Recipe.BaseAvatar;
                float maleError = MaxError(baseAvatar.maleBase.FindRenderer("Body").vertices, AvatarBodyChecks.BakeBody(male));
                float femaleError = MaxError(baseAvatar.femaleBase.FindRenderer("Body").vertices, AvatarBodyChecks.BakeBody(female));
                log.Check($"P1 debug avatars render exactly MaleBase / FemaleBase ({Mm(maleError)} / {Mm(femaleError)})",
                          maleError <= Tolerance && femaleError <= Tolerance, "endpoint mismatch");
                log.Check("P1 both avatars share one Humanoid Avatar", male.Animator.avatar == female.Animator.avatar, "different Avatars");

                var root = male.Root;
                foreach (var animation in new[] { DebugAnimation.Idle, DebugAnimation.Walk, DebugAnimation.Run })
                    await CheckAnimatedGenderTransition(log, preview, male, animation);
                preview.SetDebugAnimation(DebugAnimation.None);
                male.ApplyBody(Body(1f, 0.5f, 0f));
                await Task.Yield();
                float restError = MaxError(baseAvatar.femaleBase.FindRenderer("Body").vertices, AvatarBodyChecks.BakeBody(male));
                log.Check($"P2 after all that, rest Female neutral renders exactly FemaleBase ({Mm(restError)})", restError <= Tolerance, $"{Mm(restError)} off");
                log.Check("P2 switching Idle/Walk/Run never rebuilt the avatar (same instance, same root)", preview.Instances[0] == male && male.Root == root, "avatar rebuilt");
            }

            // B — the real Fight pipeline: OpponentLevelConfig.avatarRecipe -> FightSceneBootstrap -> AvatarFactory -> FighterActor.
            var rex = AssetDatabase.LoadAssetAtPath<OpponentDefinition>(MakeHumanBodyBuilder.TestOpponentPath);
            var levelConfig = rex != null && rex.levels.Length > 0 ? rex.levels[0] : null;
            log.Check("P3 Opponent_Rex level 1 has the MakeHuman recipe", levelConfig != null && levelConfig.avatarRecipe != null, "no avatarRecipe assigned");

            var sessionGO = new GameObject("TestGameSession");
            var session = sessionGO.AddComponent<GameSession>();
            session.SelectedOpponent = rex;
            session.SelectedOpponentLevelConfig = levelConfig;

            var loadOp = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Fight.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            while (!loadOp.isDone) await Task.Yield();

            FighterActor opponent = null;
            deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var go = GameObject.Find("FighterOpponent");
                opponent = go != null ? go.GetComponent<FighterActor>() : null;
                if (opponent != null && !opponent.UsedFallbackCapsule) break;
                await Task.Yield();
            }

            bool built = opponent != null && !opponent.UsedFallbackCapsule;
            log.Check("P3 Fight opponent visual replaced by the AvatarFactory avatar (not the capsule)", built, "opponent still uses the fallback capsule");
            if (built)
            {
                var animator = opponent.VisualRoot.GetComponentInChildren<Animator>();
                var smr = opponent.VisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                log.Check("P3 Fight opponent is the Humanoid MakeHuman avatar", animator != null && animator.isHuman &&
                          smr != null && smr.sharedMesh.GetBlendShapeIndex(MorphChannel.Gender.ToString()) >= 0, "not the MakeHuman humanoid");
                log.Check("P3 Fight opponent uses its own runtime mesh", smr != null && !AssetDatabase.Contains(smr.sharedMesh), "shared asset mesh in use");
                log.Check("P3 Rex (Male recipe) renders at Gender 0", smr != null && smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex("Gender")) == 0f, "Gender weight not 0");
            }
        }
        catch (System.Exception e)
        {
            log.Fail("Exception", e.ToString());
        }

        string report = log.Finish(out bool passed);
        File.AppendAllText(ResultFile, report + "\n");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
    }

    /// <summary>
    /// The headline behaviour: a playing avatar (Idle, Walk or Run via the real
    /// AvatarDebugPreview.SetDebugAnimation) morphs Male -> Female -> Male through the real
    /// AvatarBodyTransition while the real Animator keeps playing. Per frame: same Animator, same
    /// Avatar, same state, normalized time never jumps back (no Rebind/restart), root/scale untouched,
    /// feet stay within the animation's own grounded range.
    /// </summary>
    private static async Task CheckAnimatedGenderTransition(TestLog log, AvatarDebugPreview preview, AvatarInstance avatar, DebugAnimation animation)
    {
        var animator = avatar.Animator;
        var humanoid = animator.avatar;
        string tag = $"P2 {animation}:";
        avatar.ApplyBody(Body(0f, 0.8f, 0.4f)); // Male, weight +0.6, muscle 0.4 — must survive the transition
        preview.SetDebugAnimation(animation);

        // Let the cross-fade settle, then record how low/high the soles go on the plain Male body.
        float until = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < until) await Task.Yield();
        float baseMin = float.MaxValue, baseMax = float.MinValue;
        until = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < until)
        {
            await Task.Yield();
            float sole = SoleHeight(avatar);
            baseMin = Mathf.Min(baseMin, sole);
            baseMax = Mathf.Max(baseMax, sole);
        }

        int expectedHash = Animator.StringToHash("Base Layer." + animation);
        int stateHash = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        float lastTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        float startTime = lastTime;
        var rootPosition = avatar.Root.localPosition;
        bool sameState = true, timeContinuous = true, sameAvatar = true, rootStable = true;
        float soleMin = float.MaxValue, soleMax = float.MinValue, maxJump = 0f;
        int frames = 0;

        foreach (float target in new[] { 1f, 0f })
        {
            var transition = AvatarBodyTransition.StartGender(avatar, target, 1.5f);
            float previousGender = avatar.BodyMorphValues.Gender;
            while (transition.IsRunning)
            {
                await Task.Yield();
                frames++;
                var info = animator.GetCurrentAnimatorStateInfo(0);
                sameState &= info.fullPathHash == stateHash;
                timeContinuous &= info.normalizedTime >= lastTime - 1e-4f;
                lastTime = info.normalizedTime;
                sameAvatar &= animator.avatar == humanoid && avatar.Animator == animator;
                rootStable &= avatar.Root.localPosition == rootPosition && avatar.Root.localScale == Vector3.one;
                maxJump = Mathf.Max(maxJump, Mathf.Abs(avatar.BodyMorphValues.Gender - previousGender));
                previousGender = avatar.BodyMorphValues.Gender;
                if (frames % 3 == 0)
                {
                    float sole = SoleHeight(avatar);
                    soleMin = Mathf.Min(soleMin, sole);
                    soleMax = Mathf.Max(soleMax, sole);
                }
            }
            log.Check($"{tag} transition to Gender {target:0} lands exactly, weight/muscle kept ({avatar.BodyMorphValues.Weight:0.0}/{avatar.BodyMorphValues.Muscle:0.0})",
                      avatar.BodyMorphValues.Gender == target && avatar.BodyMorphValues.Weight == 0.8f && avatar.BodyMorphValues.Muscle == 0.4f, "didn't land / lost weight-muscle");
        }

        log.Check($"{tag} playing the '{animation}' state throughout {frames} morph frames", sameState && stateHash == expectedHash, "state changed / wrong state");
        log.Check($"{tag} animation never restarted (normalized time {startTime:0.00} -> {lastTime:0.00}, monotonic)", timeContinuous && lastTime > startTime + 0.5f, "time jumped back — Rebind/restart");
        log.Check($"{tag} same Animator + same Humanoid Avatar every frame", sameAvatar, "Animator/Avatar replaced");
        log.Check($"{tag} root never moved/scaled (in place, no root motion)", rootStable && !animator.applyRootMotion, "root transform changed");
        log.Check($"{tag} smooth: largest per-frame Gender step {maxJump:0.000}", maxJump < 0.1f, "visible pop");
        log.Check($"{tag} feet on the floor with the real Animator (lowest sole {Mm(baseMin)}, highest {Mm(baseMax)})", baseMin > -0.015f && baseMin < 0.015f, $"lowest sole {Mm(baseMin)}");
        log.Check($"{tag} feet grounded like the plain animation (soles {Mm(soleMin)}..{Mm(soleMax)} vs {Mm(baseMin)}..{Mm(baseMax)})",
                  soleMin >= baseMin - 0.015f && soleMax <= baseMax + 0.015f, "feet float/sink during the morph");
    }

    private static float SoleHeight(AvatarInstance avatar)
    {
        float minY = float.MaxValue;
        foreach (var v in AvatarBodyChecks.BakeBody(avatar)) minY = Mathf.Min(minY, v.y);
        return minY;
    }

    // ── Visual check (batch) ─────────────────────────────────────────────────────

    /// <summary>Renders the Gender axis (0 / 0.25 / 0.5 / 0.75 / 1) for neutral, heavy and strong bodies,
    /// plus walk-posed avatars along the axis, to Logs/MakeHumanPreview_*.png — for eyeballing shading/
    /// skinning at intermediate states. Batch: -executeMethod MakeHumanBodyTests.RenderPreviewFromCommandLine -quit</summary>
    [MenuItem("Tools/MusicGame/Avatars/Render MakeHuman Preview Images")]
    public static void RenderPreviewFromCommandLine()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // before loading: a new scene unloads unused assets
        var male = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MakeHumanBodyBuilder.MaleRecipePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        var clips = LoadValidationClips();
        var instances = new List<AvatarInstance>();
        var rowAnimations = new[] { DebugAnimation.Idle, DebugAnimation.Walk, DebugAnimation.Run };
        int columns = AnimationBodies.Length;
        for (int r = 0; r < rowAnimations.Length; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                // Mirror X so the first body appears on the LEFT of a camera looking down -Z at the avatars' fronts.
                var instance = Assemble(prefab, male, new Vector3(-(c - (columns - 1) * 0.5f) * 0.8f, 0f, -r * 3f));
                instance.ApplyBody(AnimationBodies[c].body);
                if (clips.TryGetValue(rowAnimations[r], out var clip)) EvaluateLikeRuntime(instance, clip, clip.length * 0.3f);
                instances.Add(instance);
            }
        }

        var lightGO = new GameObject("Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        lightGO.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

        var camGO = new GameObject("Camera");
        var cam = camGO.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.05f;

        void Shot(string file, Vector3 position, Vector3 lookAt, int width, int height)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        void ShowRow(int row)
        {
            for (int i = 0; i < instances.Count; i++) instances[i].Root.gameObject.SetActive(row < 0 || i / columns == row);
        }
        for (int r = 0; r < rowAnimations.Length; r++)
        {
            ShowRow(r);
            float z = -r * 3f;
            Shot($"Logs/MakeHumanPreview_{rowAnimations[r]}.png", new Vector3(0f, 0.95f, z + 7.8f), new Vector3(0f, 0.9f, z), 2400, 900);
            Shot($"Logs/MakeHumanPreview_{rowAnimations[r]}_side.png", new Vector3(8f, 0.95f, z + 0.3f), new Vector3(0f, 0.9f, z), 2000, 900);
            // Hands close-up on the first two bodies (Male / Female neutral).
            Shot($"Logs/MakeHumanPreview_{rowAnimations[r]}_hands.png", new Vector3(2.4f, 1.0f, z + 1.7f), new Vector3(2.4f, 0.95f, z), 2000, 900);
        }

        foreach (var instance in instances) instance.Dispose();

        // Gait strips: 8 phases of each animation, side view on a visible floor, evaluated exactly like
        // the debug Animator states (PlayableGraph + Foot IK on the derived clips).
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.localScale = new Vector3(0.9f, 0.02f, 20f);
        floor.transform.position = new Vector3(0f, -0.01f, 3f);
        foreach (var (animation, clip) in clips)
        {
            var strip = new List<(AvatarInstance instance, bool _)>();
            for (int i = 0; i < 8; i++)
            {
                var instance = Assemble(prefab, male, new Vector3(0f, 0f, i * 0.9f));
                instance.ApplyBody(Body(0f, 0.5f, 0f));
                strip.Add((instance, EvaluateLikeRuntime(instance, clip, clip.length * i / 8f)));
            }
            cam.orthographic = true;
            cam.orthographicSize = 1.05f;
            Shot($"Logs/MakeHumanPreview_{animation}_gait.png", new Vector3(6f, 0.95f, 3.15f), new Vector3(0f, 0.95f, 3.15f), 3200, 840);
            cam.orthographic = false;
            foreach (var (instance, graph) in strip) instance.Dispose();
        }
        Object.DestroyImmediate(floor);
        Debug.Log("[MakeHumanBodyTests] Preview images written to Logs/MakeHumanPreview_*.png");
    }

    /// <summary>Poses `instance` at `time` exactly like the debug Animator states do (PlayableGraph,
    /// Foot IK on, no root motion) — AnimationClip.SampleAnimation ignores the clip's root/height
    /// settings and Foot IK, so it misrepresents grounding and facing. Returns true when posed.</summary>
    private static bool EvaluateLikeRuntime(AvatarInstance instance, AnimationClip clip, float time)
    {
        instance.Animator.applyRootMotion = false;
        var graph = PlayableGraph.Create("EvaluateLikeRuntime");
        var output = AnimationPlayableOutput.Create(graph, "out", instance.Animator);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(true);
        output.SetSourcePlayable(playable);
        playable.SetTime(time);
        graph.Evaluate(0f);
        graph.Destroy();
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private const float RoundTripTolerance = 1e-4f; // 0.1 mm through JSON -> Blender -> FBX -> Unity
    private static int[] s_bodyOrder, s_eyesOrder;  // imported vertex -> bake vertex

    private static Vector3[] Fbx(Vector3[] bakeOrder, int part = 0)
    {
        var order = part == 0 ? s_bodyOrder : s_eyesOrder;
        var result = new Vector3[order.Length];
        for (int i = 0; i < order.Length; i++) result[i] = bakeOrder[order[i]];
        return result;
    }

    /// <summary>imported mesh vertex -> bake mesh vertex, by exact position + UV (every Unity vertex is a
    /// unique (position, uv) pair). Null (and a FAIL) if any imported vertex has no counterpart.</summary>
    private static int[] MapImportedToBake(MakeHumanBakeResult bake, int partIndex, TestLog log)
    {
        var part = bake.Source.Parts[partIndex];
        var mesh = MakeHumanFbxPipeline.LoadMesh(part.Name);
        if (mesh == null) { log.Fail($"FBX mesh '{part.Name}'", "missing from MakeHuman_Canonical.fbx"); return null; }

        var bakeVerts = bake.Male.Parts[partIndex].Vertices;
        var grid = new Dictionary<Vector3Int, List<int>>();
        Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x * 1000f), Mathf.FloorToInt(p.y * 1000f), Mathf.FloorToInt(p.z * 1000f));
        for (int i = 0; i < bakeVerts.Length; i++)
        {
            var c = Cell(bakeVerts[i]);
            if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
            list.Add(i);
        }

        var verts = mesh.vertices;
        var uvs = mesh.uv;
        var order = new int[verts.Length];
        int missing = 0;
        float worst = 0f;
        for (int i = 0; i < verts.Length; i++)
        {
            var c = Cell(verts[i]);
            int best = -1;
            float bestCost = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var list)) continue;
                foreach (int j in list)
                {
                    float cost = (bakeVerts[j] - verts[i]).sqrMagnitude + (part.UVs[j] - uvs[i]).sqrMagnitude;
                    if (cost < bestCost) { bestCost = cost; best = j; }
                }
            }
            if (best < 0 || bestCost > 1e-8f) { missing++; continue; }
            worst = Mathf.Max(worst, Mathf.Sqrt(bestCost));
            order[i] = best;
        }
        log.Check($"FBX '{part.Name}': every imported vertex matches a bake vertex (position+UV; {verts.Length} imported vs {bakeVerts.Length} baked, worst {worst:0.0e0})",
                  missing == 0 && verts.Length == bakeVerts.Length, $"{missing} unmatched");
        return missing == 0 ? order : null;
    }

    /// <summary>FBX round trip vs the bake: skeleton (hierarchy + T-pose joints), T-pose shape, skin
    /// weights, normals, blendshape names.</summary>
    private static void CheckFbxRoundTrip(TestLog log, MakeHumanBakeResult bake, GameObject prefab)
    {
        var bones = new Dictionary<string, Transform>();
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;

        float worstJoint = 0f; string worstJointName = ""; int hierarchyErrors = 0;
        for (int i = 0; i < bake.Rig.Bones.Count; i++)
        {
            var b = bake.Rig.Bones[i];
            if (!bones.TryGetValue(b.Name, out var t)) { hierarchyErrors++; continue; }
            if (b.Parent != null && (t.parent == null || t.parent.name != b.Parent)) hierarchyErrors++;
            float e = (prefab.transform.InverseTransformPoint(t.position) - bake.TPose.Position[i]).magnitude;
            if (e > worstJoint) { worstJoint = e; worstJointName = b.Name; }
        }
        log.Check($"FBX skeleton: all {bake.Rig.Bones.Count} bones, same hierarchy, joints at the baked T-pose (worst {Mm(worstJoint)} at {worstJointName})",
                  hierarchyErrors == 0 && worstJoint < 0.0005f, $"{hierarchyErrors} hierarchy errors, worst joint {Mm(worstJoint)}");

        // Real T-pose (rest), measured on the imported transforms.
        Vector3 Dir(string a, string c) => (bones[c].position - bones[a].position).normalized;
        float armL = Vector3.Angle(Dir("upperarm_l", "hand_l"), Vector3.left), armR = Vector3.Angle(Dir("upperarm_r", "hand_r"), Vector3.right);
        float legL = Vector3.Angle(Dir("thigh_l", "foot_l"), Vector3.down), legR = Vector3.Angle(Dir("thigh_r", "foot_r"), Vector3.down);
        float elbowL = Vector3.Angle(Dir("upperarm_l", "lowerarm_l"), Dir("lowerarm_l", "hand_l"));
        float kneeL = Vector3.Angle(Dir("thigh_l", "calf_l"), Dir("calf_l", "foot_l"));
        float footYaw = Vector3.Angle(Vector3.ProjectOnPlane(Dir("foot_l", "ball_l"), Vector3.up), Vector3.forward);
        log.Info($"Rest T-pose: arm L {armL:0.0} / R {armR:0.0} deg off horizontal, legs L {legL:0.0} / R {legR:0.0} deg off vertical, elbow {elbowL:0.0}, knee {kneeL:0.0}, foot yaw {footYaw:0.0} deg");
        log.Check("Rest pose IS a T-pose: arms horizontal, elbows/knees straight, legs vertical (all within 1 deg)",
                  armL < 1f && armR < 1f && legL < 1f && legR < 1f && elbowL < 1f && kneeL < 1f, "not a clean T-pose");
        log.Check($"Character faces +Z, feet forward (foot yaw {footYaw:0.0} deg)", footYaw < 10f && bones["ball_l"].position.z > bones["foot_l"].position.z, "facing wrong way");

        var body = MakeHumanFbxPipeline.LoadMesh("Body");
        var smr = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
        foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == "Body") smr = r;

        // Skin weights: imported == the bake's top-4 rule, bone by bone.
        var weights = body.boneWeights;
        var smrBones = smr.bones;
        float worstWeight = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            var expected = bake.Rig.RawTopInfluences(bake.Source.Parts[0].MeshToRawIndex[s_bodyOrder[i]]);
            var w = weights[i];
            var got = new Dictionary<string, float>();
            void Add(int bi, float bw) { if (bw > 0f) got[smrBones[bi].name] = (got.TryGetValue(smrBones[bi].name, out var x) ? x : 0f) + bw; }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            foreach (var (bone, weight) in expected)
            {
                got.TryGetValue(bake.Rig.Bones[bone].Name, out float g);
                worstWeight = Mathf.Max(worstWeight, Mathf.Abs(g - weight));
            }
        }
        log.Check($"Skin weights survive the FBX (worst per-bone weight error {worstWeight:0.0000})", worstWeight < 0.01f, $"weights differ by {worstWeight:0.000}");

        // Normals: Unity-calculated vs the bake's seam-free normals.
        var normals = body.normals;
        var bakeNormals = Fbx(bake.Male.Parts[0].Normals);
        float worstNormal = 0f;
        int over5 = 0, over45 = 0;
        var angles = new List<float>();
        string worstWhere = "";
        for (int i = 0; i < normals.Length; i++)
        {
            float angle = Vector3.Angle(normals[i], bakeNormals[i]);
            angles.Add(angle);
            if (angle > 5f) over5++;
            if (angle > 45f) over45++;
            if (angle > worstNormal) { worstNormal = angle; worstWhere = $"vertex {i} at {body.vertices[i]}"; }
        }
        angles.Sort();
        log.Info($"Normals vs bake: median {angles[angles.Count / 2]:0.00} deg, 99% {angles[(int)(angles.Count * 0.99f)]:0.00} deg, {over5} > 5 deg, {over45} > 45 deg, worst {worstNormal:0.0} ({worstWhere})");
        log.Check($"Unity-calculated normals agree with the bake's (median {angles[angles.Count / 2]:0.00} deg; outliers are the folded eyelids)",
                  angles[angles.Count / 2] < 3f, $"median {angles[angles.Count / 2]:0.0} deg");

        // No seams: every vertex duplicated along a UV seam must carry the SAME normal.
        var byPosition = new Dictionary<Vector3, Vector3>();
        float worstSeam = 0f; int seamVertices = 0;
        var positions = body.vertices;
        for (int i = 0; i < positions.Length; i++)
        {
            if (byPosition.TryGetValue(positions[i], out var n)) { seamVertices++; worstSeam = Mathf.Max(worstSeam, Vector3.Angle(n, normals[i])); }
            else byPosition[positions[i]] = normals[i];
        }
        log.Check($"No normal seams: {seamVertices} UV-seam duplicates share their normal (worst {worstSeam:0.000} deg)", worstSeam < 0.5f, $"seam normals differ by {worstSeam:0.0} deg");

        var names = new List<string>();
        for (int i = 0; i < body.blendShapeCount; i++) names.Add(body.GetBlendShapeName(i));
        log.Check($"Body blendshapes through the FBX: {string.Join(", ", names)}", names.Count == 7 && names.Contains("Gender") && names.Contains("FemaleMuscle"), "blendshapes missing");
    }

    private static BodyMorphValues Body(float gender, float weight01, float muscle) =>
        new BodyMorphValues { Gender = gender, Weight = weight01, Muscle = muscle };

    private static AvatarInstance Assemble(GameObject prefab, AvatarRecipeSO recipeSO, Vector3 position)
    {
        var recipe = recipeSO.ToRuntime();
        var go = Object.Instantiate(prefab, position, Quaternion.identity);
        go.name = $"Test_{recipeSO.name}";
        var instance = new AvatarInstance { Recipe = recipe, FinalIdentity = recipe.Identity, BodyMorphValues = recipe.Identity.Body };
        AvatarFactory.AssembleBody(instance, go, recipe);
        return instance;
    }

    /// <summary>The closed-form expectation, built only from the eight independently baked states:
    /// lerp(MaleBase + maleMorphs, FemaleBase + femaleMorphs, gender).</summary>
    private static Vector3[] Expected(MakeHumanBakeResult bake, float gender, float weight01, float muscle)
    {
        float slim = weight01 < 0.5f ? (0.5f - weight01) / 0.5f : 0f;
        float heavy = weight01 > 0.5f ? (weight01 - 0.5f) / 0.5f : 0f;

        Vector3[] Family(BodyBaseType type, MorphChannel s, MorphChannel h, MorphChannel m)
        {
            var b = bake.GetBase(type).Parts[0].Vertices;
            var vs = bake.Morphs[s].Parts[0].Vertices;
            var vh = bake.Morphs[h].Parts[0].Vertices;
            var vm = bake.Morphs[m].Parts[0].Vertices;
            var result = new Vector3[b.Length];
            for (int i = 0; i < b.Length; i++)
                result[i] = b[i] + slim * (vs[i] - b[i]) + heavy * (vh[i] - b[i]) + muscle * (vm[i] - b[i]);
            return result;
        }

        var male = Family(BodyBaseType.Male, MorphChannel.MaleSlim, MorphChannel.MaleHeavy, MorphChannel.MaleMuscle);
        var female = Family(BodyBaseType.Female, MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle);
        var expected = new Vector3[male.Length];
        for (int i = 0; i < male.Length; i++) expected[i] = Vector3.LerpUnclamped(male[i], female[i], gender);
        return Fbx(expected);
    }

    private static void ExpectBody(TestLog log, string name, AvatarInstance instance, BodyMorphValues values, MakeHumanBakeResult bake)
    {
        instance.ApplyBody(values);
        float error = MaxError(Expected(bake, values.Gender, values.Weight, values.Muscle), AvatarBodyChecks.BakeBody(instance));
        log.Check($"{name} (max error {Mm(error)})", error <= Tolerance, $"max vertex error {Mm(error)}");
    }

    private static bool AllStatesScaleWithin(MakeHumanBakeResult bake, float fraction)
    {
        foreach (var state in bake.AllStates())
            if (Mathf.Abs(state.NormalizeScale - 1f) > fraction) return false;
        return true;
    }

    private static (Transform bone, Vector3 position, Quaternion rotation)[] SnapshotSkeleton(AvatarInstance a)
    {
        var bones = a.SkeletonRoot.GetComponentsInChildren<Transform>(true);
        var snapshot = new (Transform, Vector3, Quaternion)[bones.Length];
        for (int i = 0; i < bones.Length; i++) snapshot[i] = (bones[i], bones[i].localPosition, bones[i].localRotation);
        return snapshot;
    }

    private static bool SkeletonUnchanged((Transform bone, Vector3 position, Quaternion rotation)[] snapshot)
    {
        foreach (var (bone, position, rotation) in snapshot)
            if (bone.localPosition != position || bone.localRotation != rotation) return false;
        return true;
    }

    private static bool SameMatrices(Matrix4x4[] a, Matrix4x4[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static float MaxError(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return float.MaxValue;
        float max = 0f;
        for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, (a[i] - b[i]).magnitude);
        return max;
    }

    private static int NearestVertex(Vector3[] vertices, Vector3 point)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            float d = (vertices[i] - point).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    private static string Mm(float meters) => $"{meters * 1000f:0.000} mm";

    private class TestLog
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private int _passed, _failed;

        public void Info(string line) => _sb.AppendLine($"  INFO  {line}");

        public void Check(string name, bool ok, string failure)
        {
            if (ok) { _passed++; _sb.AppendLine($"  PASS  {name}"); }
            else Fail(name, failure);
        }

        public void Fail(string name, string failure)
        {
            _failed++;
            _sb.AppendLine($"  FAIL  {name} — {failure}");
        }

        public string Finish(out bool passed)
        {
            passed = _failed == 0;
            string report = $"[MakeHumanBodyTests] {(passed ? "ALL PASSED" : "FAILURES")} — {_passed} passed, {_failed} failed\n{_sb}";
            if (passed) Debug.Log(report); else Debug.LogError(report);
            return report;
        }
    }
}
