using UnityEngine;

/// <summary>
/// One combo — a sequence of button+direction steps, entirely data-driven (see FightComboSetSO).
/// Deliberately holds NOTHING about animation/damage/stamina/VFX yet — moveId is the seam where
/// the future Move System plugs in (this combo, once recognized, will trigger the action moveId
/// names), but nothing reads it yet.
/// </summary>
[System.Serializable]
public class FightComboDefinition
{
    public string id;
    public string debugName;

    [System.Serializable]
    public struct Step
    {
        [Tooltip("Punch / Kick, or None for a bare DIRECTION step (a press of that direction).")]
        public FightButton button;
        public FightHorizontalDirection horizontal;
        public FightVerticalDirection vertical;
        [Tooltip("Button steps only: accept the press whatever direction is held at that moment " +
                 "(e.g. the Punch at the end of Down → Forward → Punch while Forward is still held). " +
                 "Off = the held direction must be exactly horizontal/vertical above.")]
        public bool anyDirection;

        public bool Matches(FightInputEvent e) =>
            button == e.Button && (button != FightButton.None && anyDirection || (horizontal == e.Horizontal && vertical == e.Vertical));

        public override string ToString()
        {
            string dir = (vertical != FightVerticalDirection.Neutral ? vertical + "+" : "")
                       + (horizontal != FightHorizontalDirection.Neutral ? horizontal + "+" : "");
            if (button == FightButton.None) return dir.TrimEnd('+');
            return anyDirection ? $"{button}(any dir)" : $"{dir}{button}";
        }
    }

    [Tooltip("The button+direction sequence this combo requires, in order.")]
    public Step[] steps = System.Array.Empty<Step>();

    [Tooltip("Max seconds allowed between two consecutive steps for the combo to still be recognized.")]
    public float maxTimeBetweenInputs = 0.5f;

    [Tooltip("Optional cap on the whole sequence (first step → last step), seconds. 0 = no cap.")]
    [Min(0f)] public float maxTotalDuration = 0f;

    [Tooltip("Extra attack multipliers this combo's move gets (values in FightCombatBalanceConfig).")]
    public FightComboBonus attackBonus = FightComboBonus.None;

    [Tooltip("Tiebreaker ONLY between combos that would otherwise match with the SAME number of " +
             "steps — see FightComboRecognizer's own doc on the prefix policy for how a combo " +
             "that's a strict prefix of a longer one is handled (it's not about priority).")]
    public int priority = 0;

    [Tooltip("The FightMoveSetSO move (by id) this combo executes. EMPTY = the last step's own " +
             "normal (posture-aware Punch/Kick) — e.g. the Punch/Kick finishers.")]
    public string moveId;

    /// <summary>"Punch Punch Kick"-style text for debug.</summary>
    public string StepsText => string.Join(" ", System.Array.ConvertAll(steps, s => s.ToString()));
}

/// <summary>Which FightCombatBalanceConfig bonus a combo's move carries.</summary>
public enum FightComboBonus
{
    None,
    PunchFinisher,
    KickFinisher,
}

/// <summary>
/// The full list of combos the recognizer checks against — same "one asset, an inline array of
/// small data rows" shape as MusicRunnerScoringConfig/FightStatsConfig's own arrays, since combos
/// are numerous, lightweight, and best compared side-by-side while tuning (unlike OpponentDefinition,
/// which is heavyweight enough per-entry to deserve its own asset file).
/// </summary>
[CreateAssetMenu(fileName = "FightComboSet", menuName = "MusicGame/Fight/Combo Set")]
public class FightComboSetSO : ScriptableObject
{
    public FightComboDefinition[] combos = System.Array.Empty<FightComboDefinition>();
}
