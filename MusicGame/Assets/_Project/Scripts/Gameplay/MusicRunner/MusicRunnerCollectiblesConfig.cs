using UnityEngine;

/// <summary>
/// Everything about the Music Runner's collectible/ring/bonus system: prefabs, colors, spawn
/// enable flags, density/confidence thinning, placement (density/height/off-track), object
/// pooling (ring-pool specific, not a generic system), beat feedback and visual anticipation.
/// Split out of the old monolithic GameplayConfig — see MusicRunnerGameplayConfig for the full
/// picture.
/// </summary>
[CreateAssetMenu(fileName = "MusicRunnerCollectiblesConfig", menuName = "MusicGame/MusicRunner/Collectibles Config")]
public class MusicRunnerCollectiblesConfig : ScriptableObject
{
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

    // ── Ring Emission (Bloom) ────────────────────────────────────────────────
    // Deliberately independent of the Horizon World bars' own (much higher) emission — rings
    // should stay visually BELOW the bars in intensity (or at least be tunable separately), never
    // "any colored object automatically gets Bloom". OFF by default keeps a ring's fallback-cube
    // Lit material fully non-emissive (bloomThreshold in HorizonConfig is kept above the normal
    // 0..1 color range specifically so a NON-emissive ring never blooms on its own).
    [Header("Ring Emission (Bloom) — subtle, independent of Horizon bars")]
    [Tooltip("If on, ring/bonus materials get a modest HDR emission (their own RingColor, scaled " +
             "by Ring Emission Intensity) so the player can spot them quickly — kept OFF by " +
             "default so bloom stays selective to the Horizon World's neon bars.")]
    public bool  ringEmissionEnabled = false;
    [Tooltip("HDR multiplier on top of RingColor when Ring Emission Enabled is on — keep this " +
             "modest (well under the bars' own emission boost) so rings read as 'a bit brighter', " +
             "never competing with the bars as the scene's brightest element.")]
    public float ringEmissionIntensity = 0.8f;

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

    // ── Object Pooling (ring-pool specific) ───────────────────────────────────
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
