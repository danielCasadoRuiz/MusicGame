using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects dynamic events and structure:
/// crescendo/decrescendo zones, buildups, drops, impacts, silences, breaks.
/// Requires TimbralAnalyzer to have run first (reads profile.intensity).
/// </summary>
public class DynamicsAnalyzer : IOfflineAnalyzer
{
    public bool IsEnabled(AudioAnalysisConfig config)
        => config.advancedEnabled && config.advancedDynamics;

    public void Analyze(OfflineAnalysisContext ctx, SongProfile profile)
    {
        int n = ctx.NumFrames;
        if (n < 4 || profile.intensity == null) return;

        float[] energy    = ctx.EnergyEnvelope;
        float[] intensity = profile.intensity;  // 0..1, from TimbralAnalyzer
        float   hopTime   = ctx.HopTime;
        float   avgE      = ctx.AvgEnergy;
        float   cfg_silence = ctx.Config.silenceThreshold;
        float   cfg_break   = ctx.Config.breakThreshold;
        float   cfg_impact  = ctx.Config.impactThreshold;

        // ── Energy gradient (dE/dt, normalized) ──────────────────────────────
        var gradient = new float[n];
        float maxGrad = 1e-6f;
        for (int f = 1; f < n - 1; f++)
        {
            gradient[f] = (energy[f + 1] - energy[f - 1]) * 0.5f;
            if (Mathf.Abs(gradient[f]) > maxGrad) maxGrad = Mathf.Abs(gradient[f]);
        }
        gradient[0] = gradient[1];
        gradient[n - 1] = gradient[n - 2];
        for (int f = 0; f < n; f++) gradient[f] /= maxGrad; // normalize to [-1, 1]
        AdvancedFFTFeatures.BoxSmooth(gradient, 9);
        profile.energyGradient = gradient;

        // ── Buildup curve (sustained positive momentum) ───────────────────────
        // Look-back window: how much energy has risen in the last buildupMinLength seconds
        var buildupCurve = new float[n];
        int lookback = Mathf.Max(1, Mathf.RoundToInt(ctx.Config.buildupMinLength / hopTime));
        for (int f = lookback; f < n; f++)
        {
            float start = intensity[f - lookback];
            float end   = intensity[f];
            float delta = end - start;
            // Positive delta normalized by lookback = average slope
            buildupCurve[f] = Mathf.Clamp01(delta / 0.5f + 0.5f) - 0.5f; // centre at 0
            buildupCurve[f] = Mathf.Clamp01(buildupCurve[f] * 2f); // rescale 0..1
        }
        AdvancedFFTFeatures.BoxSmooth(buildupCurve, 11);
        profile.buildupCurve = buildupCurve;

        // ── Impact times (strong local peaks in intensity) ────────────────────
        int minGap = Mathf.Max(1, Mathf.RoundToInt(0.4f / hopTime));
        var impacts = new List<float>();
        for (int f = minGap; f < n - minGap; f++)
        {
            if (intensity[f] < cfg_impact) continue;
            bool isMax = true;
            for (int j = f - minGap; j <= f + minGap && isMax; j++)
                if (j != f && intensity[j] >= intensity[f]) isMax = false;
            if (isMax) impacts.Add(f * hopTime);
        }
        profile.impactTimes = impacts.ToArray();

        // ── Drop times (sudden energy decrease or increase after calm) ─────────
        var drops = new List<float>();
        int dropLook = Mathf.Max(2, Mathf.RoundToInt(0.25f / hopTime));
        for (int f = dropLook; f < n - dropLook; f++)
        {
            float before = intensity[f - dropLook];
            float after  = intensity[f];
            float delta  = after - before;

            bool suddenDrop = delta < -0.35f && before > 0.5f;       // release
            bool suddenHit  = delta >  0.40f && before < 0.25f;      // EDM drop
            if (suddenDrop || suddenHit) drops.Add(f * hopTime);
        }
        profile.dropTimes = drops.ToArray();

        // ── Buildup start times (where a buildup leading to impact begins) ─────
        var buildupStarts = new List<float>();
        foreach (float impactT in profile.impactTimes)
        {
            int impactF   = ctx.TimeToFrame(impactT);
            int lookbackF = Mathf.Max(0, impactF - Mathf.RoundToInt(8f / hopTime));

            // Walk back from impact until buildup curve drops below 0.15
            int startF = impactF;
            for (int f = impactF; f >= lookbackF; f--)
            {
                if (buildupCurve[f] < 0.15f) { startF = f + 1; break; }
                if (f == lookbackF) startF = f;
            }
            float startT = startF * hopTime;
            if (impactT - startT >= ctx.Config.buildupMinLength * 0.5f)
                buildupStarts.Add(startT);
        }
        profile.buildupStartTimes = buildupStarts.ToArray();

        // ── Zone detection (silence, break, crescendo, decrescendo) ────────────
        profile.silenceZones    = DetectZones(energy,    n, hopTime, avgE * cfg_silence,    0.5f);
        profile.breakZones      = DetectZones(energy,    n, hopTime, avgE * cfg_break,       2f);
        profile.crescendoZones  = DetectGradientZones(gradient, n, hopTime,  0.15f, 2.5f);
        profile.decrescendoZones= DetectGradientZones(gradient, n, hopTime, -0.15f, 2.5f);
    }

    // Finds contiguous regions where arr[f] < threshold, with minimum duration
    private static TimeRange[] DetectZones(float[] arr, int n, float hopTime,
                                           float threshold, float minDuration)
    {
        var zones = new List<TimeRange>();
        int minF  = Mathf.Max(1, Mathf.RoundToInt(minDuration / hopTime));
        int start = -1;
        for (int f = 0; f <= n; f++)
        {
            bool below = f < n && arr[f] < threshold;
            if (below && start < 0) start = f;
            else if (!below && start >= 0)
            {
                if (f - start >= minF)
                    zones.Add(new TimeRange { start = start * hopTime, end = f * hopTime });
                start = -1;
            }
        }
        return zones.ToArray();
    }

    // Finds sustained positive (dir>0) or negative (dir<0) gradient regions
    private static TimeRange[] DetectGradientZones(float[] gradient, int n, float hopTime,
                                                   float threshold, float minDuration)
    {
        var zones  = new List<TimeRange>();
        int minF   = Mathf.Max(1, Mathf.RoundToInt(minDuration / hopTime));
        bool positive = threshold > 0f;
        int  start = -1;

        for (int f = 0; f <= n; f++)
        {
            bool active = f < n && (positive ? gradient[f] >= threshold : gradient[f] <= threshold);
            if (active && start < 0) start = f;
            else if (!active && start >= 0)
            {
                if (f - start >= minF)
                    zones.Add(new TimeRange { start = start * hopTime, end = f * hopTime });
                start = -1;
            }
        }
        return zones.ToArray();
    }
}
