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
    public FightArenaConfig    arena;
    public FightCameraConfig   fightCamera;
    public FightCombatBalanceConfig combatBalance;

    [Tooltip("EDITOR ONLY (see PlatformService) — forces mobile/touch UI and input while testing in " +
             "the Editor, where Application.isMobilePlatform is always false. Ignored in real builds, " +
             "which always use the real platform.")]
    public PlatformMode editorPlatformSimulation = PlatformMode.Desktop;
}
