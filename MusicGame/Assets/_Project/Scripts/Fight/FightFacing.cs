/// <summary>
/// Which way a fighter is currently facing — the ONLY thing FightDirectionResolver needs to turn
/// an absolute input axis into Forward/Back. A real implementation (once fighters have actual
/// arena positions) would report `player.position.x &lt; opponent.position.x`, updated live as
/// either fighter moves/gets pushed past the other (a "cross-up").
/// </summary>
public interface IFightFacingProvider
{
    /// <summary>True if this fighter's Forward is currently the positive-X (screen-right) direction.</summary>
    bool FacingRight { get; }
}

/// <summary>
/// V1 placeholder — always true, matching FightSceneBootstrap's own placeholder arena layout
/// (player capsule at x=-1.5, enemy at x=+1.5: the player already faces screen-right, towards the
/// opponent, by that existing convention). Swap FighterInputController's facing provider for a
/// real one once fighters have actual positions — nothing else in this input/combo layer changes.
/// </summary>
public class DebugFightFacingProvider : IFightFacingProvider
{
    public bool FacingRight => true;
}
