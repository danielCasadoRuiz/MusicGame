using UnityEngine;

/// <summary>
/// EVALUATION scale (stars) — separate from XP, Musical Mastery and the runner's own gameplay score.
/// AppConfigSO.evaluation. See PhaseEvaluator for the formulas.
///
/// Stars are stored as HALF-STARS (0..10). halfStars = 10 only when normalized ≥ fiveStarThreshold;
/// below it, halfStars = floor(normalized / fiveStarThreshold × 10) capped at 9 — so 5★ can never come
/// from rounding.
/// </summary>
[CreateAssetMenu(fileName = "EvaluationConfig", menuName = "MusicGame/Campaign/Evaluation Config")]
public class EvaluationConfigSO : ScriptableObject
{
    [Header("Scale (presentation points)")]
    [Min(1)] public int runnerMaxPoints = 5000;
    [Min(1)] public int combatMaxPoints = 5000;

    [Header("Runner")]
    [Tooltip("Normalized runner score (earned / max achievable of THIS run's playable pickups, perfect timing and " +
             "no-fall bonus included) needed for 5★.")]
    [Range(0.5f, 1f)] public float runnerFiveStarThreshold = 0.95f;

    [Header("Combat")]
    [Tooltip("Normalized combat score needed for 5★. Only a PERFECT win (no damage taken in the whole match) reaches it.")]
    [Range(0.5f, 1f)] public float combatFiveStarThreshold = 0.95f;
    [Tooltip("Weight of winning the match.")]
    [Range(0f, 1f)] public float combatWinWeight = 0.4f;
    [Tooltip("Weight of health kept: 1 − damage taken / (max health × rounds to win). Healing never erases damage.")]
    [Range(0f, 1f)] public float combatHealthWeight = 0.4f;
    [Tooltip("Weight of accuracy: hits landed / attacks started by the player (bounded 0..1).")]
    [Range(0f, 1f)] public float combatAccuracyWeight = 0.2f;
    [Tooltip("Non-perfect wins are capped here (just under the 5★ threshold).")]
    [Range(0f, 1f)] public float combatNonPerfectCap = 0.94f;
    [Tooltip("A lost match scores this fraction of its health + accuracy part (informative only: never a record).")]
    [Range(0f, 1f)] public float combatLossFactor = 0.5f;
    [Tooltip("Below this many attacks started, accuracy counts as neutral (0.5) instead of a noisy ratio.")]
    [Min(0)] public int combatMinAttacksForAccuracy = 3;

    public static EvaluationConfigSO Fallback => s_fallback != null ? s_fallback : s_fallback = CreateInstance<EvaluationConfigSO>();
    private static EvaluationConfigSO s_fallback;
}
