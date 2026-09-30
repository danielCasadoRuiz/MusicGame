using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Structural tests for the combat animation library (CombatAnimationImporter / CombatAnimationLibrarySO /
/// AvatarClipPlayer). They do NOT judge how good a clip looks — that is done by eye on the preview
/// renders (Render Combat Library Previews) and in the AvatarDebug browser.
///
///   Edit  (Tools > MusicGame > Animations > Run Combat Animation Library Tests): the source/processed
///         folders; the whole pipeline run on the synthetic Mixamo fixture; every library entry (processed
///         + in-place Humanoid .anim, own valid source Avatar, root motion kept, finger curves preserved,
///         grounded/facing, finger capture reaching our hands); role/demo references; and that
///         MakeHuman_Canonical.fbx / its Humanoid Avatar are unchanged.
///   Play  (batch: -executeMethod MakeHumanBodyTests.RunCombatTestsFromCommandLine): a library clip on
///         the live debug avatars — no Rebind/restart/rebuild, morphs keep working mid-clip, instances
///         independent, speed / loop / stop / controller hand-over, facing pair.
/// </summary>
public static partial class MakeHumanBodyTests
{
    private const string CombatPendingKey = "MakeHumanBodyTests.CombatPlayPending";
    private const string CombatResultFile = "Logs/CombatAnimationTests.txt";

    [MenuItem("Tools/MusicGame/Animations/Run Combat Animation Library Tests")]
    public static void RunCombatEditTestsMenu() => RunCombatEditTests(out _);

    public static string RunCombatEditTests(out bool passed)
    {
        var log = new TestLog();
        try
        {
            RunCombatEditChecks(log);
        }
        catch (System.Exception e)
        {
            log.Fail("Exception", e.ToString());
        }
        return log.Finish(out passed);
    }

    private static void RunCombatEditChecks(TestLog log)
    {
        foreach (var pack in CombatAnimationImporter.Packs)
            log.Check($"Source folder {pack.SourceFolder} exists", AssetDatabase.IsValidFolder(pack.SourceFolder), "missing — Tools > MusicGame > Animations > Create Rokoko Source Folders");
        log.Check("Processed/Combat and Debug folders exist",
                  AssetDatabase.IsValidFolder(CombatAnimationImporter.ProcessedRoot) && AssetDatabase.IsValidFolder(CombatAnimationImporter.DebugRoot), "missing");

        // Target avatar baseline (must be identical after every import below).
        string hashBefore = CombatAnimationImporter.CanonicalFbxHash();
        string descriptionBefore = CanonicalDescription();

        var report = new List<string>();
        var fixture = CombatAnimationImporter.ImportFixture(report, out bool fixtureOk);
        foreach (var line in report) log.Info("fixture import: " + line);
        log.Check("Pipeline runs end to end on the synthetic Mixamo fixture", fixtureOk && fixture.entries.Count > 0, "fixture import failed");
        CheckLibrary(log, "fixture", fixture, expectFingerCapture: true);

        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        log.Check("CombatAnimationLibrary.asset exists", library != null, "run Tools > MusicGame > Animations > Import Rokoko Combat Packs");
        if (library != null)
        {
            if (library.entries.Count == 0) log.Info("Rokoko library is empty — the packs are still to be downloaded (see Assets/_Project/Animations/README.md).");
            CheckLibrary(log, "rokoko", library, expectFingerCapture: false);
            if (!string.IsNullOrEmpty(library.canonicalFbxHash))
                log.Check("Library was imported against the current MakeHuman_Canonical.fbx", library.canonicalFbxHash == hashBefore, "canonical FBX changed since the import");
        }

        log.Check("MakeHuman_Canonical.fbx byte-identical after the imports", CombatAnimationImporter.CanonicalFbxHash() == hashBefore, "FBX changed");
        log.Check("MakeHuman canonical Humanoid description (bone map + T-pose) unchanged", CanonicalDescription() == descriptionBefore, "description changed");
        var importer = AssetImporter.GetAtPath(MakeHumanFbxPipeline.FbxPath) as ModelImporter;
        var avatar = MakeHumanFbxPipeline.LoadAvatar();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        bool mapOk = importer != null && MakeHumanHumanoid.BoneMap.All(b => importer.humanDescription.human.Any(h => h.humanName == b.human && h.boneName == b.bone)) &&
                     importer.humanDescription.human.Length == MakeHumanHumanoid.BoneMap.Length;
        log.Check("MakeHuman_CanonicalAvatar still Humanoid / Create From This Model with the explicit 52-bone map", importer != null &&
                  importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel && mapOk, "importer changed");
        log.Check("Prefab still uses MakeHuman_CanonicalAvatar (valid, human)", avatar != null && avatar.isValid && avatar.isHuman &&
                  prefab != null && prefab.GetComponent<Animator>().avatar == avatar, "avatar changed");
    }

    private static string CanonicalDescription()
    {
        var importer = AssetImporter.GetAtPath(MakeHumanFbxPipeline.FbxPath) as ModelImporter;
        if (importer == null) return "";
        var d = importer.humanDescription;
        var sb = new System.Text.StringBuilder();
        foreach (var h in d.human) sb.Append(h.humanName).Append('=').Append(h.boneName).Append(';');
        foreach (var s in d.skeleton) sb.Append(s.name).Append(s.position.ToString("F5")).Append(s.rotation.ToString("F5")).Append(';');
        return sb.ToString();
    }

    private static void CheckLibrary(TestLog log, string tag, CombatAnimationLibrarySO library, bool expectFingerCapture)
    {
        var entries = library.entries.Where(e => e != null).ToList();
        int n = entries.Count;
        log.Check($"[{tag}] entry names unique ({n} entries)", entries.Select(e => e.name).Distinct().Count() == n, "duplicate names");

        var canonicalAvatar = MakeHumanFbxPipeline.LoadAvatar();
        var badClips = new List<string>();
        var badSource = new List<string>();
        var badRoot = new List<string>();
        var badFingers = new List<string>();
        var badSettings = new List<string>();
        var badReview = new List<string>();
        foreach (var e in entries)
        {
            bool clipsOk = e.clip != null && e.inPlaceClip != null && e.clip.isHumanMotion && e.inPlaceClip.isHumanMotion &&
                           AssetDatabase.GetAssetPath(e.clip).EndsWith(".anim") && AssetDatabase.GetAssetPath(e.inPlaceClip).EndsWith(".anim") &&
                           Mathf.Abs(e.clip.length - e.inPlaceClip.length) < 1e-3f && e.clip.length > 0f;
            if (!clipsOk) badClips.Add(e.name);

            var importer = AssetImporter.GetAtPath(e.sourceFbx) as ModelImporter;
            var sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(e.sourceFbx);
            var source = AssetDatabase.LoadAllAssetsAtPath(e.sourceFbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == e.name);
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                sourceAvatar == null || !sourceAvatar.isValid || !sourceAvatar.isHuman || sourceAvatar == canonicalAvatar || source == null)
            {
                badSource.Add(e.name);
                continue;
            }
            if (!clipsOk) continue;

            // Root motion preserved: identical root curves, nothing baked into the pose.
            var rootSettings = AnimationUtility.GetAnimationClipSettings(e.clip);
            if (!SameCurves(source, e.clip, p => p.StartsWith("Root")) || rootSettings.loopBlendOrientation || rootSettings.loopBlendPositionY || rootSettings.loopBlendPositionXZ)
                badRoot.Add(e.name);

            // In-place settings: yaw + height baked, XZ travel left to (unapplied) root motion.
            var inPlace = AnimationUtility.GetAnimationClipSettings(e.inPlaceClip);
            if (inPlace.loopBlendOrientation == e.keepFacing || !inPlace.loopBlendPositionY || inPlace.loopBlendPositionXZ || inPlace.loopTime != e.loop)
                badSettings.Add(e.name);

            // Fingers: preserved means the exact source curves (no blanket rest-hand fix).
            if (e.fingerMode == CombatFingerMode.Preserve && !SameCurves(source, e.inPlaceClip, MakeHumanAnimationTestSetup.IsFingerCurve)) badFingers.Add(e.name);
            if (expectFingerCapture && !e.fingerCapture) badFingers.Add(e.name + " (capture not detected)");

            if (e.review == CombatReviewStatus.Rejected && string.IsNullOrWhiteSpace(e.notes)) badReview.Add(e.name);
        }
        log.Check($"[{tag}] every entry has processed + in-place Humanoid .anim clips", badClips.Count == 0, string.Join(", ", badClips));
        log.Check($"[{tag}] every source FBX: Humanoid, own valid source Avatar (not MakeHuman_CanonicalAvatar)", badSource.Count == 0, string.Join(", ", badSource));
        log.Check($"[{tag}] processed clips keep the source root motion (identical Root curves, nothing baked)", badRoot.Count == 0, string.Join(", ", badRoot));
        log.Check($"[{tag}] in-place clips: height baked, yaw baked unless keepFacing, XZ travel not, loop flag as the entry", badSettings.Count == 0, string.Join(", ", badSettings));
        log.Check($"[{tag}] finger curves preserved from the source (no blanket relaxed-hand fix)", badFingers.Count == 0, string.Join(", ", badFingers));
        log.Check($"[{tag}] rejected entries say why (notes)", badReview.Count == 0, string.Join(", ", badReview));

        foreach (var role in library.roles)
        {
            var entry = library.Find(role.entryName);
            log.Check($"[{tag}] role {role.role} -> '{role.entryName}' exists and is not rejected", entry != null && entry.review != CombatReviewStatus.Rejected, "dangling/rejected");
        }
        bool demoOk = library.demoSequence.All(s => library.Find(s.entryName) != null && (string.IsNullOrEmpty(s.partnerEntryName) || library.Find(s.partnerEntryName) != null));
        log.Check($"[{tag}] demo sequence ({library.demoSequence.Count} steps) references existing entries", demoOk, "dangling demo step");

        CheckOnAvatar(log, tag, entries.Where(e => e.inPlaceClip != null).ToList());
    }

    /// <summary>Curves selected by `select` (Animator bindings) identical key-for-key in both clips.</summary>
    private static bool SameCurves(AnimationClip a, AnimationClip b, System.Func<string, bool> select)
    {
        var bindingsB = AnimationUtility.GetCurveBindings(b).Where(x => x.type == typeof(Animator) && select(x.propertyName)).ToDictionary(x => x.propertyName);
        int count = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(a))
        {
            if (binding.type != typeof(Animator) || !select(binding.propertyName)) continue;
            count++;
            if (!bindingsB.TryGetValue(binding.propertyName, out var other)) return false;
            var ka = AnimationUtility.GetEditorCurve(a, binding).keys;
            var kb = AnimationUtility.GetEditorCurve(b, other).keys;
            if (ka.Length != kb.Length) return false;
            for (int i = 0; i < ka.Length; i++)
                if (Mathf.Abs(ka[i].time - kb[i].time) > 1e-5f || Mathf.Abs(ka[i].value - kb[i].value) > 1e-5f) return false;
        }
        return count == bindingsB.Count;
    }

    /// <summary>Evaluates each in-place clip on the MakeHuman avatar exactly like AvatarClipPlayer does
    /// (PlayableGraph, Foot IK): grounded, facing +Z at frame 0, and captured fingers really move our fingers.</summary>
    private static void CheckOnAvatar(TestLog log, string tag, List<CombatAnimationEntry> entries)
    {
        if (entries.Count == 0) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        var male = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MakeHumanBodyBuilder.MaleRecipePath);
        var instance = Assemble(prefab, male, Vector3.zero);
        try
        {
            Transform Bone(string name) => instance.SkeletonMapper.FindBone(name) ??
                throw new System.Exception($"bone {name} missing under {instance.SkeletonRoot?.name}: " +
                    string.Join(",", instance.Root.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(x => x.Contains("ind") || x.Contains("thigh"))));
            var badGround = new List<string>();
            var badFacing = new List<string>();
            var badFingers = new List<string>();
            float worstFinger = 0f;
            foreach (var e in entries)
            {
                Quaternion fingerStart = Quaternion.identity;
                float fingerMove = 0f;
                // Robust ground (5th percentile of the per-frame lowest point), exactly what the importer calibrates.
                float lowest = CombatAnimationImporter.GroundPercentile(
                    CombatAnimationImporter.LowestPerFrame(e.inPlaceClip, instance.Root.gameObject, CombatAnimationImporter.GroundSamples(e.inPlaceClip)));
                for (int i = 0; i < 30; i++)
                {
                    EvaluateLikeRuntime(instance, e.inPlaceClip, e.inPlaceClip.length * i / 30f);
                    var finger = Bone("index_02_r").localRotation;
                    if (i == 0)
                    {
                        fingerStart = finger;
                        var across = instance.Root.InverseTransformDirection(Bone("thigh_r").position - Bone("thigh_l").position);
                        var forward = Vector3.Cross(across, Vector3.up);
                        float facing = Vector3.SignedAngle(Vector3.forward, new Vector3(forward.x, 0f, forward.z), Vector3.up);
                        if (Mathf.Abs(facing) > 5f) badFacing.Add($"{e.name} ({facing:+0;-0} deg, offset {AnimationUtility.GetAnimationClipSettings(e.inPlaceClip).orientationOffsetY:+0;-0})");
                    }
                    fingerMove = Mathf.Max(fingerMove, Quaternion.Angle(fingerStart, finger));
                }
                if (Mathf.Abs(lowest) > 0.02f) badGround.Add($"{e.name} ({lowest * 100f:0.0} cm)");
                // Full takes only: a short segment may legitimately hold a clenched fist the whole time.
                if (e.fingerCapture && e.fingerMode == CombatFingerMode.Preserve && string.IsNullOrEmpty(e.segmentOf))
                {
                    worstFinger = Mathf.Max(worstFinger, fingerMove);
                    if (fingerMove < 10f) badFingers.Add($"{e.name} ({fingerMove:0.0} deg)");
                }
            }
            log.Check($"[{tag}] in-place clips grounded on the MakeHuman avatar (robust lowest point within 2 cm of y = 0)", badGround.Count == 0, string.Join(", ", badGround));
            log.Check($"[{tag}] in-place clips face +Z at frame 0 (within 5 deg)", badFacing.Count == 0, string.Join(", ", badFacing));
            log.Check($"[{tag}] captured finger curves move our fingers (full takes: index_02_r >= 10 deg over the clip, max {worstFinger:0} deg)", badFingers.Count == 0, string.Join(", ", badFingers));
        }
        finally
        {
            instance.Dispose();
        }
    }

    // ── Play mode ────────────────────────────────────────────────────────────────

    /// <summary>Unity.exe -batchmode -projectPath ... -executeMethod MakeHumanBodyTests.RunCombatTestsFromCommandLine
    /// (no -quit: exits itself once the play-mode checks finish).</summary>
    public static void RunCombatTestsFromCommandLine()
    {
        string editReport = RunCombatEditTests(out bool editPassed);
        File.WriteAllText(CombatResultFile, editReport + "\n");
        if (!editPassed) { EditorApplication.Exit(1); return; }

        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(CombatPendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static async Task RunCombatPlayChecks()
    {
        var log = new TestLog();
        try
        {
            var preview = Object.FindFirstObjectByType<AvatarDebugPreview>();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (preview != null && (preview.IsBuilding || preview.Instances.Count < 2) && Time.realtimeSinceStartup < deadline) await Task.Yield();
            log.Check("C-P1 AvatarDebugPreview built 2 avatars", preview != null && preview.Instances.Count == 2, "preview did not build");
            log.Check("C-P1 AvatarDebugPreview has the combat library assigned", preview != null && preview.combatLibrary != null, "combatLibrary not assigned");
            if (preview == null || preview.Instances.Count < 2) throw new System.Exception("no avatars");

            var library = preview.combatLibrary != null && preview.combatLibrary.entries.Count > 0
                ? preview.combatLibrary
                : AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.FixtureLibraryPath);
            var entry = library != null ? library.entries.FirstOrDefault(e => e != null && e.inPlaceClip != null) : null;
            log.Check("C-P1 a library clip to play", entry != null, "no library entries (not even the fixture)");
            if (entry == null) throw new System.Exception("no clip");
            log.Info($"playing '{entry.name}' from {(library == preview.combatLibrary ? "the Rokoko library" : "the pipeline fixture")}");

            var a = preview.Instances[0];
            var b = preview.Instances[1];
            var rootA = a.Root;
            var animatorA = a.Animator;
            var avatarA = animatorA.avatar;
            var localPos = rootA.localPosition;
            var localRot = rootA.localRotation;
            var hand = a.SkeletonMapper.FindBone("hand_r");

            preview.PlayClip(entry.inPlaceClip, loop: true, speed: 1f);
            var player = preview.PlayerOf(a);
            log.Check("C-P2 PlayClip gives each avatar an AvatarClipPlayer", player != null && preview.PlayerOf(b) != null, "no player");

            int frames = 0;
            bool monotonic = true, sameObjects = true, rootStable = true, morphsTrack = true;
            float lastTotal = -1f, handTravel = 0f;
            var handStart = rootA.InverseTransformPoint(hand.position);
            int genderIndex = a.BodyRenderers[0].sharedMesh.GetBlendShapeIndex(MorphChannel.Gender.ToString());
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 2.5f || frames < 30)
            {
                float gender = Mathf.PingPong(frames * 0.05f, 1f);
                a.ApplyBody(new BodyMorphValues { Gender = gender, Weight = 0.5f, Muscle = 0.3f });
                await Task.Yield();
                frames++;
                var p = preview.PlayerOf(a);
                if (p == null || p != player) { sameObjects = false; break; }
                if (p.TotalTime < lastTotal) monotonic = false;
                lastTotal = p.TotalTime;
                if (a.Root != rootA || a.Animator != animatorA || animatorA.avatar != avatarA || animatorA.runtimeAnimatorController != null) sameObjects = false;
                if (rootA.localPosition != localPos || rootA.localRotation != localRot || rootA.localScale != Vector3.one || animatorA.applyRootMotion) rootStable = false;
                if (Mathf.Abs(a.BodyRenderers[0].GetBlendShapeWeight(genderIndex) - gender * 100f) > 0.01f) morphsTrack = false;
                handTravel = Mathf.Max(handTravel, (rootA.InverseTransformPoint(hand.position) - handStart).magnitude);
            }
            log.Check($"C-P2 {frames} frames: same instance / Animator / Humanoid Avatar, no controller swapped in", sameObjects, "rebuilt or replaced");
            log.Check($"C-P2 playback time never restarted (monotonic, reached {lastTotal:0.00} s)", monotonic && lastTotal > 1f, "time went backwards — Rebind/restart");
            log.Check("C-P2 avatar root never moved/rotated/scaled, root motion off", rootStable, "root transform changed");
            log.Check("C-P2 Gender morph applied every frame while the clip plays", morphsTrack, "blendshape weight lagged/reset");
            log.Check($"C-P2 the clip actually animates the skeleton (hand travel {handTravel * 100f:0} cm)", handTravel > 0.03f, "no motion");

            // Independence: freeze both at the same time, morph A, B must stay bit-identical.
            var pa = preview.PlayerOf(a);
            var pb = preview.PlayerOf(b);
            pa.Paused = pb.Paused = true;
            pa.Seek(entry.inPlaceClip.length * 0.5f);
            pb.Seek(entry.inPlaceClip.length * 0.5f);
            await Task.Yield();
            var bBefore = AvatarBodyChecks.BakeBody(b);
            var aBefore = AvatarBodyChecks.BakeBody(a);
            a.ApplyBody(new BodyMorphValues { Gender = 0.8f, Weight = 1f, Muscle = 1f });
            await Task.Yield();
            await Task.Yield();
            int bChanged = AvatarBodyChecks.CountDifferent(bBefore, AvatarBodyChecks.BakeBody(b));
            int aChanged = AvatarBodyChecks.CountDifferent(aBefore, AvatarBodyChecks.BakeBody(a));
            log.Check($"C-P3 morphing avatar A mid-clip leaves avatar B bit-identical ({bChanged} of B's vertices changed; A: {aChanged})", bChanged == 0 && aChanged > 0, "instances not independent");
            log.Check("C-P3 paused players hold their time", Mathf.Abs(pa.Time - entry.inPlaceClip.length * 0.5f) < 1e-4f, "paused player advanced");

            // Speed: 0.5x advances half as fast as game time.
            pa.Paused = pb.Paused = false;
            preview.PlayClip(entry.inPlaceClip, loop: true, speed: 0.5f);
            pa = preview.PlayerOf(a);
            float t0 = pa.TotalTime, game = 0f;
            for (int i = 0; i < 20; i++) { await Task.Yield(); game += Time.deltaTime; }
            float ratio = game > 0f ? (pa.TotalTime - t0) / game : 0f;
            log.Check($"C-P4 Slow 0.5x plays at half speed (ratio {ratio:0.000})", Mathf.Abs(ratio - 0.5f) < 0.05f, "wrong speed");

            // Loop off: holds the last frame.
            preview.PlayClip(entry.inPlaceClip, loop: false, speed: 1f);
            pa = preview.PlayerOf(a);
            pa.Seek(entry.inPlaceClip.length - 0.05f);
            float waitStart = Time.time;
            while (Time.time - waitStart < 0.3f) await Task.Yield();
            log.Check("C-P4 Loop off: clip finishes and holds its last frame", pa.IsFinished && Mathf.Approximately(pa.Time, entry.inPlaceClip.length), $"time {pa.Time:0.000}/{entry.inPlaceClip.length:0.000}");

            // Hand-over to the Idle/Walk/Run controller and back.
            if (preview.debugAnimatorController != null)
            {
                preview.SetDebugAnimation(DebugAnimation.Idle);
                await Task.Yield();
                log.Check("C-P5 SetDebugAnimation(Idle) stops the clip players and restores the controller",
                          preview.PlayerOf(a) == null && animatorA.runtimeAnimatorController == preview.debugAnimatorController && a.Root == rootA, "players left running");
                preview.PlayClip(entry.inPlaceClip, loop: true);
                await Task.Yield();
                log.Check("C-P5 ...and a library clip can take over again (same avatar)", preview.PlayerOf(a) != null && a.Root == rootA && animatorA.avatar == avatarA, "failed");
            }

            // Facing pair.
            preview.SetFacingMode(true);
            float facingDot = Vector3.Dot(a.Root.forward, b.Root.forward);
            float distance = Vector3.Distance(a.Root.position, b.Root.position);
            var towards = (b.Root.position - a.Root.position).normalized;
            log.Check($"C-P6 facing mode: avatars face each other ({facingDot:0.00}), {distance:0.00} m apart",
                      facingDot < -0.99f && Vector3.Dot(a.Root.forward, towards) > 0.99f && Mathf.Abs(distance - preview.facingDistance) < 1e-3f, "not facing");
            preview.SetFacingMode(false);
            log.Check("C-P6 line-up restored", Vector3.Dot(a.Root.forward, b.Root.forward) > 0.99f, "not restored");

            preview.StopClips(resetPose: true);
            log.Check("C-P7 StopClips removes every player", preview.PlayerOf(a) == null && preview.PlayerOf(b) == null, "players left");
        }
        catch (System.Exception e)
        {
            log.Fail("Exception", e.ToString());
        }

        string report = log.Finish(out bool passed);
        File.AppendAllText(CombatResultFile, report + "\n");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
    }

    // ── Preview renders (visual review) ──────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Animations/Render Combat Library Previews")]
    public static void RenderCombatPreviewsMenu() => RenderCombatPreviews(CombatAnimationImporter.LibraryPath);

    /// <summary>Batch: -executeMethod MakeHumanBodyTests.RenderCombatPreviewsFromCommandLine [-combatFixture] -quit</summary>
    public static void RenderCombatPreviewsFromCommandLine()
    {
        bool fixture = System.Environment.GetCommandLineArgs().Contains("-combatFixture");
        RenderCombatPreviews(fixture ? CombatAnimationImporter.FixtureLibraryPath : CombatAnimationImporter.LibraryPath);
    }

    /// <summary>Per entry, Logs/CombatPreview/&lt;name&gt;.png: a contact sheet of the in-place clip on the
    /// Male MakeHuman avatar, 8 columns x N rows, one phase every ~1.5 s (8..32 phases, labelled by time
    /// in the log), front 3/4 view on a floor; &lt;name&gt;_hands_*.png right-hand close-ups at 4 phases.
    /// `-combatOnly Name1,Name2` restricts the entries; `-combatPhases N` forces the phase count.</summary>
    public static void RenderCombatPreviews(string libraryPath)
    {
        var args = System.Environment.GetCommandLineArgs();
        string Arg(string key) { int i = System.Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var only = Arg("-combatOnly")?.Split(',');
        int forcedPhases = int.TryParse(Arg("-combatPhases"), out int fp) ? fp : 0;
        // -combatWindow 3.5:5.0 renders only that time range (with -combatPhases steps, default 16).
        float windowStart = -1f, windowEnd = -1f;
        var window = Arg("-combatWindow")?.Split(':');
        if (window != null && window.Length == 2)
        {
            windowStart = float.Parse(window[0], System.Globalization.CultureInfo.InvariantCulture);
            windowEnd = float.Parse(window[1], System.Globalization.CultureInfo.InvariantCulture);
            if (forcedPhases == 0) forcedPhases = 16;
        }
        string suffix = Arg("-combatSuffix") ?? (window != null ? $"_{windowStart:0.0}-{windowEnd:0.0}".Replace(',', '.') : "");
        bool skipMotion = window != null;

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(libraryPath);
        if (library == null) { Debug.LogError($"[CombatPreview] no library at {libraryPath}"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        var male = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MakeHumanBodyBuilder.MaleRecipePath);
        Directory.CreateDirectory("Logs/CombatPreview");

        var lightGO = new GameObject("Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        lightGO.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = true;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;

        const int columns = 8;
        const float spacing = 2.0f;
        const float rowSpacing = 2.6f;
        const int maxPhases = 32;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var avatars = new List<AvatarInstance>();
        for (int i = 0; i < maxPhases; i++)
        {
            var instance = Assemble(prefab, male, new Vector3(-(i % columns) * spacing, (i / columns) * -rowSpacing, 0f));
            instance.ApplyBody(Body(0f, 0.5f, 0f));
            avatars.Add(instance);
        }
        Object.DestroyImmediate(floor);

        void Shot(string file, Vector3 lookAt, Vector3 from, float size, int width, int height)
        {
            cam.orthographicSize = size;
            cam.transform.position = lookAt + from;
            cam.transform.LookAt(lookAt);
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

        // Rows are stacked vertically (each avatar on its own little floor tile), seen from the front,
        // slightly from the right and above, so every phase is readable.
        var tiles = new List<GameObject>();
        for (int i = 0; i < maxPhases; i++)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.transform.localScale = new Vector3(1.8f, 0.02f, 1.8f);
            tile.transform.position = avatars[i].Root.position + Vector3.down * 0.01f;
            tiles.Add(tile);
        }

        var view = new Vector3(0.3f, 0.12f, 1f).normalized * 40f;
        var sb = new System.Text.StringBuilder();
        // -combatWindows "Entry@3.5:5.0;Entry2@1:2" renders several windows in one run.
        var jobs = new List<(CombatAnimationEntry e, float start, float end)>();
        var windowList = Arg("-combatWindows");
        if (windowList != null)
        {
            foreach (var item in windowList.Split(';'))
            {
                var parts = item.Split('@');
                var range = parts[1].Split(':');
                var target = library.Find(parts[0]);
                if (target != null)
                    jobs.Add((target, float.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture)));
                else Debug.LogError($"[CombatPreview] no entry {parts[0]}");
            }
            if (forcedPhases == 0) forcedPhases = 16;
        }
        else
        {
            foreach (var entry in library.entries)
                if (entry?.inPlaceClip != null && (only == null || only.Contains(entry.name))) jobs.Add((entry, windowStart, windowEnd));
        }

        foreach (var (e, jobStart, jobEnd) in jobs)
        {
            bool windowed = jobStart >= 0f;
            window = windowed ? new[] { "", "" } : null;
            windowStart = jobStart;
            windowEnd = jobEnd;
            suffix = windowed ? $"_{windowStart:0.0}-{windowEnd:0.0}".Replace(',', '.') : "";
            skipMotion = windowed;
            float length = e.inPlaceClip.length;
            int phases = forcedPhases > 0 ? Mathf.Min(forcedPhases, maxPhases) : Mathf.Clamp(Mathf.RoundToInt(length / 1.5f), 8, maxPhases);
            int rows = (phases + columns - 1) / columns;
            var times = new List<string>();
            for (int i = 0; i < maxPhases; i++)
            {
                bool used = i < phases;
                avatars[i].Root.gameObject.SetActive(used);
                tiles[i].SetActive(used);
                if (!used) continue;
                float t = window != null
                    ? Mathf.Clamp(Mathf.Lerp(windowStart, windowEnd, i / (float)Mathf.Max(1, phases - 1)), 0f, length)
                    : length * i / Mathf.Max(1, phases - 1);
                times.Add($"{t:0.0}");
                EvaluateLikeRuntime(avatars[i], e.inPlaceClip, t);
                // Edit mode does not re-skin a SkinnedMeshRenderer for Camera.Render after an out-of-loop
                // evaluation: toggling the renderer forces it (otherwise a previous clip's pose shows).
                foreach (var smr in avatars[i].Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.enabled = false;
                    smr.enabled = true;
                }
            }
            sb.AppendLine($"{e.name}: {phases} phases, 8 per row, times (s) {string.Join(" ", times)}");

            var centre = new Vector3(-(columns - 1) * spacing * 0.5f, 0.9f - (rows - 1) * rowSpacing * 0.5f, 0f);
            float size = Mathf.Max(rows * rowSpacing * 0.5f + 0.3f, 2.4f);
            Shot($"Logs/CombatPreview/{e.name}{suffix}.png", centre, view, size, 3200, Mathf.RoundToInt(3200f * 2f * size / (columns * spacing + 0.8f)));

            if (!skipMotion) sb.Append(MotionPeaks(avatars[0], e.inPlaceClip));
            if (e.fingerCapture && window == null)
                for (int k = 0; k < 4; k++)
                {
                    int index = k * Mathf.Max(1, phases / 4);
                    var hand = avatars[index].SkeletonMapper.FindBone("hand_r");
                    // Look at the hand from outside the body (chest -> hand, plus a bit of front), so the
                    // forearm/torso never hide it.
                    var chest = avatars[index].SkeletonMapper.FindBone("spine_03").position;
                    var outward = hand.position - chest;
                    outward.y *= 0.3f;
                    var from = (outward.normalized + avatars[index].Root.forward * 0.7f + Vector3.up * 0.2f).normalized * 5f;
                    Shot($"Logs/CombatPreview/{e.name}_hands_{k}.png", hand.position, from, 0.14f, 700, 700);
                }
        }

        foreach (var tile in tiles) Object.DestroyImmediate(tile);
        foreach (var instance in avatars) instance.Dispose();
        File.WriteAllText(windowList != null ? "Logs/CombatPreview/_index_windows.txt" : $"Logs/CombatPreview/_index{suffix}.txt", sb.ToString());
        Debug.Log($"[CombatPreview] rendered to Logs/CombatPreview/ (see _index.txt)");
    }

    /// <summary>Where the action is in a long take, measured on the MakeHuman avatar (in place, 30 fps):
    /// peaks of hand / foot speed relative to the pelvis (strikes), and spans with the pelvis low (on the floor).</summary>
    private static string MotionPeaks(AvatarInstance avatar, AnimationClip clip)
    {
        var bones = new[] { "hand_l", "hand_r", "foot_l", "foot_r" }.Select(n => avatar.SkeletonMapper.FindBone(n)).ToArray();
        var pelvis = avatar.SkeletonMapper.FindBone("pelvis");
        int frames = Mathf.RoundToInt(clip.length * 30f);
        var previous = new Vector3[bones.Length];
        var speed = new float[bones.Length, frames + 1];
        var pelvisY = new float[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            EvaluateLikeRuntime(avatar, clip, f / 30f);
            pelvisY[f] = avatar.Root.InverseTransformPoint(pelvis.position).y;
            for (int b = 0; b < bones.Length; b++)
            {
                var local = avatar.Root.InverseTransformPoint(bones[b].position) - avatar.Root.InverseTransformPoint(pelvis.position);
                speed[b, f] = f == 0 ? 0f : (local - previous[b]).magnitude * 30f;
                previous[b] = local;
            }
        }
        var sb = new System.Text.StringBuilder();
        string[] labels = { "L hand", "R hand", "L foot", "R foot" };
        for (int b = 0; b < bones.Length; b++)
        {
            var peaks = new List<(float t, float v)>();
            for (int f = 2; f < frames - 1; f++)
            {
                float v = speed[b, f];
                if (v < 3.5f || v < speed[b, f - 1] || v < speed[b, f + 1]) continue;
                if (peaks.Count > 0 && f / 30f - peaks[peaks.Count - 1].t < 0.3f)
                {
                    if (v > peaks[peaks.Count - 1].v) peaks[peaks.Count - 1] = (f / 30f, v);
                    continue;
                }
                peaks.Add((f / 30f, v));
            }
            sb.AppendLine($"   {labels[b]} speed peaks > 3.5 m/s: {string.Join(" ", peaks.Select(p => $"{p.t:0.00}s({p.v:0.0})"))}");
        }
        var low = new List<string>();
        int startLow = -1;
        for (int f = 0; f <= frames + 1; f++)
        {
            bool isLow = f <= frames && pelvisY[f] < 0.45f;
            if (isLow && startLow < 0) startLow = f;
            if (!isLow && startLow >= 0) { low.Add($"{startLow / 30f:0.0}-{(f - 1) / 30f:0.0}s"); startLow = -1; }
        }
        sb.AppendLine($"   pelvis below 0.45 m (on the floor): {(low.Count > 0 ? string.Join(" ", low) : "never")}");
        return sb.ToString();
    }
}
