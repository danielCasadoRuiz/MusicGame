using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MakeHuman macro slider state — ported from AvatarLab's AvatarDefinition (renamed: MusicGame
/// already has its own gameplay-facing "definition" types). All values 0..1, MakeHuman's own
/// convention: 0.5 = the "average" corner.
/// </summary>
public struct MakeHumanMacroState
{
    public float Gender;          // 0 = female, 1 = male
    public float Weight;
    public float Muscle;
    public float Height;
    public float BodyProportions;
    public float Age;             // 0.5 == pure "young"
    public float RaceCaucasian;
    public float RaceAsian;
    public float RaceAfrican;

    /// <summary>Young, caucasian, average everything — the fixed corner every MusicGame bake starts
    /// from; only Gender/Weight/Muscle are then pushed to their extremes (see MakeHumanBodyShapes).</summary>
    public static MakeHumanMacroState Average(float gender) => new MakeHumanMacroState
    {
        Gender = gender,
        Weight = 0.5f,
        Muscle = 0.5f,
        Height = 0.5f,
        BodyProportions = 0.5f,
        Age = 0.5f,
        RaceCaucasian = 1f,
    };
}

/// <summary>
/// Reproduces MakeHuman's macrodetails axis system (makehuman lib/targets.py + apps/human.py
/// _setXVals) — ported verbatim from AvatarLab's MakeHumanMacroAxes. Each axis owns a fixed set of
/// named corners whose weights are non-negative and sum to 1; a target's weight is the PRODUCT of
/// its corners' per-axis weights (see MakeHumanTargetEvaluator).
/// </summary>
public static class MakeHumanMacroAxes
{
    public static Dictionary<string, float> ComputeCornerWeights(MakeHumanMacroState state)
    {
        var weights = new Dictionary<string, float>(24);

        float gender = Clamp01(state.Gender);
        weights["male"] = gender;
        weights["female"] = 1f - gender;

        // Age: 4-corner piecewise-linear tent, breakpoints at 0 / 0.1875 / 0.5 / 1.0.
        float age = Clamp01(state.Age);
        float babyVal, childVal, youngVal, oldVal;
        if (age < 0.5f)
        {
            oldVal = 0f;
            babyVal = Max0(1f - age * 5.333f);
            youngVal = Max0((age - 0.1875f) * 3.2f);
            childVal = Max0(Min1(5.333f * age) - youngVal);
        }
        else
        {
            childVal = 0f;
            babyVal = 0f;
            oldVal = Max0(age * 2f - 1f);
            youngVal = 1f - oldVal;
        }
        weights["baby"] = babyVal;
        weights["child"] = childVal;
        weights["young"] = youngVal;
        weights["old"] = oldVal;

        float caucasian = Clamp01(state.RaceCaucasian);
        float asian = Clamp01(state.RaceAsian);
        float african = Clamp01(state.RaceAfrican);
        float raceSum = caucasian + asian + african;
        if (raceSum <= 0f) { caucasian = 1f; asian = 0f; african = 0f; raceSum = 1f; }
        weights["caucasian"] = caucasian / raceSum;
        weights["asian"] = asian / raceSum;
        weights["african"] = african / raceSum;

        AddSymmetricTent(weights, "minmuscle", "averagemuscle", "maxmuscle", state.Muscle);
        AddSymmetricTent(weights, "minweight", "averageweight", "maxweight", state.Weight);
        AddSymmetricTent(weights, "minheight", "averageheight", "maxheight", state.Height);
        AddSymmetricTent(weights, "uncommonproportions", "regularproportions", "idealproportions", state.BodyProportions);

        return weights;
    }

    /// <summary>Symmetric 3-corner tent around 0.5 (human.py _setWeightVals):
    /// max = max(0, v*2-1); min = max(0, 1-v*2); mid = 1-(max+min).</summary>
    private static void AddSymmetricTent(Dictionary<string, float> weights, string minName, string midName, string maxName, float value)
    {
        float v = Clamp01(value);
        float maxVal = Max0(v * 2f - 1f);
        float minVal = Max0(1f - v * 2f);
        weights[minName] = minVal;
        weights[midName] = 1f - (maxVal + minVal);
        weights[maxName] = maxVal;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    private static float Max0(float v) => v > 0f ? v : 0f;
    private static float Min1(float v) => v < 1f ? v : 1f;
}

/// <summary>
/// MakeHuman's own blending model (humanmodifier.py getTargetWeights / human.py applyAllTargets) —
/// ported from AvatarLab's MakeHumanTargetRuntime:
///   position = base + Σ cornerWeight_i * delta_i,   cornerWeight_i = Π per-axis weight
/// i.e. true multilinear interpolation between corner targets. Always starts from a COPY of the
/// pristine base positions, so evaluating any state never depends on a previous evaluation.
/// </summary>
public class MakeHumanTargetEvaluator
{
    private readonly MakeHumanTargetLibrary _library;

    public MakeHumanTargetEvaluator(MakeHumanTargetLibrary library) => _library = library;

    public Vector3[] Evaluate(MakeHumanMacroState state, Vector3[] baseRawPositions)
    {
        var axisWeights = MakeHumanMacroAxes.ComputeCornerWeights(state);

        var result = (Vector3[])baseRawPositions.Clone();
        foreach (var target in _library.Targets)
        {
            float weight = CornerWeight(target, axisWeights);
            if (weight <= 0.0001f) continue;

            int[] indices = target.VertexIndices;
            Vector3[] deltas = target.Deltas;
            for (int i = 0; i < indices.Length; i++) result[indices[i]] += deltas[i] * weight;
        }
        return result;
    }

    private static float CornerWeight(MakeHumanTarget target, Dictionary<string, float> axisWeights)
    {
        float weight = 1f;
        foreach (string token in target.CornerTokens)
        {
            if (!axisWeights.TryGetValue(token, out float axisWeight) || axisWeight <= 0f) return 0f;
            weight *= axisWeight;
        }
        return weight;
    }
}
