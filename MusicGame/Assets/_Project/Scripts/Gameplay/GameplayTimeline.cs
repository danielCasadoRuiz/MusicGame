using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The composer: turns SongProfile analysis into playable gameplay. Two layers, kept
/// structurally separate so neither one silently swallows the other's information:
///
///  MICRO — instant rhythm (Kick/Snare/HiHat/Onset/Beat). One classified onset gets exactly
///  one label; when the evidence is genuinely ambiguous it honestly falls back to generic
///  Onset instead of guessing (see ClassifyOnset). Density-thinned for a musically-sensible
///  count, with buildup sections progressively denser (profile.buildupCurve) — the only
///  Buildup gameplay effect in this iteration, deliberately simple but real.
///
///  MACRO — structural moments (Impact/Drop/BuildupStart/Peak), built independently of Micro
///  in BuildMacroEvents. A Kick classified with confidence at the same instant as an Impact
///  keeps BOTH — Impact never displaces or merges with whatever Micro event(s) share its
///  moment; the only "composition" rule this iteration needs is that Impact spawns centered
///  on the path (lateralOffset = 0) so it reads as visually distinct from Micro's scattered
///  placement, even while both are still plain primitives. Drop/BuildupStart/standalone-Peak
///  are pure signals (MacroEvent[]) — not spawned as objects — consumed via
///  MacroEventOccurredEvent (fired by GameplayManager at eventTime) by things like
///  MusicEnvironmentController.
///
/// eventTime     = warmupTime + onsetTime   (matches MusicClock.SongTime at the event)
/// eventDistance = eventTime * unitsPerSecond
/// World position is computed at ACTIVATION time from MusicPath.GetSample(eventDistance)
/// + lateralOffset/verticalOffset (both decided here, once, never re-rolled from the pool).
/// </summary>
public class GameplayTimeline
{
    // Types eligible for the rarity scoring formula — Impact/Peak are structurally
    // guaranteed/macro rather than statistically rare, so they're scored with their own fixed
    // config values instead (see GameplayManager.ScoreFor).
    private static readonly RingType[] RarityTypes =
        { RingType.Kick, RingType.Snare, RingType.HiHat, RingType.Beat, RingType.Onset };

    // How close a Peak may land to an existing Impact/Drop and still just TAG it as the climax
    // instead of becoming its own standalone MacroEvent.
    private const float ClimaxTagTolerance = 1.0f;

    /// <summary>
    /// Debug-only snapshot of when each pipeline stage first produces something, all expressed
    /// in the SAME eventTime domain as MusicClock.SongTime (warmup already added) so they're
    /// directly comparable to what you see at runtime. -1 = that stage produced nothing at all.
    /// See GameplayDebugHUD's "SYNC AUDIT" section.
    /// </summary>
    public readonly struct SyncDebugInfo
    {
        public readonly float firstAnalyzedOnset;   // profile.onsetTimes[0] + warmup
        public readonly float firstClassified;      // first onset-driven Micro candidate (any confidence) + warmup
        public readonly float firstMicroKept;       // first Micro candidate surviving spawn-enable/absorption/thinning + warmup
        public readonly float firstMacro;           // first MacroEvent.eventTime
        public readonly float firstFinalEvent;       // Events[0].eventTime

        public SyncDebugInfo(float onset, float classified, float kept, float macro, float final)
        {
            firstAnalyzedOnset = onset;
            firstClassified    = classified;
            firstMicroKept     = kept;
            firstMacro         = macro;
            firstFinalEvent    = final;
        }
    }

    public TimelineEvent[] Events      { get; }
    public MacroEvent[]    MacroEvents { get; }
    public SyncDebugInfo   SyncDebug   { get; private set; }
    private readonly Dictionary<RingType, float> _rarityMultipliers;

    public GameplayTimeline(TimelineEvent[] events, MacroEvent[] macroEvents,
                           Dictionary<RingType, float> rarityMultipliers)
    {
        Events             = events;
        MacroEvents        = macroEvents;
        _rarityMultipliers = rarityMultipliers;
    }

    /// <summary>1.0 = exactly at the median count for this song; see ComputeRarityMultipliers.</summary>
    public float RarityMultiplier(RingType type)
        => _rarityMultipliers != null && _rarityMultipliers.TryGetValue(type, out var m) ? m : 1f;

    // A Micro rhythm signal, before density thinning/placement.
    private struct Candidate
    {
        public float    time;
        public RingType type;
        public float    strength;
        public string   sourceFeature;
        public bool     dampedByVocal;  // onset classification is less reliable during vocals
        public float    confidence;     // 0..1 — see ClassifyOnset; >= confidenceKeepThreshold bypasses thinning
    }

    public static GameplayTimeline Generate(SongProfile profile, MusicRunnerGameplayConfig config, MusicPath path)
    {
        float warmup = config.core.warmupTime;
        float speed  = config.core.playerSpeed;

        // Macro first — Impact placement doesn't depend on Micro, but keeping the order
        // explicit documents that Micro's buildup-density read (profile.GetBuildupAt) and
        // Macro's own construction are independent of each other.
        var macroEvents = BuildMacroEvents(profile, config);

        // ── MICRO ──────────────────────────────────────────────────────────────────────
        var micro = CollectMicroCandidates(profile, config);

        // Debug-only snapshot, captured BEFORE any filtering — see SyncDebugInfo.
        float firstAnalyzedOnsetRaw = (profile.onsetTimes != null && profile.onsetTimes.Length > 0)
            ? profile.onsetTimes[0] : -1f;
        float firstClassifiedRaw = MinTime(micro, c => c.sourceFeature != "Beat");

        micro.RemoveAll(c => !config.collectibles.IsSpawnEnabled(c.type));

        // Composition rule: a non-confident Micro candidate (ambiguous Onset fallback, or the
        // synthetic Beat filler) right on top of a reliable Impact/Drop is redundant — the
        // Impact already represents that moment well, and keeping both would just be visual
        // noise with no extra musical information. A CONFIDENTLY classified Kick/Snare/HiHat is
        // NEVER absorbed this way — it coexists with the Impact, since it IS real information.
        micro.RemoveAll(c => c.confidence < config.collectibles.confidenceKeepThreshold
                           && IsNearMacroImpact(c.time, macroEvents, config));

        var rng  = new System.Random(config.collectibles.collectibleSeed);
        var kept = new List<Candidate>(micro.Count);
        foreach (var c in micro)
        {
            // A confidently classified onset (see ClassifyOnset) must always have gameplay
            // representation — never lost to a random roll. Only genuinely uncertain
            // classifications (ambiguous Onset fallback) and the synthetic beat-grid filler
            // fall through to density thinning below.
            if (c.confidence >= config.collectibles.confidenceKeepThreshold) { kept.Add(c); continue; }

            // Buildup sections get progressively denser — the simple, real Buildup gameplay
            // effect for this iteration (see class doc).
            float buildupFactor = Mathf.Lerp(1f, config.collectibles.buildupDensityBoost, profile.GetBuildupAt(c.time));

            // Strength-aware survival: at strength≈1 this approaches a guaranteed keep; at
            // strength≈0 it falls back to collectibleDensity's baseline odds. Thinning should
            // mostly cut weak/marginal/uncertain detections, never a clearly loud, obvious hit.
            float strengthAware = Mathf.Lerp(config.collectibles.collectibleDensity, 1f, c.strength);
            float prob = Mathf.Lerp(config.collectibles.collectibleDensity, strengthAware, config.collectibles.strengthThinningBias);
            prob *= buildupFactor;
            prob *= c.dampedByVocal ? 0.6f : 1f;
            if (rng.NextDouble() < Mathf.Clamp01(prob)) kept.Add(c);
        }

        float firstMicroKeptRaw = MinTime(kept);

        // Single source of truth for "how high can the player actually jump" (v²/2g) — every
        // bonus height derives from THIS, never from an independent world-unit range, so changing
        // jumpForce/gravity re-tunes every bonus height automatically. bonusMinJumpHeightFactor/
        // bonusMaxJumpHeightFactor (both 0..1) carve out the usable band within it; vFloorDesign is
        // only a design target — CollectibleFloor still raises it per-type for physical clearance.
        float maxJumpHeight = config.core.gravity < 0f
            ? (config.core.jumpForce * config.core.jumpForce) / (2f * -config.core.gravity)
            : 0f;
        float vFloorDesign = maxJumpHeight * config.collectibles.bonusMinJumpHeightFactor;
        float vCeil        = maxJumpHeight * config.collectibles.bonusMaxJumpHeightFactor;

        // OFF-TRACK bonuses are deliberately DECOUPLED from the normal-bonus range above — they're
        // framed as "how close to the player's FULL jump capability" directly against maxJumpHeight,
        // not as a fraction of the (separately tunable, often more conservative) normal ceiling. If
        // they shared one range, lowering bonusMaxJumpHeightFactor for ordinary bonuses would
        // quietly make off-track bonuses easier too, even though those should always demand a real,
        // near-full-height jump regardless of how the normal ceiling is tuned.
        float offTrackFloorDesign = maxJumpHeight * config.collectibles.offTrackBonusMinJumpHeightFactor;
        float offTrackCeil        = maxJumpHeight * config.collectibles.offTrackBonusMaxJumpHeightFactor;

        var events = new List<TimelineEvent>(kept.Count);
        int  i     = 0;
        while (i < kept.Count)
        {
            int run = 1;
            if (rng.NextDouble() < config.collectibles.patternProbability)
            {
                int maxRun = 2 + rng.Next(0, 3); // 2..4
                while (run < maxRun && i + run < kept.Count &&
                       kept[i + run].time - kept[i + run - 1].time <= 0.9f)
                    run++;
            }

            if (run > 1) EmitPattern(kept, i, run, config, path, rng, vFloorDesign, vCeil, warmup, speed, events);
            else         EmitSingle(kept[i], config, path, rng, vFloorDesign, vCeil, offTrackFloorDesign, offTrackCeil, warmup, speed, events);

            i += run;
        }

        // ── MACRO-as-collectible ──────────────────────────────────────────────────────
        // Impact spawns its own centered, always-kept pickup — entirely independent of
        // Micro's thinning/pattern logic. It never competes with or replaces whatever Micro
        // event(s) happen to land at the same instant; both simply coexist.
        if (config.collectibles.IsSpawnEnabled(RingType.Impact))
            foreach (var m in macroEvents)
                if (m.type == MacroEventType.Impact)
                    EmitMacroCollectible(m, RingType.Impact, config, path, vFloorDesign, vCeil, events);

        // Peak no longer auto-spawns a collectible — it's climax-tagging metadata on
        // MacroEvents (see BuildMacroEvents). config.collectibles.spawnPeak (default OFF) opts back into a
        // standalone "climax" collectible for the rare case a Peak has no nearby Impact/Drop
        // to tag instead.
        if (config.collectibles.spawnPeak)
            foreach (var m in macroEvents)
                if (m.type == MacroEventType.Peak)
                    EmitMacroCollectible(m, RingType.Peak, config, path, vFloorDesign, vCeil, events);

        events.Sort((a, b) => a.eventTime.CompareTo(b.eventTime));

        var counts = new Dictionary<RingType, int>();
        foreach (var rt in RarityTypes) counts[rt] = 0;
        foreach (var e in events)
            if (counts.ContainsKey(e.ringType)) counts[e.ringType]++;

        var rarity = ComputeRarityMultipliers(counts, config);

        var result = new GameplayTimeline(events.ToArray(), macroEvents, rarity)
        {
            SyncDebug = new SyncDebugInfo(
                onset:      firstAnalyzedOnsetRaw >= 0f ? warmup + firstAnalyzedOnsetRaw : -1f,
                classified: firstClassifiedRaw     >= 0f ? warmup + firstClassifiedRaw     : -1f,
                kept:       firstMicroKeptRaw       >= 0f ? warmup + firstMicroKeptRaw       : -1f,
                macro:      macroEvents.Length > 0 ? macroEvents[0].eventTime : -1f,
                final:      events.Count > 0 ? events[0].eventTime : -1f),
        };
        return result;
    }

    // Min `time` among candidates matching `predicate` (or all, if null) — -1 if none match.
    // Used only for the debug-only SyncDebugInfo snapshot above.
    private static float MinTime(List<Candidate> list, System.Func<Candidate, bool> predicate = null)
    {
        float min = float.MaxValue;
        bool  found = false;
        foreach (var c in list)
        {
            if (predicate != null && !predicate(c)) continue;
            if (c.time < min) { min = c.time; found = true; }
        }
        return found ? min : -1f;
    }

    // ── Rarity scoring ────────────────────────────────────────────────────────────

    /// <summary>
    /// Median-relative, log-scale, clamped rarity multiplier — counts near the median barely
    /// move (e.g. 100 vs 96 stay within a fraction of a percent of each other), while counts
    /// far from it saturate at the configured range instead of blowing up (a type with 2
    /// occurrences and one with 10 both hit the same ceiling rather than the rarer one
    /// spiking arbitrarily higher). Types with count == 0 (spawn-disabled) don't affect the
    /// median and just get the neutral 1.0 (they can never be collected anyway).
    /// </summary>
    private static Dictionary<RingType, float> ComputeRarityMultipliers(
        Dictionary<RingType, int> counts, MusicRunnerGameplayConfig config)
    {
        var result = new Dictionary<RingType, float>();

        var active = new List<int>();
        foreach (var kv in counts) if (kv.Value > 0) active.Add(kv.Value);

        if (active.Count == 0)
        {
            foreach (var rt in RarityTypes) result[rt] = 1f;
            return result;
        }

        active.Sort();
        float median = active.Count % 2 == 1
            ? active[active.Count / 2]
            : (active[active.Count / 2 - 1] + active[active.Count / 2]) * 0.5f;

        float spread = Mathf.Max(0.0001f, config.scoring.rarityLogSpread);
        foreach (var kv in counts)
        {
            if (kv.Value <= 0) { result[kv.Key] = 1f; continue; }
            float logRatio    = Mathf.Log(kv.Value / median);
            float clamped     = Mathf.Clamp(logRatio, -spread, spread);
            float normalized  = (spread - clamped) / (2f * spread); // 0 = most common, 1 = rarest
            result[kv.Key] = Mathf.Lerp(config.scoring.rarityMultiplierRange.x, config.scoring.rarityMultiplierRange.y, normalized);
        }
        return result;
    }

    // ── Macro construction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Impact/Drop come straight from DynamicsAnalyzer (already computed, previously only used
    /// to shape the path). BuildupStart is a marker only — the actual Buildup gameplay effect
    /// reads profile.GetBuildupAt(time) continuously in Generate(), not gated by this discrete
    /// event. Peak (the song's single loudest frame) is usually redundant with an Impact
    /// already in this list — the loudest instant is almost always also a detected Impact — so
    /// instead of adding a separate object it TAGS the nearest Impact/Drop within
    /// ClimaxTagTolerance as the climax; only if nothing is nearby does it become its own
    /// standalone MacroEvent.
    /// </summary>
    private static MacroEvent[] BuildMacroEvents(SongProfile profile, MusicRunnerGameplayConfig config)
    {
        float warmup = config.core.warmupTime;
        float speed  = config.core.playerSpeed;

        MacroEvent Make(float t, MacroEventType type, float strength, bool climax = false)
        {
            float eventTime = warmup + t;
            return new MacroEvent
            {
                eventTime     = eventTime,
                eventDistance = eventTime * speed,
                type          = type,
                strength      = strength,
                isClimax      = climax,
            };
        }

        var result = new List<MacroEvent>();

        var impactAndDrops = new List<(float time, MacroEventType type)>();
        if (profile.impactTimes != null) foreach (var t in profile.impactTimes) impactAndDrops.Add((t, MacroEventType.Impact));
        if (profile.dropTimes   != null) foreach (var t in profile.dropTimes)   impactAndDrops.Add((t, MacroEventType.Drop));
        impactAndDrops.Sort((a, b) => a.time.CompareTo(b.time));

        float lastTime = -999f;
        foreach (var (t, type) in impactAndDrops)
        {
            if (t - lastTime < 0.4f) continue;
            lastTime = t;
            result.Add(Make(t, type, 1f));
        }

        if (profile.buildupStartTimes != null)
            foreach (var t in profile.buildupStartTimes)
                result.Add(Make(t, MacroEventType.BuildupStart, 1f));

        float peakTime = FindPeakTime(profile);
        if (peakTime >= 0f)
        {
            float peakEventTime = warmup + peakTime;
            int   nearest       = -1;
            float bestDist      = ClimaxTagTolerance;
            for (int k = 0; k < result.Count; k++)
            {
                if (result[k].type != MacroEventType.Impact && result[k].type != MacroEventType.Drop) continue;
                float d = Mathf.Abs(result[k].eventTime - peakEventTime);
                if (d < bestDist) { bestDist = d; nearest = k; }
            }

            if (nearest >= 0)
            {
                var tagged = result[nearest];
                tagged.isClimax = true;
                result[nearest] = tagged;
            }
            else
            {
                result.Add(Make(peakTime, MacroEventType.Peak, 1f, climax: true));
            }
        }

        result.Sort((a, b) => a.eventTime.CompareTo(b.eventTime));
        return result.ToArray();
    }

    // Used only for the ambiguous/filler-absorption rule above — deliberately a tighter window
    // (config.collectibles.ambiguousAbsorptionWindow) than ClimaxTagTolerance, which is about "is this THE
    // song's climax", a different question from "is this Micro candidate redundant right here".
    private static bool IsNearMacroImpact(float microTime, MacroEvent[] macroEvents, MusicRunnerGameplayConfig config)
    {
        float microEventTime = config.core.warmupTime + microTime;
        foreach (var m in macroEvents)
        {
            if (m.type != MacroEventType.Impact && m.type != MacroEventType.Drop) continue;
            if (Mathf.Abs(m.eventTime - microEventTime) <= config.collectibles.ambiguousAbsorptionWindow) return true;
        }
        return false;
    }

    // Places a Macro moment as its own collectible — centered on the path (lateralOffset = 0),
    // a simple, deliberate composition rule that keeps it visually distinct from the
    // scattered Micro collectibles even while both are still plain primitives, and keeps it
    // reachable regardless of whatever Micro event(s) share this exact instant.
    private static void EmitMacroCollectible(MacroEvent m, RingType type, MusicRunnerGameplayConfig config,
                                             MusicPath path, float vFloorDesign, float vCeil,
                                             List<TimelineEvent> events)
    {
        float vFloor = CollectibleFloor(type, config, vFloorDesign, vCeil);
        float vOff   = Mathf.Lerp(vFloor, vCeil, 0.5f);

        events.Add(new TimelineEvent
        {
            eventTime      = m.eventTime,
            eventDistance  = m.eventDistance,
            eventType      = EventType.Ring,
            ringType       = type,
            lateralOffset  = 0f,
            verticalOffset = vOff,
            floorClearance = vFloor,
            strength       = m.strength,
            sourceFeature  = type.ToString(),
            confidence     = 1f,
            contributors   = m.isClimax ? $"{type}+Peak" : type.ToString(),
        });
    }

    // ── Micro candidate collection ───────────────────────────────────────────────────

    private static List<Candidate> CollectMicroCandidates(SongProfile profile, MusicRunnerGameplayConfig config)
    {
        var list      = new List<Candidate>();
        var bandAvgs  = ComputeBandAverages(profile);
        var lastTypeT = new Dictionary<RingType, float>();

        float LastTime(RingType t) => lastTypeT.TryGetValue(t, out var v) ? v : -999f;

        // config.levelGeneration.minRingSpacing is a CEILING, not a fixed floor — a flat 0.20s absolute minimum
        // silently discards every other hit of a straight 16th-note pattern at ~120+ BPM (16th
        // note = 125ms), exactly the fast repeated same-instrument patterns (hi-hat rolls,
        // funk/disco) that should read as rich, not sparse. Scale it down for faster songs;
        // never scale it UP past the configured value for slow ones.
        float minSpacing = config.levelGeneration.minRingSpacing;
        if (profile.estimatedBPM > 0f)
        {
            float sixteenthNote = 15f / profile.estimatedBPM; // 60/BPM/4
            minSpacing = Mathf.Min(config.levelGeneration.minRingSpacing, sixteenthNote * 0.6f);
        }

        // ── Onset-driven: classified by spectral shape into Kick/Snare/HiHat/Onset ──────────
        if (config.levelGeneration.useOnsets && profile.onsetTimes != null)
        {
            foreach (float t in profile.onsetTimes)
            {
                if (profile.GetEnergyAt(t) < profile.averageEnergy * config.levelGeneration.energyThreshold)
                    continue;

                var (type, classConfidence) = ClassifyOnset(profile, t, bandAvgs, config);
                if (t - LastTime(type) < minSpacing) continue;
                lastTypeT[type] = t;

                list.Add(new Candidate
                {
                    time          = t,
                    type          = type,
                    strength      = profile.intensity != null ? profile.GetIntensityAt(t) : 0.5f,
                    sourceFeature = type.ToString(),
                    dampedByVocal = profile.IsVocalAt(t),
                    confidence    = classConfidence,
                });
            }
        }

        // ── Beat-grid fill: lowest-priority filler for gaps the onsets didn't cover ─────────
        // Deliberately NOT damped by vocals — it exists precisely to keep a baseline rhythm
        // going through sections (like vocals) where onset classification backs off.
        if (config.levelGeneration.useBeatGrid && profile.estimatedBPM > 0f)
        {
            float period    = 60f / profile.estimatedBPM * config.levelGeneration.beatGridSubdivision;
            float proximity = period * 0.28f;

            for (float t = period; t < profile.duration - 0.5f; t += period)
            {
                if (profile.GetEnergyAt(t) < profile.averageEnergy * config.levelGeneration.energyThreshold)
                    continue;

                bool occupied = false;
                foreach (var existing in list)
                    if (Mathf.Abs(existing.time - t) < proximity) { occupied = true; break; }
                if (occupied) continue;

                list.Add(new Candidate
                {
                    time          = t,
                    type          = RingType.Beat,
                    strength      = profile.intensity != null ? profile.GetIntensityAt(t) : 0.5f,
                    sourceFeature = "Beat",
                    // Synthetic filler, not a real detection — lowest inherent trust, so it's
                    // the one thinning should actually thin when density needs to come down.
                    confidence    = 0.15f,
                });
            }
        }

        return list;
    }

    // ── Placement ─────────────────────────────────────────────────────────────────

    private static void EmitSingle(Candidate c, MusicRunnerGameplayConfig config, MusicPath path, System.Random rng,
                                   float vFloorDesign, float vCeil,
                                   float offTrackFloorDesign, float offTrackCeil,
                                   float warmup, float speed,
                                   List<TimelineEvent> events)
    {
        float songTime  = warmup + c.time;
        float dist      = songTime * speed;
        bool  offTrack  = rng.NextDouble() < config.collectibles.offTrackBonusChance;

        float lateral;
        float vOff;
        float floorClearance;
        if (offTrack)
        {
            // Beyond the track's REAL local half-width (never a world X/Z constant) by a small,
            // jump+air-control-reachable extra — same collectibleRadius clearance normal
            // placement uses, plus up to offTrackBonusMaxOffset, randomized left/right.
            float halfWidth = path.GetWidth(dist) * 0.5f;
            float extra     = (float)rng.NextDouble() * config.collectibles.offTrackBonusMaxOffset;
            float side      = rng.NextDouble() < 0.5 ? -1f : 1f;
            lateral = side * (halfWidth + config.collectibles.collectibleRadius + extra);

            // Its OWN range — offTrackBonusMinJumpHeightFactor..offTrackBonusMaxJumpHeightFactor
            // of maxJumpHeight DIRECTLY, decoupled from the normal-bonus ceiling (see Generate's
            // own comment on offTrackFloorDesign/offTrackCeil). Uniform roll, bypassing
            // verticalOffsetCurve (that shaping is about "how often should this need a jump";
            // off-track bonuses always need one, and sit high on purpose: visually obvious as a
            // jump target, naturally in-path during a jump's arc, never floating low with nothing
            // beneath it). Still hard-floored per type for physical clearance, same as normal bonuses.
            floorClearance = CollectibleFloor(c.type, config, offTrackFloorDesign, offTrackCeil);
            vOff = floorClearance + (float)rng.NextDouble() * (offTrackCeil - floorClearance);
        }
        else
        {
            floorClearance = CollectibleFloor(c.type, config, vFloorDesign, vCeil);
            float half = LateralHalfRange(path, config, dist);
            lateral = half > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * half : 0f;
            vOff    = RollVertical(config, rng, floorClearance, vCeil);
        }

        events.Add(new TimelineEvent
        {
            eventTime      = songTime,
            eventDistance  = dist,
            eventType      = EventType.Ring,
            ringType       = c.type,
            lateralOffset  = lateral,
            verticalOffset = vOff,
            floorClearance = floorClearance,
            strength       = c.strength,
            sourceFeature  = c.sourceFeature,
            confidence     = c.confidence,
            contributors   = c.sourceFeature,
            isOffTrack     = offTrack,
        });
    }

    // A short run of consecutive kept candidates, close together in time, becomes one
    // coordinated shape (arc or stair) instead of independent random heights/positions.
    private static void EmitPattern(List<Candidate> kept, int start, int run, MusicRunnerGameplayConfig config,
                                    MusicPath path, System.Random rng, float vFloorDesign, float vCeil,
                                    float warmup, float speed, List<TimelineEvent> events)
    {
        bool  stair = rng.NextDouble() < 0.5;

        float avgDist = 0f;
        for (int k = 0; k < run; k++) avgDist += (warmup + kept[start + k].time) * speed;
        avgDist /= run;

        // One shared lateral anchor for the whole run (+ small per-event jitter) so it reads
        // as one coherent line rather than independent scatter.
        float baseHalf     = LateralHalfRange(path, config, avgDist);
        float baseLateral  = baseHalf > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * baseHalf * 0.6f : 0f;

        for (int k = 0; k < run; k++)
        {
            var   c        = kept[start + k];
            float songTime = warmup + c.time;
            float dist     = songTime * speed;

            float vFloor = CollectibleFloor(c.type, config, vFloorDesign, vCeil);
            float t       = run > 1 ? k / (float)(run - 1) : 0f;
            float shapeT  = stair ? t : (t < 0.5f ? t * 2f : (1f - t) * 2f); // stair: linear, arc: up-then-down
            // Same compression curve as single collectibles (RollVertical) — a pattern's peak
            // can still reach vCeil (a rewarding "reach up" moment), but most of its shape stays
            // in the easy/no-jump band, consistent with ordinary collectibles.
            float vOff = Mathf.Lerp(vFloor, vCeil, Mathf.Clamp01(config.collectibles.verticalOffsetCurve.Evaluate(shapeT)));

            float half    = LateralHalfRange(path, config, dist);
            float jitter  = half > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * half * 0.25f : 0f;
            float lateral = Mathf.Clamp(baseLateral + jitter, -half, half);

            events.Add(new TimelineEvent
            {
                eventTime      = songTime,
                eventDistance  = dist,
                eventType      = EventType.Ring,
                ringType       = c.type,
                lateralOffset  = lateral,
                verticalOffset = vOff,
                floorClearance = vFloor,
                strength       = c.strength,
                sourceFeature  = c.sourceFeature,
                confidence     = c.confidence,
                contributors   = c.sourceFeature,
            });
        }
    }

    private static float LateralHalfRange(MusicPath path, MusicRunnerGameplayConfig config, float eventDistance)
    {
        float halfWidth = path.GetWidth(eventDistance) * 0.5f;
        float usable    = halfWidth - config.collectibles.collectibleLateralMargin - config.collectibles.collectibleRadius;
        return Mathf.Max(0f, usable);
    }

    // config.collectibles.verticalOffsetCurve reshapes a uniform 0..1 roll into the actual height fraction —
    // its default shape keeps most rolls low (no jump needed), a smaller share moderately
    // elevated, and only a few reaching vCeil (a clear jump). See its tooltip for the exact
    // default keys; this is the single knob that controls "how often should this require a jump".
    private static float RollVertical(MusicRunnerGameplayConfig config, System.Random rng, float vFloor, float vCeil)
    {
        float t          = (float)rng.NextDouble();
        float heightFrac = Mathf.Clamp01(config.collectibles.verticalOffsetCurve.Evaluate(t));
        return Mathf.Lerp(vFloor, vCeil, heightFrac);
    }

    // Minimum CLEARANCE above the real surface for this specific collectible type, derived from
    // its actual authored prefab's mesh bounds where available (never hardcoded) — a cube half-
    // height + a small safety margin, so nothing can ever be generated embedded in the ground no
    // matter what vFloorDesign (bonusMinJumpHeightFactor * maxJumpHeight) is set to.
    //
    // Reserves clearance for the LARGEST the visual mesh ever gets, not just its resting
    // (scale=1) size: RingController's beat-synced pulse briefly scales the mesh up (an instant
    // "punch") past 1.0 — up to RingController.MaxPulseScale(type) — around the object's own
    // pivot, so without this the bottom of that momentarily-larger mesh could dip below the
    // surface even though the resting collider/mesh sat cleanly on top.
    private static float CollectibleFloor(RingType type, MusicRunnerGameplayConfig config, float vFloorDesign, float vCeil)
    {
        float safeMinClearance = CollectibleMaxHalfHeight(type, config) + config.collectibles.collectibleSurfaceClearance;
        return Mathf.Clamp(Mathf.Max(vFloorDesign, safeMinClearance), 0f, vCeil);
    }

    /// <summary>
    /// This type's half-height at the LARGEST scale its visual mesh ever reaches (its beat-pulse
    /// punch, RingController.MaxPulseScale — not just resting scale=1). Public/shared so both
    /// CollectibleFloor (generation-time) and GameplayManager.ActivateEvent (placement-time
    /// clearance split) use the exact same number — a debug tool verifying ground clearance can
    /// also call this directly instead of re-deriving it.
    /// </summary>
    public static float CollectibleMaxHalfHeight(RingType type, MusicRunnerGameplayConfig config)
    {
        var   prefab     = config.collectibles.ResolvePrefab(type);
        float halfHeight = config.collectibles.collectibleRadius;
        if (prefab != null)
        {
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                halfHeight = mf.sharedMesh.bounds.extents.y * prefab.transform.localScale.y;
        }
        return halfHeight * RingController.MaxPulseScale(type);
    }

    // ── Classification (Micro only — Impact/Drop/Peak are Macro, see BuildMacroEvents) ──────

    private const float ConfidentClassification = 0.6f; // a real classified onset, no help needed
    private const float AmbiguousConfidence      = 0.4f; // couldn't confidently tell which instrument

    /// <summary>
    /// Distinguishes Kick/Snare/HiHat from a single onset's spectral snapshot using centroid,
    /// band-ratio and spectral-flatness heuristics — no ML. When the evidence doesn't clearly
    /// point to one instrument, this returns Onset rather than forcing a guess — see the
    /// ambiguity margin check below. That is the honesty boundary this method can offer.
    ///
    /// The structural limit this can't cross without ML: a single detected onset gets exactly
    /// ONE label. A Kick and a HiHat landing on the exact same analysis frame (very common in
    /// four-on-the-floor dance music) sum into ONE combined spectrum — no amount of band-ratio
    /// or flatness heuristics on that single combined spectrum can recover two separate
    /// instruments from it. True separation of simultaneous overlapping transients needs
    /// source separation or a trained classifier, not more heuristics on one spectrum.
    /// </summary>
    private static (RingType type, float confidence) ClassifyOnset(
        SongProfile profile, float time, float[] bandAvgs, MusicRunnerGameplayConfig config)
    {
        // -1 = flatness data not computed (advancedTimbre off) — every check below treats that
        // as "no opinion" and falls back to the centroid/band-only behaviour.
        float flatness = profile.spectralFlatness != null ? profile.GetFlatnessAt(time) : -1f;

        // ── Primary: spectral centroid in Hz, gated by flatness where available ────────────
        //   < 350 Hz  → Kick (sub-bass thump; nothing else percussive lives this low)
        //   > 3500 Hz AND noisy (flatness > 0.5) → HiHat. The flatness gate matters: without
        //   it, a bright but TONAL onset (a high plucked note, a horn stab) misfires as HiHat
        //   just for being bright — flatness confirms it's actually noise-like, not melodic.
        if (profile.spectralCentroid != null &&
            profile.spectralCentroid.Length > 0 &&
            profile.analysisHopTime > 0f)
        {
            int   f = Mathf.Clamp(
                          Mathf.RoundToInt(time / profile.analysisHopTime),
                          0, profile.spectralCentroid.Length - 1);
            float c = profile.spectralCentroid[f];

            if (c < 350f) return (RingType.Kick, ConfidentClassification);
            if (c > 3500f && (flatness < 0f || flatness > 0.5f)) return (RingType.HiHat, ConfidentClassification);
        }

        // ── Secondary: band ratios normalised by per-band average ─────────────────────────
        int n = profile.bandEnvelopes?.Length ?? 0;
        if (n == 0 || bandAvgs == null || bandAvgs.Length == 0)
            return (RingType.Beat, AmbiguousConfidence); // no band data at all — can't classify

        float BandNorm(int b)
        {
            if (b >= n || b >= bandAvgs.Length || bandAvgs[b] < 0.0001f) return 0f;
            return profile.GetBandEnergyAt(b, time) / bandAvgs[b];
        }

        float low    = BandNorm(0) + BandNorm(1); // Sub Bass + Bass    (20-250Hz)   — Kick territory
        float snare  = BandNorm(2) + BandNorm(3); // Low Mid + Mid      (250-2000Hz) — snare body + crack
        float treble = BandNorm(4) + BandNorm(5); // High Mid + Treble (2000Hz+)     — HiHat / bright snare

        float max = Mathf.Max(low, Mathf.Max(snare, treble));
        if (max < 0.0001f) return (RingType.Onset, AmbiguousConfidence);
        if (low >= max)    return (RingType.Kick, ConfidentClassification);

        // A noisy transient in the mid/treble range is percussive (snare/hat); a clearly TONAL
        // one this loud is much more likely a melodic instrument the onset detector caught —
        // route it to Onset instead of forcing a Snare/HiHat label onto a guitar/keys/horn hit.
        if (flatness >= 0f && flatness < 0.35f) return (RingType.Onset, AmbiguousConfidence);

        // Ambiguity check: Snare vs HiHat must be a clear win, not a coin-flip — this is
        // exactly "several heuristics giving a similar probability", which should honestly
        // report Onset instead of forcing a specific instrument label onto uncertain evidence.
        float winner   = Mathf.Max(snare, treble);
        float runnerUp = Mathf.Min(snare, treble);
        float margin   = winner > 0.0001f ? (winner - runnerUp) / winner : 0f;
        if (margin < config.levelGeneration.classificationConfidenceMargin) return (RingType.Onset, AmbiguousConfidence);

        return (treble >= snare ? RingType.HiHat : RingType.Snare, ConfidentClassification);
    }

    private static float[] ComputeBandAverages(SongProfile profile)
    {
        int n = profile.bandEnvelopes?.Length ?? 0;
        if (n == 0) return System.Array.Empty<float>();

        var avgs = new float[n];
        for (int b = 0; b < n; b++)
        {
            var band = profile.bandEnvelopes[b];
            if (band == null || band.Length == 0) continue;
            float sum = 0f;
            for (int f = 0; f < band.Length; f++) sum += band[f];
            avgs[b] = sum / band.Length;
        }
        return avgs;
    }

    // Finds the song-time of the single highest-energy analysis frame — the track's climax.
    private static float FindPeakTime(SongProfile profile)
    {
        var env = profile.energyEnvelope;
        if (env == null || env.Length == 0) return -1f;

        int bestFrame = 0;
        for (int f = 1; f < env.Length; f++)
            if (env[f] > env[bestFrame]) bestFrame = f;

        return bestFrame * profile.analysisHopTime;
    }
}
