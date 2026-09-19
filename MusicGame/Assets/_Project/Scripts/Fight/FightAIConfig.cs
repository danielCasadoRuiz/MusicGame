using UnityEngine;

/// <summary>
/// The few tunables shared by EVERY FighterAI instance, regardless of AIDifficultyProfile — things
/// that aren't "how skilled is this rival" but "how does the AI layer itself behave" (see each
/// field's own doc). Same "no numbers hardcoded in a controller" rule as every other Fight*Config.
/// </summary>
[CreateAssetMenu(fileName = "FightAIConfig", menuName = "MusicGame/Fight/AI Config")]
public class FightAIConfig : ScriptableObject
{
    [Header("Reaction")]
    [Tooltip("Hard floor under AIDifficultyProfile.reactionTime — even a very low-reactionTime " +
             "Hard profile never re-decides faster than this (task's own explicit \"fins i tot Hard " +
             "ha de tenir una reaction time humana mínima\" requirement).")]
    public float minReactionTime = 0.08f;

    [Header("Random")]
    [Tooltip("0 = each FighterAI seeds itself from Unity's own global random state (non-reproducible " +
             "run to run). Nonzero seeds every FighterAI's own private System.Random identically, so " +
             "a specific match's AI decisions can be reproduced for debugging.")]
    public int debugSeed = 0;

    [Header("Spacing")]
    [Tooltip("Added on top of a move's own reach when judging \"close enough to attack\".")]
    public float meleeRangePadding = 0.4f;
    [Tooltip("Preferred distance is roughly (melee range + this) — close enough to threaten, far " +
             "enough to react.")]
    public float idealDistancePadding = 0.6f;
    [Tooltip("Maximum random error added to every spacing judgement at spacingAccuracy = 0 — shrinks " +
             "linearly to 0 at spacingAccuracy = 1 (see FighterAI's own doc).")]
    public float maxSpacingError = 1.5f;

    [Header("Punish")]
    [Tooltip("An opponent's Recovery phase only counts as a punishable opening while its own " +
             "PhaseProgress01 is BELOW this — deep into Recovery, there usually isn't enough of the " +
             "window left to actually land something.")]
    [Range(0f, 1f)] public float punishWindowMaxProgress = 0.6f;
}
