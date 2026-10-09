using UnityEngine;

/// <summary>
/// Root config asset AppBootstrap loads at startup (via Resources — this must always be available
/// locally, before any Addressables system is even up) — composes specialized module configs
/// rather than holding properties directly. Add a field here only when a new module actually has
/// real editable configuration (see IConfigurableModule); a module with no config needs no entry.
/// </summary>
[CreateAssetMenu(fileName = "AppConfig", menuName = "MusicGame/App/App Config")]
public class AppConfigSO : ScriptableObject
{
    public FlowConfigSO        flow;
    public ThemeSystemConfigSO theme;
    public AudioAnalysisConfig audioAnalysis;
    public FightStatsConfig    fightStats;
    public FightFlowConfig     fightFlow;
    public OpponentRosterSO    opponentRoster;
    public ProgressionConfigSO progression;
    public FightArenaConfig    arena;
    public FightCameraConfig   fightCamera;
    public FightCombatBalanceConfig combatBalance;
    public FightAIConfig aiConfig;
    public PlayerAvatarConfigSO playerAvatar;
    [Tooltip("Per-song preview ranges (Song Selection preview + rival roulette snippets).")]
    public SongPreviewConfigSO songPreview;
    [Tooltip("Weights mapping raw music tags + analysis features to the game-facing GameMusicStyle " +
             "(MusicStyleResolver). Optional — without an asset the built-in default rules apply.")]
    public MusicStyleRulesSO musicStyleRules;
    [Tooltip("Pre-computed similarity vectors of the PLAYABLE catalog (Tools > MusicGame > Song Similarity > Bake). Optional: missing vectors are cached at runtime after analysis.")]
    public SongSimilarityCatalogSO songSimilarity;
    [Tooltip("Stable ids of the playable songs (songId ⇄ Addressable address) — Tools > MusicGame > Setup Song Addressables.")]
    public PlayableSongCatalogSO songCatalog;
    [Tooltip("Device-quality rules source + Addressables download settings (optional; defaults apply without it).")]
    public ContentDeliveryConfigSO contentDelivery;

    [Tooltip("EDITOR ONLY (see PlatformService) — forces mobile/touch UI and input while testing in " +
             "the Editor, where Application.isMobilePlatform is always false. Ignored in real builds, " +
             "which always use the real platform.")]
    public PlatformMode editorPlatformSimulation = PlatformMode.Desktop;
}
