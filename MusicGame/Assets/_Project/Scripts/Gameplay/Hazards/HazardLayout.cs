using System.Collections.Generic;
using UnityEngine;

/// <summary>One placed hazard: an obstacle of `kind`, or the chaser (isChaser), at a path distance.</summary>
public struct HazardPlacement
{
    public float distance;       // path distance of its centre (= songTime · speed, like the bonuses)
    public float songTime;
    public bool  isChaser;
    public RunnerObstacleKind kind;
    public float lateral;        // obstacle: centre of its block / opening; chaser: sleeping side (±1)
    public float trackWidth;     // the track width it was fitted to
}

/// <summary>
/// Deterministic hazard placement for one combination: same bonus layout seed + difficulty + timeline ⇒ the
/// same hazards (no attempt number, nothing stored — it is recomputed identically every time). It READS the
/// bonuses and never moves them: an obstacle only goes where no bonus (musical, Life, Special) is within
/// bonusClearance of its faces. Few, gentle hazards at low difficulty; more frequent and more demanding kinds
/// (portal → crawl wall → window) as the difficulty rises. Every obstacle keeps a passage that the player's
/// real controls can use (block: a free side ≥ minPassage; walls/frames: their opening). No obstacle while a
/// chase may be running, and never two hazards closer than the difficulty's gap.
/// </summary>
public static class HazardLayout
{
    public const int Version = 1;

    public static List<HazardPlacement> Generate(RunnerHazardsConfig cfg, TimelineEvent[] events, MusicPath path,
                                                 int layoutSeed, float difficulty, float playStartSongTime, float playEndSongTime, float speed)
    {
        var list = new List<HazardPlacement>();
        if (cfg == null || !cfg.enabled || path == null) return list;
        if (cfg.testGallery && RunnerHazardsConfig.TestAllowed) return Gallery(cfg, path, playStartSongTime, playEndSongTime, speed);

        var rng = new System.Random(unchecked(layoutSeed * 31 + 0x5A17C0DE + Version));
        float d = Mathf.Clamp01(difficulty);
        float gap = RunnerHazardsConfig.Lerp(cfg.easyObstacleGapSeconds, cfg.hardObstacleGapSeconds, d);
        float chaserGap = RunnerHazardsConfig.Lerp(cfg.easyChaserGapSeconds, cfg.hardChaserGapSeconds, d);
        float chaserDuration = RunnerHazardsConfig.Lerp(cfg.easyChaserDuration, cfg.hardChaserDuration, d);
        float end = playEndSongTime - cfg.endClearSeconds;

        var kinds = new List<RunnerObstacleKind>();
        foreach (RunnerObstacleKind k in System.Enum.GetValues(typeof(RunnerObstacleKind)))
            if (d >= cfg.UnlockFor(k) && cfg.PrefabFor(k) != null) kinds.Add(k);

        float t = playStartSongTime + cfg.firstHazardAfterSeconds;
        float nextChaser = playStartSongTime + cfg.firstChaserAfterSeconds;
        float chaseBusyUntil = float.MinValue;
        while (t < end)
        {
            // A chaser when its own gap allows it (only one at a time; the next obstacle waits for its chase).
            if (cfg.chaserPrefab != null && t >= nextChaser && rng.NextDouble() < 0.5)
            {
                float dist = t * speed;
                list.Add(new HazardPlacement { distance = dist, songTime = t, isChaser = true, lateral = rng.NextDouble() < 0.5 ? -1f : 1f, trackWidth = path.GetWidth(dist) });
                chaseBusyUntil = t + chaserDuration + 3f;
                nextChaser = t + chaserDuration + chaserGap;
                t = chaseBusyUntil;
                continue;
            }
            if (kinds.Count > 0 && t >= chaseBusyUntil && TryPlaceObstacle(cfg, events, path, rng, kinds, t, speed, out var placed))
            {
                list.Add(placed);
                t = placed.songTime + gap * (1f + cfg.gapJitter * (float)rng.NextDouble());
            }
            else t += 0.5f; // no free / wide enough spot here: look a bit later
        }
        return list;
    }

    // Searches the next ~2 s for a spot whose clearance window is free of bonuses and whose track is wide enough.
    private static bool TryPlaceObstacle(RunnerHazardsConfig cfg, TimelineEvent[] events, MusicPath path, System.Random rng,
                                         List<RunnerObstacleKind> kinds, float t0, float speed, out HazardPlacement placed)
    {
        placed = default;
        var kind = kinds[rng.Next(kinds.Count)];
        var prefab = cfg.PrefabFor(kind);
        for (float t = t0; t < t0 + 2f; t += 0.1f)
        {
            float dist = t * speed;
            float width = path.GetWidth(dist);
            if (width < Mathf.Max(cfg.minTrackWidth, prefab.RequiredTrackWidth)) continue;
            float clear = prefab.Length * 0.5f + cfg.bonusClearance;
            float lateral = LateralFor(prefab, width, rng);
            if (!FreeOfBonuses(events, dist, clear, BlockFootprint(prefab, width, lateral))) continue;
            placed = new HazardPlacement { distance = dist, songTime = t, kind = kind, lateral = lateral, trackWidth = width };
            return true;
        }
        return false;
    }

    // Block: on one side or the centre (the rest of the track stays free); window: centred; openings: anywhere
    // that keeps them fully on the track (0.3 m from each edge).
    private static float LateralFor(RunnerObstacle prefab, float width, System.Random rng)
    {
        float half = width * 0.5f;
        switch (prefab.kind)
        {
            case RunnerObstacleKind.WindowWall:
            case RunnerObstacleKind.Portal:
                return 0f;
            case RunnerObstacleKind.LowBlock:
            case RunnerObstacleKind.HighBlock:
            {
                float bw = prefab.BlockWidth(width);
                float edge = half - bw * 0.5f;           // flush with an edge
                int pick = rng.Next(3);                   // left / centre / right
                float lat = pick == 0 ? -edge : pick == 2 ? edge : 0f;
                // Centre only if BOTH sides keep a passage; otherwise flush with an edge (the other side is free).
                if (pick == 1 && half - bw * 0.5f < prefab.minPassage) lat = rng.NextDouble() < 0.5 ? -edge : edge;
                return lat;
            }
            default:
            {
                float range = Mathf.Max(0f, half - prefab.HalfFeatureWidth(width) - 0.3f);
                return ((float)rng.NextDouble() * 2f - 1f) * range * 0.6f;
            }
        }
    }

    /// <summary>A block's lateral footprint (centre, half width + 0.8 m margin) — bonuses beyond it are on the free
    /// side and don't conflict; null for full-width obstacles (walls / frames / U), which need the window clear.</summary>
    public static (float lateral, float half)? BlockFootprint(RunnerObstacle prefab, float width, float lateral) =>
        prefab.kind == RunnerObstacleKind.LowBlock || prefab.kind == RunnerObstacleKind.HighBlock
            ? (lateral, prefab.BlockWidth(width) * 0.5f + 0.8f) : null;

    public static bool FreeOfBonuses(TimelineEvent[] events, float distance, float clear, (float lateral, float half)? footprint = null)
    {
        if (events == null) return true;
        // events are sorted by eventTime (= distance / speed): binary search the window start.
        int lo = 0, hi = events.Length;
        while (lo < hi) { int m = (lo + hi) >> 1; if (events[m].eventDistance < distance - clear) lo = m + 1; else hi = m; }
        for (int i = lo; i < events.Length && events[i].eventDistance <= distance + clear; i++)
            if (events[i].eventType == EventType.Ring &&
                (!footprint.HasValue || Mathf.Abs(events[i].lateralOffset - footprint.Value.lateral) < footprint.Value.half)) return false;
        return true;
    }

    // TEST gallery: every obstacle kind in turn (whatever the difficulty), a chaser after each round.
    private static List<HazardPlacement> Gallery(RunnerHazardsConfig cfg, MusicPath path, float start, float end, float speed)
    {
        var list = new List<HazardPlacement>();
        var kinds = (RunnerObstacleKind[])System.Enum.GetValues(typeof(RunnerObstacleKind));
        int i = 0;
        for (float t = start + 4f; t < end - 2f; t += cfg.testGalleryGapSeconds, i++)
        {
            float dist = t * speed, width = Mathf.Max(path.GetWidth(dist), 0.1f);
            int slot = i % (kinds.Length + 1);
            if (slot == kinds.Length)
            {
                if (cfg.chaserPrefab != null) list.Add(new HazardPlacement { distance = dist, songTime = t, isChaser = true, lateral = (i & 1) == 0 ? 1f : -1f, trackWidth = width });
                t += 6f;
                continue;
            }
            var prefab = cfg.PrefabFor(kinds[slot]);
            if (prefab == null) continue;
            list.Add(new HazardPlacement { distance = dist, songTime = t, kind = kinds[slot], lateral = LateralFor(prefab, width, new System.Random(i)), trackWidth = width });
        }
        return list;
    }
}
