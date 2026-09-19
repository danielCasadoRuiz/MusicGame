using UnityEngine;

/// <summary>
/// Which way a fighter is currently facing — now a REAL 3D (well, XZ-planar) direction, not just a
/// left/right bool. ForwardXZ is the single source every movement/hitbox/projectile/knockback
/// calculation should read for "which way do I actually walk/attack towards" (see FighterActor's own
/// ForwardXZ/SideXZ doc); FacingRight is kept ONLY as a coarse convenience for the one thing that
/// still genuinely only needs a flat left/right bit — resolving a raw keyboard/joystick Horizontal
/// axis into Forward/Back (see FightDirectionResolver) — a physical A/D key has no inherent notion of
/// "towards the opponent along a possibly-angled line", so it only ever needs to know "is Forward
/// currently the +axis or -axis side", not the exact angle.
/// </summary>
public interface IFightFacingProvider
{
    /// <summary>Coarse convenience only — see class doc. True if Forward currently leans towards
    /// screen-right (ForwardXZ.x &gt;= 0).</summary>
    bool FacingRight { get; }

    /// <summary>Unit vector, world-space, XZ-plane (Y always 0) — the real direction "towards the
    /// opponent" right now. The single thing every movement/hitbox/projectile/knockback calculation
    /// should actually use.</summary>
    Vector3 ForwardXZ { get; }
}

/// <summary>
/// V1 placeholder — always world +X, matching FightSceneBootstrap's own placeholder arena layout
/// (player capsule at x=-1.5, enemy at x=+1.5: the player already faces screen-right, towards the
/// opponent, by that existing convention). Used only as FighterInputController/FighterActor's
/// default before real FighterActors are wired (see RealFightFacingProvider below) — nothing else
/// in this input/combo layer changes when that swap happens.
/// </summary>
public class DebugFightFacingProvider : IFightFacingProvider
{
    public bool FacingRight => true;
    public Vector3 ForwardXZ => Vector3.right;
}

/// <summary>
/// Real implementation — self/other are the two FighterActors' own gameplay-root Transforms (never
/// VisualRoot's — facing is a gameplay concept, not a presentational one). One instance per fighter
/// (self/other swapped) so BOTH fighters can use the exact same class for their own visual facing,
/// while only the Player's instance is additionally wired into FighterInputController — Forward/Back
/// input resolution only exists for whichever fighter actually has an IFightInputSource.
///
/// ForwardXZ = (other.position - self.position) with Y zeroed, normalized — exactly
/// "lookDirection = opponentPosition - selfPosition, ignorant Y" (this phase's own explicit ask),
/// now that fighters can occupy a small depth range instead of being confined to a single world
/// axis. Degenerates to Vector3.right only in the (should-never-happen) case the two fighters occupy
/// the exact same XZ position, so this never returns a zero/NaN vector. This is the ONE physical
/// authority for "towards the opponent" — FacingRight below never changes what this reports.
/// </summary>
public class RealFightFacingProvider : IFightFacingProvider
{
    // How close the ForwardXZ-vs-camera-right dot product must get to zero before a side flip is
    // even considered — see FacingRight's own doc on why this exists (hysteresis, not a hard snap).
    private const float FacingDotEpsilon = 0.05f;

    private readonly Transform _self;
    private readonly Transform _other;

    // Per-instance (one per fighter) — starts true only as an arbitrary, harmless seed; it's
    // overwritten the first time the dot product below is ever unambiguous, which in practice is
    // immediate (see FacingRight's own doc on the fallback path before any camera exists).
    private bool _lastFacingRight = true;

    public RealFightFacingProvider(Transform self, Transform other)
    {
        _self  = self;
        _other = other;
    }

    public Vector3 ForwardXZ
    {
        get
        {
            if (_self == null || _other == null) return Vector3.right;
            Vector3 diff = _other.position - _self.position;
            diff.y = 0f;
            return diff.sqrMagnitude > 0.0001f ? diff.normalized : Vector3.right;
        }
    }

    /// <summary>
    /// "Is Forward currently rendered on the SCREEN-right side" — compared against the ACTIVE Fight
    /// camera's own right axis (projected onto XZ), never world +X directly. The old
    /// `ForwardXZ.x >= 0` check assumed world +X always renders as screen-right, which was true only
    /// while the camera sat at a fixed world-space offset (the original 2D/2.5D setup) — now that
    /// FightCameraController orbits the live line between the two fighters (see its own doc), world
    /// X can point anywhere on screen, including straight at/away from the camera, so that old check
    /// could silently invert a human's own A/D (or Left/Right) keys exactly when they'd notice most:
    /// fighters aligned mostly in depth, or the instant ForwardXZ.x itself crossed zero.
    ///
    /// ONLY consumed by FighterInputController, to resolve a raw physical left/right axis (from
    /// HumanFightInputSource OR AIFightInputSource — both go through the exact same
    /// FightDirectionResolver) into Forward/Back — see IFightFacingProvider's own doc. ForwardXZ
    /// above remains the one true "towards the opponent" authority; nothing here ever changes it.
    /// FighterAI itself never reads this directly or touches the camera — see
    /// FighterAI.ApproachSign's own doc on why it stays camera-agnostic even though this class isn't.
    ///
    /// Falls back to world +X — today's ORIGINAL convention — whenever no Fight camera exists yet
    /// (ResolveCameraRightXZ returns Vector3.right), which only ever happens for the first few
    /// frames of Opponent Selection/Versus, well before real Fighting input is even possible.
    ///
    /// HYSTERESIS: only actually flips _lastFacingRight when the dot product clears
    /// FacingDotEpsilon in either direction — while it's near zero (the fighters' line briefly
    /// close to perpendicular to the camera's own right axis, e.g. while the camera's own smoothing
    /// is still catching up to a fast reposition), this simply keeps reporting whatever side was
    /// last unambiguous, instead of nervously flipping Forward/Back moment to moment.
    /// </summary>
    public bool FacingRight
    {
        get
        {
            float dot = Vector3.Dot(ForwardXZ, ResolveCameraRightXZ());
            if (Mathf.Abs(dot) > FacingDotEpsilon) _lastFacingRight = dot >= 0f;
            return _lastFacingRight;
        }
    }

    private static Vector3 ResolveCameraRightXZ()
    {
        var cameraTransform = FightCameraController.Instance != null ? FightCameraController.Instance.transform : null;
        if (cameraTransform == null) return Vector3.right;

        Vector3 right = cameraTransform.right;
        right.y = 0f;
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
    }
}
