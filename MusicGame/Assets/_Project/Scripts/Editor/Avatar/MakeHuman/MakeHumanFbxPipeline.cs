using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// MakeHuman bake -> MakeHuman_Canonical.fbx -> Unity ModelImporter (Humanoid, Create From This Model).
///
///   1. ExportJson: the bake (T-pose bodies, canonical skeleton, weights, UVs, 7 morphs) as plain data.
///   2. RunBlender: Source/Blender/build_canonical_fbx.py turns it into one clean FBX (headless Blender).
///   3. ConfigureImporter: Humanoid rig, avatar created FROM THIS MODEL with an explicit bone map;
///      blendshapes + calculated normals/tangents; readable meshes (instances clone them at runtime).
///
/// The FBX is the canonical, inspectable model asset (Model / Rig / Animation / Materials, and
/// Rig > Configure...). Its imported meshes, skeleton and "MakeHuman_CanonicalAvatar" are what the
/// avatar prefab, AvatarFactory and Fight use.
/// </summary>
public static class MakeHumanFbxPipeline
{
    public const string FbxPath = MakeHumanBodyBuilder.GeneratedFolder + "/MakeHuman_Canonical.fbx";
    public const string BlenderPathPref = "MusicGame.BlenderPath";
    public static string ScriptPath => Path.Combine(MakeHumanBodyBaker.SourceRoot, "Blender/build_canonical_fbx.py");
    public static string JsonPath => Path.GetFullPath("Library/MakeHumanFbx/canonical.json");

    public static readonly (MorphChannel channel, string name)[] ShapeKeys =
    {
        (MorphChannel.Gender, "Gender"),
        (MorphChannel.MaleSlim, "MaleSlim"), (MorphChannel.MaleHeavy, "MaleHeavy"), (MorphChannel.MaleMuscle, "MaleMuscle"),
        (MorphChannel.FemaleSlim, "FemaleSlim"), (MorphChannel.FemaleHeavy, "FemaleHeavy"), (MorphChannel.FemaleMuscle, "FemaleMuscle"),
    };

    // ── 1. Data export ─────────────────────────────────────────────────────────────

    public static void ExportJson(MakeHumanBakeResult bake, string path)
    {
        var sb = new StringBuilder(1 << 24);
        var rig = bake.Rig;
        var pose = bake.TPose;
        sb.Append("{\"tposeScale\":").Append(F(bake.Male.TPoseScale * 0.5f + bake.Female.TPoseScale * 0.5f));
        sb.Append(",\"bones\":[");
        for (int i = 0; i < rig.Bones.Count; i++)
        {
            var b = rig.Bones[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"name\":\"").Append(b.Name).Append("\",\"parent\":").Append(b.Parent == null ? "null" : $"\"{b.Parent}\"");
            sb.Append(",\"headA\":"); V(sb, bake.JointAPose[i]);
            sb.Append(",\"tailA\":"); V(sb, bake.TailAPose[i]);
            sb.Append(",\"roll\":").Append(F(b.Roll));
            var q = pose.Rotation[i];
            sb.Append(",\"rotation\":[").Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w)).Append(']');
            sb.Append(",\"headT\":"); V(sb, pose.Position[i]);
            sb.Append('}');
        }
        sb.Append("],\"parts\":[");

        for (int p = 0; p < bake.Source.Parts.Length; p++)
        {
            var part = bake.Source.Parts[p];
            // Blender mesh = the part's RAW MakeHuman vertices (merged, as in base.obj); UVs per corner.
            var rawList = new List<int>(new HashSet<int>(part.MeshToRawIndex));
            rawList.Sort();
            var local = new Dictionary<int, int>();
            for (int i = 0; i < rawList.Count; i++) local[rawList[i]] = i;

            if (p > 0) sb.Append(',');
            sb.Append("{\"name\":\"").Append(part.Name).Append("\",\"positions\":");
            Raw(sb, bake.Male.Raw, rawList);
            sb.Append(",\"triangles\":[");
            for (int t = 0; t < part.Triangles.Length; t++)
            {
                if (t > 0) sb.Append(',');
                sb.Append(local[part.MeshToRawIndex[part.Triangles[t]]]);
            }
            sb.Append("],\"uvs\":[");
            for (int t = 0; t < part.Triangles.Length; t++)
            {
                var uv = part.UVs[part.Triangles[t]];
                if (t > 0) sb.Append(',');
                sb.Append(F(uv.x)).Append(',').Append(F(uv.y));
            }
            sb.Append("],\"weights\":[");
            for (int i = 0; i < rawList.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[');
                var influences = rig.RawTopInfluences(rawList[i]);
                for (int k = 0; k < influences.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(influences[k].bone).Append(',').Append(F(influences[k].weight));
                }
                sb.Append(']');
            }
            sb.Append("],\"shapes\":{");
            for (int s = 0; s < ShapeKeys.Length; s++)
            {
                if (s > 0) sb.Append(',');
                sb.Append('"').Append(ShapeKeys[s].name).Append("\":");
                Raw(sb, ShapeKeyAbsolute(bake, ShapeKeys[s].channel), rawList);
            }
            sb.Append("}}");
        }
        sb.Append("]}");

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Absolute shape-key positions (FBX blendshapes are absolute; Unity re-derives deltas
    /// against the basis = MaleBase): Gender = FemaleBase; MaleX = MaleX; FemaleX = MaleBase + (FemaleX -
    /// FemaleBase), i.e. the female channels stay rebased on FemaleBase.</summary>
    public static Vector3[] ShapeKeyAbsolute(MakeHumanBakeResult bake, MorphChannel channel)
    {
        if (channel == MorphChannel.Gender) return bake.Female.Raw;
        var shape = bake.Morphs[channel].Raw;
        if (MakeHumanBodyShapes.BaseOf(channel) == BodyBaseType.Male) return shape;
        var result = new Vector3[shape.Length];
        for (int i = 0; i < shape.Length; i++) result[i] = bake.Male.Raw[i] + (shape[i] - bake.Female.Raw[i]);
        return result;
    }

    private static void Raw(StringBuilder sb, Vector3[] raw, List<int> indices)
    {
        sb.Append('[');
        for (int i = 0; i < indices.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var v = raw[indices[i]];
            sb.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z));
        }
        sb.Append(']');
    }

    private static void V(StringBuilder sb, Vector3 v) => sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(']');

    private static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    // ── 2. Blender ─────────────────────────────────────────────────────────────────

    public static string FindBlender()
    {
        string configured = EditorPrefs.GetString(BlenderPathPref, "");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        string root = @"C:\Program Files\Blender Foundation";
        if (!Directory.Exists(root)) return null;
        var candidates = new List<string>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            string exe = Path.Combine(dir, "blender.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }
        candidates.Sort(System.StringComparer.OrdinalIgnoreCase);
        return candidates.Count > 0 ? candidates[candidates.Count - 1] : null; // newest version folder
    }

    public static bool RunBlender(string jsonPath, string fbxFullPath, out string log)
    {
        string blender = FindBlender();
        if (blender == null)
        {
            log = $"Blender not found — install Blender 4.x or set EditorPrefs '{BlenderPathPref}' to blender.exe.";
            return false;
        }
        var info = new ProcessStartInfo(blender,
            $"-b --factory-startup --python \"{ScriptPath}\" -- \"{jsonPath}\" \"{fbxFullPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        log = $"[{blender}] exit {process.ExitCode}\n{stdout}\n{stderr}";
        return process.ExitCode == 0 && File.Exists(fbxFullPath) && stdout.Contains("[build_canonical_fbx] wrote");
    }

    // ── 3. Unity import ────────────────────────────────────────────────────────────

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
