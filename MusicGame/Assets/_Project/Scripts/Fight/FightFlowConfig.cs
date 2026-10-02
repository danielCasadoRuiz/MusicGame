using UnityEngine;

/// <summary>
/// Every timing/tunable for the pre-combat Fight flow (Opponent Selection → Versus → Round Intro
/// → Countdown → Fighting) — same "no numbers hardcoded in a controller" rule as
/// MusicRunnerCoreConfig/FightStatsConfig. roundDuration/roundsToWin/maxRounds are here already
/// (real combat needs them eventually) but carry NO logic yet — nothing reads them as anything
/// more than plain numbers today.
/// </summary>
[CreateAssetMenu(fileName = "FightFlowConfig", menuName = "MusicGame/Fight/Fight Flow Config")]
public class FightFlowConfig : ScriptableObject
{
    [Header("Opponent Selection (roulette)")]
    [Tooltip("How many FAKE rivals the roulette shows (each with its music snippet) before revealing " +
             "the real, precomputed one. Never repeats a rival within one roulette, excludes the final " +
             "rival when enough others exist, and clamps to the unique candidates available.")]
    [Min(0)] public int fakeSelectionCount = 6;
    [Tooltip("Max seconds the roulette waits for its songs (the fakes' + the final one) to finish loading " +
             "before it starts — the step timer only runs once the songs are ready to play instantly.")]
    [Min(0f)] public float songPreloadTimeout = 3f;
    [Tooltip("A tiny radio-tuning / static burst between fake rivals' snippets. Purely cosmetic.")]
    public bool radioStaticEnabled = true;
    [Min(0.01f)] public float radioStaticDuration = 0.08f; // 0.07–0.10 s reads as a quick tuning blip
    [Range(0f, 1f)] public float radioStaticVolume = 0.12f;
    [Tooltip("LEGACY — no longer read: the roulette length is fakeSelectionCount × selectionStepDuration.")]
    public float opponentSelectionDuration = 6f;
    [Tooltip("How long each intermediate highlight (and its music snippet) stays up before " +
             "moving to the next opponent — long enough to actually hear each fragment.")]
    public float selectionStepDuration = 0.75f;
    [Tooltip("How long the FINAL opponent stays highlighted, clearly as the definitive pick, " +
             "before transitioning to the Versus screen.")]
    public float finalOpponentHoldDuration = 1.5f;

    [Header("Combat music")]
    [Tooltip("Seconds the combat track fades out when leaving the fight (Continue / Replay Song / " +
             "Main Menu) instead of stopping abruptly. 0 = immediate stop.")]
    [Min(0f)] public float combatMusicFadeOutSeconds = 1.5f;

    [Header("Versus")]
    public float versusDuration = 2.5f;

    [Header("Round Intro / Countdown")]
    [Tooltip("How long \"ROUND {n}\" is shown before the 3-2-1 countdown starts.")]
    public float roundIntroDuration = 1.5f;
    [Tooltip("How long each of \"3\", \"2\", \"1\" is shown.")]
    public float countdownStepDuration = 0.8f;
    [Tooltip("How long \"FIGHT!\" is shown before the flow actually enters the Fighting state — the " +
             "dark countdown overlay (see countdownOverlayAlpha) fades to 0 progressively over this " +
             "exact same duration, so the reveal finishes the instant \"FIGHT!\" itself ends, with no " +
             "separate fade afterward.")]
    public float fightBannerDuration = 1f;
    [Range(0f, 1f)]
    [Tooltip("How dark the arena+HUD are dimmed behind \"ROUND {n}\"/3-2-1/FIGHT! — the ONE curtain " +
             "for the whole VS->Fighting reveal (see RoundIntroController's own doc). 0 = no dimming.")]
    public float countdownOverlayAlpha = 0.55f;

    [Header("Match rules")]
    public float roundDuration = 60f;
    [Tooltip("First side to win this many rounds wins the match. The match can also end BEFORE " +
             "reaching this if maxRounds is exhausted first (e.g. 1-0 after 3 rounds, the third a " +
             "Draw) — see FightMatchController.NotifyRoundEndDisplayComplete's own doc.")]
    public int   roundsToWin   = 2;
    [Tooltip("HARD CAP — the match is ALWAYS decided by the time this many rounds have been played, " +
             "no exceptions, no round repeats (see FightMatchController's own doc). A round-level " +
             "Draw NEVER extends the match past this: it simply awards 0 round wins and, once " +
             "maxRounds is reached with round wins still tied, matchPointsTiebreakEpsilon below " +
             "decides the match instead.")]
    public int   maxRounds     = 3;
    [Tooltip("Epsilon for comparing MatchPointDifferential once round wins are tied after maxRounds " +
             "rounds — avoids a false tie-break flip from float noise. Below this magnitude, the " +
             "match falls further back to FightMatchController.ResolveTiedMatch's own deterministic " +
             "fallback (never a 4th round).")]
    public float matchPointsTiebreakEpsilon = 0.01f;

    [Header("Round End")]
    [Tooltip("How long the \"KO\" reason banner is shown before the round result text.")]
    public float koBannerDuration = 1f;
    [Tooltip("How long the \"TIME UP\" reason banner is shown before the round result text.")]
    public float timeUpBannerDuration = 1f;
    [Tooltip("How long the round result (\"X wins the round\" / \"DRAW\") is shown before " +
             "FightMatchController decides what happens next.")]
    public float roundResultDuration = 1.5f;

    [Header("Next Song Transition (post Match-Won Continue)")]
    [Tooltip("How long the \"LEVEL {n} / NEXT SONG / {name}\" cartela stays fully visible before " +
             "fading into Song Analysis.")]
    public float nextSongCardHoldDuration = 1.6f;
    [Tooltip("Fade-out duration for the Next Song cartela right before it's fully replaced by the " +
             "Analyzing screen underneath.")]
    public float nextSongCardFadeDuration = 0.35f;

    [Header("Input / Combos")]
    [Tooltip("How far back FightInputBuffer keeps recent button presses — old ones are dropped " +
             "past this, so it never grows unbounded. Should comfortably fit the slowest combo's " +
             "own total timing (sum of its maxTimeBetweenInputs gaps) in the set below.")]
    public float inputBufferWindowSeconds = 1.5f;

    [Tooltip("While Down is held, a Punch or Kick press waits up to this many seconds for the OTHER " +
             "button (Down + Punch + Kick = Power charge) before being processed normally. Only " +
             "Down + button presses are ever delayed, and only by this much.")]
    [Range(0f, 0.25f)] public float simultaneousPressWindow = 0.1f;

    [Tooltip("After a combo has COMPLETED (and its move started), how long the next input may take to " +
             "extend it into a longer registered combo (PPP → PPPK), which then cancels and replaces " +
             "it. Also capped per step by the longer combo's own maxTimeBetweenInputs.")]
    [Min(0.01f)] public float comboContinuationWindow = 0.5f;

    [Tooltip("Hard cap on buffered inputs (on top of inputBufferWindowSeconds).")]
    [Min(4)] public int inputBufferMaxEntries = 24;

    [Tooltip("The data-driven combo list FightComboRecognizer checks input against. Empty/null is " +
             "tolerated (normals still fire, just nothing ever completes as a combo).")]
    public FightComboSetSO comboSet;

    [Header("Moves")]
    [Tooltip("Resolves normals (normalPunch/normalKick) and combo moveIds into real " +
             "FightMoveDefinition data for FighterMoveController to execute. Null is tolerated " +
             "(normals/combos still get recognized, they just never resolve into a running move).")]
    public FightMoveSetSO defaultMoveSet;

    [Header("Combat profiles / animation (see FighterCombatProfileSO, AnimatorFighterAnimationDriver)")]
    [Tooltip("The ONE shared logical combat AnimatorController (one state per CombatRole, placeholder " +
             "clips). Every fighter gets its own AnimatorOverrideController on top of it.")]
    public RuntimeAnimatorController combatAnimatorController;
    [Tooltip("TEMPORARY Player profile until the Player's music-style system exists.")]
    public FighterCombatProfileSO defaultPlayerCombatProfile;
    [Tooltip("Used for an opponent tier config with no combatProfile assigned.")]
    public FighterCombatProfileSO defaultOpponentCombatProfile;
}
