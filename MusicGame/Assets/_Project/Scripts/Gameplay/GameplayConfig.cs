using UnityEngine;

[CreateAssetMenu(fileName = "GameplayConfig", menuName = "MusicGame/Gameplay Config")]
public class GameplayConfig : ScriptableObject
{
    // ── Track ─────────────────────────────────────────────────────────────────
    [Header("Track")]
    public float warmupTime  = 3f;

    // ── Player ────────────────────────────────────────────────────────────────
    [Header("Player")]
    public float playerSpeed = 10f;
    public float strafeSpeed = 8f;
    public float strafeLerp  = 10f;
    public float jumpForce   = 9f;
    public float gravity     = -22f;

    [Header("Player Surge (W / ↑)")]
    public float maxSurge   = 5f;
    public float surgeSpeed = 8f;
    public float surgeDecay = 6f;

    [Header("Player — Air Control")]
    [Tooltip("true: full lateral control while airborne (strafe input keeps steering mid-jump, " +
             "same as it always has). false: lateral input has no effect while airborne — " +
             "whatever lateral momentum existed the instant the player left the ground carries " +
             "through unchanged (no decay, no new player-driven acceleration/direction change) " +
             "until landing, when full control returns immediately.")]
    public bool allowAirControl = false;

    // ── Ring Prefabs ──────────────────────────────────────────────────────────
    [Header("Ring Prefabs — leave empty to use coloured cubes")]
    public GameObject ringDefault;
    public GameObject ringKick;
    public GameObject ringSnare;
    public GameObject ringHiHat;
    public GameObject ringBeat;
    public GameObject ringOnset;
    public GameObject ringPeak;
    public GameObject ringImpact;

    // ── Ring Colors ───────────────────────────────────────────────────────────
    // Single source of truth for a RingType's colour — consumed by BOTH the world (every
    // pooled ring's material, whether it uses a custom prefab or the fallback cube) AND the UI
    // (live HUD, end screen), via RingColor() below, so a collected bonus's colour in-world can
    // never disagree with its colour on screen.
    [Header("Ring Colors — world objects AND UI share this")]
    public Color kickColor   = new Color(0.90f, 0.22f, 0.10f);
    public Color snareColor  = new Color(0.90f, 0.80f, 0.10f);
    public Color hiHatColor  = new Color(0.15f, 0.62f, 1.00f);
    public Color beatColor   = new Color(0.62f, 0.20f, 0.90f);
    public Color onsetColor  = new Color(0.10f, 0.90f, 0.50f);
    public Color peakColor   = new Color(1.00f, 0.92f, 0.55f);
    public Color impactColor = new Color(1.00f, 0.40f, 0.05f);

    public Color RingColor(RingType type) => type switch
    {
        RingType.Kick   => kickColor,
        RingType.Snare  => snareColor,
        RingType.HiHat  => hiHatColor,
        RingType.Beat   => beatColor,
        RingType.Onset  => onsetColor,
        RingType.Peak   => peakColor,
        RingType.Impact => impactColor,
        _               => Color.white,
    };

    [Tooltip("Seconds the live HUD's category label flashes at full colour right after a " +
             "collection, fading back to a dim neutral baseline between collections.")]
    public float uiCollectFlashDuration = 0.6f;

    // ── Collectibles — Spawn Enable ───────────────────────────────────────────
    // Purely a GAMEPLAY/visual filter — analysis, classification and SongProfile/timeline
    // candidate generation are entirely unaffected. A disabled type's musical moments are still
    // detected and still exist as candidates; GameplayTimeline just never turns them into a
    // TimelineEvent, so they never reach the pool/spawn system at all (never activated then
    // immediately hidden).
    [Header("Collectibles — Spawn Enable (visual/gameplay filter, NOT analysis)")]
    public bool spawnKick   = true;
    public bool spawnSnare  = true;
    public bool spawnHiHat  = true;
    public bool spawnBeat   = true;
    [Tooltip("Onset is the honest fallback for a real detected transient we can't confidently " +
             "classify as Kick/Snare/HiHat (see GameplayTimeline.ClassifyOnset) — real musical " +
             "information, not noise, so it's ON by default.")]
    public bool spawnOnset  = true;
    [Tooltip("Impact is a MACRO moment (see MacroEventType), not a Micro rhythm event — placed " +
             "centered on the path, independent of Kick/Snare/HiHat thinning. On by default so " +
             "it's visible/testable as its own distinct thing.")]
    public bool spawnImpact = true;
    [Tooltip("Peak (the song's single loudest frame) mostly duplicates an Impact that's already " +
             "there — see MacroEventType.Peak / GameplayTimeline.BuildMacroEvents. OFF by " +
             "default: it now only tags the nearest Impact/Drop as the climax instead of " +
             "spawning its own big generic cube. Turn on only if you want a standalone bonus for " +
             "the rare case a Peak has nothing nearby to tag.")]
    public bool spawnPeak   = false;

    public bool IsSpawnEnabled(RingType type) => type switch
    {
        RingType.Kick   => spawnKick,
        RingType.Snare  => spawnSnare,
        RingType.HiHat  => spawnHiHat,
        RingType.Beat   => spawnBeat,
        RingType.Onset  => spawnOnset,
        RingType.Impact => spawnImpact,
        RingType.Peak   => spawnPeak,
        _               => true,
    };

    // ── Scoring ───────────────────────────────────────────────────────────────
    [Header("Scoring")]
    [Tooltip("Points awarded for collecting the single Peak ring placed at the song's most intense moment")]
    public int peakBonusPoints = 500;
    [Tooltip("Points awarded for collecting an Impact ring (strong hit/drop moment)")]
    public int impactBonusPoints = 300;

    [Tooltip("Base points for Kick/Snare/HiHat/Beat/Onset before the rarity and timing " +
             "multipliers are applied.")]
    public float baseScorePerRing = 10f;
    [Tooltip("Output range of the rarity multiplier (X = most common type in this song's " +
             "final timeline, Y = rarest). 1.0 = exactly at the median.")]
    public Vector2 rarityMultiplierRange = new Vector2(0.85f, 1.15f);
    [Tooltip("How many natural-log units of count-vs-median ratio it takes to reach the " +
             "extreme of rarityMultiplierRange. 1.5 ≈ a type needs to appear ~4.5x more or " +
             "less than the median type to hit the cap. Higher = flatter/harder to reach the extremes.")]
    public float rarityLogSpread = 1.5f;

    [Tooltip("Seconds of song-time timing error beyond which timing stops mattering any further " +
             "(the floor value on timingQualityCurve applies from here on). Also defines the " +
             "curve's x-axis scale: fraction = timingError / maxUsefulTimingWindow.")]
    public float maxUsefulTimingWindow = 0.8f;
    [Tooltip("X = |collectionSongTime - ring's eventTime| / maxUsefulTimingWindow (0..1). " +
             "Y = fraction of (rarity-adjusted) points awarded. Default: flat 100% up to ~0.12s " +
             "off, then eases down to a 55% floor by maxUsefulTimingWindow — never zero, a late/" +
             "early pickup still counts, just for less.")]
    public AnimationCurve timingQualityCurve = new AnimationCurve(
        new Keyframe(0f,    1.00f),
        new Keyframe(0.15f, 1.00f),
        new Keyframe(1f,    0.55f));

    [Tooltip("Reshapes the raw NormalizedScore (X, 0..1 — the SAME value used internally for " +
             "scoring/session accumulation, left completely unchanged) into a DISPLAY rating " +
             "(Y, 0..1) for the end screen ONLY — a presentation layer, not a new scoring system. " +
             "Given how demanding this game is, a raw ~25% can already be a genuinely good run; " +
             "this is what lets the SHOWN percentage/label reflect that instead of reading like " +
             "a school grade. Default: ~5%→POBRE, ~15%→FLUIX, ~25%→DECENT, ~35%→MOLT BO, ~40%+→BRUTAL.")]
    public AnimationCurve performanceRatingCurve = new AnimationCurve(
        new Keyframe(0f,    0f),
        new Keyframe(0.05f, 0.15f),
        new Keyframe(0.15f, 0.35f),
        new Keyframe(0.25f, 0.55f),
        new Keyframe(0.35f, 0.80f),
        new Keyframe(0.40f, 0.95f),
        new Keyframe(1f,    1f));

    // ── Level Generation ──────────────────────────────────────────────────────
    [Header("Level Generation")]
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

    // ── Micro — Density & Confidence Thinning ─────────────────────────────────
    [Header("Micro — Density & Confidence Thinning")]
    [Tooltip("A Micro candidate (Kick/Snare/HiHat/Onset/Beat) with confidence >= this bypasses " +
             "density thinning entirely — never lost to a random roll. A confidently classified " +
             "onset (see GameplayTimeline.ClassifyOnset) starts at confidence 0.6, so at the 0.5 " +
             "default EVERY genuinely classified drum hit always gets gameplay representation. " +
             "Only an ambiguous Onset fallback (0.4) and the synthetic beat-grid filler (0.15) " +
             "stay below this and remain subject to thinning.")]
    [Range(0f, 1f)]
    public float confidenceKeepThreshold = 0.5f;
    [Tooltip("How much a candidate's own musical strength raises its odds of surviving density " +
             "thinning above collectibleDensity. 0 = flat collectibleDensity for everyone (old " +
             "behaviour). 1 = a full-strength hit is (near-)guaranteed to survive, while a weak " +
             "one still only gets collectibleDensity's odds. Thinning should mostly remove weak/" +
             "marginal/uncertain detections, not break a clear rhythmic pattern.")]
    [Range(0f, 1f)]
    public float strengthThinningBias = 1.0f;
    [Tooltip("During a buildup (profile.buildupCurve), a Micro candidate's survival odds are " +
             "multiplied by up to this much at full buildup intensity — collectibles get " +
             "progressively denser leading into an Impact/Drop. 1 = no effect.")]
    public float buildupDensityBoost = 1.6f;
    [Tooltip("A non-confident Micro candidate (ambiguous Onset fallback, or Beat filler) within " +
             "this many seconds of a Macro Impact/Drop is dropped entirely — redundant visual " +
             "noise, since the Impact already represents that moment. Never absorbs a " +
             "confidently classified Kick/Snare/HiHat.")]
    public float ambiguousAbsorptionWindow = 0.3f;

    // ── Collectibles ──────────────────────────────────────────────────────────
    [Header("Collectibles — Density & Placement")]
    [Tooltip("Baseline fraction of WEAK/marginal/uncertain Micro candidates that survive " +
             "thinning (strong, confidently-classified ones survive at up to ~100% regardless " +
             "of this — see strengthThinningBias/confidenceKeepThreshold). Does not affect " +
             "Impact/Peak — those are Macro, placed independently, never thinned. Lower = " +
             "sparser, higher = denser.")]
    [Range(0f, 1f)] public float collectibleDensity = 0.7f;
    [Tooltip("Approximate collectible radius, used with collectibleLateralMargin to keep it " +
             "clear of the path edge")]
    public float collectibleRadius        = 0.5f;
    [Tooltip("Extra clearance kept between a collectible's edge and the path's walkable edge")]
    public float collectibleLateralMargin = 0.6f;
    [Tooltip("Extra safety margin above the collectible's own half-height when computing the " +
             "minimum clearance above the real surface. This is a FLOOR that OVERRIDES " +
             "bonusMinJumpHeightFactor whenever it would be higher (see CollectibleFloor) — with " +
             "the actual ring meshes (small, ~0.15 half-height) the old 0.3 default made this " +
             "physical floor bigger than the jump-based design floor for every single type, so " +
             "EVERY bonus sat at ~0.5-0.6 no matter how low the curve rolled, regardless of " +
             "verticalOffsetCurve tuning. Lowered so the jump-based range is normally what decides " +
             "height; this only kicks in as a true embedding backstop. Raise it again only if " +
             "collectibles visibly clip into the ground.")]
    public float collectibleSurfaceClearance = 0.05f;
    [Tooltip("Bottom of the bonus vertical range, as a 0..1 FRACTION of maxJumpHeight " +
             "(jumpForce²/(2·|gravity|), the player's own real jump reach) — never an absolute " +
             "world-unit height. Hard-floored per collectible type at generation time to its own " +
             "physical clearance (mesh half-height + collectibleSurfaceClearance) no matter what " +
             "this is set to, so nothing can ever be embedded. Change jumpForce/gravity and every " +
             "bonus height adapts automatically — no world-unit range to keep in sync by hand.")]
    [Range(0f, 1f)] public float bonusMinJumpHeightFactor = 0.15f;
    [Tooltip("Top of the bonus vertical range, as a 0..1 fraction of maxJumpHeight. Normal bonuses " +
             "use the FULL [bonusMinJumpHeightFactor..bonusMaxJumpHeightFactor] range (shaped by " +
             "verticalOffsetCurve below). OffTrack bonuses always use the TOP 30% of this SAME " +
             "range (see GameplayTimeline.EmitSingle) — never a second, independent range. Lower " +
             "this if bonuses still feel too high/floaty overall; raise it for a higher, more " +
             "jump-heavy ceiling — this is the single knob for 'how high can the tallest bonus be'.")]
    [Range(0f, 1f)] public float bonusMaxJumpHeightFactor = 0.6f;
    [Tooltip("X = a uniform 0..1 roll. Y = 0..1 fraction of the bonus vertical range " +
             "(bonusMinJumpHeightFactor..bonusMaxJumpHeightFactor of maxJumpHeight) the collectible " +
             "actually lands at. Reshapes the roll so the large majority hug the floor (no jump " +
             "needed — lateral movement stays the main gameplay), a small minority land moderately " +
             "elevated, and only a rare handful reach the top (a deliberate jump) — rare, not scarce. " +
             "Default keeps the curve under ~0.08 for the bottom ~82% of rolls, ~0.08→0.3 for the " +
             "next ~13%, and only the top ~5% climb to 1.0. To recalibrate 'how often should this " +
             "need a jump' without touching the height RANGE itself, move the middle keyframes' " +
             "TIME (x) — push them right (e.g. 0.9/0.97) for even fewer jumps, left (e.g. 0.7/0.9) " +
             "for more. Move their VALUE (y) down for an even flatter/closer-to-ground plateau.")]
    public AnimationCurve verticalOffsetCurve = new AnimationCurve(
        new Keyframe(0f,    0f),
        new Keyframe(0.82f, 0.08f),
        new Keyframe(0.95f, 0.3f),
        new Keyframe(1f,    1f));
    [Tooltip("0..1 chance a short run of consecutive collectibles becomes one coordinated small " +
             "height pattern (arc / stair) instead of independent random heights")]
    [Range(0f, 1f)] public float patternProbability = 0.12f;
    [Tooltip("Seed for collectible lateral/vertical placement — same song + same seed = same layout")]
    public int   collectibleSeed = 1337;

    // ── Off-Track Bonuses ─────────────────────────────────────────────────────
    // Risky variants of an ordinary single collectible (GameplayTimeline.EmitSingle only —
    // pattern runs are untouched): placed just beyond the track's real local half-width instead
    // of safely inside it, so reaching one requires jumping off the path and using air control
    // to get back — see PlayerController.IsBelowTrackSurface/FallRespawnSystem for why that's
    // now survivable. isOffTrack is decided and stored on the TimelineEvent right here, at
    // generation time — never re-derived from position later.
    [Header("Off-Track Bonuses")]
    [Tooltip("0..1 chance an ordinary single collectible becomes an off-track one instead.")]
    [Range(0f, 1f)] public float offTrackBonusChance = 0.15f;
    [Tooltip("How far BEYOND the track's real local half-width (path.GetWidth/2, not world X/Z) " +
             "an off-track bonus can additionally sit — a small, jump+air-control-reachable " +
             "extension, picked randomly left or right each time.")]
    public float offTrackBonusMaxOffset = 1.5f;
    [Tooltip("Score multiplier for collecting an off-track bonus vs. an equivalent normal one — " +
             "risk/reward, applied on top of the SAME ScoreFor() calculation every other " +
             "collectible uses (rarity/timing/type all still apply first).")]
    public float offTrackBonusScoreMultiplier = 1.5f;
    [Tooltip("Bottom of the OFF-TRACK bonus height range, as a 0..1 fraction of maxJumpHeight " +
             "directly (jumpForce²/(2·|gravity|)) — deliberately INDEPENDENT of " +
             "bonusMinJumpHeightFactor/bonusMaxJumpHeightFactor (the normal-bonus range). An " +
             "off-track bonus must always demand something close to the player's FULL jump " +
             "capability, regardless of how conservatively the normal-bonus ceiling is tuned — " +
             "if the two shared one range, lowering the normal ceiling would quietly lower how " +
             "high off-track bonuses sit too, even though those are meant to stay demanding.")]
    [Range(0f, 1f)] public float offTrackBonusMinJumpHeightFactor = 0.7f;
    [Tooltip("Top of the OFF-TRACK bonus height range, as a 0..1 fraction of maxJumpHeight " +
             "directly. 1.0 = right at the theoretical peak of a full jump.")]
    [Range(0f, 1f)] public float offTrackBonusMaxJumpHeightFactor = 1.0f;

    // ── Object Pooling ────────────────────────────────────────────────────────
    [Header("Object Pooling")]
    public int   poolInitialSize = 8;
    public int   poolMaxSize     = 25;
    [Tooltip("Seconds of song time ahead of the player's ACTUAL distance (musicDistance + " +
             "forwardOffset — includes surge) to activate events from the pool")]
    public float spawnLookAhead  = 1.0f;
    [Tooltip("World units behind the player's ACTUAL distance (musicDistance + forwardOffset) " +
             "an uncollected collectible is allowed to remain before it's recycled. Generous on " +
             "purpose — a collectible should only ever vanish once it's genuinely unreachable, " +
             "never while still visible/reachable.")]
    public float recycleGrace    = 18f;

    // ── Beat Feedback ─────────────────────────────────────────────────────────
    [Header("Beat Feedback")]
    [Tooltip("Seconds BEFORE eventTime that a ring's beat pulse (instant scale punch + camera " +
             "kick) fires. Default 0 — the punch is now instantaneous (no ease-in), so it " +
             "should land exactly on the musical moment, not early; a nonzero value here " +
             "systematically desyncs the visual pop from what you actually hear. Only raise it " +
             "if you deliberately want the punch to read as slightly ahead of the beat.")]
    public float pulseLeadTime   = 0f;

    // ── Visual Anticipation ───────────────────────────────────────────────────
    // Deliberately SPATIAL, not time-based — the path can curve/climb/descend, and this is a
    // "how far ahead on the path" question, not a "how many seconds" one (unlike pulseLeadTime
    // above, which IS about timing and stays exactly on the beat, untouched by this).
    [Header("Visual Anticipation")]
    // World units ahead of the player's actual distance (PlayerController.ActualDistance) at
    // which a ring/bonus starts visually growing/revealing — a separate, EARLY, purely visual
    // cue (a mild GameplayManager.RevealEvent → RingController.Pulse(0)) so you SEE it
    // materialize ahead of you instead of right as you cross it. The real musical reaction
    // (full-strength Pulse + BeatPulseEvent/camera kick, via pulseLeadTime above) still fires
    // exactly on the beat, completely unaffected by either of these. Keep both comfortably
    // BELOW spawnLookAhead*playerSpeed, or a ring wouldn't exist in the pool yet when its reveal
    // distance is reached. Split Third/First Person because perceived distance differs a lot
    // between the two — GetBonusVisualActivationDistance() below is the single place that picks
    // between them, so nobody else needs an if(firstPerson) branch of their own.
    [Tooltip("Reveal distance while in Third Person.")]
    public float bonusVisualActivationDistanceThirdPerson = 8f;
    [Tooltip("Reveal distance while in First Person — perception of distance is different up " +
             "close, so this is deliberately a separate value from the Third Person one.")]
    public float bonusVisualActivationDistanceFirstPerson = 8f;

    /// <summary>Single source of truth for "which of the two values applies right now" —
    /// GameplayManager just calls this (via CameraFollow.EffectiveBonusVisualActivationDistance,
    /// which supplies the current CameraViewMode), never branching on the mode itself.</summary>
    public float GetBonusVisualActivationDistance(CameraViewMode mode) =>
        mode == CameraViewMode.FirstPerson ? bonusVisualActivationDistanceFirstPerson : bonusVisualActivationDistanceThirdPerson;

    // ── Music Environment (background color) ──────────────────────────────────
    // Ambient mood driven by the same SongProfile analysis, not a per-frame FFT visualizer and
    // not a naive "note = color" lookup (a full mix is polyphonic — chroma is an energy
    // DISTRIBUTION over the 12 pitch classes). See MusicEnvironmentController for the full
    // reasoning. Two smoothing stages keep it slow/musical instead of flickery: feature-level
    // (chromaEMAAlpha, sampled every colorSampleInterval) and display-level
    // (colorSmoothingTimeConstant).
    [Header("Music Environment (background color)")]
    public bool  enableMusicEnvironment      = true;
    [Tooltip("Seconds between feature samples (chroma/intensity). NOT a per-frame update — " +
             "sampling less often than every frame is itself part of what keeps this slow and " +
             "musical instead of jittery.")]
    public float colorSampleInterval         = 0.4f;
    [Tooltip("Exponential-moving-average smoothing applied to the sampled chroma/intensity " +
             "each tick (0..1). Lower = slower-evolving target hue/brightness.")]
    [Range(0.01f, 1f)]
    public float chromaEMAAlpha              = 0.12f;
    [Tooltip("Seconds for the DISPLAYED background color to chase its (already-smoothed) " +
             "target. This is the main anti-flicker knob — higher = calmer/slower.")]
    public float colorSmoothingTimeConstant  = 3f;
    [Tooltip("Minimum harmonic clarity (0..1 — normalized chroma vector magnitude; low during a " +
             "drum break/noise/silence, high when one tonal centre clearly dominates) required " +
             "before the target hue updates. Below this, hue HOLDS its last value instead of " +
             "drifting to an arbitrary angle from a directionless chroma vector.")]
    [Range(0f, 1f)]
    public float hueConfidenceThreshold      = 0.12f;
    [Range(0f, 1f)] public float baseSaturation = 0.55f;
    [Range(0f, 1f)] public float baseValue      = 0.35f;
    [Tooltip("Saturation/value multiplier at full buildup intensity (profile.buildupCurve). " +
             "1 = buildup has no visual effect on color.")]
    public float buildupIntensityBoost       = 1.4f;
    [Tooltip("Fraction of the remaining gap to the target color that snaps instantly on an " +
             "Impact/Drop MacroEvent — reads as the transition 'culminating' at that moment.")]
    [Range(0f, 1f)]
    public float macroSnapFraction           = 0.5f;

    // ── Path / World Generation ───────────────────────────────────────────────
    // Simplified this iteration: the many near-duplicate width/smoothing/subdivision fields
    // this system used to have are consolidated below into fewer, more directly interpretable
    // controls (see each field's own comment for what it absorbed and why).
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

    // ── Fall Off Path ─────────────────────────────────────────────────────────
    [Header("Fall Off Path")]
    public bool  enableFallOffPath   = true;
    [Tooltip("How far BELOW the track's local surface plane (PlayerController.IsBelowTrackSurface, " +
             "using the path sample's own position/up at the player's distance — not a world Y) " +
             "the player must sink before a fall is confirmed. Being laterally outside the track " +
             "but still above this plane (e.g. mid-air over the void, correctable with air control " +
             "— see PlayerController.UpdateLateralOffset/config.allowAirControl) no longer counts " +
             "as a fall by itself. Small on purpose: it's just a buffer against false " +
             "positives right at the surface, not a tolerance for genuinely hanging in the air.")]
    public float fallDeathDepth      = 0.1f;
    [Tooltip("Duration of the audio fade-out on fall")]
    public float fallFadeOutDuration = 0.6f;

    // ── Fall / Respawn — Score protection & penalty ───────────────────────────
    // No discrete checkpoints anymore — a fall respawns the player at the EXACT songTime they
    // fell at (see FallRespawnSystem), and score protection is a CONTINUOUS function of song
    // progress instead of "everything since the last checkpoint". At the moment of a fall:
    //   fallProgress      = (fallSongTime - warmupTime) / song duration      — 0..1
    //   protectedProgress = fallProtectionCurve.Evaluate(fallProgress)       — 0..1, always <= fallProgress
    // Every individual pickup's own (songTime, points) is kept (GameplayManager._pickupHistory,
    // per type, never cleared mid-run) — pickups collected AFTER protectedProgress's song time
    // are "at risk"; everything before it is permanently safe. fallPenaltyCurve (below, UNCHANGED
    // mechanism) then removes a FRACTION of just that at-risk pool — never a flat percentage of
    // the whole accumulated score, so a fall near the end of a song can't wipe out a run's worth
    // of progress the way a naive "% of total" penalty would.
    [Header("Fall / Respawn — Score Protection")]
    [Tooltip("X = fallProgress (0..1, song progress at the moment of the fall). Y = " +
             "protectedProgress (0..1, always <= X) — how far into the song points are already " +
             "permanently safe. Points collected between Y and X are what the penalty below can " +
             "actually remove. Example: fallProgress=0.70 -> protectedProgress=0.55 means only " +
             "points collected between 55% and 70% of the song are at risk.")]
    public AnimationCurve fallProtectionCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.7f, 0.55f), new Keyframe(1f, 0.82f));

    [Header("Fall / Respawn — Penalty")]
    [Tooltip("X = fallProgress (0..1). Y = fraction of the AT-RISK pool (see " +
             "fallProtectionCurve above) lost — never a fraction of the whole score. Default: " +
             "~60% early in the song, ~30% near the end — a mistake late in an otherwise-good " +
             "run costs less.")]
    public AnimationCurve fallPenaltyCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 0.3f);
    [Tooltip("Final score multiplier applied only if the run ends with zero falls " +
             "(fallCount == 0). 1.20 = +20% for a perfectly clean run. Not applied at all if " +
             "even one fall occurred.")]
    public float noFallScoreMultiplier = 1.2f;

    // ── Respawn ───────────────────────────────────────────────────────────────
    [Header("Respawn")]
    [Tooltip("Pause between audio fade-out and repositioning")]
    public float checkpointRespawnDelay         = 0.5f;
    [Tooltip("Duration of the audio fade-in after respawn")]
    public float checkpointMusicFadeInDuration  = 0.8f;

    // ── Checkpoints ───────────────────────────────────────────────────────────
    // No longer used for respawn or scoring (see above) — CheckpointSystem still runs purely as
    // a passive/debug system (F1 debug HUD + gizmos), so it's kept rather than ripped out.
    [Header("Checkpoints (debug/gizmos only — no longer drives respawn or scoring)")]
    public bool  enableCheckpoints            = true;
    [Tooltip("Song-time interval in seconds between auto-generated checkpoints")]
    public float checkpointIntervalSeconds    = 60f;

    // ── World (misc) ──────────────────────────────────────────────────────────
    [Header("World")]
    public bool createGroundPlane = false;  // disabled: path is the ground now

    [Header("World Mesh — Lighting")]
    [Tooltip("Minimum light contribution in full shadow, as a fraction of the vertex's musical " +
             "colour (0 = can go to black, 1 = shadow has no visible effect). Keeps the green/" +
             "yellow/red gradient readable even in shadowed areas.")]
    [Range(0f, 1f)] public float terrainAmbientFloor = 0.35f;

    // ── Gameplay Fog (circuit depth-fade — a different concept from Horizon Haze) ─────────────
    // Real distance fog for the GAMEPLAY world only (ground/circuit + rings/bonuses) so distant
    // parts of the track fade into darkness instead of being perfectly visible the whole time.
    // Implemented via Unity's own RenderSettings.fog (Linear mode) + a small explicit fog blend
    // added to VertexColorLit.shader (the ground) — ring/bonus materials use the built-in URP Lit
    // shader, which already blends RenderSettings.fog automatically, no extra code needed there.
    // Deliberately NEVER touches the Horizon World: none of its shaders (HorizonBar/CheapWater/
    // ProceduralSky/mountain layers) sample fog at all, so RenderSettings.fog being globally "on"
    // has zero visual effect on them regardless — see GameplayFogController.
    [Header("Gameplay Fog — Circuit Distance Fade (NOT Horizon Haze)")]
    public bool  gameplayFogEnabled = true;
    public Color gameplayFogColor = new Color(0.04f, 0.045f, 0.10f);
    [Tooltip("World-units distance from the camera where fog starts becoming visible.")]
    public float gameplayFogStartDistance = 18f;
    [Tooltip("World-units distance from the camera where fog reaches full density.")]
    public float gameplayFogEndDistance = 75f;
    [Tooltip("Steepens (>1) or relaxes (<1) the fog falloff between Start/End Distance without " +
             "having to re-tune both distances by hand — 1 = falloff exactly as authored above.")]
    [Range(0.1f, 3f)] public float gameplayFogStrength = 1f;

    // ── Horizon World ─────────────────────────────────────────────────────────
    // A separate, camera-stacked 3D world (HorizonCameraController/SpectrumBars3D/
    // HorizonBarsReflectionCamera/HorizonWater/ProceduralSky/HorizonMountainLayers, all in
    // Assets/_Project/Scripts/World/Horizon) holding real volumetric spectrum bars + a mirrored
    // RenderTexture bar reflection + water + procedural sky + PNG mountain layers. Rendered by
    // its OWN camera (Base of a URP camera stack; the gameplay Main Camera becomes an Overlay
    // with Depth Only clear) sitting at a near-FIXED position (only horizonParallaxFactor of the
    // main camera's own movement bleeds through) that copies the main camera's rotation/FOV every
    // frame — genuinely distant/parallax-free, unlike re-centering geometry on the camera every
    // frame (which still reads as "attached", since it never has ANY relative motion at all).
    // Bar amplitude reads the SAME MusicWorldManager.NormalizedBandValue data the ground mesh
    // already uses (remapped onto Bar Count columns) — never a second analysis.
    [Header("Horizon World — Enable")]
    public bool enableHorizonWorld = true;
    [Tooltip("0 = Horizon Camera position never moves (pure skybox-like distant backdrop). Small " +
             "values (e.g. 0.02-0.1) let a fraction of the main camera's own translation bleed " +
             "through for a subtle sense of depth/parallax, without ever looking 'attached'.")]
    [Range(0f, 1f)] public float horizonParallaxFactor = 0.03f;
    [Tooltip("Bloom intensity — applied onto whichever Volume/profile the scene actually uses " +
             "(see horizonBloomForceApply). Pushed up for this iteration's 'dark scene, very " +
             "bright neon' look.")]
    public float horizonBloomIntensity = 1.1f;
    [Tooltip("Bloom threshold — lower = more of the scene starts glowing, not just the brightest " +
             "highlights.")]
    [Range(0f, 2f)] public float horizonBloomThreshold = 0.6f;
    [Tooltip("Bloom scatter (URP's blur-radius-like spread) — higher = softer/wider halo around " +
             "each bright neon source.")]
    [Range(0f, 1f)] public float horizonBloomScatter = 0.7f;
    [Tooltip("Applied every frame onto WHATEVER Volume/profile the scene actually uses for Bloom " +
             "(hand-authored 'Global Volume' included) — HorizonWorld no longer silently skips " +
             "applying these values just because a Volume already exists in the scene.")]
    public bool horizonBloomForceApply = true;

    [Header("Horizon World — Bars: Arc Shape")]
    [Tooltip("Number of vertical bars around the arc — independent from the real analysis " +
             "resolution (AudioAnalysisConfig.visualBandCount); real band data is averaged/" +
             "remapped onto however many bars this is.")]
    [Range(6, 64)] public int horizonBarCount = 48;
    [Tooltip("Total angular span of the arc, centered directly ahead. 180 = a full semicircle.")]
    public float horizonArcSpanDegrees = 180f;
    [Tooltip("Radius of the arc (Horizon World units — this is its OWN small, fixed coordinate " +
             "space, not the gameplay world, so this can stay small/manageable regardless of how " +
             "long the song/path is).")]
    public float horizonArcRadius = 40f;
    [Tooltip("Vertical offset of the whole bar row from the Horizon Camera's own fixed height.")]
    public float horizonBarVerticalOffset = -2f;

    [Header("Horizon World — Bars: Shape & Color")]
    [Tooltip("Bar height at amplitude = 0.")]
    public float horizonBarMinHeight = 0.3f;
    [Tooltip("Bar height at amplitude = 1.")]
    public float horizonBarMaxHeight = 6f;
    [Tooltip("Fraction (0..1) of each bar's own angular slice actually filled by geometry — lower " +
             "than 1 leaves a visible gap between bars (classic equalizer look). Kept close to 1 " +
             "so the arc reads as densely packed, still individually readable bars.")]
    [Range(0.1f, 1f)] public float horizonBarWidthFraction = 0.94f;
    [Tooltip("Radial thickness (depth) of each bar box, in Horizon World units.")]
    public float horizonBarDepth = 1.5f;
    [Tooltip("Amplitude (0..1) → color. Evaluated from the RAW normalized amplitude, never from " +
             "the final height — one solid color per bar per moment, no bass/mid/treble special-" +
             "casing. Freely editable as a Unity Gradient in the Inspector.")]
    public Gradient horizonBarAmplitudeGradient = DefaultAmplitudeGradient();
    [Tooltip("Emission present even at amplitude = 0 — keeps quiet bars faintly glowing instead of " +
             "going fully dark/dull, for a consistently neon look.")]
    public float horizonBarBaseEmission = 0.6f;
    [Tooltip("Extra emission ADDED on top of Base Emission, scaled by amplitude — this is the main " +
             "'punchier on louder bands' knob.")]
    public float horizonBarAmplitudeEmissionBoost = 3.5f;
    [Tooltip("Hard clamp on the final emission multiplier (Base + Amplitude*Boost), regardless of " +
             "how the two above are tuned — keeps Bloom from blowing out to flat white.")]
    public float horizonBarMaxEmission = 6f;
    [Tooltip("Multiplies the normalized band value before height/color — >1 makes quiet moments " +
             "read as louder, <1 tames overly hot signals.")]
    public float horizonBarGain = 1f;
    [Tooltip("Exponential rise-speed toward a LOUDER target amplitude each frame (0 = instant, " +
             "close to 1 = very lazy) — kept fast/low by default so bars punch on the beat.")]
    [Range(0f, 0.99f)] public float horizonBarAttack = 0.15f;
    [Tooltip("Exponential fall-speed toward a QUIETER target amplitude each frame — kept slower " +
             "than Attack by default so bars have a brief decay tail instead of snapping down.")]
    [Range(0f, 0.99f)] public float horizonBarRelease = 0.6f;

    [Header("Horizon World — Bar Reflection (RenderTexture mirror capture)")]
    [Tooltip("A dedicated camera renders ONLY the neon bars (their own layer, see " +
             "HorizonCameraController.BarsLayerName) into a small mirrored RenderTexture; the " +
             "water shader samples it, so the reflection is automatically the same shape/color/" +
             "height as the real bars — never a hand-tuned separate fan mesh. Requires a second " +
             "Project Settings layer (default name 'HorizonBars') — falls back to reflections " +
             "disabled (still fully playable) if that layer doesn't exist, same graceful-degrade " +
             "pattern as the 'Horizon' layer itself.")]
    public bool horizonReflectionEnabled = true;
    [Tooltip("Resolution of the reflection capture RenderTexture — kept small on purpose (this is " +
             "meant to read as a distorted/blurred mirror image, not a sharp second copy).")]
    public Vector2Int horizonReflectionRTSize = new Vector2Int(512, 256);
    [Range(0f, 1f)] public float horizonReflectionOpacity = 0.6f;
    [Tooltip("Shimmer/distortion strength applied to the sampled reflection UV — reuses the same " +
             "water-surface normal perturbation, so the reflection distorts consistently with the " +
             "water surface it sits on.")]
    [Range(0f, 1f)] public float horizonReflectionDistortion = 0.35f;
    [Tooltip("Extra HDR emission multiplier specific to the reflection (on top of the bar's own " +
             "color/emission) — reflections read as glowing light on water, not just a dim copy.")]
    public float horizonReflectionEmission = 1.6f;

    [Header("Horizon World — Water")]
    // Two independently-tiling/scrolling NORMAL MAPS (assign real tileable normal-map textures
    // here — see HorizonWater.cs doc for exactly what to provide) combined for a subtle, still-
    // readable-as-water micro ripple. Falls back to Unity's flat default-normal texture if left
    // unassigned (near-perfectly flat surface, no ripple detail — still fully functional, just
    // less detailed) so the water never errors out with nothing assigned.
    [Tooltip("Vertical level (relative to the Horizon Camera) the water plane sits at — " +
             "independent from Bar Vertical Offset, which only affects the bars.")]
    public float horizonWaterLevel = -1.4f;
    [Tooltip("Overall darkness of the water base color — near 1 reads as almost-black at night.")]
    [Range(0f, 1f)] public float horizonWaterDarkness = 0.92f;
    [Tooltip("Tileable normal map A — e.g. a 'water normal' texture from any free PBR water/ripple " +
             "pack. Left empty: falls back to a flat normal (still works, just no ripple detail).")]
    public Texture2D horizonWaterNormalMapA;
    [Tooltip("Tileable normal map B — should differ from A (different tiling/pattern) so the " +
             "combined ripple never reads as one obviously-repeating texture.")]
    public Texture2D horizonWaterNormalMapB;
    [Tooltip("UV tiling of normal map A.")]
    public float horizonWaterTilingA = 6f;
    [Tooltip("Scroll velocity of normal map A (UV units/second, both axes) — diagonal by default.")]
    public Vector2 horizonWaterScrollA = new Vector2(0.035f, 0.015f);
    [Tooltip("UV tiling of normal map B — kept different from Tiling A so the two never align.")]
    public float horizonWaterTilingB = 17f;
    [Tooltip("Scroll velocity of normal map B — a different direction than A on purpose.")]
    public Vector2 horizonWaterScrollB = new Vector2(-0.012f, 0.028f);
    [Tooltip("How strongly the combined normal maps perturb the surface — small values keep the " +
             "surface reading as calm/near-flat instead of big rolling waves.")]
    [Range(0f, 2f)] public float horizonWaterNormalStrength = 0.4f;
    [Tooltip("Fresnel (view-angle rim light) power — higher = tighter/sharper rim.")]
    public float horizonWaterFresnelPower = 5f;
    [Tooltip("Specular highlight tightness (higher = smaller/sharper glints, lower = broader/" +
             "softer) — the water's own smoothness, independent of Fresnel.")]
    [Range(4f, 256f)] public float horizonWaterSpecularPower = 48f;
    [Tooltip("Specular highlight brightness multiplier.")]
    public float horizonWaterSpecularIntensity = 0.6f;
    [Tooltip("Faint color tint added where the water surface faces toward the horizon (grazing " +
             "angle), picking up a hint of the sky's own horizon color.")]
    public Color horizonWaterHorizonTint = new Color(0.9f, 0.5f, 0.4f);
    [Range(0f, 1f)] public float horizonWaterHorizonTintStrength = 0.25f;

    [Header("Horizon World — Procedural Sky")]
    // Deliberately just a plain, cheap vertical gradient (near-black navy zenith, dark blue/
    // purple mid-sky, a subtle purple/pink/coral band right at the horizon) + a barely-there
    // noise wobble so it never reads as a perfectly flat linear ramp. The sky is a simple
    // BACKGROUND — the mountains (PNG layers) and haze below are what carry the actual visual
    // interest of the horizon line, not this shader.
    public Color horizonSkyZenithColor  = new Color(0.015f, 0.015f, 0.035f);
    public Color horizonSkyUpperColor   = new Color(0.05f, 0.05f, 0.12f);
    public Color horizonSkyLowerColor   = new Color(0.16f, 0.09f, 0.22f);
    public Color horizonSkyHorizonColor = new Color(0.55f, 0.28f, 0.34f);
    [Tooltip("Low-frequency noise warping the vertical gradient bands so they don't read as a " +
             "flat linear gradient. Kept subtle on purpose — this is a plain background, not a " +
             "detailed procedural sky.")]
    public float horizonSkyNoiseScale    = 1.2f;
    [Range(0f, 1f)] public float horizonSkyNoiseStrength = 0.04f;
    public Color horizonGlowColor     = new Color(1f, 0.6f, 0.5f);
    [Range(0f, 3f)] public float horizonGlowIntensity = 1f;
    public Color horizonSunColor = new Color(1f, 0.85f, 0.7f);
    [Tooltip("Sun position in the sky, degrees (0 = straight ahead/ +Z, 90 = due right).")]
    public float horizonSunAzimuthDeg   = 0f;
    [Tooltip("Sun height, degrees above the horizon.")]
    public float horizonSunElevationDeg = 12f;
    [Range(0.001f, 0.2f)] public float horizonSunSize = 0.03f;
    [Range(0f, 1f)] public float horizonSunGlowSize = 0.25f;
    public float horizonSunGlowIntensity = 1.2f;

    [Header("Horizon World — Mountains (PNG silhouette layers)")]
    // No procedural mesh/noise anymore — each layer is a flat textured quad using a hand-authored
    // PNG silhouette WITH ALPHA that you assign below, tinted/scaled/positioned from here. Assign
    // NOTHING and a layer simply doesn't render (no procedural fallback texture is generated —
    // see HorizonMountainLayers.cs doc for exactly what kind of PNG to provide).
    public bool  horizonMountainsEnabled = true;
    [Tooltip("Far-layer silhouette PNG (alpha channel = shape). Assign from Assets — none = this " +
             "layer doesn't render.")]
    public Texture2D horizonMountainFarTexture;
    [Tooltip("Near-layer silhouette PNG (alpha channel = shape).")]
    public Texture2D horizonMountainNearTexture;
    [Tooltip("Tint multiplied over the FAR texture — less contrast, blue/violet, reads as " +
             "integrated with the haze/sky.")]
    public Color horizonMountainFarTint = new Color(0.30f, 0.24f, 0.48f, 1f);
    [Tooltip("Tint multiplied over the NEAR texture — darker, more contrast, almost-black " +
             "silhouette.")]
    public Color horizonMountainNearTint = new Color(0.05f, 0.04f, 0.08f, 1f);
    [Range(0f, 1f)] public float horizonMountainFarOpacity  = 0.85f;
    [Range(0f, 1f)] public float horizonMountainNearOpacity = 1f;
    [Tooltip("Uniform scale of the far layer's quad, in Horizon World units (width, height).")]
    public Vector2 horizonMountainFarScale  = new Vector2(90f, 16f);
    [Tooltip("Uniform scale of the near layer's quad, in Horizon World units (width, height).")]
    public Vector2 horizonMountainNearScale = new Vector2(90f, 14f);
    [Tooltip("Vertical offset of the far layer above the base horizon line.")]
    public float horizonMountainFarVerticalOffset = 0f;
    [Tooltip("Vertical offset of the near layer above the base horizon line.")]
    public float horizonMountainNearVerticalOffset = -1f;
    [Tooltip("Brightness multiplier applied to the far layer, after tint.")]
    public float horizonMountainFarBrightness = 1f;
    [Tooltip("Brightness multiplier applied to the near layer, after tint.")]
    public float horizonMountainNearBrightness = 1f;
    [Tooltip("Distance of the far layer from center, as a multiple of Arc Radius — kept further " +
             "out than the bars so opaque depth-testing alone puts it behind them.")]
    public float horizonMountainFarDistance  = 1.6f;
    [Tooltip("Distance of the near layer from center, as a multiple of Arc Radius.")]
    public float horizonMountainNearDistance = 1.3f;
    [Tooltip("Fraction (0..1) of the Horizon Camera's own parallax translation the far layer " +
             "additionally bleeds through — a very subtle extra sense of depth between layers. " +
             "0 = perfectly locked to the horizon like the bars/sky.")]
    [Range(0f, 1f)] public float horizonMountainFarParallax  = 0.015f;
    [Tooltip("Parallax fraction for the near layer — kept higher than Far so the near layer " +
             "drifts slightly more, selling the two-layer depth separation.")]
    [Range(0f, 1f)] public float horizonMountainNearParallax = 0.05f;

    [Header("Horizon World — Atmosphere / Haze")]
    // Specific to the Horizon World ONLY — never Unity's global RenderSettings.fog (that's the
    // separate Gameplay Fog concept above, for the circuit). Baked into the bars' own vertex
    // colors (SpectrumBars3D) and the mountain layers' tint (HorizonMountainLayers) via
    // HorizonHaze.Apply — cheap, no per-pixel distance-to-camera needed since everything here
    // already sits within a small, known-radius space.
    public Color horizonHazeColor = new Color(0.16f, 0.14f, 0.30f);
    [Range(0f, 1f)] public float horizonHazeDensity = 0.35f;
    [Tooltip("Height (Horizon World units, relative to the bar baseline) below which haze is at " +
             "full density.")]
    public float horizonHazeStartHeight = 0f;
    [Tooltip("Height above which haze has fully cleared.")]
    public float horizonHazeEndHeight = 10f;
    [Tooltip("Extra haze boost right at the horizon line, on top of the height-based density.")]
    [Range(0f, 1f)] public float horizonHazeHorizonIntensity = 0.3f;
    [Tooltip("Optional warm/pink tint blended in ONLY at the horizon line itself (on top of the " +
             "cooler Haze Color used everywhere else) — a small artistic touch, not the haze's " +
             "main color.")]
    public Color horizonHazeHorizonTintColor = new Color(0.55f, 0.30f, 0.38f);
    [Range(0f, 1f)] public float horizonHazeHorizonTintAmount = 0.35f;
    [Tooltip("How much a caller-supplied normalized DISTANCE (0 = close, e.g. the bars/near " +
             "mountains; 1 = far, e.g. far mountains) adds on top of the height-based haze amount " +
             "— this is what gives genuine atmospheric depth between layers instead of a single " +
             "flat vertical gradient applied identically to everything.")]
    [Range(0f, 1f)] public float horizonHazeDistanceIntensity = 0.5f;

    [Header("Horizon World — Macro Reactivity")]
    [Tooltip("If on, profile.GetBuildupAt/MacroEventOccurredEvent (Impact/Drop) gently bias sky " +
             "glow/saturation over several seconds — deliberately slow/smoothed, never a per-beat " +
             "flicker. Bars stay fast/reactive regardless of this toggle; this only affects the " +
             "sky/water/glow layer.")]
    public bool  horizonMacroReactivity = true;
    [Tooltip("Seconds for the macro-driven glow bias to smooth toward its target.")]
    public float horizonMacroSmoothingTime = 4f;
    [Tooltip("How much an Impact/Drop MacroEvent snaps the glow bias toward full intensity (0..1, " +
             "same 'partway there' idea as MusicEnvironmentController.macroSnapFraction).")]
    [Range(0f, 1f)] public float horizonMacroSnapFraction = 0.4f;

    private static Gradient DefaultAmplitudeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.10f, 0.95f, 0.85f), 0.00f), // turquoise/cyan
                new GradientColorKey(new Color(0.15f, 0.55f, 0.98f), 0.25f), // cyan/blue
                new GradientColorKey(new Color(0.55f, 0.25f, 0.95f), 0.45f), // violet
                new GradientColorKey(new Color(0.90f, 0.20f, 0.75f), 0.60f), // pink/magenta
                new GradientColorKey(new Color(1.00f, 0.45f, 0.30f), 0.75f), // coral/orange
                new GradientColorKey(new Color(1.00f, 0.20f, 0.15f), 1.00f), // red-orange
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // Single shared source of truth for RingType → prefab, so placement (GameplayTimeline,
    // which needs a prefab's real bounds) and pooling (GameplayManager) can never disagree.
    public GameObject ResolvePrefab(RingType type) => type switch
    {
        RingType.Kick   => ringKick   ?? ringDefault,
        RingType.Snare  => ringSnare  ?? ringDefault,
        RingType.HiHat  => ringHiHat  ?? ringDefault,
        RingType.Beat   => ringBeat   ?? ringDefault,
        RingType.Onset  => ringOnset  ?? ringDefault,
        RingType.Peak   => ringPeak   ?? ringDefault,
        RingType.Impact => ringImpact ?? ringDefault,
        _               => ringDefault,
    };
}
