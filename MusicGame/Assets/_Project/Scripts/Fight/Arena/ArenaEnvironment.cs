using UnityEngine;

/// <summary>
/// Marker + contract of an arena prefab (on its root). Origin = fight centre on the floor (y = 0), facing
/// +Z towards the camera side; fighters spawn and move inside FightArenaConfig's bounds (X ±4, Z ±1.5 by
/// default) — keep that "free zone" clear. Children: Ground (floor + collider), Background, Midground,
/// Foreground, Lighting (extra lights only — the scene owns the main light), Interactables.
/// </summary>
public class ArenaEnvironment : MonoBehaviour
{
    [Tooltip("Free zone kept clear for the fighters (metres, centred on the origin). Informative: the real " +
             "limits are FightArenaConfig's bounds.")]
    public Vector3 freeZoneSize = new(8f, 3f, 3f);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * freeZoneSize.y * 0.5f, freeZoneSize);
    }
}
