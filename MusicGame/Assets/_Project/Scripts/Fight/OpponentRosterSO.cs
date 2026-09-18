using UnityEngine;

/// <summary>
/// The full list of selectable rivals — OpponentSelectionController populates its grid directly
/// from opponents.Length, never a hardcoded count (see its own doc). Add/remove an
/// OpponentDefinition here and the grid, the roulette, and everything downstream adapt with no
/// code change.
/// </summary>
[CreateAssetMenu(fileName = "OpponentRoster", menuName = "MusicGame/Fight/Opponent Roster")]
public class OpponentRosterSO : ScriptableObject
{
    public OpponentDefinition[] opponents = System.Array.Empty<OpponentDefinition>();
}
