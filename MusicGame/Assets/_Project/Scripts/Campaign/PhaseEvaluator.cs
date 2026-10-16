using System.Collections.Generic;
using UnityEngine;

/// <summary>Half-star arithmetic — integers only (0..10 = 0..5★), so mastery is never a rounding accident.</summary>
public static class StarMath
{
    public const int Max = 10;

    /// <summary>10 only at/above the threshold; otherwise floor(normalized / threshold × 10), capped at 9.</summary>
    public static int HalfStarsFor(float normalized, float fiveStarThreshold)
    {
        if (float.IsNaN(normalized) || normalized <= 0f) return 0;
        float t = Mathf.Clamp(fiveStarThreshold, 0.01f, 1f);
        if (normalized >= t - 1e-6f) return Max;
        return Mathf.Clamp(Mathf.FloorToInt(normalized / t * Max + 1e-4f), 0, Max - 1);
    }

    /// <summary>Average of two half-star values, rounded DOWN to a half step: 6+10 → 8 (4★), 8+10 → 9 (4.5★), 7+10 → 8.</summary>
    public static int Combine(int a, int b) => (Mathf.Clamp(a, 0, Max) + Mathf.Clamp(b, 0, Max)) / 2;

    /// <summary>Average of many half-star values, rounded down to a half step (internal precision kept by the caller).</summary>
    public static int AverageDown(float exactHalfStars) => Mathf.Clamp(Mathf.FloorToInt(exactHalfStars + 1e-4f), 0, Max);

    public static float ToStars(int halfStars) => halfStars * 0.5f;

    /// <summary>"4,5" / "4.5" style label in the current culture's decimal separator.</summary>
    public static string Label(int halfStars)
    {
        float s = ToStars(halfStars);
        return s % 1f == 0f ? ((int)s).ToString() : s.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
    }
}

/// <summary>One evaluated phase (runner or combat), before it is compared with the stored record.</summary>
public struct PhaseEvaluation
{
    public int Score, MaxScore, HalfStars;
    public float Normalized;
    public bool Valid, Perfect;
    public List<MetricData> Metrics;

    public PhaseRecordData ToRecord(int lap, string songId, string opponentId, int opponentLevel) => new()
    {
        has = true, score = Score, maxScore = MaxScore, normalized = Normalized, halfStars = HalfStars,
        valid = Valid, perfect = Perfect, lap = lap, songId = songId ?? "", opponentId = opponentId ?? "",
        opponentLevel = opponentLevel,
        utcTicks = System.DateTime.UtcNow.Ticks, metrics = Metrics ?? new List<MetricData>(),
    };
}

/// <summary>Per-match combat metrics (one per attempt: reset on VersusIntro, i.e. a new match or Fight
/// Again; accumulated across rounds). Damage is SUMMED as taken — healing never subtracts it.</summary>
public class CombatAttemptMetrics
{
    public bool Won;
    public float DamageTaken;
    public float PlayerMaxHealth = 100f;
    public int RoundsToWin = 2;
    public int RoundsPlayed;
    public int AttacksStarted;
    public int HitsLanded;

    public void Reset(float maxHealth, int roundsToWin)
    {
        Won = false; DamageTaken = 0f; RoundsPlayed = 0; AttacksStarted = 0; HitsLanded = 0;
        PlayerMaxHealth = Mathf.Max(1f, maxHealth);
        RoundsToWin = Mathf.Max(1, roundsToWin);
    }
}

/// <summary>
/// The evaluation FORMULAS (all bounded to the phase's real maximum):
///
/// RUNNER  normalized = clamp01(runner NormalizedScore), i.e. points earned ÷ the maximum achievable for
///         THIS run's actually generated, reachable pickups (perfect timing on every one × no-fall bonus) —
///         already accounts for song length, generation, multipliers and falls, so runs of different songs
///         and difficulties compare by execution. No pickups at all → 0 (no data, never a free 5★).
///         score = round(normalized × runnerMaxPoints).
///
/// COMBAT  won and damage taken == 0  → PERFECT: normalized 1.0 (5★).
///         won otherwise              → min(cap, win·Ww + keptHealth·Wh + accuracy·Wa)   (cap < 5★ threshold)
///         lost                       → lossFactor × (keptHealth·Wh + accuracy·Wa)      (informative, never valid)
///         keptHealth = 1 − clamp01(damage taken ÷ (max health × rounds to win)) — whole match, all rounds
///         accuracy   = clamp01(hits landed ÷ attacks started); neutral 0.5 below the minimum attempts.
///         Every term is a bounded ratio: prolonging the fight or spamming attacks cannot raise it.
///
/// Stars: StarMath.HalfStarsFor with each phase's 5★ threshold.
/// </summary>
public static class PhaseEvaluator
{
    public static PhaseEvaluation EvaluateRunner(float normalizedScore, int musicalAvailable, int musicalCollected,
                                                 float timingAccuracy, int falls, EvaluationConfigSO config)
    {
        config ??= EvaluationConfigSO.Fallback;
        float n = musicalAvailable > 0 && !float.IsNaN(normalizedScore) ? Mathf.Clamp01(normalizedScore) : 0f;
        var e = new PhaseEvaluation
        {
            Normalized = n,
            MaxScore = config.runnerMaxPoints,
            Score = Mathf.RoundToInt(n * config.runnerMaxPoints),
            HalfStars = StarMath.HalfStarsFor(n, config.runnerFiveStarThreshold),
            Valid = true,
            Perfect = n >= 0.9999f,
            Metrics = new List<MetricData>
            {
                new() { key = "Collected", value = musicalCollected, max = musicalAvailable },
                new() { key = "Timing", value = Mathf.Clamp01(timingAccuracy), max = 1f },
                new() { key = "Falls", value = Mathf.Max(0, falls), max = 0f },
                new() { key = "Execution", value = n, max = 1f },
            },
        };
        return e;
    }

    public static PhaseEvaluation EvaluateCombat(CombatAttemptMetrics m, EvaluationConfigSO config)
    {
        config ??= EvaluationConfigSO.Fallback;
        m ??= new CombatAttemptMetrics();
        float budget = Mathf.Max(1f, m.PlayerMaxHealth * Mathf.Max(1, m.RoundsToWin));
        float kept = 1f - Mathf.Clamp01(m.DamageTaken / budget);
        float accuracy = m.AttacksStarted >= Mathf.Max(1, config.combatMinAttacksForAccuracy)
            ? Mathf.Clamp01((float)m.HitsLanded / m.AttacksStarted) : 0.5f;
        bool perfect = m.Won && m.DamageTaken <= 0.0001f;

        float n;
        if (perfect) n = 1f;
        else if (m.Won) n = Mathf.Min(config.combatNonPerfectCap,
                                      config.combatWinWeight + kept * config.combatHealthWeight + accuracy * config.combatAccuracyWeight);
        else n = config.combatLossFactor * (kept * config.combatHealthWeight + accuracy * config.combatAccuracyWeight);
        n = Mathf.Clamp01(n);

        return new PhaseEvaluation
        {
            Normalized = n,
            MaxScore = config.combatMaxPoints,
            Score = Mathf.RoundToInt(n * config.combatMaxPoints),
            HalfStars = StarMath.HalfStarsFor(n, config.combatFiveStarThreshold),
            Valid = m.Won,
            Perfect = perfect,
            Metrics = new List<MetricData>
            {
                new() { key = "Won", value = m.Won ? 1f : 0f, max = 1f },
                new() { key = "HealthKept", value = kept, max = 1f },
                new() { key = "DamageTaken", value = m.DamageTaken, max = budget },
                new() { key = "Accuracy", value = accuracy, max = 1f },
                new() { key = "Hits", value = m.HitsLanded, max = m.AttacksStarted },
            },
        };
    }

    /// <summary>Stable record comparison: higher normalized wins; on a tie (equal within 1e-6) the
    /// stored record is kept. A combat result must be valid (won) to replace anything.</summary>
    public static bool Improves(PhaseRecordData candidate, PhaseRecordData best)
    {
        if (candidate == null || !candidate.has || !candidate.valid) return false;
        if (best == null || !best.has || !best.valid) return true;
        return candidate.normalized > best.normalized + 1e-6f;
    }
}
