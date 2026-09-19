using UnityEngine;

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
/// opponent, by that existing convention). Used only as FighterInputController/FighterActor's
/// default before real FighterActors are wired (see RealFightFacingProvider below) — nothing else
/// in this input/combo layer changes when that swap happens.
/// </summary>
public class DebugFightFacingProvider : IFightFacingProvider
{
    public bool FacingRight => true;
}

/// <summary>
/// Real implementation, once fighters have actual arena positions (see FighterActor's own doc) —
/// self/other are the two FighterActors' own gameplay-root Transforms (never VisualRoot's — facing
/// is a gameplay concept, not a presentational one). One instance per fighter (self/other swapped)
/// so BOTH fighters can use the exact same class for their own visual facing, while only the
/// Player's instance is additionally wired into FighterInputController (see FightSceneBootstrap's
/// own wiring) — Forward/Back input resolution only exists for whichever fighter actually has an
/// IFightInputSource.
/// </summary>
public class RealFightFacingProvider : IFightFacingProvider
{
    private readonly Transform _self;
    private readonly Transform _other;

    public RealFightFacingProvider(Transform self, Transform other)
    {
        _self  = self;
        _other = other;
    }

    public bool FacingRight => _self != null && _other != null && _self.position.x <= _other.position.x;
}
