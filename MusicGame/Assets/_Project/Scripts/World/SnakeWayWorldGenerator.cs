using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a floating "snake way" path driven by audio analysis data.
///
/// Algorithm:
///   1. Turtle-walk: start at startPosition, advance one control point every
///      controlPointInterval seconds of music.
///   2. Turn angle, height and width at each CP come from SongProfile.
///   3. Warmup period → straight approach, max width, no curves.
///   4. Catmull-Rom spline connects CPs.
///   5. Dense spline march produces the final MusicPath.Sample array.
///
/// Musical modulations:
///   - Horizontal curvature  ← timbralChange  (+ seeded noise)
///   - Height                ← intensity + buildup
///   - Width                 ← density (sparse music → narrower, dense music → wider)
///   - Impact / drop events  ← brief hard kink in height (within jugability limit)
/// </summary>
public class SnakeWayWorldGenerator : IMusicWorldGenerator
{
    public string Name => "SnakeWay";

    // Cached from config during Generate()
    private float _cpInterval;        // seconds per control point
    private float _sampleSpacing;     // arc-length units between MusicPath samples
    private float _baseY;             // starting height
    private float _heightAmp;         // max height variation (units)
    private float _maxTurn;           // max turn per interval (degrees)
    private float _minWidth;
    private float _maxWidth;
    private float _speed;

    public MusicPath Generate(SongProfile profile, MusicRunnerGameplayConfig config, Vector3 startPos)
    {
        var level = config.levelGeneration;
        _cpInterval   = level.pathControlPointInterval;
        _sampleSpacing = level.pathSampleSpacing;
        _baseY        = startPos.y;
        _heightAmp    = level.pathHeightAmplitude;
        _maxTurn      = level.pathMaxTurnAngle;
        // basePathWidth ± half the configured variation — see MusicRunnerLevelConfig's doc
        // comment on basePathWidth for why this is expressed as base+variation instead of a raw
        // min/max pair.
        float halfVariation = Mathf.Max(0f, level.pathWidthVariation) * 0.5f;
        _minWidth     = Mathf.Max(1f, level.basePathWidth - halfVariation);
        _maxWidth     = Mathf.Max(_minWidth, level.basePathWidth + halfVariation);
        _speed        = config.core.playerSpeed;

        var rng = new System.Random(level.pathSeed);

        var cps    = new List<Vector3>();
        var widths = new List<float>();

        // ── Warmup approach: straight runway, full width ──────────────────────
        Vector3 pos   = startPos;
        float   theta = 0f;            // heading in radians; 0 = +Z world

        float warmupDist = config.core.warmupTime * _speed;
        int   warmupCPs  = Mathf.Max(1, Mathf.CeilToInt(config.core.warmupTime / _cpInterval));
        float stepWarmup = warmupDist / warmupCPs;

        cps.Add(pos);
        widths.Add(_maxWidth);

        for (int i = 1; i <= warmupCPs; i++)
        {
            pos += new Vector3(0f, 0f, stepWarmup);
            cps.Add(pos);
            widths.Add(_maxWidth);
        }

        // ── Musical section ───────────────────────────────────────────────────
        float songDuration = profile.duration;
        float stepSong     = _cpInterval * _speed;

        // Collect impact / drop times for marked kinks
        var impactSet = new HashSet<float>();
        if (profile.impactTimes != null)
            foreach (float it in profile.impactTimes) impactSet.Add(it);
        if (profile.dropTimes != null)
            foreach (float dt in profile.dropTimes) impactSet.Add(dt);

        // Half-width of the smoothing window applied to every musical signal below, BEFORE it
        // becomes a control-point target — see SampleWindowed's doc comment for why this (not
        // the per-CP lerp/clamps further down) is the real fix for sharp path spikes.
        float signalHalfWindow = _cpInterval * 0.5f;

        for (float t = _cpInterval; t < songDuration + _cpInterval; t += _cpInterval)
        {
            float tc = Mathf.Min(t, songDuration - 0.05f);

            float intensity = profile.intensity    != null ? SampleWindowed(profile.intensity,    profile.analysisHopTime, tc, signalHalfWindow) : 0.5f;
            float timbral   = profile.timbralChange != null ? SampleWindowed(profile.timbralChange, profile.analysisHopTime, tc, signalHalfWindow) : 0.0f;
            float density   = profile.density      != null ? SampleWindowed(profile.density,      profile.analysisHopTime, tc, signalHalfWindow) : 0.5f;
            float buildup   = profile.buildupCurve != null ? SampleWindowed(profile.buildupCurve, profile.analysisHopTime, tc, signalHalfWindow) : 0f;
            float rawEnergy = profile.averageEnergy > 0f
                ? SampleWindowed(profile.energyEnvelope, profile.analysisHopTime, tc, signalHalfWindow) / profile.averageEnergy
                : 1f;

            // Horizontal turn: timbral change + seeded noise
            float noise  = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.15f;
            float dTheta = (timbral + noise) * _maxTurn * Mathf.Deg2Rad * _cpInterval;
            theta = Mathf.Clamp(theta + dTheta, -80f * Mathf.Deg2Rad, 80f * Mathf.Deg2Rad);

            // Height: energy + buildup push up; silence lets it fall gently
            float hTarget = _baseY + (rawEnergy * 0.55f + intensity * 0.25f + buildup * 0.20f) * _heightAmp;

            // Impact/drop: sharp but bounded height spike
            bool nearImpact = false;
            foreach (float et in impactSet)
                if (Mathf.Abs(et - tc) < _cpInterval * 0.6f) { nearImpact = true; break; }

            if (nearImpact)
                hTarget += _heightAmp * 0.25f;  // noticeable bump, never huge

            hTarget = Mathf.Clamp(hTarget, _baseY - _heightAmp * 0.35f, _baseY + _heightAmp);

            // Smooth height toward last CP
            float prevH = cps.Count > 0 ? cps[cps.Count - 1].y : _baseY;
            hTarget = Mathf.Lerp(prevH, hTarget, 0.45f);  // smoothing prevents jolts

            // Width: sparse/quiet music → narrower, contained; dense/busy music → wider, more
            // room to spread out activity and collectibles.
            float w = Mathf.Lerp(_minWidth, _maxWidth, Mathf.Clamp01(density));

            // Advance position
            pos.x += Mathf.Sin(theta) * stepSong;
            pos.z += Mathf.Cos(theta) * stepSong;
            pos.y  = hTarget;

            cps.Add(pos);
            widths.Add(w);
        }

        // ── Farewell tail: flat, straight extension past the song's own natural end ────────────
        // MusicPath.GetSample clamps to [0, TotalLength] — without real path here, the farewell
        // stretch (GameplayManager keeps the player/camera/ground advancing via
        // MusicClock.BeginManualAdvance even after the AudioSource stops — see its own doc) would
        // sample a CLAMPED, frozen position the instant distance ran past the end of the musical
        // section above, instead of a real one: Player/camera/ground all visibly stop dead while
        // the farewell timer keeps counting internally. This only actually matters when the played
        // window reaches (or nearly reaches) the song's own real end — a short manual play range
        // already has plenty of already-generated path beyond it — but is always added
        // unconditionally rather than conditionally sized to the resolved play range, since it's
        // harmless surplus otherwise (no gameplay event ever spawns past _songEndDistance — see
        // GameplayManager's own doc — so nothing ever reacts to this extra stretch existing).
        // Flat (no turn, constant height, full width) matches the ground mesh's own end-of-song
        // visual flattening (MusicWorldManager.EndingFadeMultiplier) — nothing musical should still
        // be steering/bumping the path once the song has actually finished.
        float farewellDistance = Mathf.Max(0f, config.core.farewellSeconds) * _speed;
        if (farewellDistance > 0f)
        {
            int   tailCPs    = Mathf.Max(1, Mathf.CeilToInt((config.core.farewellSeconds + _cpInterval) / _cpInterval));
            float stepTail   = (farewellDistance + _cpInterval * _speed) / tailCPs; // +1 extra interval of margin
            float tailHeight = cps.Count > 0 ? cps[cps.Count - 1].y : _baseY;

            for (int i = 0; i < tailCPs; i++)
            {
                pos.x += Mathf.Sin(theta) * stepTail;
                pos.z += Mathf.Cos(theta) * stepTail;
                pos.y  = tailHeight;
                cps.Add(pos);
                widths.Add(_maxWidth);
            }
        }

        return BuildPath(cps, widths);
    }

    // ── Catmull-Rom spline → MusicPath ────────────────────────────────────────
    //
    // This path stays a single smooth centreline (coarse, per-control-point contour only —
    // buildups/drops/sections). The per-BAND frequency deformation (the thing you actually
    // see and stand on) now lives entirely in MusicWorldManager's ground mesh, because it
    // varies across the path's WIDTH (one "column" per band), which a single centreline
    // can't represent. Gameplay logic (rings, camera, spawn timing) keeps using this simple
    // line; only the visual/physical ground layers extra shape on top of it.

    private MusicPath BuildPath(List<Vector3> cps, List<float> widths)
    {
        int n = cps.Count;
        if (n < 2) return new MusicPath(new MusicPath.Sample[0]);

        // Fine-resolution spline march
        const int StepsPerSeg = 80;
        var fine = new List<(Vector3 pos, float width)>(n * StepsPerSeg);

        for (int seg = 0; seg < n - 1; seg++)
        {
            // Phantom endpoints at boundaries
            Vector3 p0 = seg > 0     ? cps[seg - 1] : 2f * cps[seg] - cps[seg + 1];
            Vector3 p1 = cps[seg];
            Vector3 p2 = cps[seg + 1];
            Vector3 p3 = seg + 2 < n ? cps[seg + 2] : 2f * cps[seg + 1] - cps[seg];

            float w1 = widths[seg], w2 = widths[seg + 1];

            for (int s = 0; s < StepsPerSeg; s++)
            {
                float t = (float)s / StepsPerSeg;
                fine.Add((CatmullRom(p0, p1, p2, p3, t), Mathf.Lerp(w1, w2, t)));
            }
        }
        fine.Add((cps[n - 1], widths[n - 1]));

        // Arc-length subsampling at _sampleSpacing
        var samples = new List<MusicPath.Sample>();
        float totalDist   = 0f;
        float nextSample  = 0f;

        for (int i = 0; i < fine.Count; i++)
        {
            if (i > 0) totalDist += Vector3.Distance(fine[i].pos, fine[i - 1].pos);

            while (totalDist >= nextSample)
            {
                // Tangent via central difference in the fine array
                int prev = Mathf.Max(0, i - 1);
                int next = Mathf.Min(fine.Count - 1, i + 1);
                Vector3 tan = fine[next].pos - fine[prev].pos;
                if (tan.sqrMagnitude < 0.0001f) tan = Vector3.forward;

                samples.Add(new MusicPath.Sample(fine[i].pos, tan, nextSample, fine[i].width));
                nextSample += _sampleSpacing;
            }
        }

        // Guarantee final sample
        var last = fine[fine.Count - 1];
        int lp   = fine.Count - 2 >= 0 ? fine.Count - 2 : 0;
        Vector3 lt = last.pos - fine[lp].pos;
        if (lt.sqrMagnitude < 0.0001f) lt = Vector3.forward;
        if (samples.Count == 0 || samples[samples.Count - 1].distance < totalDist - 0.001f)
            samples.Add(new MusicPath.Sample(last.pos, lt, totalDist, last.width));

        return new MusicPath(samples.ToArray());
    }

    // SongProfile.GetXAt() helpers all do a NEAREST-FRAME lookup — a single raw per-hop-analysis
    // value, no temporal averaging. Feeding that single frame straight into a control point
    // means ordinary frame-to-frame analysis noise (especially in timbralChange, a rate-of-
    // change signal) shows up directly as a control-point-sized kink every pathControlPointInterval
    // seconds. This averages the signal over a small window BEFORE it becomes a target — the
    // musical-signal-level smoothing step the centerline was missing (the per-CP height lerp and
    // the theta/height clamps further down are a secondary safety net, not the primary fix).
    private static float SampleWindowed(float[] arr, float hopTime, float time, float halfWindow)
    {
        if (arr == null || arr.Length == 0 || hopTime <= 0f) return 0f;

        int centerFrame = Mathf.Clamp(Mathf.FloorToInt(time / hopTime), 0, arr.Length - 1);
        int radius      = Mathf.Max(1, Mathf.RoundToInt(halfWindow / hopTime));
        int lo = Mathf.Max(0, centerFrame - radius);
        int hi = Mathf.Min(arr.Length - 1, centerFrame + radius);

        float sum = 0f;
        for (int f = lo; f <= hi; f++) sum += arr[f];
        return sum / (hi - lo + 1);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}
