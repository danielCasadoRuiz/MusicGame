using UnityEngine;

/// <summary>
/// How the Music Runner's level is generated from a song: path shape/width/turns (SnakeWayWorldGenerator),
/// the frequency-terrain ground mesh (height/color/resolution/smoothness/safety-limits, MusicWorldManager),
/// the playhead scanline overlay, and the musical-event timeline/classification stage
/// (GameplayTimeline) that decides WHICH musical moments become candidates in the first place —
/// distinct from the Collectibles config, which decides how a candidate becomes a physical,
/// visible, spawnable bonus. Split out of the old monolithic GameplayConfig — see
/// MusicRunnerGameplayConfig for the full picture.
/// </summary>
[CreateAssetMenu(fileName = "MusicRunnerLevelConfig", menuName = "MusicGame/MusicRunner/Level Generation Config")]
public class MusicRunnerLevelConfig : ScriptableObject
{
    // ── Path / World Generation ───────────────────────────────────────────────
    [Header("Path Generation")]
    [Tooltip("World Y of the path's start and neutral height")]
    public float pathBaseHeight              = 0f;
    [Tooltip("Seconds of song between path control points (smaller = more detail)")]
    public float pathControlPointInterval    = 2f;
    [Tooltip("Arc-length units between MusicPath samples (smaller = smoother but more memory)")]
    public float pathSampleSpacing           = 0.5f;
    [Tooltip("Maximum height variation above/below base height — the path's own big rolling " +
             "shape (hills/valleys as the song builds), driven by intensity/energy/buildup. " +
             "This is a DIFFERENT layer from Max Frequency Height below (the fine per-band " +
             "relief textured on top of this shape) — two genuinely different musical layers, " +
             "not two controls for the same thing.")]
    public float pathHeightAmplitude         = 6f;
    [Tooltip("Maximum horizontal turn angle (degrees) per control point interval. Kept small on " +
             "purpose — the player barely perceives these curves anyway, but they DO turn the " +
             "gameplay camera's yaw, which used to visibly swing the Horizon World's spectrum arc " +
             "off-center (see HorizonCameraController, which now also independently ignores the " +
             "gameplay camera's yaw as a second, robust layer of protection against this).")]
    public float pathMaxTurnAngle            = 2f;
    // Width is EDGE-TO-EDGE (full width, not half-width) in Unity units — and by this project's
    // existing scale (CharacterController radius 0.45, height 1.5) 1 unit ≈ 1 metre, so this
    // number is directly readable as metres. Both mesh generation (MusicWorldManager) and
    // gameplay limits (PlayerController's lateral clamp/fall detection, GameplayTimeline's
    // collectible lateral placement) read the SAME MusicPath.Sample.width — there is no
    // separate/duplicated width value anywhere, so changing these two scales the whole playable
    // zone coherently.
    [Tooltip("Path width (edge-to-edge, Unity units ≈ metres) at NEUTRAL music density (0.5). " +
             "The music still widens/narrows around this — see Width Music Variation.")]
    public float basePathWidth               = 13f;
    [Tooltip("How much profile.density can widen or narrow the path around basePathWidth — " +
             "SPARSE/quiet music narrows it (a more contained, focused space), DENSE/busy music " +
             "widens it (room to spread out activity/collectibles). Kept modest on purpose: " +
             "visible, never extreme. Actual width ranges from (basePathWidth - variation/2) at " +
             "minimum density to (basePathWidth + variation/2) at maximum density.")]
    public float pathWidthVariation          = 8f;
    [Tooltip("Random seed for path noise")]
    public int   pathSeed                    = 42;

    // ── World Mesh — Frequency Terrain ────────────────────────────────────────
    // The fine per-band relief textured onto the path (a DIFFERENT layer from pathHeightAmplitude
    // above — see its comment). height = normalizedFrequency(0..1) × Max Frequency Height, floor
    // is always exactly 0 — no other height multiplier exists.
    [Header("World Mesh — Frequency Terrain")]
    [Tooltip("Height (world units) at normalized frequency = 1.0. The floor is always exactly " +
             "0 — height = normalizedFrequency(0..1) × this, nothing else scales it. Layered ON " +
             "TOP of the path's own pathHeightAmplitude shape, not a replacement for it. Slope " +
             "safety limits below keep it walkable regardless of how large this is.")]
    public float maxFrequencyHeight = 3.85f;
    [Tooltip("Each visual band is normalized against its OWN energy at this percentile of its " +
             "whole-song distribution (reaching it = normalizedFrequency 1.0 for that band). " +
             "Energy envelopes are right-skewed, so a plain average is rarely exceeded; a high " +
             "percentile (default 0.9 = that band's own top 10% loudest moments) reliably reaches " +
             "the top of the range without comparing bands against each other or against " +
             "absolute magnitude. Shared by the terrain AND the UI equalizer — one normalization.")]
    [Range(0.5f, 0.99f)]
    public float frequencyColorReferencePercentile = 0.9f;
    [Tooltip("Reshapes normalizedFrequency (norm^gamma) before it drives BOTH height and colour " +
             "— always the same shared value, so they can never show contradictory information. " +
             "Energy distributions stay low-mid even after percentile referencing, which is why " +
             "the terrain read as mostly green/yellow with barely any red. gamma < 1 pushes " +
             "typical moments up (spends more of the gradient's orange/red range); 1 = no change; " +
             "very roughly, 0.6 ≈ a mid-range value now reaches ~2/3 up the gradient instead of " +
             "sitting at its own middle.")]
    [Range(0.2f, 2f)]
    public float frequencyContrastGamma = 0.6f;

    [Header("World Mesh — Resolution")]
    [Tooltip("Visible geometric subdivisions ACROSS the path width. Independent from the number " +
             "of real frequency bands (AudioAnalysisConfig.visualBandCount) — higher only means " +
             "smoother interpolation between the same band values, not more musical detail. Kept " +
             "separate from the longitudinal density below because they resolve different axes " +
             "(width vs length) — merging them would either waste polygons on one axis or starve " +
             "the other.")]
    public int   crossMeshSegments            = 24;
    [Tooltip("Ground-mesh row density ALONG travel (rows per world unit), independent of " +
             "MusicPath's own control-point/curve resolution (pathControlPointInterval).")]
    public float longitudinalSegmentsPerMeter = 4f;

    [Header("World Mesh — Smoothness")]
    [Tooltip("How organic vs. sharp the terrain reads ALONG travel (0 = minimum, 1 = maximum) — " +
             "does NOT affect the across-the-width look, see crossSmoothness for that. Secondary " +
             "layer on top of the real fix (Catmull-Rom interpolated sampling, see SongProfile." +
             "GetVisualBandEnergyAtSmooth): that's what keeps real musical peaks from being " +
             "flattened; this just rounds off residual roughness and the small seam the " +
             "longitudinal slope-safety clamp below can leave.")]
    [Range(0f, 1f)]
    public float pathSmoothness = 0.5f;
    [Tooltip("How organic vs. sharp the terrain reads ACROSS the width (0 = minimum, 1 = " +
             "maximum) — independent of pathSmoothness (along travel). Raise this if cross-" +
             "sections look sharp/triangular/pointy; it never changes crossMeshSegments (no extra " +
             "vertices/performance cost — same grid, just more CPU box-blur passes at rebuild " +
             "time, ~every 1.5s of travel, not per-frame).")]
    [Range(0f, 1f)]
    public float crossSmoothness = 0.35f;

    [Header("World Mesh — Safety Limits")]
    [Tooltip("Max world-space rise per unit distance ACROSS the width (lateral). Expressed as a " +
             "slope (rise/run), same units as CharacterController.slopeLimit's tangent — keep " +
             "below ~1.0 (45°) with headroom for the mesh's discretization. A correctness limit " +
             "(the CharacterController must be able to climb it), not a look/style choice.")]
    public float crossSlopeLimit        = 0.85f;
    [Tooltip("Max world-space rise per unit distance ALONG travel. Same slope units as " +
             "crossSlopeLimit — keep below ~1.0 (45°) with headroom.")]
    public float longitudinalSlopeLimit = 0.7f;

    [Header("World Mesh — Color")]
    [Tooltip("Vertex color at normalizedFrequency = 0.0 (after frequencyContrastGamma)")]
    public Color lowEnergyColor  = new Color(0.15f, 0.85f, 0.25f);
    [Tooltip("Vertex color at normalizedFrequency = 0.5")]
    public Color midEnergyColor  = new Color(0.95f, 0.85f, 0.10f);
    [Tooltip("Vertex color at normalizedFrequency = 1.0")]
    public Color highEnergyColor = new Color(0.95f, 0.15f, 0.10f);
    [Tooltip("Purely a COLOR remap (never touches height/geometry) — the processed value is " +
             "divided by this before picking green/yellow/red, then clamped, so the palette " +
             "reaches red at value == this instead of only at 1.0. Lower it if the terrain almost " +
             "never reads as red/hot; 1.0 = no exaggeration (original behavior).")]
    [Range(0.05f, 1f)] public float terrainColorRedThreshold = 0.75f;

    [Header("World Mesh — Lighting")]
    [Tooltip("Minimum light contribution in full shadow, as a fraction of the vertex's musical " +
             "colour (0 = can go to black, 1 = shadow has no visible effect). Keeps the green/" +
             "yellow/red gradient readable even in shadowed areas.")]
    [Range(0f, 1f)] public float terrainAmbientFloor = 0.35f;

    // ── Playhead Scanline ────────────────────────────────────────────────────────
    // A transversal neon line on the ground mesh itself, tracking the current music position —
    // pure shader work (VertexColorLit.shader), no extra geometry/GameObjects. Each ground
    // window ("chunk") gets its own start/end MusicDistance via a MaterialPropertyBlock (no new
    // Material instance); MusicWorldManager pushes the few frame-varying values (current
    // position + offset, frequency texture) as GLOBAL shader properties once per frame, so every
    // chunk's shader resolves independently whether the playhead falls inside it — no per-frame
    // CPU lookup of "which chunk is active".
    [Header("Playhead Scanline")]
    public bool  playheadEnabled = true;
    [Tooltip("How far AHEAD of the player's canonical music-distance the line sits, in the same " +
             "distance units as everything else here (== world meters along the path) — e.g. 1 " +
             "means the line always sits ~1m ahead of the player.")]
    public float playheadOffset = 1f;
    [Tooltip("World-space width (meters) of the glowing band, measured ALONG the path (converted " +
             "to UV internally against the CURRENT chunk's own length, so it always reads as the " +
             "same physical width regardless of how long a given ground window happens to be) — " +
             "constant regardless of how long the current ground window happens to be. Small on " +
             "purpose for a thin laser-scan look; raise it for a thicker band.")]
    public float playheadLineWidth = 0.06f;
    [Tooltip("Multiplies the line's color before output — values > 1 push it into HDR so Bloom " +
             "(URP Volume) picks it up as a glow. Has no effect without Bloom enabled.")]
    public float playheadEmissionIntensity = 3f;
    [Tooltip("true: the line shows the CURRENT frequency spectrum left(low)->right(high), reusing " +
             "the exact same band data + green/yellow/red palette (lowEnergyColor/midEnergyColor/" +
             "highEnergyColor) the ground's own color already uses. false: a single flat color " +
             "(playheadSingleColor) — usually reads cleaner/more like a deliberate UI element.")]
    public bool  playheadUseFrequencyColors = false;
    [Tooltip("Flat HDR color used when playheadUseFrequencyColors is false — push it above 1 " +
             "intensity (the HDR color picker's slider) for a neon/glowing look with Bloom, on " +
             "top of the separate playheadEmissionIntensity multiplier.")]
    [ColorUsage(true, true)]
    public Color playheadSingleColor = new Color(0.15f, 1f, 0.4f, 1f);

    // ── World (misc) ──────────────────────────────────────────────────────────
    [Header("World")]
    public bool createGroundPlane = false;  // disabled: path is the ground now

    // ── Level Generation (musical event timeline & classification) ───────────
    [Header("Level Generation — Musical Event Timeline")]
    [Range(0f, 1f)]
    public float energyThreshold     = 0.25f;
    [Tooltip("Maximum minimum-spacing between two consecutive onsets of the SAME classified " +
             "type. Acts as a ceiling, not a fixed floor: GameplayTimeline derives a tighter, " +
             "tempo-relative spacing (≈60% of a 16th note) for faster songs so a real repeated " +
             "pattern (e.g. 16th-note hi-hats) doesn't get half its hits silently discarded.")]
    public float minRingSpacing      = 0.20f;
    public bool  useOnsets           = true;
    public bool  useBeatGrid         = true;
    [Range(0.25f, 1f)]
    public float beatGridSubdivision = 1f;
    [Tooltip("Minimum relative margin the winning Snare-vs-HiHat band score must have over the " +
             "runner-up before ClassifyOnset commits to that label. Below this margin the " +
             "evidence is genuinely ambiguous — it becomes generic Onset instead of a guess. " +
             "Higher = more honest/conservative (more things fall back to Onset), lower = more " +
             "willing to commit to a specific instrument on thinner evidence.")]
    [Range(0f, 0.6f)]
    public float classificationConfidenceMargin = 0.18f;
}
