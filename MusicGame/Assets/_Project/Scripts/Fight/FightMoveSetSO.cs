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
    [Tooltip("Kick while RUNNING (MovementState Run) — carries the run's momentum (running kick).")]
    public FightMoveDefinition runNormalKick;

    [Header("Attack heights — Up + button = HIGH, button alone = MID (normalPunch/normalKick), Down + button = LOW")]
    public FightMoveDefinition highNormalPunch;
    public FightMoveDefinition lowNormalPunch;
    public FightMoveDefinition highNormalKick;
    [Tooltip("Down + Kick is normally caught first by the down_b combo (also LOW); this is the plain fallback.")]
    public FightMoveDefinition lowNormalKick;

    [Tooltip("Combo-triggered moves (including Dash/Specials — see this phase's own scope note) — " +
             "looked up by id via GetByMoveId, matched against the detected FightComboDefinition's " +
             "own moveId.")]
    public FightMoveDefinition[] moves = System.Array.Empty<FightMoveDefinition>();

    [Header("Signature Move")]
    [Tooltip("ENHANCED Signature — the combo whose moveId names this move is the Signature input. " +
             "Should cost 1 Special (resourceCost) — used only when the fighter can afford it.")]
    public FightMoveDefinition signatureSpecial;
    [Tooltip("BASIC Signature — what the same input does with no Special available (no projectile).")]
    public FightMoveDefinition signatureBasic;

    public FightMoveDefinition GetByMoveId(string moveId)
    {
        if (string.IsNullOrEmpty(moveId) || moves == null) return null;
        for (int i = 0; i < moves.Length; i++)
            if (moves[i] != null && moves[i].id == moveId) return moves[i];
        return null;
    }
}
