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
    public Vector3 offset = new Vector3(0f, 1.7f, -3.2f);
    [Tooltip("Closest straight-line distance (m) from the camera to the fighters' midpoint.")]
    public float minDistance = 2.6f;
    [Tooltip("FARTHEST straight-line distance (m) from the camera to the fighters' midpoint — the normal " +
             "third-person framing never goes beyond this (it comes closer when the fighters are close).")]
    public float maxDistance = 4f;
    [Tooltip("Extra distance added per metre of separation between the fighters, before clamping to min/max.")]
    public float separationPadding = 0.35f;

    [Header("Smoothing")]
    public float positionSmoothTime = 0.15f;
    public float rotationSmoothSpeed = 6f;

    [Tooltip("How quickly the camera's own orbit angle follows the live line between the two " +
             "fighters (see FightCameraController's own doc) — a SEPARATE, typically slower knob " +
             "than positionSmoothTime/rotationSmoothSpeed above, so a quick sidestep/backdash " +
             "changing the fighters' relative depth doesn't visibly whip the camera around it; only " +
             "a sustained change in the combat line's angle actually re-orbits the view.")]
    public float orbitSmoothSpeed = 3f;

    [Header("Intro travelling (Versus presentation — FightCameraController.BeginIntroTravelling)")]
    [Tooltip("Seconds of the pass from the close shot of the rival to the combat framing. The composer's " +
             "Intro line is read DURING it; the countdown waits for both (see FightFlowConfig.dialogueReadHoldSeconds). " +
             "0 = no pass (the camera starts on the combat framing).")]
    [Min(0f)] public float introTravellingSeconds = 3f;
    [Tooltip("Start of the pass, relative to the rival's feet: X = metres past the rival along the " +
             "fighters' line (away from the player), Y = height, Z = metres out to the side the combat " +
             "camera watches from. Keeps the rival (and its speech bubble) in shot the whole way.")]
    public Vector3 introStartOffset = new Vector3(0.6f, 1.6f, 2.2f);
    [Tooltip("Height on the rival the start shot looks at (m above the floor).")]
    public float introLookHeight = 1.5f;

    [Header("First person (the player's own eyes — FirstPersonHeadRig) — presentation only")]
    [Tooltip("Eye point above the player avatar's Head bone (m, canonical 1.75 m body, scaled with the " +
             "avatar). Used when the rig has no eye bones (MakeHuman).")]
    public float firstPersonEyeUp = FirstPersonHeadRig.DefaultEyeUp;
    [Tooltip("Lens in front of the Head bone pivot (m, canonical body, scaled with the avatar): just in front of " +
             "the face, so the inside of the own head is never in view.")]
    public float firstPersonEyeForward = FirstPersonHeadRig.DefaultEyeForward;
    [Tooltip("Rotation stabilisation (s): the view turns WITH the head, smoothed over this time so punches / " +
             "hit reactions never jerk it. Position is never smoothed (glued to the eye pivot).")]
    public float firstPersonRotationSmoothTime = 0.1f;
    [Tooltip("How much of the head's roll (sideways tilt) is levelled toward the horizon: 0 = full head roll, " +
             "1 = always level. Moderate by default — the view stays the head's.")]
    [Range(0f, 1f)] public float firstPersonRollDamping = 0.5f;
    [Tooltip("Eyes inside the head: the gaze turns from the head's facing toward the rival's upper body by at " +
             "most this many degrees (a boxing stance carries the chin down). 0 = pure head direction. While " +
             "downed (fall / KO) the gaze turns fully to the rival, from the eyes on the floor.")]
    [Range(0f, 45f)] public float firstPersonEyeGazeMaxAngle = 25f;
    [Tooltip("Seconds to blend from the third-person framing into the eyes when switching view (then rigid).")]
    public float firstPersonEnterBlendTime = 0.3f;
    [Tooltip("Fallback eye height above the floor (m) while the player has no Humanoid avatar yet.")]
    public float firstPersonFallbackEyeHeight = 1.63f;
    [Tooltip("Camera near clip plane while in first person (restored in third person): the lens sits just in " +
             "front of the face — a short near plane keeps the own hands visible without reaching into the head.")]
    public float firstPersonNearClip = 0.03f;
    [Tooltip("Vertical field of view (degrees) in first person (restored in third person): a little wider so " +
             "the own arms and the arena read naturally.")]
    [Range(40f, 100f)] public float firstPersonFieldOfView = 80f;

    [Header("Side stability — fighters crossing never flips the view")]
    [Tooltip("The camera keeps watching from the SAME side of the fighters' line when they cross " +
             "(the player may briefly be on screen-right). Only after the player has stayed crossed " +
             "this many seconds does the camera deliberately move round to put them back on the left. " +
             "0 or less = never swap sides.")]
    public float sideSwapDelay = 2.5f;
    [Tooltip("Max orbit speed (°/s) of that deliberate side swap — slow enough to read as intentional.")]
    [Min(1f)] public float sideSwapMaxSpeed = 110f;
    [Tooltip("No side swap while the fighters are closer than this (clinches/cross-ups resolve first).")]
    [Min(0f)] public float sideSwapMinSeparation = 1.3f;
}
