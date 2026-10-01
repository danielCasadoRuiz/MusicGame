using UnityEngine;

/// <summary>
/// HOW a fighter FIGHTS (not how it looks — that is AvatarRecipeSO): which moves it knows (a
/// FightMoveSetSO — normals + role/combo-triggered moves) and which animation style performs them
/// (FighterAnimationSetSO). Referenced per opponent TIER (OpponentLevelConfig.combatProfile), and for
/// the Player by FightFlowConfig.defaultPlayerCombatProfile. Deliberately small.
/// </summary>
[CreateAssetMenu(fileName = "FighterCombatProfile", menuName = "MusicGame/Fight/Fighter Combat Profile")]
public class FighterCombatProfileSO : ScriptableObject
{
    public FightMoveSetSO moveSet;
    public FighterAnimationSetSO animationSet;

    /// <summary>The move this profile uses for a logical role (normals first, then the move list);
    /// null if the fighter doesn't know one.</summary>
    public FightMoveDefinition GetMove(CombatRole role)
    {
        if (moveSet == null) return null;
        if (moveSet.normalPunch != null && moveSet.normalPunch.role == role) return moveSet.normalPunch;
        if (moveSet.normalKick  != null && moveSet.normalKick.role  == role) return moveSet.normalKick;
        if (moveSet.moves != null)
            foreach (var m in moveSet.moves)
                if (m != null && m.role == role) return m;
        return null;
    }

    public FightMoveDefinition GetMoveById(string id) =>
        moveSet == null ? null :
        moveSet.normalPunch != null && moveSet.normalPunch.id == id ? moveSet.normalPunch :
        moveSet.normalKick  != null && moveSet.normalKick.id  == id ? moveSet.normalKick  :
        moveSet.GetByMoveId(id);
}
