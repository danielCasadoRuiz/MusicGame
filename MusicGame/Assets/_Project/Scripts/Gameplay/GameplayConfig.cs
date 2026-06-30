using UnityEngine;

[CreateAssetMenu(fileName = "GameplayConfig", menuName = "MusicGame/Gameplay Config")]
public class GameplayConfig : ScriptableObject
{
    [Header("Track")]
    public float trackSpeed  = 10f;   // units per second — must match playerSpeed
    public float laneWidth   = 2.5f;
    public int   laneCount   = 3;
    public float warmupTime  = 3f;    // seconds of empty track before song starts

    [Header("Ring Heights")]
    public float groundY = 1f;        // Y for Kick / Snare / Beat rings
    public float jumpY   = 3f;        // Y for HiHat rings (requires jump)

    [Header("Player")]
    public float playerSpeed = 10f;   // must match trackSpeed for perfect sync
    public float strafeSpeed = 8f;    // units/s for lateral movement
    public float strafeLerp  = 10f;   // smoothing on X
    public float jumpForce   = 9f;
    public float gravity     = -22f;

    [Header("Player Surge (W / ↑)")]
    public float maxSurge   = 5f;    // max units the player can push ahead of song position
    public float surgeSpeed = 8f;    // units/s when building surge
    public float surgeDecay = 6f;    // units/s decay on release

    [Header("Ring Prefabs — leave empty to use coloured cubes")]
    public GameObject ringDefault;
    public GameObject ringKick;
    public GameObject ringSnare;
    public GameObject ringHiHat;
    public GameObject ringBeat;
    public GameObject ringOnset;

    [Header("Level Generation")]
    [Range(0f, 1f)]
    public float energyThreshold     = 0.25f;  // fraction of avg energy to include an onset
    public float minRingSpacing      = 0.20f;  // min seconds between rings in the same lane
    public bool  useOnsets           = true;
    public bool  useBeatGrid         = true;
    [Range(0.25f, 1f)]
    public float beatGridSubdivision = 1f;     // 1 = quarter notes, 0.5 = eighth notes

    [Header("World")]
    public bool createGroundPlane = true;
}
