using UnityEngine;

/// <summary>
/// Central entry point for Music Runner gameplay configuration — deliberately thin: it holds
/// only references to the specialized sub-configs below, each with a single, cohesive
/// responsibility. Replaces the old monolithic GameplayConfig "god ScriptableObject".
///
/// Consumers that genuinely need only one sub-domain take that sub-config type directly
/// (e.g. PlayerController takes MusicRunnerCoreConfig, HorizonWorld takes HorizonConfig).
/// Consumers whose responsibilities legitimately span multiple sub-domains (GameplayManager,
/// GameplayTimeline, MusicWorldManager, GameplayHUD, CameraFollow) hold this root config and
/// read whichever sub-config.field they need — this is expected, not a smell, per the explicit
/// "low risk over DI purism" guidance this refactor was done under.
/// </summary>
[CreateAssetMenu(fileName = "MusicRunnerGameplayConfig", menuName = "MusicGame/MusicRunner/Gameplay Config (Root)")]
public class MusicRunnerGameplayConfig : ScriptableObject
{
    [Header("Core / Player")]
    public MusicRunnerCoreConfig core;

    [Header("Collectibles / Bonuses")]
    public MusicRunnerCollectiblesConfig collectibles;

    [Header("Scoring")]
    public MusicRunnerScoringConfig scoring;

    [Header("Level Generation")]
    public MusicRunnerLevelConfig levelGeneration;

    [Header("Environment / Visuals")]
    public EnvironmentConfig environment;
}
