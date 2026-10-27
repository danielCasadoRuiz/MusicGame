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
    [Tooltip("Campaign: levels, matches per level, rival version + runner difficulty per level, final rival.")]
    public CampaignConfigSO    campaign;
    [Tooltip("Evaluation (stars) scale, thresholds and weights for runner and combat.")]
    public EvaluationConfigSO  evaluation;
    [Tooltip("Editable UI prefabs the controllers instantiate (Tools > MusicGame > UI > Build Component Prefabs).")]
    public UIPrefabLibrarySO   uiPrefabs;
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

    [Header("Testing (temporary — leave empty for the real game)")]
    [Tooltip("TEST ONLY: every normal fight is against this composer id, or a shuffled pool of comma-separated ids, whatever the campaign or " +
             "the pending match assigned — to review an arena/avatar repeatedly. Saved progress keeps its real data. " +
             "Final-rival stages are not affected. Empty = off.")]
    public string testForceOpponentId = "";
    [Tooltip("TEST ONLY: the rival version (OpponentLevelConfig.level) used with testForceOpponentId — it also picks " +
             "the arena (version 1 → the composer's arena 01). 0 = the version the game assigned.")]
    [Min(0)] public int testForceOpponentLevel = 0;
}
