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
    [Tooltip("How long \"FIGHT!\" is shown before the flow actually enters the Fighting state.")]
    public float fightBannerDuration = 1f;

    [Header("Match rules (data only — no real combat logic reads these yet)")]
    public float roundDuration = 60f;
    public int   roundsToWin   = 2;
    public int   maxRounds     = 3;

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
