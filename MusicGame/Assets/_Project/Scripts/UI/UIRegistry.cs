using UnityEngine;

/// <summary>
/// Runner-scene pointers to the UI prefab instances placed in Runner.unity (LiveHud, EndScreen, PauseMenu),
/// read by GameplayManager → GameplayHUD/PauseController. The UI-scene screens do NOT use this: their
/// controllers instantiate their prefab from UIPrefabLibrarySO (Tools > MusicGame > UI > Build All UI Prefabs).
/// The Frontend/Fight fields below are legacy and stay empty. Nothing falls back to building UI in code.
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
    [SerializeField] private FightHudView            fightHud;
    [SerializeField] private OpponentSelectionView   opponentSelection;
    [SerializeField] private VersusScreenView        versusScreen;
    [SerializeField] private RoundIntroView          roundIntro;
    [SerializeField] private RoundEndView            roundEnd;
    [SerializeField] private MatchResultView         matchResult;
    [SerializeField] private NextSongTransitionView  nextSongTransition;

    public LiveHudView   LiveHud   => liveHud;
    public EndScreenView EndScreen => endScreen;
    public PauseView     Pause     => pause;

    public IntroScreenView     IntroScreen   => introScreen;
    public MainMenuView        MainMenu      => mainMenu;
    public SongSelectionView   SongSelection => songSelection;
    public AnalyzingScreenView Analyzing     => analyzing;
    public CountdownScreenView Countdown     => countdown;
    public FinishBannerView    FinishBanner  => finishBanner;

    public FightHudView          FightHud          => fightHud;
    public OpponentSelectionView OpponentSelection => opponentSelection;
    public VersusScreenView      VersusScreen      => versusScreen;
    public RoundIntroView        RoundIntro        => roundIntro;
    public RoundEndView          RoundEnd          => roundEnd;
    public MatchResultView       MatchResult       => matchResult;
    public NextSongTransitionView NextSongTransition => nextSongTransition;
}
