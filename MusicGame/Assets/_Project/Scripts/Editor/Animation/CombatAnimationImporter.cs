using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Tools > MusicGame > Animations > Import Rokoko Combat Packs — turns the official Rokoko free mocap
/// packs (FBX, 30 fps, "Mixamo skeleton") dropped into Animations/Source/Rokoko/&lt;Pack&gt;/ into a
/// browsable CombatAnimationLibrarySO. Debug/browsing only: nothing here touches production gameplay.
///
/// Per source FBX (the FBX file itself is never edited; only its import settings in the .meta):
///   1. Humanoid, avatar CREATED FROM THE FBX'S OWN SKELETON (its own source Avatar). Mixamo bone
///      names ("mixamorig:Hips", with or without the prefix) are mapped explicitly, fingers included;
///      anything else falls back to Unity's auto-mapping (reported). Arms/legs are straightened to an
///      exact T-pose in the source description only (and the spine when it is tilted more than 10 deg),
///      like the CMU pipeline. MakeHuman_CanonicalAvatar is never touched (its FBX hash is checked).
///   2. Root motion kept at import: no root option is baked into the pose, so the imported clip carries
///      the full source root motion. A leading/trailing pop (e.g. a T-pose calibration frame) is trimmed
///      through the clip's first/last frame import settings.
///   3. Two editable .anim copies in Animations/Processed/Combat/&lt;Pack&gt;/:
///        &lt;Name&gt;.anim             — every curve as imported, root motion intact (for a future
///                                  root-motion gameplay controller).
///        InPlace/&lt;Name&gt;_InPlace.anim — derived debug clip: root yaw and height baked into the pose,
///                                  XZ travel left to (discarded) root motion, facing +Z at frame 0 and the
///                                  lowest point of the body on y = 0 for the MakeHuman avatar.
///      Finger curves are PRESERVED (Rokoko Smartgloves capture) — the CMU "relaxed rest hand" fix is
///      only applied to an entry whose fingerMode is set to RelaxedRestHand by review.
///   4. Library entry: category guessed from the file name (flagged as auto), loop, root-motion
///      recommendation from the measured travel/turn, finger capture detection, import warnings.
///      Review fields (review, notes, and — once reviewed — category / loop / fingerMode) survive re-imports.
/// </summary>
public static class CombatAnimationImporter
{
    public const string AnimationsRoot = "Assets/_Project/Animations";
    public const string SourceRoot = AnimationsRoot + "/Source/Rokoko";
    public const string ProcessedRoot = AnimationsRoot + "/Processed/Combat";
    public const string DebugRoot = AnimationsRoot + "/Debug";
    public const string LibraryPath = ProcessedRoot + "/CombatAnimationLibrary.asset";

    public const string FixtureFolder = DebugRoot + "/PipelineFixture";
    public const string FixtureProcessed = FixtureFolder + "/Processed";
    public const string FixtureLibraryPath = FixtureFolder + "/PipelineFixtureLibrary.asset";

    public readonly struct Pack
    {
        public readonly string Folder, Title, Page;
        public Pack(string folder, string title, string page) { Folder = folder; Title = title; Page = page; }
        public string SourceFolder => SourceRoot + "/" + Folder;
        public string ProcessedFolder => ProcessedRoot + "/" + Folder;
    }

    public static readonly Pack[] Packs =
    {
        new Pack("Fight", "Rokoko Mocap - 13 Free Fight Animations",
                 "https://www.rokoko.com/resources/rokoko-mocap-13-free-fight-animations"),
        new Pack("MartialArts", "Rokoko Mocap - 6 Free Martial Arts Animations",
                 "https://www.rokoko.com/resources/rokoko-mocap-6-free-martial-arts-animations"),
        new Pack("EricJacobus", "Rokoko Motion Library - 10 free motion assets from Eric Jacobus (God of War)",
                 "https://www.rokoko.com/resources/motion-library-10-free-motion-assets-from-eric-jacobus-god-of-war"),
        new Pack("Superhero", "Rokoko Mocap - 15 Free Superhero Animations",
                 "https://www.rokoko.com/resources/rokoko-mocap-15-free-superhero-animations"),
    };

    private const float RootMotionTravel = 0.3f;  // m — more net travel than this: recommend root motion
    private const float RootMotionYaw = 45f;      // deg — more net turn than this: recommend root motion

    // ── Menu ─────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Animations/Import Rokoko Combat Packs")]
    public static void ImportMenu() => Debug.Log(ImportAll(out _));

    [MenuItem("Tools/MusicGame/Animations/Create Rokoko Source Folders")]
    public static void CreateFoldersMenu()
    {
        EnsureFolders();
        Debug.Log("[CombatAnimationImporter] Source folders ready:\n  " + string.Join("\n  ", Packs.Select(p => p.SourceFolder)));
    }

    /// <summary>Batch: -executeMethod CombatAnimationImporter.ImportFromCommandLine -quit</summary>
    public static void ImportFromCommandLine()
    {
        string report = ImportAll(out bool ok);
        File.WriteAllText("Logs/CombatAnimationImport.txt", report);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void EnsureFolders()
    {
        EnsureFolder(SourceRoot);
        foreach (var pack in Packs) EnsureFolder(pack.SourceFolder);
        EnsureFolder(ProcessedRoot);
        EnsureFolder(DebugRoot);
    }

    /// <summary>Imports every FBX under the four pack folders into the combat library.</summary>
    public static string ImportAll(out bool success)
    {
        EnsureFolders();
        var report = new List<string>();
        string canonicalBefore = CanonicalFbxHash();
        var library = LoadOrCreateLibrary(LibraryPath);

        var sources = new List<(string fbx, Pack pack)>();
        foreach (var pack in Packs)
        {
            var files = FindFbx(pack.SourceFolder, recursive: true);
            if (files.Count == 0) report.Add($"PENDING  {pack.Folder}: no FBX in {pack.SourceFolder} — download \"{pack.Title}\" from {pack.Page}");
            else report.Add($"FOUND    {pack.Folder}: {files.Count} FBX file(s)");
            foreach (var f in files) sources.Add((f, pack));
        }

        success = Import(library, sources.Select(s => (s.fbx, s.pack.Title, s.pack.ProcessedFolder, s.pack.Folder)), report);
        RebuildDemoSequence(library, report);

        string canonicalAfter = CanonicalFbxHash();
        library.canonicalFbxHash = canonicalAfter;
        if (canonicalBefore != canonicalAfter)
        {
            success = false;
            report.Add("ERROR    MakeHuman_Canonical.fbx changed during the import!");
        }
        library.lastImportReport = string.Join("\n", report);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        AssignLibraryToDebugScene(library, report);

        return "[CombatAnimationImporter] " + (success ? "Done" : "FINISHED WITH ERRORS") + $" — {library.entries.Count} entries:\n  " + string.Join("\n  ", report);
    }

    /// <summary>Runs the whole pipeline on the synthetic Mixamo-named fixture (Debug/PipelineFixture)
    /// into its own library — used by the tests before the real packs are downloaded.</summary>
    public static CombatAnimationLibrarySO ImportFixture(List<string> report, out bool success)
    {
        var library = LoadOrCreateLibrary(FixtureLibraryPath);
        var files = FindFbx(FixtureFolder, recursive: false);
        success = files.Count > 0 && Import(library, files.Select(f => (f, "PipelineFixture (synthetic, not Rokoko)", FixtureProcessed, "Fixture")), report);
        RebuildDemoSequence(library, report);
        library.canonicalFbxHash = CanonicalFbxHash();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return library;
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────────

    private static bool Import(CombatAnimationLibrarySO library, IEnumerable<(string fbx, string packTitle, string processedFolder, string prefix)> sources, List<string> report)
    {
        var previous = new Dictionary<string, CombatAnimationEntry>();
        foreach (var e in library.entries) if (e != null && !string.IsNullOrEmpty(e.name)) previous[e.name] = e;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MakeHumanBodyBuilder.PrefabPath);
        if (prefab == null) { report.Add("ERROR    MakeHuman prefab missing — bake the body first."); return false; }

        var entries = new List<CombatAnimationEntry>();
        bool ok = true;
        var avatarGO = Object.Instantiate(prefab);
        avatarGO.name = "CombatImport_MakeHuman";
        try
        {
            var animator = avatarGO.GetComponent<Animator>();
            var restMuscles = MakeHumanAnimationTestSetup.MeasureRestMuscles(avatarGO, animator.avatar);
            foreach (var (fbx, packTitle, processedFolder, prefix) in sources)
            {
                try
                {
                    ok &= ImportFbx(fbx, packTitle, processedFolder, prefix, previous, avatarGO, restMuscles, entries, report);
                }
                catch (System.Exception e)
                {
                    ok = false;
                    report.Add($"ERROR    {fbx}: {e.Message}");
                    Debug.LogException(e);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(avatarGO);
        }

        foreach (var name in previous.Keys)
            if (!entries.Any(e => e.name == name)) report.Add($"REMOVED  {name} (its source FBX/take is gone; processed .anim files left on disk)");
        library.entries = entries;
        return ok;
    }

    private static bool ImportFbx(string fbx, string packTitle, string processedFolder, string prefix,
                                  Dictionary<string, CombatAnimationEntry> previous, GameObject avatarGO,
                                  Dictionary<string, float> restMuscles, List<CombatAnimationEntry> entries, List<string> report)
    {
        var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (importer == null) { report.Add($"ERROR    {fbx}: not a model"); return false; }

        // 1. Humanoid with the FBX's own avatar. Round-trip through Generic so the importer refreshes the
        //    cached skeleton description (see MakeHumanAnimationTestSetup).
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.clipAnimations = System.Array.Empty<ModelImporterClipAnimation>();
        importer.SaveAndReimport();
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        var description = importer.humanDescription;
        string mapping = ApplyMixamoMapping(ref description);
        string tpose = EnforceSourceTPose(ref description, fbx);
        importer.humanDescription = description;

        var takes = importer.defaultClipAnimations;
        if (takes.Length == 0) { report.Add($"ERROR    {fbx}: no animation take"); return false; }
        string baseName = prefix + "_" + CleanName(Path.GetFileNameWithoutExtension(fbx));
        var clipSettings = new ModelImporterClipAnimation[takes.Length];
        var names = new string[takes.Length];
        for (int i = 0; i < takes.Length; i++)
        {
            names[i] = takes.Length == 1 ? baseName : baseName + "_" + CleanName(takes[i].takeName);
            var take = takes[i];
            take.name = names[i];
            take.loopTime = false;
            take.loopPose = false;
            take.lockRootRotation = false;       // root yaw    -> root motion
            take.lockRootHeightY = false;        // root height -> root motion
            take.lockRootPositionXZ = false;     // root XZ     -> root motion
            take.keepOriginalOrientation = true;
            take.keepOriginalPositionY = true;
            take.keepOriginalPositionXZ = true;
            take.heightFromFeet = false;
            take.maskType = ClipAnimationMaskType.None;
            clipSettings[i] = take;
        }
        importer.clipAnimations = clipSettings;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(fbx);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            report.Add($"ERROR    {fbx}: source Humanoid avatar invalid ({mapping})");
            return false;
        }

        // 2. Trim a leading/trailing pop (T-pose calibration frame) through the import settings.
        bool trimmed = false;
        var trims = new int[takes.Length];
        for (int i = 0; i < takes.Length; i++)
        {
            var clip = LoadClip(fbx, names[i]);
            if (clip == null) continue;
            var (lead, tail) = DetectEdgePops(clip);
            trims[i] = lead;
            if (lead > 0 || tail > 0)
            {
                clipSettings[i].firstFrame += lead;
                clipSettings[i].lastFrame -= tail;
                trimmed = true;
            }
        }
        if (trimmed)
        {
            importer.clipAnimations = clipSettings;
            importer.SaveAndReimport();
        }

        report.Add($"IMPORTED {fbx}: {takes.Length} take(s); {mapping}; {tpose}");

        // 3-4. Processed clips + library entries.
        EnsureFolder(processedFolder);
        EnsureFolder(processedFolder + "/InPlace");
        for (int i = 0; i < takes.Length; i++)
        {
            var source = LoadClip(fbx, names[i]);
            if (source == null || !source.isHumanMotion) { report.Add($"ERROR    {names[i]}: imported clip missing or not Humanoid"); continue; }

            previous.TryGetValue(names[i], out var old);
            var entry = new CombatAnimationEntry
            {
                name = names[i],
                sourcePack = packTitle,
                sourceFbx = fbx,
                sourceTake = takes[i].takeName,
                duration = source.length,
                frameRate = source.frameRate,
                trimmedStartFrames = trims[i],
                category = Categorize(Path.GetFileNameWithoutExtension(fbx) + " " + takes[i].takeName),
            };
            entry.loop = DefaultLoop(entry.category, entry.name);
            entry.fingerCapture = HasFingerCapture(source);
            entry.fingerMode = CombatFingerMode.Preserve;
            if (old != null)
            {
                entry.review = old.review;
                entry.notes = old.notes;
                if (old.categoryReviewed || old.review != CombatReviewStatus.Unreviewed)
                {
                    entry.category = old.category;
                    entry.categoryReviewed = old.categoryReviewed;
                }
                if (old.review != CombatReviewStatus.Unreviewed)
                {
                    entry.loop = old.loop;
                    entry.fingerMode = old.fingerMode;
                }
            }

            entry.clip = WriteCopy(source, $"{processedFolder}/{names[i]}.anim", null, null);
            entry.inPlaceClip = WriteCopy(source, $"{processedFolder}/InPlace/{names[i]}_InPlace.anim",
                                          entry.fingerMode == CombatFingerMode.RelaxedRestHand ? restMuscles : null,
                                          settings =>
                                          {
                                              settings.loopTime = entry.loop;
                                              settings.loopBlend = entry.loop;
                                              settings.loopBlendOrientation = true;  // bake root yaw into the pose
                                              settings.keepOriginalOrientation = true;
                                              settings.loopBlendPositionY = true;    // bake root height into the pose
                                              settings.keepOriginalPositionY = true;
                                              settings.loopBlendPositionXZ = false;  // XZ travel -> root motion (not applied)
                                              settings.heightFromFeet = false;
                                              settings.level = 0f;
                                              settings.orientationOffsetY = 0f;
                                          });

            // Root-motion clip keeps the importer's loop flag off; the library entry says how to play it.
            float humanScale = avatarGO.GetComponent<Animator>().humanScale;
            MeasureRoot(entry, source, humanScale > 0f ? humanScale : 1f);
            float yaw = CalibrateStartFacing(entry.inPlaceClip, avatarGO);
            float level = MakeHumanAnimationTestSetup.CalibrateGround(entry.inPlaceClip, avatarGO);
            entry.importWarnings = Warnings(source, entry);
            EditorUtility.SetDirty(entry.clip);
            EditorUtility.SetDirty(entry.inPlaceClip);

            entries.Add(entry);
            report.Add($"   {entry.name}: {entry.category}{(entry.categoryReviewed ? "" : " (auto)")}, {entry.duration:0.00} s, loop {entry.loop}, " +
                       $"{entry.rootMotion} (travel {new Vector2(entry.rootTravel.x, entry.rootTravel.z).magnitude:0.00} m, yaw {entry.rootYaw:+0;-0}), " +
                       $"fingers {(entry.fingerCapture ? "captured" : "static")}/{entry.fingerMode}, trimmed {entry.trimmedStartFrames}, " +
                       $"facing {yaw:+0.0;-0.0} deg, level {level:+0.000;-0.000}{(string.IsNullOrEmpty(entry.importWarnings) ? "" : "; WARN " + entry.importWarnings)}");
        }
        return true;
    }

    // ── Mapping / T-pose ─────────────────────────────────────────────────────────

    /// <summary>Mixamo bone name (without "mixamorig:" prefix) -> Unity human bone.</summary>
    private static readonly Dictionary<string, string> MixamoToHuman = BuildMixamoMap();

    private static Dictionary<string, string> BuildMixamoMap()
    {
        var map = new Dictionary<string, string>
        {
            ["Hips"] = "Hips", ["Spine"] = "Spine", ["Spine1"] = "Chest", ["Spine2"] = "UpperChest",
            ["Neck"] = "Neck", ["Head"] = "Head",
        };
        foreach (var side in new[] { "Left", "Right" })
        {
            map[side + "Shoulder"] = side + "Shoulder";
            map[side + "Arm"] = side + "UpperArm";
            map[side + "ForeArm"] = side + "LowerArm";
            map[side + "Hand"] = side + "Hand";
            map[side + "UpLeg"] = side + "UpperLeg";
            map[side + "Leg"] = side + "LowerLeg";
            map[side + "Foot"] = side + "Foot";
            map[side + "ToeBase"] = side + "Toes";
            foreach (var (mixamo, human) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Pinky", "Little") })
            {
                map[$"{side}Hand{mixamo}1"] = $"{side} {human} Proximal";
                map[$"{side}Hand{mixamo}2"] = $"{side} {human} Intermediate";
                map[$"{side}Hand{mixamo}3"] = $"{side} {human} Distal";
            }
        }
        return map;
    }

    private static readonly string[] RequiredHuman =
    {
        "Hips", "Spine", "Head",
        "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };

    public static string MixamoKey(string bone)
    {
        int colon = bone.LastIndexOf(':');
        string key = colon >= 0 ? bone.Substring(colon + 1) : bone;
        if (key.StartsWith("mixamorig", System.StringComparison.OrdinalIgnoreCase))
            key = key.Substring("mixamorig".Length).TrimStart('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return key;
    }

    /// <summary>Explicit Mixamo mapping when every required bone is found (fingers mapped when all three
    /// phalanges exist); otherwise keeps Unity's own auto-mapping from pass 1.</summary>
    private static string ApplyMixamoMapping(ref HumanDescription description)
    {
        var human = new List<HumanBone>();
        var used = new HashSet<string>();
        foreach (var bone in description.skeleton)
        {
            if (!MixamoToHuman.TryGetValue(MixamoKey(bone.name), out var humanName) || !used.Add(humanName)) continue;
            var hb = new HumanBone { humanName = humanName, boneName = bone.name };
            hb.limit.useDefaultValues = true;
            human.Add(hb);
        }

        var missing = RequiredHuman.Where(r => !used.Contains(r)).ToList();
        if (missing.Count > 0)
            return $"Mixamo names not found ({string.Join(", ", missing)}) — kept Unity auto-mapping ({description.human.Length} bones)";

        // Drop incomplete fingers (a finger needs all three phalanges).
        int fingers = 0;
        foreach (var side in new[] { "Left", "Right" })
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                var names = new[] { "Proximal", "Intermediate", "Distal" }.Select(p => $"{side} {finger} {p}").ToArray();
                if (names.All(used.Contains)) fingers++;
                else human.RemoveAll(h => names.Contains(h.humanName));
            }

        description.human = human.ToArray();
        description.upperArmTwist = 0.5f;
        description.lowerArmTwist = 0.5f;
        description.upperLegTwist = 0.5f;
        description.lowerLegTwist = 0.5f;
        description.armStretch = 0.05f;
        description.legStretch = 0.05f;
        description.feetSpacing = 0f;
        description.hasTranslationDoF = false;
        return $"Mixamo mapping, {human.Count} bones, {fingers}/10 fingers";
    }

    /// <summary>(human bone, candidate next human bones, direction). Only mapped bones are rotated.</summary>
    private static readonly (string bone, string[] next, string direction, float minDegrees)[] TPoseChains =
    {
        ("Spine", new[] { "Chest", "UpperChest", "Neck", "Head" }, "up", 10f),
        ("Chest", new[] { "UpperChest", "Neck", "Head" }, "up", 10f),
        ("UpperChest", new[] { "Neck", "Head" }, "up", 10f),
        ("Neck", new[] { "Head" }, "up", 10f),
        ("LeftUpperLeg", new[] { "LeftLowerLeg" }, "down", 0f), ("LeftLowerLeg", new[] { "LeftFoot" }, "down", 0f),
        ("RightUpperLeg", new[] { "RightLowerLeg" }, "down", 0f), ("RightLowerLeg", new[] { "RightFoot" }, "down", 0f),
        ("LeftUpperArm", new[] { "LeftLowerArm" }, "left", 0f), ("LeftLowerArm", new[] { "LeftHand" }, "left", 0f),
        ("RightUpperArm", new[] { "RightLowerArm" }, "right", 0f), ("RightLowerArm", new[] { "RightHand" }, "right", 0f),
    };

    /// <summary>Straightens the source description's rest to an exact T-pose (see the class summary).</summary>
    private static string EnforceSourceTPose(ref HumanDescription description, string fbx)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (model == null) return "T-pose: no model";
        var boneOf = description.human.ToDictionary(h => h.humanName, h => h.boneName);
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

            Transform T(string human) => boneOf.TryGetValue(human, out var b) && transforms.TryGetValue(b, out var t) ? t : null;
            var leftArm = T("LeftUpperArm");
            var rightArm = T("RightUpperArm");
            if (leftArm == null || rightArm == null) return "T-pose: arms not mapped";
            var span = leftArm.position - rightArm.position;
            var left = new Vector3(span.x, 0f, span.z).normalized;

            float worst = 0f;
            string worstBone = "";
            foreach (var (bone, next, direction, minDegrees) in TPoseChains)
            {
                var b = T(bone);
                var c = next.Select(T).FirstOrDefault(x => x != null);
                if (b == null || c == null) continue;
                var target = direction == "down" ? Vector3.down : direction == "up" ? Vector3.up : direction == "left" ? left : -left;
                var current = (c.position - b.position).normalized;
                float angle = Vector3.Angle(current, target);
                if (angle < minDegrees) continue;
                if (angle > worst) { worst = angle; worstBone = bone; }
                b.rotation = Quaternion.FromToRotation(current, target) * b.rotation;
            }

            for (int i = 0; i < skeleton.Length; i++)
                if (transforms.TryGetValue(skeleton[i].name, out var t) && t != go.transform)
                    skeleton[i].rotation = t.localRotation;
            description.skeleton = skeleton;
            return $"source T-pose enforced (largest correction {worst:0.0} deg{(worst > 0f ? " at " + worstBone : "")})";
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ── Clips ────────────────────────────────────────────────────────────────────

    private static AnimationClip LoadClip(string fbx, string name)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (asset is AnimationClip c && c.name == name) return c;
        return null;
    }

    /// <summary>Writes an editable copy of `source` (all curves). With `restMuscles`, finger curves are
    /// replaced by the avatar's relaxed rest hand. `configure` edits the copy's clip settings.</summary>
    private static AnimationClip WriteCopy(AnimationClip source, string path, Dictionary<string, float> restMuscles,
                                           System.Func<AnimationClipSettings, AnimationClipSettings> configure)
    {
        var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (copy == null)
        {
            copy = new AnimationClip();
            AssetDatabase.CreateAsset(copy, path);
        }
        copy.ClearCurves();
        copy.frameRate = source.frameRate;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            if (restMuscles != null && binding.type == typeof(Animator) && MakeHumanAnimationTestSetup.IsFingerCurve(binding.propertyName) &&
                restMuscles.TryGetValue(MakeHumanAnimationTestSetup.MuscleNameOf(binding.propertyName), out float rest))
                curve = AnimationCurve.Constant(0f, source.length, rest);
            AnimationUtility.SetEditorCurve(copy, binding, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.startTime = 0f;
        settings.stopTime = source.length;
        if (configure != null) settings = configure(settings);
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        copy.name = Path.GetFileNameWithoutExtension(path);
        return copy;
    }

    /// <summary>Per-frame muscle values of a Humanoid clip (all Animator curves except root/IK goals).</summary>
    private static List<float[]> SampleMuscles(AnimationClip clip, out List<string> attributes)
    {
        var curves = new List<AnimationCurve>();
        attributes = new List<string>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator)) continue;
            string p = binding.propertyName;
            if (p.StartsWith("Root") || p.StartsWith("MotionT") || p.StartsWith("MotionQ") || p.EndsWith("T.x") || p.EndsWith("T.y") || p.EndsWith("T.z") ||
                p.Contains("Q.")) continue; // root / IK goals
            curves.Add(AnimationUtility.GetEditorCurve(clip, binding));
            attributes.Add(p);
        }
        int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * clip.frameRate) + 1);
        var samples = new List<float[]>(frames);
        for (int f = 0; f < frames; f++)
        {
            float t = Mathf.Min(f / clip.frameRate, clip.length);
            var row = new float[curves.Count];
            for (int c = 0; c < curves.Count; c++) row[c] = curves[c].Evaluate(t);
            samples.Add(row);
        }
        return samples;
    }

    private static float[] FrameDeltas(List<float[]> samples)
    {
        var deltas = new float[samples.Count - 1];
        for (int f = 0; f < deltas.Length; f++)
        {
            float max = 0f;
            for (int c = 0; c < samples[f].Length; c++) max = Mathf.Max(max, Mathf.Abs(samples[f + 1][c] - samples[f][c]));
            deltas[f] = max;
        }
        return deltas;
    }

    private static float PopThreshold(float[] deltas)
    {
        var sorted = deltas.OrderBy(d => d).ToArray();
        float median = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0f;
        return Mathf.Max(0.25f, median * 8f);
    }

    /// <summary>Frames to trim at the start/end: a pop (per-frame muscle jump far above the clip's
    /// median) within the first/last 5 frames — typically a T-pose calibration frame.</summary>
    private static (int lead, int tail) DetectEdgePops(AnimationClip clip)
    {
        var samples = SampleMuscles(clip, out _);
        if (samples.Count < 12) return (0, 0);
        var deltas = FrameDeltas(samples);
        float threshold = PopThreshold(deltas);
        int lead = 0, tail = 0;
        for (int f = 0; f < 5; f++) if (deltas[f] > threshold) lead = f + 1;
        for (int f = 0; f < 5; f++) if (deltas[deltas.Length - 1 - f] > threshold) tail = f + 1;
        return (lead, tail);
    }

    private static bool HasFingerCapture(AnimationClip clip)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator) || !MakeHumanAnimationTestSetup.IsFingerCurve(binding.propertyName)) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i <= 60; i++)
            {
                float v = curve.Evaluate(clip.length * i / 60f);
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            if (max - min > 0.05f) return true;
        }
        return false;
    }

    private static string Warnings(AnimationClip clip, CombatAnimationEntry entry)
    {
        var warnings = new List<string>();
        if (clip.length < 0.3f) warnings.Add($"very short ({clip.length:0.00} s)");
        var samples = SampleMuscles(clip, out var attributes);
        if (samples.Count > 2)
        {
            var deltas = FrameDeltas(samples);
            float threshold = Mathf.Max(0.35f, PopThreshold(deltas) * 1.25f);
            var pops = new List<string>();
            for (int f = 0; f < deltas.Length; f++) if (deltas[f] > threshold) pops.Add($"{f / clip.frameRate:0.00}s");
            if (pops.Count > 0) warnings.Add($"pose pop(s) at {string.Join(", ", pops.Take(5))}{(pops.Count > 5 ? "..." : "")}");

            float extreme = 0f;
            string extremeName = "";
            foreach (var row in samples)
                for (int c = 0; c < row.Length; c++)
                    if (Mathf.Abs(row[c]) > extreme) { extreme = Mathf.Abs(row[c]); extremeName = attributes[c]; }
            if (extreme > 1.5f) warnings.Add($"muscle beyond limits ({extremeName} {extreme:0.00})");
        }
        return string.Join("; ", warnings);
    }

    /// <summary>Net root travel (in the starting frame, metres on the MakeHuman avatar) and net yaw from
    /// the clip's RootT / RootQ curves; sets the root-motion recommendation.</summary>
    private static void MeasureRoot(CombatAnimationEntry entry, AnimationClip clip, float humanScale)
    {
        var curves = new Dictionary<string, AnimationCurve>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Animator) && binding.propertyName.StartsWith("Root"))
                curves[binding.propertyName] = AnimationUtility.GetEditorCurve(clip, binding);

        float V(string p, float t) => curves.TryGetValue(p, out var c) ? c.Evaluate(t) : 0f;
        Vector3 Pos(float t) => new Vector3(V("RootT.x", t), V("RootT.y", t), V("RootT.z", t));
        Quaternion Rot(float t)
        {
            var q = new Quaternion(V("RootQ.x", t), V("RootQ.y", t), V("RootQ.z", t), curves.ContainsKey("RootQ.w") ? V("RootQ.w", t) : 1f);
            return q.normalized;
        }

        float end = clip.length;
        float yaw0 = Yaw(Rot(0f));
        var delta = Quaternion.Euler(0f, -yaw0, 0f) * (Pos(end) - Pos(0f)) * humanScale;
        entry.rootTravel = new Vector3(delta.x, delta.y, delta.z);

        // Net yaw accumulated frame by frame (a 360 spin counts, but so does its direction).
        float yaw = 0f, last = yaw0;
        int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * clip.frameRate));
        for (int f = 1; f <= frames; f++)
        {
            float y = Yaw(Rot(clip.length * f / frames));
            yaw += Mathf.DeltaAngle(last, y);
            last = y;
        }
        entry.rootYaw = yaw;
        entry.rootMotion = new Vector2(delta.x, delta.z).magnitude > RootMotionTravel || Mathf.Abs(yaw) > RootMotionYaw
            ? CombatRootMotion.RootMotion : CombatRootMotion.InPlace;
    }

    private static float Yaw(Quaternion q)
    {
        var forward = q * Vector3.forward;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    /// <summary>orientationOffsetY so the MakeHuman avatar's hips face +Z at the clip's first frame (the
    /// in-place debug clip then always starts facing the camera / its opponent).</summary>
    private static float CalibrateStartFacing(AnimationClip clip, GameObject avatarGO)
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

        var graph = PlayableGraph.Create("CalibrateStartFacing");
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimationClipPlayable.Create(graph, clip);
        output.SetSourcePlayable(playable);
        playable.SetTime(0f);
        graph.Evaluate(0f);
        var across = avatarGO.transform.InverseTransformDirection(right.position - left.position);
        var forward = Vector3.Cross(across, Vector3.up);
        graph.Destroy();

        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        settings.orientationOffsetY = -yaw;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return -yaw;
    }

    // ── Categories ───────────────────────────────────────────────────────────────

    /// <summary>First matching rule wins — order matters ("get up" before "fall", reactions before attacks).</summary>
    private static readonly (CombatAnimationCategory category, string[] words)[] CategoryRules =
    {
        (CombatAnimationCategory.GetUp, new[] { "getup", "get up", "standup", "stand up", "getting up", "rise" }),
        (CombatAnimationCategory.Knockdown, new[] { "knockdown", "knock down", "knocked", "knockout", "fall", "death", "dying", "die" }),
        (CombatAnimationCategory.HitReaction, new[] { "hit reaction", "reaction", "react", "impact", "hurt", "stagger", "damage", "gethit", "get hit", "hit" }),
        (CombatAnimationCategory.Block, new[] { "block", "parry", "defend", "defence", "defense" }),
        (CombatAnimationCategory.Dodge, new[] { "dodge", "evade", "evasion", "duck", "sidestep", "side step", "weave", "slip", "roll", "backstep" }),
        (CombatAnimationCategory.Victory, new[] { "victory", "win", "celebrat", "cheer", "triumph" }),
        (CombatAnimationCategory.Taunt, new[] { "taunt", "provoke", "mock", "showoff", "show off", "flex" }),
        (CombatAnimationCategory.AttackKick, new[] { "kick", "knee", "roundhouse", "sweep" }),
        (CombatAnimationCategory.AttackPunch, new[] { "punch", "jab", "cross", "hook", "uppercut", "boxing" }),
        (CombatAnimationCategory.AttackOther, new[] { "elbow", "headbutt", "slash", "sword", "stab", "attack", "strike", "combo", "throw", "grab", "smash", "slam" }),
        (CombatAnimationCategory.Special, new[] { "super", "hero", "power", "fly", "flight", "hover", "laser", "beam", "charge", "energy", "blast", "landing", "land" }),
        (CombatAnimationCategory.IdleCombat, new[] { "idle", "stance", "guard", "ready", "fight pose" }),
        (CombatAnimationCategory.Movement, new[] { "walk", "run", "jog", "sprint", "strafe", "step", "jump", "shuffle", "move", "turn" }),
    };

    public static CombatAnimationCategory Categorize(string name)
    {
        // Split CamelCase / separators into lowercase words: "Roundhouse_Kick01" -> "roundhouse kick 01".
        var sb = new StringBuilder(" ");
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(name[i - 1])) sb.Append(' ');
            if (char.IsDigit(c) && i > 0 && char.IsLetter(name[i - 1])) sb.Append(' ');
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        string text = System.Text.RegularExpressions.Regex.Replace(sb.Append(' ').ToString(), " +", " ");
        string squashed = text.Replace(" ", "");
        foreach (var (category, words) in CategoryRules)
            foreach (var word in words)
                if (word.Contains(' ') ? text.Contains(" " + word + " ") || squashed.Contains(word.Replace(" ", "")) : ContainsWordStart(text, word))
                    return category;
        return CombatAnimationCategory.Unknown;
    }

    /// <summary>`word` at the start of a word ("kicks" / "kicking" match "kick", "backhand" does not match "hand").</summary>
    private static bool ContainsWordStart(string text, string word) => text.Contains(" " + word);

    private static bool DefaultLoop(CombatAnimationCategory category, string name)
    {
        if (category == CombatAnimationCategory.IdleCombat) return true;
        if (category != CombatAnimationCategory.Movement) return false;
        string n = name.ToLowerInvariant();
        return n.Contains("walk") || n.Contains("run") || n.Contains("jog") || n.Contains("strafe") || n.Contains("shuffle");
    }

    // ── Demo sequence ────────────────────────────────────────────────────────────

    private static readonly Dictionary<CombatAnimationRole, CombatAnimationCategory> RoleCategory = new()
    {
        [CombatAnimationRole.CombatIdle] = CombatAnimationCategory.IdleCombat,
        [CombatAnimationRole.Punch] = CombatAnimationCategory.AttackPunch,
        [CombatAnimationRole.HeavyPunch] = CombatAnimationCategory.AttackPunch,
        [CombatAnimationRole.Kick] = CombatAnimationCategory.AttackKick,
        [CombatAnimationRole.Block] = CombatAnimationCategory.Block,
        [CombatAnimationRole.Dodge] = CombatAnimationCategory.Dodge,
        [CombatAnimationRole.HitReaction] = CombatAnimationCategory.HitReaction,
        [CombatAnimationRole.Knockdown] = CombatAnimationCategory.Knockdown,
        [CombatAnimationRole.GetUp] = CombatAnimationCategory.GetUp,
        [CombatAnimationRole.Taunt] = CombatAnimationCategory.Taunt,
        [CombatAnimationRole.Victory] = CombatAnimationCategory.Victory,
    };

    /// <summary>The role's reviewed clip, or (marked "auto") the first non-rejected clip of the role's
    /// category — approved ones first — skipping `exclude`.</summary>
    private static (CombatAnimationEntry entry, bool reviewed) Resolve(CombatAnimationLibrarySO library, CombatAnimationRole role, string exclude = null)
    {
        var assigned = library.ForRole(role);
        if (assigned != null && assigned.review != CombatReviewStatus.Rejected && assigned.name != exclude) return (assigned, true);
        var candidate = library.entries
            .Where(e => e != null && e.category == RoleCategory[role] && e.review != CombatReviewStatus.Rejected && e.name != exclude)
            .OrderBy(e => e.review == CombatReviewStatus.Approved ? 0 : 1)
            .FirstOrDefault();
        return (candidate, false);
    }

    /// <summary>Guard -> Punch -> Punch -> Kick -> Dodge -> Hit Reaction -> Victory/Taunt, from the
    /// reviewed role assignments (falling back to unreviewed category candidates, labelled "*").</summary>
    public static void RebuildDemoSequence(CombatAnimationLibrarySO library, List<string> report)
    {
        var steps = new List<CombatDemoStep>();
        var hit = Resolve(library, CombatAnimationRole.HitReaction).entry;
        var punch = Resolve(library, CombatAnimationRole.Punch);
        var heavy = Resolve(library, CombatAnimationRole.HeavyPunch, punch.entry?.name);
        if (heavy.entry == null) heavy = punch;
        var victory = Resolve(library, CombatAnimationRole.Victory);
        if (victory.entry == null) victory = Resolve(library, CombatAnimationRole.Taunt);

        void Add(string label, (CombatAnimationEntry entry, bool reviewed) r, CombatAnimationEntry partner = null, float delay = 0.4f)
        {
            if (r.entry == null) { report?.Add($"DEMO     {label}: no clip available"); return; }
            steps.Add(new CombatDemoStep
            {
                label = label + (r.reviewed ? "" : " *"),
                entryName = r.entry.name,
                partnerEntryName = partner != null ? partner.name : "",
                partnerDelay = delay,
            });
        }

        Add("Guard", Resolve(library, CombatAnimationRole.CombatIdle));
        Add("Punch", punch, hit, 0.35f);
        Add("Punch", heavy, hit, 0.35f);
        Add("Kick", Resolve(library, CombatAnimationRole.Kick), hit, 0.4f);
        Add("Dodge", Resolve(library, CombatAnimationRole.Dodge), punch.entry, 0f);
        Add("Hit Reaction", Resolve(library, CombatAnimationRole.HitReaction));
        Add("Victory", victory, Resolve(library, CombatAnimationRole.Knockdown).entry, 0f);
        library.demoSequence = steps;
        report?.Add($"DEMO     {steps.Count} step(s): {string.Join(" -> ", steps.Select(s => s.label))} (* = unreviewed auto candidate)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    public static CombatAnimationLibrarySO LoadOrCreateLibrary(string path)
    {
        var library = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(path);
        if (library != null) return library;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        library = ScriptableObject.CreateInstance<CombatAnimationLibrarySO>();
        AssetDatabase.CreateAsset(library, path);
        return library;
    }

    public static List<string> FindFbx(string folder, bool recursive)
    {
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) return new List<string>();
        string projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
        return Directory.GetFiles(full, "*.fbx", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFullPath(f).Replace('\\', '/').Replace(projectRoot, ""))
            .OrderBy(f => f)
            .ToList();
    }

    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        foreach (var noise in new[] { "Rokoko_Mocap_", "Rokoko_", "Mocap_" })
            if (s.StartsWith(noise, System.StringComparison.OrdinalIgnoreCase) && s.Length > noise.Length) s = s.Substring(noise.Length);
        return s;
    }

    public static string CanonicalFbxHash()
    {
        string path = Path.GetFullPath(MakeHumanFbxPipeline.FbxPath);
        if (!File.Exists(path)) return "";
        using var sha = SHA256.Create();
        return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    public static void EnsureFolder(string folder)
    {
        folder = folder.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    /// <summary>Points the avatar debug scene's AvatarDebugPreview at the library (scene saved; a scene
    /// the user has open is edited in place and saved, otherwise it's opened additively and closed).</summary>
    public static void AssignLibraryToDebugScene(CombatAnimationLibrarySO library, List<string> report)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { report.Add("SCENE    (play mode — assign the library to AvatarDebugPreview manually)"); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MakeHumanBodyBuilder.DebugScenePath) == null) { report.Add("SCENE    AvatarDebug scene missing"); return; }

        var scene = EditorSceneManager.GetSceneByPath(MakeHumanBodyBuilder.DebugScenePath);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Additive);
        bool changed = false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var preview in root.GetComponentsInChildren<AvatarDebugPreview>(true))
                if (preview.combatLibrary != library)
                {
                    preview.combatLibrary = library;
                    EditorUtility.SetDirty(preview);
                    changed = true;
                }
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        report.Add($"SCENE    AvatarDebug: combat library {(changed ? "assigned" : "already assigned")}");
    }
}
