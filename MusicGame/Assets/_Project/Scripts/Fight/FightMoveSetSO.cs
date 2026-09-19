using UnityEngine;

/// <summary>
/// One fighter's full move list — NormalPunch/NormalKick get their own dedicated slots (they're
/// triggered directly by FightNormalPunchEvent/FightNormalKickEvent, never by a moveId lookup),
/// everything else (every combo-triggered move) lives in `moves`, matched by id against whatever
/// FightComboDefinition.moveId the combo recognizer reports — see FightComboDefinition's own doc.
///
/// Deliberately just ONE set today (FighterMoveController loads it from
/// AppConfigSO.fightFlow.defaultMoveSet) — a real "which fighter uses which moveset" assignment is
/// a natural next step once real FighterActors exist, not before.
/// </summary>
[CreateAssetMenu(fileName = "FightMoveSet", menuName = "MusicGame/Fight/Move Set")]
public class FightMoveSetSO : ScriptableObject
{
    public FightMoveDefinition normalPunch;
    public FightMoveDefinition normalKick;

    [Header("Context-specific normals — see FighterMoveController.ResolvePunch/ResolveKick's own doc")]
    [Tooltip("Used instead of normalPunch/normalKick while FighterPosture is Airborne. Null = no " +
             "air attack at all (the button press is simply ignored while airborne).")]
    public FightMoveDefinition airNormalPunch;
    public FightMoveDefinition airNormalKick;
    [Tooltip("Used instead of normalPunch while FighterMovementState is Run. Null = the grounded " +
             "normalPunch still fires while running.")]
    public FightMoveDefinition runNormalPunch;

    [Tooltip("Combo-triggered moves (including Dash/Specials — see this phase's own scope note) — " +
             "looked up by id via GetByMoveId, matched against the detected FightComboDefinition's " +
             "own moveId.")]
    public FightMoveDefinition[] moves = System.Array.Empty<FightMoveDefinition>();

    public FightMoveDefinition GetByMoveId(string moveId)
    {
        if (string.IsNullOrEmpty(moveId) || moves == null) return null;
        for (int i = 0; i < moves.Length; i++)
            if (moves[i] != null && moves[i].id == moveId) return moves[i];
        return null;
    }
}
