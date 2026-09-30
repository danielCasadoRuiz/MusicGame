using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Tools > MusicGame > Avatars > MakeHuman Body Builder — the one-click production workflow that turns
/// the bundled MakeHuman CC0 source data into MusicGame's avatar assets:
///
///   MakeHumanBodyBaker (pure bake)  ->  eight canonical-height bodies re-posed into the T-pose rest,
///                                       ONE canonical skeleton, top-4 skin weights
///   MakeHumanFbxPipeline            ->  JSON -> headless Blender -> Generated/MakeHuman_Canonical.fbx
///                                       (Armature + Body + Eyes, Gender + six rebased blendshapes)
///                                   ->  Unity ModelImporter: Humanoid, Create From This Model
///                                       ("MakeHuman_CanonicalAvatar", explicit bone map)
///                                   ->  MaleBase/FemaleBase presets read back from the imported meshes
///                                   ->  MakeHumanAvatar.prefab = variant of the FBX model + AvatarVisualPart
///                                   ->  Addressables + BaseAvatarDefinitionSO + minimum content
///
/// RE-RUNNABLE: the FBX and presets are regenerated at the same paths (same GUIDs), so every reference
/// (recipes, opponents, scenes) survives a rebake. Hand-authored content (body profiles, face,
/// identities, recipes, materials) is only created when missing — never overwritten.
/// </summary>
public class MakeHumanBodyBuilder : EditorWindow
{
    public const string RootFolder = "Assets/_Project/Avatar/MakeHuman";
    public const string GeneratedFolder = RootFolder + "/Generated";
    public const string ContentFolder = RootFolder + "/Content";

    public const string PrefabPath = GeneratedFolder + "/MakeHumanAvatar.prefab";
    // Before the canonical FBX, meshes/Avatar were generated as loose Unity assets — removed on rebake.
    private static readonly string[] LegacyGeneratedFolders = { GeneratedFolder + "/Meshes", GeneratedFolder + "/Avatars", GeneratedFolder + "/Animation" };
    public const string MalePresetPath = GeneratedFolder + "/Presets/MakeHuman_MaleBase.asset";
    public const string FemalePresetPath = GeneratedFolder + "/Presets/MakeHuman_FemaleBase.asset";

    public const string BaseAvatarPath = ContentFolder + "/BaseAvatar_MakeHuman.asset";
    public const string MaleRecipePath = ContentFolder + "/Recipes/Avatar_MakeHuman_TestMale.asset";
    public const string FemaleRecipePath = ContentFolder + "/Recipes/Avatar_MakeHuman_TestFemale.asset";

    public const string DebugScenePath = "Assets/_Project/Scenes/Debug/AvatarDebug.unity";
    public const string TestOpponentPath = "Assets/_Project/Configs/Fight/Opponent_Rex.asset";

    public const string AddressablesGroup = "Avatars";
    public const string PrefabAddress = "Avatar/MakeHumanBody";

    private static readonly Color DefaultSkinTone = new Color(0.87f, 0.68f, 0.57f);

    private string _lastReport = "";
    private Vector2 _scroll;

    [MenuItem("Tools/MusicGame/Avatars/MakeHuman Body Builder")]
    public static void Open() => GetWindow<MakeHumanBodyBuilder>("MakeHuman Body Builder");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("MakeHuman body (CC0) -> MusicGame avatar assets", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox($"Source: {RootFolder}/Source\nOutput: {GeneratedFolder} (regenerated) + {ContentFolder} (created if missing)", MessageType.None);

        if (GUILayout.Button("Bake / Rebake MakeHuman Body", GUILayout.Height(32))) _lastReport = BakeAll(out _);
        if (GUILayout.Button("Setup Animation Test Clips (Idle / Walk / Run)")) _lastReport = MakeHumanAnimationTestSetup.Setup(out _);
        if (GUILayout.Button("Create / Rebuild Avatar Debug Scene")) _lastReport = CreateDebugScene();
        if (GUILayout.Button("Assign Test Recipe to Opponent Rex (all levels)")) _lastReport = AssignTestRecipeToOpponent();
        if (GUILayout.Button("Run MakeHuman Body Tests (edit mode)")) _lastReport = MakeHumanBodyTests.RunEditModeTests(out _);
        if (GUILayout.Button("Validate Avatars")) AvatarValidator.Validate();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_lastReport, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    // ── Bake ─────────────────────────────────────────────────────────────────────

    public static string BakeAll(out bool success)
    {
        var errors = new List<string>();
        MakeHumanBakeResult result;
        try
        {
            result = MakeHumanBodyBaker.Bake(errors);
        }
        catch (System.Exception e)
        {
            errors.Add($"Bake threw: {e}");
            result = null;
        }

        if (result == null || errors.Count > 0)
        {
            success = false;
            string message = "[MakeHumanBodyBuilder] Bake FAILED — nothing was written:\n  - " + string.Join("\n  - ", errors);
            Debug.LogError(message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("MakeHuman bake failed", string.Join("\n", errors), "OK");
            return message;
        }

        EnsureFolder(GeneratedFolder + "/Materials");
        EnsureFolder(GeneratedFolder + "/Presets");
        EnsureFolder(ContentFolder + "/Body");
        EnsureFolder(ContentFolder + "/Face");
        EnsureFolder(ContentFolder + "/Identities");
        EnsureFolder(ContentFolder + "/Recipes");

        // 1. Canonical FBX: bake -> JSON -> headless Blender -> MakeHuman_Canonical.fbx.
        MakeHumanFbxPipeline.ExportJson(result, MakeHumanFbxPipeline.JsonPath);
        string fbxFull = Path.GetFullPath(MakeHumanFbxPipeline.FbxPath);
        if (!MakeHumanFbxPipeline.RunBlender(MakeHumanFbxPipeline.JsonPath, fbxFull, out string blenderLog))
        {
            success = false;
            string message = "[MakeHumanBodyBuilder] Blender FBX export FAILED:\n" + blenderLog;
            Debug.LogError(message);
            return message;
        }
        AssetDatabase.ImportAsset(MakeHumanFbxPipeline.FbxPath, ImportAssetOptions.ForceUpdate);

        // 2. Unity import: Humanoid, avatar created from this model.
        var importReport = new List<string>();
        if (!MakeHumanFbxPipeline.ConfigureImporter(importReport))
        {
            success = false;
            string message = "[MakeHumanBodyBuilder] FBX Humanoid import FAILED:\n  " + string.Join("\n  ", importReport);
            Debug.LogError(message);
            return message;
        }
        result.Report.AddRange(importReport);

        // 3. Materials (created once — safe for an artist to tweak afterwards).
        var skin = GetOrCreateMaterial($"{GeneratedFolder}/Materials/MakeHuman_Skin.mat", DefaultSkinTone, 0.35f);
        var eyes = GetOrCreateMaterial($"{GeneratedFolder}/Materials/MakeHuman_Eyes.mat", new Color(0.92f, 0.92f, 0.9f), 0.8f);

        // 4. Gender endpoint reference geometry, read back from the IMPORTED meshes (their vertex order).
        var malePreset = WritePresetFromFbx(MalePresetPath, "makehuman_male_base", result, female: false);
        var femalePreset = WritePresetFromFbx(FemalePresetPath, "makehuman_female_base", result, female: true);

        // 5. Avatar prefab = a variant of the canonical FBX model + the MusicGame avatar components.
        var model = MakeHumanFbxPipeline.LoadModel();
        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            root.name = "MakeHumanAvatar";
            var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
            animator.avatar = MakeHumanFbxPipeline.LoadAvatar();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var renderers = new SkinnedMeshRenderer[result.Source.Parts.Length];
            for (int p = 0; p < renderers.Length; p++)
            {
                var part = result.Source.Parts[p];
                SkinnedMeshRenderer smr = null;
                foreach (var candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (candidate.name == part.Name) smr = candidate;
                if (smr == null)
                {
                    success = false;
                    return $"[MakeHumanBodyBuilder] The FBX has no '{part.Name}' SkinnedMeshRenderer.";
                }
                smr.sharedMaterial = p == 0 ? skin : eyes;
                smr.updateWhenOffscreen = false;
                smr.localBounds = BoundsIn(smr.rootBone != null ? smr.rootBone : smr.transform, result.RendererBounds);
                renderers[p] = smr;
            }

            var visualPart = root.GetComponent<AvatarVisualPart>() ?? root.AddComponent<AvatarVisualPart>();
            visualPart.rootBone = FindChild(root.transform, result.Rig.RootBoneName);
            visualPart.skinnedRenderers = renderers;
            visualPart.skinToneRenderers = new Renderer[] { renderers[0] };
            visualPart.faceRenderer = null; // face texture system is a later milestone

            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null)
            {
                success = false;
                return "[MakeHumanBodyBuilder] Failed to save the prefab.";
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        // 6. Addressables + BaseAvatarDefinitionSO.
        string prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
        if (!MakeAddressable(prefabGuid, out string addressError))
        {
            success = false;
            Debug.LogError(addressError);
            return addressError;
        }

        var baseAvatar = LoadOrCreate<BaseAvatarDefinitionSO>(BaseAvatarPath);
        baseAvatar.id = "makehuman";
        baseAvatar.baseAvatarPrefab = new AssetReferenceGameObject(prefabGuid);
        baseAvatar.maleBase = malePreset;
        baseAvatar.femaleBase = femalePreset;
        EditorUtility.SetDirty(baseAvatar);

        // 7. Minimum content (only created when missing).
        CreateContentIfMissing(baseAvatar);

        // 8. Assets from before the canonical FBX (Unity-generated meshes / Avatar / test walk) are obsolete.
        foreach (var legacy in LegacyGeneratedFolders)
            if (AssetDatabase.IsValidFolder(legacy)) AssetDatabase.DeleteAsset(legacy);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        success = true;
        string report = "[MakeHumanBodyBuilder] Bake complete:\n  " + string.Join("\n  ", result.Report);
        Debug.Log(report);
        return report;
    }

    /// <summary>MaleBase = the imported Body/Eyes meshes' own rest data; FemaleBase = rest + the imported
    /// Gender blendshape frame. Stored in the IMPORTED vertex order, so presets always describe exactly
    /// the meshes the renderers use.</summary>
    private static AvatarMeshBasePresetSO WritePresetFromFbx(string path, string id, MakeHumanBakeResult result, bool female)
    {
        var preset = LoadOrCreate<AvatarMeshBasePresetSO>(path);
        preset.id = id;
        preset.version++;

        var renderers = new List<AvatarMeshBaseVertexData>();
        foreach (var part in result.Source.Parts)
        {
            var mesh = MakeHumanFbxPipeline.LoadMesh(part.Name);
            if (mesh == null) continue;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            if (female)
            {
                int gender = mesh.GetBlendShapeIndex(MorphChannel.Gender.ToString());
                var dV = new Vector3[mesh.vertexCount];
                var dN = new Vector3[mesh.vertexCount];
                var dT = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(gender, mesh.GetBlendShapeFrameCount(gender) - 1, dV, dN, dT);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] += dV[i];
                    normals[i] = (normals[i] + dN[i]).normalized;
                    var t = (Vector3)tangents[i] + dT[i];
                    tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }
            }
            renderers.Add(new AvatarMeshBaseVertexData { rendererName = part.Name, vertices = vertices, normals = normals, tangents = tangents });
        }
        preset.renderers = renderers.ToArray();
        EditorUtility.SetDirty(preset);
        return preset;
    }

    private static Bounds BoundsIn(Transform space, Bounds modelBounds)
    {
        var b = new Bounds(space.InverseTransformPoint(modelBounds.center), Vector3.zero);
        var e = modelBounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = modelBounds.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            b.Encapsulate(space.InverseTransformPoint(corner));
        }
        return b;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    private static void CreateContentIfMissing(BaseAvatarDefinitionSO baseAvatar)
    {
        var face = CreateIfMissing<FaceProfileSO>($"{ContentFolder}/Face/FaceProfile_MakeHumanDefault.asset", f =>
        {
            f.id = "makehuman_default";
            f.skinTone = DefaultSkinTone;
        });
        var maleBody = CreateIfMissing<BodyMorphProfileSO>($"{ContentFolder}/Body/BodyMorph_MaleAverage.asset", b =>
        {
            b.baseType = BodyBaseType.Male; b.weight = 0.5f; b.muscle = 0f;
        });
        var femaleBody = CreateIfMissing<BodyMorphProfileSO>($"{ContentFolder}/Body/BodyMorph_FemaleAverage.asset", b =>
        {
            b.baseType = BodyBaseType.Female; b.weight = 0.5f; b.muscle = 0f;
        });
        var maleIdentity = CreateIfMissing<AvatarIdentitySO>($"{ContentFolder}/Identities/Identity_MakeHumanMale.asset", i =>
        {
            i.id = "makehuman_male"; i.bodyMorphProfile = maleBody; i.face = face; i.hair = null;
        });
        var femaleIdentity = CreateIfMissing<AvatarIdentitySO>($"{ContentFolder}/Identities/Identity_MakeHumanFemale.asset", i =>
        {
            i.id = "makehuman_female"; i.bodyMorphProfile = femaleBody; i.face = face; i.hair = null;
        });
        CreateIfMissing<AvatarRecipeSO>(MaleRecipePath, r => { r.identity = maleIdentity; r.baseAvatar = baseAvatar; });
        CreateIfMissing<AvatarRecipeSO>(FemaleRecipePath, r => { r.identity = femaleIdentity; r.baseAvatar = baseAvatar; });
    }

    // ── Debug scene / Fight test hookup ──────────────────────────────────────────

    [MenuItem("Tools/MusicGame/Avatars/Create Avatar Debug Scene")]
    public static void CreateDebugSceneMenu() => CreateDebugScene();

    public static string CreateDebugScene()
    {
        var male = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MaleRecipePath);
        var female = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(FemaleRecipePath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MakeHumanAnimationTestSetup.ControllerPath);
        if (male == null || female == null) return "[MakeHumanBodyBuilder] Bake the MakeHuman body first (test recipes missing).";

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return "[MakeHumanBodyBuilder] Cancelled.";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var camera = Camera.main;
        if (camera != null)
        {
            camera.transform.position = new Vector3(0f, 1.1f, 3.6f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.95f, 0f) - camera.transform.position);
            camera.fieldOfView = 40f;
        }

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";

        var previewGO = new GameObject("AvatarDebugPreview");
        var preview = previewGO.AddComponent<AvatarDebugPreview>();
        preview.recipe = male;
        preview.additionalRecipes = new[] { female };
        preview.spacing = 1.1f;
        preview.buildOnStart = true;
        preview.debugAnimatorController = controller;
        preview.combatLibrary = AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(CombatAnimationImporter.LibraryPath);
        previewGO.AddComponent<AvatarBodyDebugPanel>().preview = preview;

        EnsureFolder(Path.GetDirectoryName(DebugScenePath).Replace('\\', '/'));
        EditorSceneManager.SaveScene(scene, DebugScenePath);
        return $"[MakeHumanBodyBuilder] Debug scene saved to {DebugScenePath} — open it and press Play.";
    }

    [MenuItem("Tools/MusicGame/Avatars/Assign MakeHuman Test Recipe to Opponent Rex")]
    public static void AssignTestRecipeMenu() => Debug.Log(AssignTestRecipeToOpponent());

    /// <summary>Uses the EXISTING Fight integration point (OpponentLevelConfig.avatarRecipe) — nothing
    /// Fight-side changes. Only fills empty slots; an already-assigned recipe is left alone.</summary>
    public static string AssignTestRecipeToOpponent()
    {
        var recipe = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(MaleRecipePath);
        var opponent = AssetDatabase.LoadAssetAtPath<OpponentDefinition>(TestOpponentPath);
        if (recipe == null) return "[MakeHumanBodyBuilder] Bake the MakeHuman body first (test recipe missing).";
        if (opponent == null) return $"[MakeHumanBodyBuilder] Opponent not found at {TestOpponentPath}.";

        int assigned = 0;
        foreach (var level in opponent.levels)
            if (level != null && level.avatarRecipe == null) { level.avatarRecipe = recipe; assigned++; }
        if (opponent.defaultConfig != null && opponent.defaultConfig.avatarRecipe == null) { opponent.defaultConfig.avatarRecipe = recipe; assigned++; }

        EditorUtility.SetDirty(opponent);
        AssetDatabase.SaveAssets();
        return $"[MakeHumanBodyBuilder] '{recipe.name}' assigned to {assigned} empty level slot(s) of '{opponent.name}'.";
    }

    // ── Batch-mode entry point ───────────────────────────────────────────────────

    /// <summary>Unity.exe -batchmode -projectPath ... -executeMethod MakeHumanBodyBuilder.BakeFromCommandLine -quit</summary>
    public static void BakeFromCommandLine()
    {
        string report = BakeAll(out bool success);
        if (success)
        {
            if (MakeHumanAnimationTestSetup.ClipsPresent()) Debug.Log(MakeHumanAnimationTestSetup.Setup(out _));
            Debug.Log(CreateDebugScene());
            Debug.Log(AssignTestRecipeToOpponent());
        }
        Debug.Log(report);
        EditorApplication.Exit(success ? 0 : 1);
    }

    // ── Asset helpers ────────────────────────────────────────────────────────────

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static T CreateIfMissing<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static Material GetOrCreateMaterial(string path, Color color, float smoothness)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static bool MakeAddressable(string guid, out string error)
    {
        error = null;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            error = "[MakeHumanBodyBuilder] No AddressableAssetSettings found — open Window > Asset Management > Addressables > Groups once first.";
            return false;
        }

        var group = settings.FindGroup(AddressablesGroup);
        if (group == null)
        {
            group = settings.CreateGroup(AddressablesGroup, false, false, true, null,
                typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        }

        var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
        entry.address = PrefabAddress;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, true);
        return true;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
