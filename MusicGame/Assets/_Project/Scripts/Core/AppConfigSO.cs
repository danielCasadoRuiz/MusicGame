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
    public SongSystemConfigSO  song;
}
