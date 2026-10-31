using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Reads and configures an FBX exported by AvatarLab; contains no generation code.</summary>
public static class CanonicalAvatarModel
{
    public const string FbxPath = CanonicalAvatarSetup.GeneratedFolder + "/MakeHuman_Canonical.fbx";
    public const float ExpectedHeight = 1.75f;
    public static Vector3[] GenderEndpoint(string rendererName, bool female)
    {
        var mesh = LoadMesh(rendererName);
        if (mesh == null) throw new System.InvalidOperationException("Missing canonical mesh: " + rendererName);
        var vertices = mesh.vertices;
        if (!female) return vertices;
        int channel = mesh.GetBlendShapeIndex(MorphChannel.Gender.ToString());
        if (channel < 0 || mesh.GetBlendShapeFrameCount(channel) != 1 ||
            Mathf.Abs(mesh.GetBlendShapeFrameWeight(channel, 0) - 100f) > .001f)
            throw new System.InvalidOperationException("Canonical Gender requires one frame at weight 100.");
        var delta = new Vector3[mesh.vertexCount];
        mesh.GetBlendShapeFrameVertices(channel, 0, delta, null, null);
        for (int i = 0; i < vertices.Length; i++) vertices[i] += delta[i];
        return vertices;
    }

    public static bool ConfigureImporter(List<string> report)
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) { report.Add($"No ModelImporter at {FbxPath}."); return false; }

        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;          // the FBX is already in Unity axes (Blender "apply transform")
        importer.importBlendShapes = true;
        importer.importNormals = ModelImporterNormals.Calculate;
        importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
        importer.normalSmoothingAngle = 180f;         // one smooth surface, no creases along UV seams
        importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = true;                   // AvatarFactory clones meshes per instance; tests read vertices
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.weldVertices = true;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.skinWeights = ModelImporterSkinWeights.Standard; // 4 bones, exactly what the bake authored
        importer.optimizeGameObjects = false;         // bones must stay real Transforms: AvatarSkeletonMapper remaps wearables onto them

        // Round-trip through Generic so the importer re-reads the (possibly re-baked) rest skeleton instead
        // of a humanDescription.skeleton cached in the .meta from a previous bake.
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.SaveAndReimport();
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        // Explicit mapping (no guessing) onto the model's own rest pose — which IS the T-pose.
        var description = importer.humanDescription;
        var human = new List<HumanBone>();
        foreach (var (humanName, bone) in MakeHumanHumanoid.BoneMap)
        {
            var hb = new HumanBone { humanName = humanName, boneName = bone };
            hb.limit.useDefaultValues = true;
            human.Add(hb);
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
        importer.humanDescription = description;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(FbxPath);
        bool ok = avatar != null && avatar.isValid && avatar.isHuman;
        report.Add($"FBX import: Humanoid avatar '{(avatar != null ? avatar.name : "<none>")}' valid={ok}, {human.Count} human bones mapped.");
        return ok;
    }

    public static GameObject LoadModel() => AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

    public static Avatar LoadAvatar() => AssetDatabase.LoadAssetAtPath<Avatar>(FbxPath);

    public static Mesh LoadMesh(string partName)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            if (asset is Mesh mesh && mesh.name == partName) return mesh;
        return null;
    }
}
