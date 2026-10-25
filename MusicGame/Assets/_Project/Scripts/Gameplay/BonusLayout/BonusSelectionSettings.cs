using UnityEngine;

/// <summary>
/// How the DETECTED musical moments of a song (SongProfile.onsetEvents) become playable bonuses (P4).
/// Selection is deterministic for a given profile + difficulty: the song's candidates are
/// ranked ONCE by musical priority (strength, family confidence, being on a tracked beat) under the minimum
/// spacing, and a difficulty keeps the first Share(difficulty) of that ranking — so every harder match keeps
/// MORE of the same song's moments (a superset of the easier layout), and nothing is lost to a coin flip. Randomness (the layout's stable
/// seed) only decides HOW a selected moment is placed: height, lateral offset, patterns, off-track picks.
/// One object per musical instant: the families of one detection (kick + hi-hat together) are one object,
/// and a moment already represented by an Impact is not spawned twice.
/// </summary>
[System.Serializable]
public class BonusSelectionSettings
{
    [Tooltip("Minimum time between two selected musical bonuses (any difficulty).")]
    [Min(0.05f)] public float minInterval = 0.16f;
    [Tooltip("Detections weaker than this (0..1 strength) are never bonuses.")]
    [Range(0f, 1f)] public float strengthFloor = 0.08f;
    [Tooltip("Share of the song's playable moments kept at difficulty 0 / 1. The moments are ranked ONCE per song " +
             "(priority under minInterval); a difficulty keeps the first share × count of that ranking — so the " +
             "density rises smoothly with every match, and a harder layout contains every moment of an easier one.")]
    [Range(0.05f, 1f)] public float easyShare = 0.8f;
    [Range(0.05f, 1f)] public float hardShare = 1f;
    [Tooltip("A family label (Kick/Snare/HiHat) is used for the object only above this confidence; " +
             "otherwise the moment is a generic Onset bonus (never a guessed instrument).")]
    [Range(0f, 1f)] public float familyMinConfidence = 0.5f;
    [Tooltip("Priority bonus of a detection within this window of a tracked beat (beatTimes).")]
    [Min(0f)] public float onBeatWindow = 0.04f;
    [Range(0f, 1f)] public float onBeatPriority = 0.15f;
    [Tooltip("A detection this close to a spawned Impact is represented by the Impact (no second object).")]
    [Min(0f)] public float impactMergeWindow = 0.09f;
    [Tooltip("Chance (per run of close moments) of a coordinated arc/stair pattern at difficulty 0 / 1.")]
    [Range(0f, 1f)] public float easyPatternProbability = 0.08f;
    [Range(0f, 1f)] public float hardPatternProbability = 0.22f;

    [Header("Synthetic fill (identifiable, off by default)")]
    [Tooltip("Fill long gaps with TRACKED beats (profile.beatTimes — never a rigid 60/BPM grid). Synthetic " +
             "items are RingType.Beat, sourceFeature \"Grid\", origin SyntheticGrid — never shown as detected hits.")]
    public bool syntheticFill = true;
    [Tooltip("A beat is only added when no selected bonus is within this time — the larger, the fewer Beat bonuses " +
             "(they only fill the song's emptier stretches, on its real tracked beats).")]
    [Min(0.1f)] public float syntheticMinGap = 0.8f;
    [Tooltip("A detection with no confident family (a generic Onset) that falls on a tracked beat (onBeatWindow) is a " +
             "Beat bonus — the same object, its musical role. No extra pickups.")]
    public bool onBeatGenericAsBeat = true;

    [Header("Accent Impacts (the song's strongest hits)")]
    [Tooltip("Impact pickups per minute taken from the song's STRONGEST detected hits (on top of any analysis Impact). " +
             "A few, well spaced: a punctual reward, never a flood. 0 = only the analysis' own Impacts.")]
    [Min(0f)] public float accentImpactsPerMinute = 4f;
    [Tooltip("Minimum seconds between two Impacts (accents and analysis ones).")]
    [Min(0.5f)] public float accentImpactMinGap = 7f;
    [Tooltip("Only hits at least this strong (0..1) — and in a passage at or above the song's average energy — can be accents.")]
    [Range(0f, 1f)] public float accentImpactMinStrength = 0.45f;

    public float MinInterval(float difficulty) => minInterval;
    public float Share(float difficulty) => Mathf.Lerp(easyShare, hardShare, Mathf.Clamp01(difficulty));
    public float StrengthFloor(float difficulty) => strengthFloor;
    public float PatternProbability(float difficulty) => Mathf.Lerp(easyPatternProbability, hardPatternProbability, Mathf.Clamp01(difficulty));
}

/// <summary>
/// DEVELOPMENT-ONLY overrides of the Runner layout (testing spacing / jump-free / on-track layouts). NOT a
/// player option: no menu or control exposes them, they are OFF by default, and they have NO effect at all
/// outside the editor / development builds (MusicRunnerCollectiblesConfig.DevOverrides returns null there).
/// In normal play difficulty, jumps and off-track bonuses depend only on the config + campaign progression.
/// When active they are part of the layout key (a dev layout never replaces a normal stored one).
/// </summary>
[System.Serializable]
public class RunnerLayoutDevOverrides
{
    [Tooltip("Master switch (editor / development builds only). Off = every field below is ignored.")]
    public bool enabled = false;
    [Tooltip("Never two musical bonuses closer than this (s), whatever the difficulty. 0 = difficulty only.")]
    [Min(0f)] public float minBonusSpacing = 0f;
    [Tooltip("No bonus beyond the track edge (off-track bonuses and difficult Life/Special detours become on-track).")]
    public bool noOffTrack = false;
    [Tooltip("No pickup that needs a jump (everything in the running band).")]
    public bool noJumps = false;
    [Tooltip("Caps the difficulty used for the layout (1 = no cap).")]
    [Range(0f, 1f)] public float maxDifficulty = 1f;
}

/// <summary>Listening diagnostics of the bonus selection (off in normal play).</summary>
public enum BonusClickMode
{
    Off,
    /// <summary>Every detected attack of the profile.</summary>
    Detections,
    /// <summary>Every selected musical bonus (detected + synthetic, different click for synthetic).</summary>
    SelectedBonuses,
    /// <summary>Only the synthetic fill.</summary>
    SyntheticOnly,
    /// <summary>The musical pulses as they are actually executed (BeatPulseEvent receipt, real frame timing).</summary>
    ExecutedPulses,
}

[System.Serializable]
public class BonusDiagnosticsSettings
{
    [Tooltip("Audible clicks over the music to compare by ear (F7 cycles the mode while playing).")]
    public BonusClickMode clickMode = BonusClickMode.Off;
    [Range(0f, 1f)] public float clickVolume = 0.6f;
    [Tooltip("Log a per-run summary of detections / selected / synthetic / per family.")]
    public bool logSummary = true;
}
