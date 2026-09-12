using UnityEngine;

/// <summary>
/// Scene-resident, pre-wired pointers to the UI prefab instances (built once via
/// Tools > MusicGame > Build UI Prefabs, which also creates this object and assigns these three
/// fields). GameplayManager looks this up at startup and hands the views to GameplayHUD /
/// PauseController — those are still created dynamically like every other gameplay system in
/// this project, they just wire up to what's already sitting in the scene here instead of
/// building their own UI from scratch.
///
/// If this object doesn't exist yet (the prefab-build tool hasn't been run), GameplayHUD /
/// PauseController fall back to building the UI procedurally exactly as before — the game never
/// breaks for not having run the tool, it just won't be prefab-editable yet.
/// </summary>
public class UIRegistry : MonoBehaviour
{
    [SerializeField] private LiveHudView  liveHud;
    [SerializeField] private EndScreenView endScreen;
    [SerializeField] private PauseView     pause;

    public LiveHudView   LiveHud   => liveHud;
    public EndScreenView EndScreen => endScreen;
    public PauseView     Pause     => pause;
}
