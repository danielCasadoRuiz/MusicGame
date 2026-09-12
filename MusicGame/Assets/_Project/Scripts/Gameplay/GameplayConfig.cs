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
    [Tooltip("Maximum horizontal turn angle (degrees) per control point interval")]
    public float pathMaxTurnAngle            = 18f;
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
    public float maxFrequencyHeight = 4f;
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
    [Tooltip("Single knob for how organic vs. sharp the terrain reads (0 = minimum, 1 = " +
             "maximum) — replaces what used to be 3 separate pass-count/radius fields that " +
             "mostly moved together. This is now a SECONDARY layer on top of the real fix " +
             "(Catmull-Rom interpolated sampling, see SongProfile.GetVisualBandEnergyAtSmooth): " +
             "that's what keeps real musical peaks from being flattened; this just rounds off " +
             "residual roughness and the small seam the slope-safety clamp below can leave.")]
    [Range(0f, 1f)]
    public float pathSmoothness = 0.5f;

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

    // ── Frequency Background ("Frequency Horizon") ────────────────────────────
    // A STATIC curved LED-grid mesh (Bar Count columns × Vertical Block Count rows, reproducing
    // the old 2D equalizer's look — green/yellow/red tiers by ROW, small black border per cell,
    // off cells fully transparent) that never deforms — amplitude only changes which cells are
    // lit (color/alpha), never vertex positions. Behaves like a distant skybox: it follows the
    // CAMERA's XZ position and yaw only (never the player/road/MusicDistance), so it never
    // shows parallax and always occupies roughly the same left-to-right screen area. A separate
    // flat fan mesh on the water plane fakes a "light reflected on water" look per column — not
    // a mirrored copy of the LED grid. Reads the SAME MusicWorldManager.NormalizedBandValue data
    // the ground mesh already uses, remapped onto Bar Count columns; color reuses the exact same
    // green/yellow/red fields (lowEnergyColor/midEnergyColor/highEnergyColor) as the ground
    // mesh's VuColor. Purely decorative — never touches gameplay/collision/sky.
    [Header("Frequency Background — Enable")]
    public bool enableFrequencyBackground = true;

    [Header("Frequency Background — Arc Shape")]
    [Tooltip("Number of horizontal columns the arc is divided into — independent from the real " +
             "analysis resolution (AudioAnalysisConfig.visualBandCount); real band data is " +
             "averaged/remapped onto however many columns this is. More columns = visually " +
             "smoother curve; fewer = chunkier/low-poly.")]
    [Range(6, 64)] public int freqBgBarCount = 24;
    [Tooltip("How many stacked LED blocks each column is divided into vertically (e.g. 8, like " +
             "the old 2D equalizer). Amplitude only changes how many of these are lit.")]
    [Range(2, 16)] public int freqBgVerticalBlockCount = 8;
    [Tooltip("Total angular span of the arc, centered directly ahead. 180 = a full semicircle.")]
    public float freqBgArcSpanDegrees = 180f;
    [Tooltip("Radius of the arc (Unity units) — distance from the camera. Kept fairly large since " +
             "this is meant to read as a distant horizon, not something close to the player.")]
    public float freqBgArcRadius = 40f;
    [Tooltip("Total vertical extent of the LED grid (Unity units) — split evenly into Vertical Block Count rows.")]
    public float freqBgGridHeight = 6f;
    [Tooltip("Vertical offset of the whole grid from the CAMERA's own height (negative = below eye level).")]
    public float freqBgHorizonVerticalOffset = -2f;

    [Header("Frequency Background — LED Look")]
    [Tooltip("Black border size per LED cell, as a fraction (0..0.45) of that cell's own size.")]
    [Range(0f, 0.45f)] public float freqBgLedBorderSize = 0.08f;
    [Tooltip("Emission strength fed into the bloom post-process — higher = more neon glow on lit cells.")]
    public float freqBgEmission = 1.2f;
    [Tooltip("Multiplies the normalized band value before it decides how many LEDs are lit — >1 " +
             "makes quiet moments read as louder, <1 tames overly hot signals.")]
    public float freqBgGain = 1f;
    [Tooltip("Exponential smoothing toward the target lit-count each frame (0 = instant, close to 1 = very lazy).")]
    [Range(0f, 0.99f)] public float freqBgSmoothing = 0.6f;

    [Header("Frequency Background — Water")]
    [Tooltip("Vertical level (relative to the camera) the water plane AND the reflection fan sit " +
             "at — independent from Horizon Vertical Offset, which only affects the LED grid.")]
    public float freqBgWaterLevel = -1.4f;
    [Range(0f, 1f)] public float freqBgWaterDarkness = 0.9f;
    [Tooltip("Tiling of the cheap analytic ripple pattern — higher = smaller/tighter ripples.")]
    public float freqBgWaterNoiseScale = 10f;
    [Tooltip("Amplitude of the cheap analytic ripple (no real wave simulation) — kept low so the " +
             "water reads as calm, not visibly deformed.")]
    public float freqBgWaveStrength = 0.02f;
    public float freqBgWaveSpeed = 0.08f;

    [Header("Frequency Background — Reflection")]
    [Range(0f, 1f)] public float freqBgReflectionOpacity = 0.4f;
    [Tooltip("How far the fake reflection fan extends from the horizon toward the camera, as a " +
             "fraction (0..1) of Arc Radius.")]
    [Range(0f, 1f)] public float freqBgReflectionLength = 0.4f;
    [Tooltip("Subtle shimmer strength applied to the reflection fan only, standing in for water distortion.")]
    [Range(0f, 1f)] public float freqBgReflectionDistortion = 0.15f;

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
