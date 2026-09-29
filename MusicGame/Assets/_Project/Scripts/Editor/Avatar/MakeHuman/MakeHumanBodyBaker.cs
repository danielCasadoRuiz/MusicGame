using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The eight deterministic MakeHuman body states MusicGame bakes — every one is AvatarLab's own
/// multilinear macro evaluation (MakeHumanTargetEvaluator) at an exact slider corner, starting from
/// the young/caucasian/average-everything state:
///   MaleBase   = gender 1                MaleSlim   = gender 1, weight 0
///   MaleHeavy  = gender 1, weight 1      MaleMuscle = gender 1, muscle 1
///   (Female*   = the same with gender 0)
/// Every state's MakeHuman HEIGHT slider is then solved so it stands at the same canonical height
/// (see MakeHumanBodyBaker.CanonicalHeight) — MakeHuman's own anthropometric height targets, not a
/// stretch.
/// </summary>
public static class MakeHumanBodyShapes
{
    public static MakeHumanMacroState BaseState(BodyBaseType type) => MakeHumanMacroState.Average(type == BodyBaseType.Male ? 1f : 0f);

    public static MakeHumanMacroState MorphState(MorphChannel channel)
    {
        var s = MakeHumanMacroState.Average(BaseOf(channel) == BodyBaseType.Male ? 1f : 0f);
        switch (channel)
        {
            case MorphChannel.MaleSlim:   case MorphChannel.FemaleSlim:   s.Weight = 0f; break;
            case MorphChannel.MaleHeavy:  case MorphChannel.FemaleHeavy:  s.Weight = 1f; break;
            case MorphChannel.MaleMuscle: case MorphChannel.FemaleMuscle: s.Muscle = 1f; break;
        }
        return s;
    }

    public static BodyBaseType BaseOf(MorphChannel channel) =>
        channel == MorphChannel.MaleSlim || channel == MorphChannel.MaleHeavy || channel == MorphChannel.MaleMuscle
            ? BodyBaseType.Male : BodyBaseType.Female;

    /// <summary>The six Slim/Heavy/Muscle channels (Gender is not a MakeHuman body state of its own —
    /// it's FemaleBase - MaleBase).</summary>
    public static readonly MorphChannel[] AllChannels =
    {
        MorphChannel.MaleSlim,   MorphChannel.MaleHeavy,   MorphChannel.MaleMuscle,
        MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle,
    };
}

/// <summary>One mesh part's vertex data for one evaluated shape.</summary>
public class MakeHumanPartShape
{
    public Vector3[] Vertices;
    public Vector3[] Normals;
    public Vector4[] Tangents;
}

/// <summary>One of the eight baked body states: canonical height, grounded (soles at y = 0), and
/// re-posed into the canonical T-pose rest (see MakeHumanTPose).</summary>
public class MakeHumanBakedState
{
    public string Name;
    public float HeightSlider;          // MakeHuman "height" macro value solved for CanonicalHeight
    public float HeightBeforeNormalize; // measured after the MakeHuman height solve, before the final uniform scale
    public float NormalizeScale;        // final uniform (XYZ) residual scale about the ground point (A-pose)
    public float TPoseScale;            // residual uniform scale after re-posing into the T-pose
    public float Height;                // final measured sole-to-crown height (T-pose)
    public float GroundY;               // final measured lowest body vertex (T-pose, ≈ 0)
    public Vector3[] RawAPose;          // canonical-height raw positions in MakeHuman's A-pose (joint cubes included)
    public Vector3[] Raw;               // the same, re-posed into the T-pose and re-normalized — what ships
    public MakeHumanPartShape[] Parts;  // T-pose mesh-buffer data, same order as MakeHumanSourceMesh.Parts
}

/// <summary>The fully-baked, not-yet-saved result — MakeHumanBodyBuilder writes it to assets.</summary>
public class MakeHumanBakeResult
{
    public MakeHumanSourceMesh Source;
    public MakeHumanRig Rig;
    public MakeHumanBakedState Male;
    public MakeHumanBakedState Female;
    public readonly Dictionary<MorphChannel, MakeHumanBakedState> Morphs = new Dictionary<MorphChannel, MakeHumanBakedState>();

    // ONE canonical skeleton for every body (see MakeHumanBodyBaker's own doc).
    public Vector3[] JointAPose;        // canonical joints in the A-pose (mean of MaleBase/FemaleBase)
    public Vector3[] TailAPose;         // canonical bone tails in the A-pose (rig's own tail definitions)
    public MakeHumanTPose TPose;        // T-pose rest: joint positions + bone rotations (the shipped skeleton)

    public Bounds RendererBounds;       // covers all eight shapes (so every blend of them), padded for animation
    public readonly List<string> Report = new List<string>();

    public MakeHumanBakedState GetBase(BodyBaseType type) => type == BodyBaseType.Male ? Male : Female;

    public IEnumerable<MakeHumanBakedState> AllStates()
    {
        yield return Male;
        yield return Female;
        foreach (var channel in MakeHumanBodyShapes.AllChannels) yield return Morphs[channel];
    }
}

/// <summary>
/// The deterministic MakeHuman -> Unity bake. Pure computation (no AssetDatabase calls) so it can be
/// run, inspected and tested independently of asset writing (MakeHumanBodyBuilder).
///
/// HEIGHT: every one of the eight states is generated at the same CanonicalHeight. First MakeHuman's
/// own height macro is solved per state (bisection on the height slider — anthropometric, not a
/// stretch), then any sub-millimetre residual is removed with a final UNIFORM XYZ scale about the
/// ground point (feet stay on y = 0, proportions preserved). Gender therefore carries no height.
///
/// GEOMETRY (per part mesh; rest vertices = MaleBase):
///   Gender        = FemaleBase - MaleBase
///   MaleX         = MaleX   - MaleBase        (X = Slim / Heavy / Muscle)
///   FemaleX       = FemaleX - FemaleBase
/// which BodyMorphValues.GetMorphWeights weights so that
///   V = lerp(MaleBase + maleMorphs, FemaleBase + femaleMorphs, Gender)   exactly.
/// Normals/tangents get the same deltas (per-shape normals computed seam-free).
///
/// SKELETON: ONE canonical rest skeleton, bindposes and Humanoid Avatar for every body — the mean of
/// MaleBase's and FemaleBase's MakeHuman joints (both at canonical height). The midpoint minimizes the
/// worst-case pivot offset at either Gender endpoint; skinning at rest reproduces every body exactly
/// regardless, so the skeleton choice only affects how joints pivot during animation.
/// </summary>
public static class MakeHumanBodyBaker
{
    public const float CanonicalHeight = 1.75f;           // ≈ MakeHuman's own average young male (1.748 m)
    public const float MinimumMorphDisplacement = 0.001f; // 1 mm — a morph that moves nothing is a data error
    public const float RendererBoundsPadding = 0.5f;
    private const int HeightSolveIterations = 30;

    public static string SourceRoot => Path.Combine(Application.dataPath, "_Project/Avatar/MakeHuman/Source");
    public static string BaseMeshPath => Path.Combine(SourceRoot, "Mesh/base_mesh.txt");
    public static string TargetsFolder => Path.Combine(SourceRoot, "Targets/macrodetails");
    public static string RigPath => Path.Combine(SourceRoot, "Rig/rig.game_engine.json");
    public static string WeightsPath => Path.Combine(SourceRoot, "Rig/weights.game_engine.json");

    /// <summary>Returns null and fills `errors` (each naming the offending source) on ANY topology or
    /// data problem — never continues with partially-valid data.</summary>
    public static MakeHumanBakeResult Bake(List<string> errors)
    {
        var source = MakeHumanSourceMesh.Load(BaseMeshPath);
        source.Validate(errors);
        if (errors.Count > 0) return null;

        var library = MakeHumanTargetLibrary.LoadFolder(TargetsFolder);
        library.Validate(source.RawPositions.Length, errors);

        var rig = MakeHumanRig.Load(RigPath, WeightsPath, source, errors);
        if (errors.Count > 0) return null;

        var evaluator = new MakeHumanTargetEvaluator(library);
        var bodyRaw = BodyRawIndices(source);
        var result = new MakeHumanBakeResult { Source = source, Rig = rig };
        int unityVertices = 0;
        foreach (var part in source.Parts) unityVertices += part.VertexCount;
        result.Report.Add($"Source: MakeHuman {source.BasemeshTag}, {source.RawPositions.Length} raw vertices -> {unityVertices} Unity vertices " +
                          $"({source.Parts.Length} parts), {library.Targets.Count} targets, {rig.Bones.Count} bones.");

        result.Male = BakeState("MaleBase", MakeHumanBodyShapes.BaseState(BodyBaseType.Male), source, rig, evaluator, bodyRaw, errors);
        result.Female = BakeState("FemaleBase", MakeHumanBodyShapes.BaseState(BodyBaseType.Female), source, rig, evaluator, bodyRaw, errors);
        foreach (var channel in MakeHumanBodyShapes.AllChannels)
            result.Morphs[channel] = BakeState(channel.ToString(), MakeHumanBodyShapes.MorphState(channel), source, rig, evaluator, bodyRaw, errors);
        if (errors.Count > 0) return null;

        BuildCanonicalSkeleton(result);

        // Re-pose every state into the canonical T-pose rest with the rig's own skinning, then remove the
        // few-mm height change straightening the legs causes (uniform scale about the ground, per state).
        result.TPose = MakeHumanTPose.Compute(rig, result.JointAPose);
        foreach (var line in result.TPose.Report) result.Report.Add("T-pose " + line);
        float groundSum = 0f, scaleSum = 0f;
        foreach (var state in result.AllStates())
        {
            var reposed = result.TPose.Repose(state.RawAPose, rig);
            float h = MeasureHeight(reposed, bodyRaw, out float minY);
            float k = CanonicalHeight / h;
            for (int i = 0; i < reposed.Length; i++)
                reposed[i] = new Vector3(reposed[i].x * k, (reposed[i].y - minY) * k, reposed[i].z * k);
            state.TPoseScale = k;
            state.Raw = reposed;
            state.Height = MeasureHeight(reposed, bodyRaw, out state.GroundY);
            for (int p = 0; p < source.Parts.Length; p++)
                state.Parts[p] = BuildPartShape(source.Parts[p], reposed, source.RawPositions.Length);
            if (state == result.Male || state == result.Female) { groundSum += minY; scaleSum += k; }
        }
        result.TPose.Normalize(groundSum * 0.5f, scaleSum * 0.5f);

        result.Report.Add($"Canonical height {CanonicalHeight:0.000} m — per state: MakeHuman height slider / A-pose height before final scale / scale / T-pose scale / final T-pose height / ground:");
        foreach (var state in result.AllStates())
            result.Report.Add($"  {state.Name,-13} slider {state.HeightSlider:0.0000}  {state.HeightBeforeNormalize:0.00000} m  x{state.NormalizeScale:0.000000}  T x{state.TPoseScale:0.000000}  -> {state.Height:0.00000} m  ground {state.GroundY * 1000f:+0.000;-0.000} mm");

        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var state in result.AllStates())
            foreach (var part in state.Parts)
                foreach (var v in part.Vertices) bounds.Encapsulate(v);

        // Every body channel must visibly move the body (rebased on its own gender's base).
        foreach (var channel in MakeHumanBodyShapes.AllChannels)
        {
            var owner = MakeHumanBodyShapes.BaseOf(channel) == BodyBaseType.Male ? result.Male : result.Female;
            float max = MaxDisplacement(result.Morphs[channel].Parts[0].Vertices, owner.Parts[0].Vertices);
            if (max < MinimumMorphDisplacement)
                errors.Add($"Morph '{channel}' moves no body vertex more than {MinimumMorphDisplacement * 1000f:0} mm from {MakeHumanBodyShapes.BaseOf(channel)}Base — its source targets are missing or empty.");
            result.Report.Add($"Morph {channel}: rebased on {MakeHumanBodyShapes.BaseOf(channel)}Base, max body displacement {max * 100f:0.0} cm.");
        }
        result.Report.Add($"Morph Gender: FemaleBase - MaleBase, max body displacement {MaxDisplacement(result.Female.Parts[0].Vertices, result.Male.Parts[0].Vertices) * 100f:0.0} cm.");
        foreach (var part in source.Parts)
        {
            int unweighted = rig.CountUnweighted(part);
            if (unweighted > 0) result.Report.Add($"WARNING: part '{part.Name}': {unweighted} vertices had no rig weights and are bound 100% to '{rig.RootBoneName}'.");
        }
        if (errors.Count > 0) return null;

        bounds.Expand(RendererBoundsPadding * 2f);
        result.RendererBounds = bounds;
        return result;
    }

    /// <summary>Solves MakeHuman's own height macro so `state` stands at CanonicalHeight, grounds it,
    /// then removes any residual with a uniform XYZ scale about the ground point.</summary>
    private static MakeHumanBakedState BakeState(string name, MakeHumanMacroState state, MakeHumanSourceMesh source, MakeHumanRig rig,
                                                 MakeHumanTargetEvaluator evaluator, int[] bodyRaw, List<string> errors)
    {
        // Bisection on the height slider (MakeHuman's height targets are monotonic in height).
        float lo = 0f, hi = 1f;
        float heightAt(float slider)
        {
            var s = state; s.Height = slider;
            return MeasureHeight(evaluator.Evaluate(s, source.RawPositions), bodyRaw, out _);
        }
        if (heightAt(hi) < CanonicalHeight || heightAt(lo) > CanonicalHeight)
            errors.Add($"State '{name}': MakeHuman's height range can't reach {CanonicalHeight} m — only the uniform normalization would apply (refusing to stretch that far).");
        for (int i = 0; i < HeightSolveIterations; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (heightAt(mid) < CanonicalHeight) lo = mid; else hi = mid;
        }

        var solved = state;
        solved.Height = (lo + hi) * 0.5f;
        var raw = evaluator.Evaluate(solved, source.RawPositions);
        ValidateShape(name, raw, source, errors);

        float measured = MeasureHeight(raw, bodyRaw, out float minY);
        var groundJoint = rig.ResolveJoints(raw)[0]; // root bone "Root" head = MakeHuman's joint-ground
        var ground = new Vector3(groundJoint.x, minY, groundJoint.z);
        float scale = CanonicalHeight / measured;

        var normalized = new Vector3[raw.Length];
        for (int i = 0; i < raw.Length; i++) normalized[i] = (raw[i] - ground) * scale; // uniform XYZ about the ground point

        var baked = new MakeHumanBakedState
        {
            Name = name,
            HeightSlider = solved.Height,
            HeightBeforeNormalize = measured,
            NormalizeScale = scale,
            RawAPose = normalized,
            Parts = new MakeHumanPartShape[source.Parts.Length],
        };
        return baked;
    }

    /// <summary>ONE canonical skeleton: the mean of MaleBase's and FemaleBase's MakeHuman joints (and
    /// bone tails), both at canonical height, in the A-pose — then re-posed to the T-pose rest.</summary>
    private static void BuildCanonicalSkeleton(MakeHumanBakeResult result)
    {
        var rig = result.Rig;
        var raw = new Vector3[result.Male.RawAPose.Length];
        for (int i = 0; i < raw.Length; i++) raw[i] = (result.Male.RawAPose[i] + result.Female.RawAPose[i]) * 0.5f;
        result.JointAPose = rig.ResolveJoints(raw);
        result.TailAPose = rig.ResolveTails(raw);

        var male = rig.ResolveJoints(result.Male.RawAPose);
        var female = rig.ResolveJoints(result.Female.RawAPose);
        float maxOffset = 0f;
        string maxBone = "";
        for (int i = 0; i < male.Length; i++)
        {
            float offset = (male[i] - female[i]).magnitude * 0.5f;
            if (offset > maxOffset) { maxOffset = offset; maxBone = rig.Bones[i].Name; }
        }
        result.Report.Add($"Canonical skeleton = mean of MaleBase/FemaleBase joints; largest endpoint pivot offset {maxOffset * 100f:0.0} cm ('{maxBone}').");
    }

    private static float MaxDisplacement(Vector3[] a, Vector3[] b)
    {
        float max = 0f;
        for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, (a[i] - b[i]).magnitude);
        return max;
    }

    private static int[] BodyRawIndices(MakeHumanSourceMesh source)
    {
        var set = new HashSet<int>(source.Parts[0].MeshToRawIndex);
        var result = new int[set.Count];
        set.CopyTo(result);
        return result;
    }

    /// <summary>Sole-to-crown height of the rendered BODY (helper/joint geometry excluded).</summary>
    public static float MeasureHeight(Vector3[] raw, int[] bodyRaw, out float minY)
    {
        minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (int i in bodyRaw)
        {
            float y = raw[i].y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return maxY - minY;
    }

    private static MakeHumanPartShape BuildPartShape(MakeHumanMeshPart part, Vector3[] raw, int rawCount)
    {
        var shape = new MakeHumanPartShape { Vertices = part.ToMeshPositions(raw, Vector3.zero) };
        shape.Normals = part.ComputeSeamlessNormals(shape.Vertices, rawCount);
        shape.Tangents = ComputeTangents(part, shape.Vertices, shape.Normals);
        return shape;
    }

    private static Vector4[] ComputeTangents(MakeHumanMeshPart part, Vector3[] vertices, Vector3[] normals)
    {
        var temp = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        temp.vertices = vertices;
        temp.uv = part.UVs;
        temp.triangles = part.Triangles;
        temp.normals = normals;
        temp.RecalculateTangents();
        var tangents = temp.tangents;
        Object.DestroyImmediate(temp);
        return tangents;
    }

    private static void ValidateShape(string label, Vector3[] raw, MakeHumanSourceMesh source, List<string> errors)
    {
        if (raw.Length != source.RawPositions.Length)
        {
            errors.Add($"Shape '{label}' has {raw.Length} raw vertices but the base mesh has {source.RawPositions.Length} — incompatible topology.");
            return;
        }
        foreach (var v in raw)
        {
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
            {
                errors.Add($"Shape '{label}' produced NaN/Infinity vertex positions.");
                return;
            }
        }
    }
}
