using UnityEngine;

/// <summary>
/// The ONE place arena-bounds geometry is computed on the XZ combat plane — two related jobs:
///   - ClampXZ: "clamp a proposed move against arena bounds + minimum fighter separation" — shared
///     by FighterMovement (normal locomotion, lunges, sidestep/sidewalk/dash/backdash) and
///     FighterHitReaction (knockback), so both respect the exact same rules without duplicating the
///     math (see this phase's own scope note on knockback reusing the existing movement system as
///     much as possible).
///   - RayDistanceToArenaBounds: "how much room is left walking in a given direction" — used by
///     FightAIContext's SpaceAhead/SpaceBehind (see its own doc).
///
/// Bounds are two-dimensional (main combat axis X + a small depth range Z — see FightArenaConfig's
/// own doc) rather than a single X clamp, since fighters can occupy a limited depth range via
/// sidestep/sidewalk. Separation/space are likewise computed on the real XZ geometry, never just
/// |x difference| or assumed-world-X-forward — this is what lets a sidestep legitimately pass a
/// fighter to the side without being treated as "trying to walk through them" (see class doc:
/// "no impedir sidestep legítim"), and what keeps SpaceAhead/SpaceBehind correct at any angle.
/// </summary>
public static class FightMovementUtility
{
    /// <summary>Applies `delta` (a world-space XZ displacement — Y is passed through unchanged) to
    /// `currentPos`, clamps the result to the arena's X/Z bounds, then pushes it back out to
    /// `minimumFighterSeparation` from `opponentPos` (on the XZ plane) if it would otherwise end up
    /// closer than that — never by moving further than needed, and always still inside bounds
    /// afterward.</summary>
    public static Vector3 ClampXZ(Vector3 currentPos, Vector3 delta, Vector3 opponentPos, FightArenaConfig config)
    {
        float minX = config != null ? config.minBoundX : -4f;
        float maxX = config != null ? config.maxBoundX : 4f;
        float minZ = config != null ? config.minDepth : -1.5f;
        float maxZ = config != null ? config.maxDepth : 1.5f;
        float minSeparation = config != null ? config.minimumFighterSeparation : 1f;

        Vector3 target = currentPos + delta;
        target.x = Mathf.Clamp(target.x, minX, maxX);
        target.z = Mathf.Clamp(target.z, minZ, maxZ);

        Vector2 toTarget = new Vector2(target.x - opponentPos.x, target.z - opponentPos.z);
        float dist = toTarget.magnitude;
        if (dist < minSeparation)
        {
            // Degenerate (exactly overlapping) fallback keeps the SAME left/right bias the old
            // pure-X clamp used, rather than picking an arbitrary axis.
            Vector2 pushDir = dist > 0.0001f
                ? toTarget / dist
                : new Vector2(currentPos.x >= opponentPos.x ? 1f : -1f, 0f);

            Vector2 corrected = new Vector2(opponentPos.x, opponentPos.z) + pushDir * minSeparation;
            target.x = Mathf.Clamp(corrected.x, minX, maxX);
            target.z = Mathf.Clamp(corrected.y, minZ, maxZ);
        }

        target.y = currentPos.y + delta.y; // Y (jump arc) is owned entirely elsewhere — passed through, never clamped here
        return target;
    }

    /// <summary>
    /// Distance from `positionXZ` (a Vector2 where .y actually holds world Z — same convention
    /// FighterActor.DistanceToOpponent already uses), walking along `directionXZ`, to the first
    /// wall of the arena's rectangular X/Z bounds it would actually reach. `directionXZ` MUST be
    /// unit-length (ForwardXZ/-ForwardXZ already are) for the returned value to be a real distance.
    ///
    /// Analytical "ray starting inside an axis-aligned rectangle" solve — no Physics/Raycast, fully
    /// deterministic: for each axis, only the wall the ray is actually moving TOWARDS can ever be
    /// hit (AxisExitDistance returns +Infinity for the other one, and for an axis the direction has
    /// ~zero component along), so the true first hit is simply whichever axis's own exit distance is
    /// smaller. This is what lets FightAIContext's SpaceAhead/SpaceBehind stay correct no matter the
    /// angle of the live line between fighters — aligned on X, aligned on Z, or anywhere in between —
    /// never assuming the combat axis is world X (see FightAIContext's own doc).
    /// </summary>
    public static float RayDistanceToArenaBounds(Vector2 positionXZ, Vector2 directionXZ, FightArenaConfig config)
    {
        float minX = config != null ? config.minBoundX : -4f;
        float maxX = config != null ? config.maxBoundX : 4f;
        float minZ = config != null ? config.minDepth : -1.5f;
        float maxZ = config != null ? config.maxDepth : 1.5f;

        float tx = AxisExitDistance(positionXZ.x, directionXZ.x, minX, maxX);
        float tz = AxisExitDistance(positionXZ.y, directionXZ.y, minZ, maxZ);

        // At least one axis always has a real (non-near-zero) component for any genuine ForwardXZ
        // (it's never the zero vector — see FighterActor's own doc), so this never actually returns
        // Infinity in practice; Max(0, ...) only guards a position that's somehow already outside
        // bounds (floating-point edge case) from returning a negative distance.
        return Mathf.Max(0f, Mathf.Min(tx, tz));
    }

    // Epsilon on the DIRECTION component, not the distance — below this, the ray is considered
    // parallel to this axis' own walls (never reaches either one), regardless of how far away they are.
    private const float DirectionEpsilon = 0.0001f;

    private static float AxisExitDistance(float pos, float dir, float min, float max)
    {
        if (dir > DirectionEpsilon)  return (max - pos) / dir;
        if (dir < -DirectionEpsilon) return (min - pos) / dir;
        return float.PositiveInfinity; // travelling (near-)parallel to this axis' own walls
    }
}
