using UnityEngine;

/// <summary>
/// Lives in the always-loaded UI Scene (scene-placed, see UI.unity) — the single composition point
/// for UI screens/services that don't belong to any one Mode Scene. Owns creation of
/// AnalyzingScreenController, SongAnalysisController, and FightController so nothing outside the UI
/// Scene needs to AddComponent them itself (previously AudioSystemBootstrapper/the Runner scene's
/// own "Scripts" GameObject did this directly — moved here since none of them have any scene-local
/// dependency: they only ever talk to EventBus/GameSession.Instance/ThemeManager.Instance/
/// AppBootstrap.Context, which work identically regardless of which loaded scene the component's
/// GameObject lives in — this is also exactly what lets SongAnalysisController run analysis with NO
/// Mode Scene loaded at all, see its own doc).
///
/// Each screen still manages its OWN precise show/hide trigger rather than a generic
/// GameFlowState-to-screen mapping: AnalyzingScreenController reacts to PreAnalysisStarted/
/// SongProfileReady (a cache-hit skips PreAnalysisStartedEvent entirely and must never flash the
/// overlay — a generic "show when state == SongAnalysis" mapping would regress that), and
/// FightController/IntroScreenController/MainMenuController/SongSelectionController react to
/// GameFlowStateChangedEvent directly, and CountdownController reacts to GameStartedEvent. As more
/// screens (Results) get added in later phases, this is the place a proper "exactly one screen
/// visible per GameFlowState" registry grows, once there's more than one screen that actually needs
/// that generic contract — not built speculatively ahead of that real need.
/// </summary>
public class UIFlowController : MonoBehaviour
{
    private void Awake()
    {
        // Each AddComponent's Awake()/OnEnable() runs synchronously as part of THIS call — an
        // exception thrown building one screen would otherwise abort every AddComponent call still
        // queued after it in this method, silently leaving the rest of the UI Scene's screens
        // missing entirely (indistinguishable from "the whole UI Scene failed to load"). Isolate
        // each screen so one broken screen never takes the others down with it.
        AddScreen<AnalyzingScreenController>();
        AddScreen<SongAnalysisController>();
        AddScreen<FightController>();
        AddScreen<IntroScreenController>();
        AddScreen<MainMenuController>();
        AddScreen<SongSelectionController>();
        AddScreen<CountdownController>();
        AddScreen<FinishBannerController>();
        AddScreen<MobileControlsController>();
        AddScreen<FightFlowController>();
        AddScreen<FightMusicController>();
        AddScreen<OpponentSelectionController>();
        AddScreen<VersusScreenController>();
        AddScreen<RoundIntroController>();
        AddScreen<FighterInputController>();
        AddScreen<FighterMoveController>();
        AddScreen<FightDebugHUD>();
    }

    private void AddScreen<T>() where T : Component
    {
        try
        {
            gameObject.AddComponent<T>();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UIFlowController] Failed to build '{typeof(T).Name}' — it will be " +
                            $"missing from the UI Scene, but every other screen still loads. {ex}");
        }
    }
}
