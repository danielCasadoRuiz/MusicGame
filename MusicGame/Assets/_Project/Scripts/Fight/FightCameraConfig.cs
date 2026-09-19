using UnityEngine;

/// <summary>
/// Tunables for FightCameraController — a first, functional Fight camera that keeps BOTH fighters
/// framed (never just the Player), see that class's own doc.
/// </summary>
[CreateAssetMenu(fileName = "FightCameraConfig", menuName = "MusicGame/Fight/Camera Config")]
public class FightCameraConfig : ScriptableObject
{
    [Header("Framing")]
    [Tooltip("Offset from the fighters' midpoint — X/Y are sideways/height, Z's magnitude is the " +
             "base back-distance before separation padding is added.")]
    public Vector3 offset = new Vector3(0f, 2.2f, -6f);
    public float minDistance = 4f;
    public float maxDistance = 9f;
    [Tooltip("Extra back-distance added per unit of DistanceToOpponent, before clamping to min/max.")]
    public float separationPadding = 0.6f;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.15f;
    public float rotationSmoothSpeed = 6f;

    [Tooltip("How quickly the camera's own orbit angle follows the live line between the two " +
             "fighters (see FightCameraController's own doc) — a SEPARATE, typically slower knob " +
             "than positionSmoothTime/rotationSmoothSpeed above, so a quick sidestep/backdash " +
             "changing the fighters' relative depth doesn't visibly whip the camera around it; only " +
             "a sustained change in the combat line's angle actually re-orbits the view.")]
    public float orbitSmoothSpeed = 3f;
}
