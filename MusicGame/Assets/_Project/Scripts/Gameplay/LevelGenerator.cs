using System.Collections.Generic;
using UnityEngine;

public class LevelGenerator
{
    // playerStartZ: the player's actual Z at game start so rings are placed in perfect sync
    public RingData[] Generate(SongProfile profile, GameplayConfig config, float playerStartZ = 0f)
    {
        var    rings        = new List<RingData>();
        float  offset       = playerStartZ + config.warmupTime * config.trackSpeed;
        float[] lastLaneTime = new float[config.laneCount];
        for (int i = 0; i < lastLaneTime.Length; i++) lastLaneTime[i] = -999f;

        int counter = 0;

        // ── Onset-driven rings ───────────────────────────────────────────────
        if (config.useOnsets && profile.onsetTimes != null)
        {
            foreach (float t in profile.onsetTimes)
            {
                if (profile.GetEnergyAt(t) < profile.averageEnergy * config.energyThreshold)
                    continue;

                RingType type = ClassifyOnset(profile, t);
                int      lane = PickLane(type, counter, config);
                float    y    = type == RingType.HiHat ? config.jumpY : config.groundY;

                if (t - lastLaneTime[lane] < config.minRingSpacing) continue;
                lastLaneTime[lane] = t;

                rings.Add(new RingData
                {
                    Type     = type,
                    Time     = t,
                    Position = new Vector3(LaneX(lane, config), y, t * config.trackSpeed + offset),
                });
                counter++;
            }
        }

        // ── Beat-grid fill ────────────────────────────────────────────────────
        if (config.useBeatGrid && profile.estimatedBPM > 0f)
        {
            float period    = 60f / profile.estimatedBPM * config.beatGridSubdivision;
            float proximity = period * 0.28f;
            int   beatCount = 0;

            for (float t = period; t < profile.duration - 0.5f; t += period)
            {
                if (profile.GetEnergyAt(t) < profile.averageEnergy * config.energyThreshold)
                    continue;

                bool occupied = false;
                foreach (var r in rings)
                    if (Mathf.Abs(r.Time - t) < proximity) { occupied = true; break; }

                if (!occupied)
                {
                    // Beat-grid rings cycle through outer lanes only (keep center free for player)
                    int lane = beatCount % 2 == 0 ? 0 : config.laneCount - 1;
                    rings.Add(new RingData
                    {
                        Type     = RingType.Beat,
                        Time     = t,
                        Position = new Vector3(LaneX(lane, config), config.groundY, t * config.trackSpeed + offset),
                    });
                    beatCount++;
                }
            }
        }

        rings.Sort((a, b) => a.Time.CompareTo(b.Time));
        return rings.ToArray();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RingType ClassifyOnset(SongProfile profile, float time)
    {
        int n = profile.bandEnvelopes?.Length ?? 0;
        if (n == 0) return RingType.Beat;

        float low    = (n > 0 ? profile.GetBandEnergyAt(0, time) : 0f)
                     + (n > 1 ? profile.GetBandEnergyAt(1, time) : 0f);
        float mid    = n > 3 ? profile.GetBandEnergyAt(3, time) : 0f;
        float treble = n > 5 ? profile.GetBandEnergyAt(5, time) : 0f;
        float max    = Mathf.Max(low, mid, treble);

        if (max <= 0f)      return RingType.Onset;
        if (low    == max)  return RingType.Kick;
        if (treble == max)  return RingType.HiHat;
        if (mid    == max)  return RingType.Snare;
        return RingType.Onset;
    }

    // Distributes rings across all lanes — no type is stuck in the center
    private static int PickLane(RingType type, int idx, GameplayConfig cfg)
    {
        int lanes = cfg.laneCount;
        int mid   = lanes / 2;

        return type switch
        {
            // Kick: cycles all lanes so bass notes spread across the track
            RingType.Kick  => idx % lanes,
            // Snare: left–right alternation (snappy, side-to-side feel)
            RingType.Snare => idx % 2 == 0 ? 0 : lanes - 1,
            // HiHat: all lanes, higher Y requires a jump
            RingType.HiHat => idx % lanes,
            // Onset: avoids center to force movement
            RingType.Onset => idx % 2 == 0 ? 0 : lanes - 1,
            // Default (Beat handled separately above)
            _              => mid,
        };
    }

    private static float LaneX(int lane, GameplayConfig cfg)
        => (lane - cfg.laneCount / 2) * cfg.laneWidth;
}
