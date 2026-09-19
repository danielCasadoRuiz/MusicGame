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
    [Tooltip("Total wall-clock time the roulette spends stepping through opponents before " +
             "settling on the final one (excludes finalOpponentHoldDuration below).")]
    public float opponentSelectionDuration = 4f;
    [Tooltip("How long each intermediate highlight (and its music snippet) stays up before " +
             "moving to the next opponent.")]
    public float selectionStepDuration = 0.25f;
    [Tooltip("How long the FINAL opponent stays highlighted, clearly as the definitive pick, " +
             "before transitioning to the Versus screen.")]
    public float finalOpponentHoldDuration = 1.5f;

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
    [Tooltip("First side to win this many rounds wins the match — the only hard rule FightMatchController enforces.")]
    public int   roundsToWin   = 2;
    [Tooltip("Informational only (e.g. FightHud's round-won pips) — see FightMatchController's own " +
             "doc on why it is NOT a hard cap: an exactly-tied TimeOut round is normally resolved by " +
             "accumulated point differential rather than repeated (see RoundResolution), so the " +
             "match essentially never needs a 4th round in a Best of 3 — only the exceptional " +
             "TrueDraw (tied health AND zero accumulated differential) still repeats a round.")]
    public int   maxRounds     = 3;

    [Header("Round End")]
    [Tooltip("How long the \"KO\" reason banner is shown before the round result text.")]
    public float koBannerDuration = 1f;
    [Tooltip("How long the \"TIME UP\" reason banner is shown before the round result text.")]
    public float timeUpBannerDuration = 1f;
    [Tooltip("How long the round result (\"X wins the round\" / \"DRAW\") is shown before " +
             "FightMatchController decides what happens next — also used for the final \"X wins on " +
             "points\" beat of a DrawResolvedByPoints round.")]
    public float roundResultDuration = 1.5f;
    [Tooltip("DrawResolvedByPoints only — the small pause between showing \"DRAW\" and revealing " +
             "\"X wins on points\", so the draw itself reads as its own beat first.")]
    public float drawPointsPauseDuration = 1f;

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

    [Tooltip("The data-driven combo list FightComboRecognizer checks input against. Empty/null is " +
             "tolerated (normals still fire, just nothing ever completes as a combo).")]
    public FightComboSetSO comboSet;

    [Header("Moves")]
    [Tooltip("Resolves normals (normalPunch/normalKick) and combo moveIds into real " +
             "FightMoveDefinition data for FighterMoveController to execute. Null is tolerated " +
             "(normals/combos still get recognized, they just never resolve into a running move).")]
    public FightMoveSetSO defaultMoveSet;
}
