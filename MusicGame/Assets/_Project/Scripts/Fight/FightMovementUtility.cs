using UnityEngine;

/// <summary>
/// The ONE place "clamp a proposed X-axis move against arena bounds + minimum fighter separation"
/// is computed — shared by FighterMovement (normal locomotion + lunges) and FighterHitReaction
/// (knockback), so both respect the exact same rules without duplicating the math (see this phase's
/// own scope note on knockback reusing the existing movement system as much as possible).
/// </summary>
public static class FightMovementUtility
{
    public static float ClampDeltaX(float currentX, float delta, float opponentX, FightArenaConfig config)
    {
        float minX          = config != null ? config.minBoundX : -4f;
        float maxX          = config != null ? config.maxBoundX : 4f;
        float minSeparation = config != null ? config.minimumFighterSeparation : 1f;

        float targetX = Mathf.Clamp(currentX + delta, minX, maxX);

        if (targetX > currentX && opponentX > currentX)
            targetX = Mathf.Min(targetX, opponentX - minSeparation);
        else if (targetX < currentX && opponentX < currentX)
            targetX = Mathf.Max(targetX, opponentX + minSeparation);

        return targetX;
    }
}
