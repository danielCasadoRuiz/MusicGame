using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TEMPORARY debug controls for the combat architecture (not the final Player controls — J/K and
/// combos keep working through FighterInputController). Added by FightSceneBootstrap in the Editor /
/// development builds only. Every action goes through the real runtime (TryExecuteRole).
///   1 Light   2 Heavy   3 Kick   4 Block   5 Dodge   6 Special   7 Triple test   8 Quad test   9 Taunt
///   hold Left Shift = the OPPONENT performs it instead
///   0 = +1 Triple / +1 Quad / +1 Special to the Player (debug grant)
/// </summary>
public class FightDebugCombatInput : MonoBehaviour
{
    private FighterActor _player;
    private FighterActor _opponent;

    private static readonly (Key key, CombatRole role, string moveId)[] Bindings =
    {
        (Key.Digit1, CombatRole.LightAttack, null),
        (Key.Digit2, CombatRole.HeavyAttack, null),
        (Key.Digit3, CombatRole.Kick,        null),
        (Key.Digit4, CombatRole.Block,       null),
        (Key.Digit5, CombatRole.Dodge,       null),
        (Key.Digit6, CombatRole.Special,     "special_test"),
        (Key.Digit7, CombatRole.HeavyAttack, "triple_technique_test"),
        (Key.Digit8, CombatRole.HeavyAttack, "quad_technique_test"),
        (Key.Digit9, CombatRole.Taunt,       null),
    };

    public void Initialize(FighterActor player, FighterActor opponent)
    {
        _player = player;
        _opponent = opponent;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || _player == null) return;
        var actor = kb.leftShiftKey.isPressed ? _opponent : _player;

        if (kb.digit0Key.wasPressedThisFrame)
        {
            _player.CombatResources.DebugGrant(1, 1, 1);
            Debug.Log($"[FightDebugCombatInput] Player resources -> {_player.CombatResources}");
        }

        foreach (var (key, role, moveId) in Bindings)
        {
            if (!kb[key].wasPressedThisFrame || actor?.MoveController == null) continue;
            var mc = actor.MoveController;
            bool ok = moveId != null && actor.CombatProfile != null
                ? mc.TryExecuteMove(actor.CombatProfile.GetMoveById(moveId))
                : mc.TryExecuteRole(role);
            Debug.Log($"[FightDebugCombatInput] {actor.Side} {(moveId ?? role.ToString())}: {(ok ? "OK" : "rejected — " + mc.LastRejection)}");
        }
    }
}
