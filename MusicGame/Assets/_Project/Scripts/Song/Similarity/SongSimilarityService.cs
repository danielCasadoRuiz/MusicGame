using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONLY place songs are compared. Deterministic, no ML:
///   1. standardise every dimension over the whole playable catalog's vectors (z-score, std floored so an
///      almost-constant dimension isn't amplified into noise, clamped to ±3);
///   2. weight the two blocks — song features 60 %, musicnn tags 40 % of the total (each dimension
///      scaled by sqrt(blockWeight / blockSize), so a 50-tag block can't drown 16 features);
///   3. cosine similarity of the weighted vectors (higher = more similar, −1..1).
/// Centering before cosine matters: raw 0..1 vectors are all "positive" and would look alike.
/// </summary>
public static class SongSimilarityService
{
    public const float FeatureBlockWeight = 0.6f;
    public const float TagBlockWeight     = 0.4f;
    private const float MinStd = 0.05f;
    private const float MaxZ   = 3f;

    public static float Similarity(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na <= 1e-12 || nb <= 1e-12 ? 0f : (float)(dot / System.Math.Sqrt(na * nb));
    }

    /// <summary>Per-dimension mean/std over `reference` (the whole playable catalog's vectors — stable
    /// across a run, so a route does not shift just because songs got completed).</summary>
    public static (float[] mean, float[] std) Stats(IReadOnlyList<SongSimilarityVector> reference)
    {
        int nf = SongSimilarityVector.FeatureNames.Length, dims = nf + SentisMusicTagger.TagLabels.Count;
        var mean = new float[dims]; var std = new float[dims];
        int n = reference.Count;
        for (int d = 0; d < dims; d++)
        {
            double s = 0, sq = 0;
            foreach (var v in reference) { float x = Dim(v, d, nf); s += x; sq += x * x; }
            double m = n > 0 ? s / n : 0;
            mean[d] = (float)m;
            std[d] = Mathf.Max(MinStd, n > 0 ? (float)System.Math.Sqrt(System.Math.Max(0, sq / n - m * m)) : 1f);
        }
        return (mean, std);
    }

    /// <summary>Standardised (z, clamped) + block-weighted copy of one vector.</summary>
    public static float[] Prepare(SongSimilarityVector v, (float[] mean, float[] std) stats)
    {
        int nf = SongSimilarityVector.FeatureNames.Length, nt = SentisMusicTagger.TagLabels.Count;
        float wf = Mathf.Sqrt(FeatureBlockWeight / nf), wt = Mathf.Sqrt(TagBlockWeight / nt);
        var r = new float[nf + nt];
        for (int d = 0; d < r.Length; d++)
            r[d] = Mathf.Clamp((Dim(v, d, nf) - stats.mean[d]) / stats.std[d], -MaxZ, MaxZ) * (d < nf ? wf : wt);
        return r;
    }

    private static float Dim(SongSimilarityVector v, int d, int nf) => d < nf ? v.features[d] : v.tags[d - nf];

    /// <summary>
    /// Ordered route: the seed first (if it is among `candidates`), then every other candidate by
    /// similarity to the seed (desc; ties by ordinal id). Candidates without a vector — or every
    /// candidate when the seed itself has none — follow in ordinal id order (stable fallback).
    /// `statsReference` = every available catalog vector (normalisation statistics).
    /// </summary>
    public static List<string> BuildRoute(string seedId, IReadOnlyList<string> candidates,
                                          System.Func<string, SongSimilarityVector> vectorOf,
                                          IReadOnlyList<SongSimilarityVector> statsReference)
    {
        var route = new List<string>();
        if (candidates == null) return route;

        var seedVec = vectorOf(seedId);
        var withVec = new List<SongSimilarityVector>();
        var without = new List<string>();
        foreach (var id in candidates)
        {
            if (id == seedId) continue;
            var v = vectorOf(id);
            if (v != null && seedVec != null) withVec.Add(v); else without.Add(id);
        }

        foreach (var id in candidates) if (id == seedId) { route.Add(seedId); break; }

        if (seedVec != null && withVec.Count > 0)
        {
            var stats = Stats(statsReference != null && statsReference.Count > 1 ? statsReference
                                                                                 : new List<SongSimilarityVector>(withVec) { seedVec });
            var seedPrepared = Prepare(seedVec, stats);
            var scored = new List<(string id, float sim)>(withVec.Count);
            foreach (var v in withVec) scored.Add((v.songId, Similarity(seedPrepared, Prepare(v, stats))));
            scored.Sort((x, y) => y.sim != x.sim ? y.sim.CompareTo(x.sim) : string.CompareOrdinal(x.id, y.id));
            foreach (var s in scored) route.Add(s.id);
        }

        without.Sort(string.CompareOrdinal);
        route.AddRange(without);
        return route;
    }
}
