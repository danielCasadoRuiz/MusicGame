using UnityEngine;

/// <summary>
/// Pure geometric overlap tests — no Unity physics/Collider/Rigidbody involved anywhere in combat
/// (see this phase's own scope note: deterministic, not physics-driven). Hurtboxes are always an
/// axis-aligned box (see FighterHurtbox); a hitbox can be Box or Sphere (see FightHitDefinition).
/// </summary>
public static class FightCombatShapes
{
    public static bool Overlaps(Vector3 hitCenter, FightHitDefinition hitDef, Vector3 boxCenter, Vector3 boxSize)
    {
        return hitDef.shape == FightHitShape.Sphere
            ? SphereVsBox(hitCenter, hitDef.size.x, boxCenter, boxSize)
            : BoxVsBox(hitCenter, hitDef.size, boxCenter, boxSize);
    }

    private static bool BoxVsBox(Vector3 aCenter, Vector3 aSize, Vector3 bCenter, Vector3 bSize)
    {
        Vector3 aMin = aCenter - aSize * 0.5f, aMax = aCenter + aSize * 0.5f;
        Vector3 bMin = bCenter - bSize * 0.5f, bMax = bCenter + bSize * 0.5f;
        return aMin.x <= bMax.x && aMax.x >= bMin.x &&
               aMin.y <= bMax.y && aMax.y >= bMin.y &&
               aMin.z <= bMax.z && aMax.z >= bMin.z;
    }

    private static bool SphereVsBox(Vector3 sphereCenter, float radius, Vector3 boxCenter, Vector3 boxSize)
    {
        Vector3 boxMin = boxCenter - boxSize * 0.5f, boxMax = boxCenter + boxSize * 0.5f;
        Vector3 closest = new Vector3(
            Mathf.Clamp(sphereCenter.x, boxMin.x, boxMax.x),
            Mathf.Clamp(sphereCenter.y, boxMin.y, boxMax.y),
            Mathf.Clamp(sphereCenter.z, boxMin.z, boxMax.z));
        return (closest - sphereCenter).sqrMagnitude <= radius * radius;
    }
}
