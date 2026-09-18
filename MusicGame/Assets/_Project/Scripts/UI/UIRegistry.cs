using UnityEngine;

/// <summary>
/// Scene-resident, pre-wired pointers to the UI prefab instances (built once via
/// Tools > MusicGame > Build UI Prefabs, which also creates this object and assigns these fields).
/// Every screen controller looks this up at startup (FindFirstObjectByType — works regardless of
/// which loaded scene either object lives in, so the gameplay screens living in a Mode Scene and the
/// Frontend/Fight screens living in the always-loaded UI Scene both find the same registry) and reads
/// the matching view instead of building its own UI from scratch.
///
/// If this object doesn't exist yet (the prefab-build tool hasn't been run), every controller falls
/// back to building its UI procedurally exactly as before — the game never breaks for not having run
/// the tool, it just won't be prefab-editable yet.
/// </summary>
public class UIRegistry : MonoBehaviour
{
    [Header("Gameplay (Runner)")]
    [SerializeField] private LiveHudView   liveHud;
    [SerializeField] private EndScreenView endScreen;
    [SerializeField] private PauseView     pause;

    [Header("Frontend (UI Scene)")]
    [SerializeField] private IntroScreenView     introScreen;
    [SerializeField] private MainMenuView        mainMenu;
    [SerializeField] private SongSelectionView   songSelection;
    [SerializeField] private AnalyzingScreenView analyzing;
    [SerializeField] private CountdownScreenView countdown;
    [SerializeField] private FinishBannerView    finishBanner;

    [Header("Fight")]
    [SerializeField] private FightHudView fightHud;

    public LiveHudView   LiveHud   => liveHud;
    public EndScreenView EndScreen => endScreen;
    public PauseView     Pause     => pause;

    public IntroScreenView     IntroScreen   => introScreen;
    public MainMenuView        MainMenu      => mainMenu;
    public SongSelectionView   SongSelection => songSelection;
    public AnalyzingScreenView Analyzing     => analyzing;
    public CountdownScreenView Countdown     => countdown;
    public FinishBannerView    FinishBanner  => finishBanner;

    public FightHudView FightHud => fightHud;
}
