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
        public FightButton button;
        public FightHorizontalDirection horizontal;
        public FightVerticalDirection vertical;

        public override string ToString()
        {
            string dir = horizontal != FightHorizontalDirection.Neutral ? horizontal + "+"
                       : vertical   != FightVerticalDirection.Neutral   ? vertical + "+"
                       : "";
            return $"{dir}{button}";
        }
    }

    [Tooltip("The button+direction sequence this combo requires, in order.")]
    public Step[] steps = System.Array.Empty<Step>();

    [Tooltip("Max seconds allowed between two consecutive steps for the combo to still be recognized.")]
    public float maxTimeBetweenInputs = 0.5f;

    [Tooltip("Tiebreaker ONLY between combos that would otherwise match with the SAME number of " +
             "steps — see FightComboRecognizer's own doc on the prefix policy for how a combo " +
             "that's a strict prefix of a longer one is handled (it's not about priority).")]
    public int priority = 0;

    [Tooltip("Not read by anything yet — the future Move System's own action/move identifier this " +
             "combo will eventually trigger.")]
    public string moveId;
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
