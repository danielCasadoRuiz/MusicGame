using UnityEngine;

/// <summary>
/// A rival's combat behavior tuning for a given OpponentLevelConfig — data only, NOT consumed by
/// any AI yet (no combat/AI system exists this phase). Shareable across multiple opponents/levels
/// (e.g. a single "Easy" asset reused by several early-game rivals) since OpponentLevelConfig
/// references it rather than embedding it.
///
/// Fields are illustrative of what a future AI will likely need, not a committed final shape —
/// expect this to grow/change once real combat exists to actually read it.
/// </summary>
[CreateAssetMenu(fileName = "AIDifficultyProfile", menuName = "MusicGame/Fight/AI Difficulty Profile")]
public class AIDifficultyProfile : ScriptableObject
{
    [Tooltip("Seconds of simulated delay before reacting to a player action.")]
    public float reactionTime = 0.3f;

    [Range(0f, 1f)] public float aggression         = 0.5f;
    [Range(0f, 1f)] public float defenseProbability = 0.5f;
    [Range(0f, 1f)] public float punishSkill        = 0.5f;
    [Range(0f, 1f)] public float comboSkill         = 0.5f;
    [Range(0f, 1f)] public float spacingAccuracy    = 0.5f;
    [Range(0f, 1f)] public float errorRate          = 0.2f;
    [Range(0f, 1f)] public float specialUsage       = 0.5f;
}
