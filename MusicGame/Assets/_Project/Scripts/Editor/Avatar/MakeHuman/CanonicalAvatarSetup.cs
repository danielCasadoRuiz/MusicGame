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
/// Consumes the canonical FBX exported by AvatarLab and configures MusicGame's prefab,
/// materials, Addressables, recipes and debug scene. Does not generate human geometry.
/// </summary>
public class CanonicalAvatarSetup : EditorWindow
{
    public const string RootFolder = "Assets/_Project/Avatar/MakeHuman";
    public const string GeneratedFolder = RootFolder + "/Generated";
    public const string ContentFolder = RootFolder + "/Content";

    public const string PrefabPath = GeneratedFolder + "/MakeHumanAvatar.prefab";
    // Before the canonical FBX, meshes/Avatar were generated as loose Unity assets — removed on rebake.
    private static readonly string[] LegacyGeneratedFolders = { GeneratedFolder + "/Meshes", GeneratedFolder + "/Avatars", GeneratedFolder + "/Animation", GeneratedFolder + "/Presets" };

    public const string BaseAvatarPath = ContentFolder + "/BaseAvatar_MakeHuman.asset";
    public const string MaleRecipePath = ContentFolder + "/Recipes/Avatar_MakeHuman_TestMale.asset";
    public const string FemaleRecipePath = ContentFolder + "/Recipes/Avatar_MakeHuman_TestFemale.asset";

    public const string DebugScenePath = "Assets/_Project/Scenes/Debug/AvatarDebug.unity";
    public const string TestOpponentPath = "Assets/_Project/Configs/Fight/Opponents/Opponent_Bach.asset"; // composer used by the Fight play-mode check

    public const string AddressablesGroup = "Avatars";
    public const string PrefabAddress = "Avatar/MakeHumanBody";

    private static readonly Color DefaultSkinTone = new Color(0.87f, 0.68f, 0.57f);

    private string _lastReport = "";
    private Vector2 _scroll;

    [MenuItem("Tools/MusicGame/Avatars/Canonical Avatar Setup")]
    public static void Open() => GetWindow<CanonicalAvatarSetup>("Canonical Avatar Setup");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Imported AvatarLab model -> MusicGame avatar assets", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox($"Canonical FBX: {GeneratedFolder}/MakeHuman_Canonical.fbx\nGame assets: {GeneratedFolder} (configured) + {ContentFolder} (created if missing)", MessageType.None);

        if (GUILayout.Button("Configure Imported Canonical Avatar", GUILayout.Height(32))) _lastReport = ConfigureImported(out _);
        if (GUILayout.Button("Setup Animation Test Clips (Idle / Walk / Run)")) _lastReport = MakeHumanAnimationTestSetup.Setup(out _);
        if (GUILayout.Button("Create / Rebuild Avatar Debug Scene")) _lastReport = CreateDebugScene();
        if (GUILayout.Button("Run MakeHuman Body Tests (edit mode)")) _lastReport = MakeHumanBodyTests.RunEditModeTests(out _);
        if (GUILayout.Button("Validate Avatars")) AvatarValidator.Validate();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_lastReport, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    // ── Bake ─────────────────────────────────────────────────────────────────────

    public static string ConfigureImported(out bool success)
    {
        var errors = new List<string>();
        var result = CanonicalModelReference.Read(errors);
        if (result == null)
        {
            success = false;
            return "Canonical FBX missing or invalid. Generate and export it from AvatarLab.\n" + string.Join("\n", errors);
        }
        EnsureFolder(GeneratedFolder + "/Materials");
        EnsureFolder(ContentFolder + "/Body");
        EnsureFolder(ContentFolder + "/Face");
        EnsureFolder(ContentFolder + "/Identities");
        EnsureFolder(ContentFolder + "/Recipes");
        // 3. Materials (created once — safe for an artist to tweak afterwards).
        // Skin uses URP Lit + the per-avatar skin visibility mask of segmented garments (SkinVisibilityShaderSetup).
        var skin = GetOrCreateMaterial($"{GeneratedFolder}/Materials/MakeHuman_Skin.mat", DefaultSkinTone, 0.35f, SkinVisibilityShaderSetup.ShaderName);
        var eyes = GetOrCreateMaterial($"{GeneratedFolder}/Materials/MakeHuman_Eyes.mat", new Color(0.92f, 0.92f, 0.9f), 0.8f);

        // 5. Avatar prefab = a variant of the canonical FBX model + the MusicGame avatar components.
        var model = CanonicalAvatarModel.LoadModel();
        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            root.name = "MakeHumanAvatar";
            var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
            animator.avatar = CanonicalAvatarModel.LoadAvatar();
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
                    return $"[CanonicalAvatarSetup] The FBX has no '{part.Name}' SkinnedMeshRenderer.";
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
                return "[CanonicalAvatarSetup] Failed to save the prefab.";
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
        EditorUtility.SetDirty(baseAvatar);

        // 7. Minimum content (only created when missing).
        CreateContentIfMissing(baseAvatar);

        // 8. Assets from before the canonical FBX (Unity-generated meshes / Avatar / test walk) are obsolete.
        foreach (var legacy in LegacyGeneratedFolders)
            if (AssetDatabase.IsValidFolder(legacy)) AssetDatabase.DeleteAsset(legacy);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        success = true;
        string report = "[CanonicalAvatarSetup] Imported avatar configured:\n  " + string.Join("\n  ", result.Report);
        Debug.Log(report);
        return report;
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
        if (male == null || female == null) return "[CanonicalAvatarSetup] Configure the imported avatar first (test recipes missing).";

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return "[CanonicalAvatarSetup] Cancelled.";

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
        return $"[CanonicalAvatarSetup] Debug scene saved to {DebugScenePath} — open it and press Play.";
    }

    // ── Batch-mode entry point ───────────────────────────────────────────────────

    /// <summary>Unity.exe -batchmode -projectPath ... -executeMethod CanonicalAvatarSetup.ConfigureFromCommandLine -quit</summary>
    public static void ConfigureFromCommandLine()
    {
        string report = ConfigureImported(out bool success);
        if (success)
        {
            if (MakeHumanAnimationTestSetup.ClipsPresent()) Debug.Log(MakeHumanAnimationTestSetup.Setup(out _));
            Debug.Log(CreateDebugScene());
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

    private static Material GetOrCreateMaterial(string path, Color color, float smoothness, string shaderName = "Universal Render Pipeline/Lit")
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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
            error = "[CanonicalAvatarSetup] No AddressableAssetSettings found — open Window > Asset Management > Addressables > Groups once first.";
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
