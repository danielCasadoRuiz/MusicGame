using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Auto-generates checkpoints at fixed song-time intervals.
/// Tracks the player's current checkpoint as they advance along the path.
/// Used by FallRespawnSystem for LastCheckpoint respawn mode.
/// </summary>
public class CheckpointSystem : MonoBehaviour
{
    public static CheckpointSystem Instance { get; private set; }

    public struct Checkpoint
    {
        public int      index;
        public float    songTime;       // MusicClock.SongTime at this checkpoint
        public float    audioTime;      // audioSource.time to seek to (= songTime - warmupTime)
        public float    musicDistance;  // = songTime * unitsPerSecond
        public Vector3  position;       // world position on path
        public Quaternion rotation;     // path-aligned rotation
        public int      firstEventIndex;// first timeline event at or after this checkpoint
    }

    private Checkpoint[] _checkpoints;
    private int          _currentIdx;

    // ── Public API ─────────────────────────────────────────────────────────────
    public bool       HasCheckpoints    => _checkpoints != null && _checkpoints.Length > 0;
    public int        CurrentIndex      => _currentIdx;
    public Checkpoint CurrentCheckpoint => HasCheckpoints ? _checkpoints[_currentIdx] : default;
    public Checkpoint[] All             => _checkpoints;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public void Initialize(SongProfile    profile,
                           GameplayConfig config,
                           MusicPath      path,
                           GameplayTimeline timeline)
    {
        _currentIdx = 0;

        if (!config.enableCheckpoints)
        {
            // Single checkpoint at start only (used for RestartSong mode)
            var startSample = path.GetSample(0f);
            _checkpoints = new[]
            {
                new Checkpoint
                {
                    index           = 0,
                    songTime        = 0f,
                    audioTime       = 0f,
                    musicDistance   = 0f,
                    position        = SurfacePosition(startSample, 0f),
                    rotation        = Quaternion.LookRotation(startSample.tangent, Vector3.up),
                    firstEventIndex = 0,
                }
            };
            return;
        }

        float warmup   = config.warmupTime;
        float interval = config.checkpointIntervalSeconds;
        float speed    = config.playerSpeed;

        var list = new List<Checkpoint>();

        // Checkpoint 0 = song start (after warmup)
        for (float songT = warmup; songT <= warmup + profile.duration + 0.01f; songT += interval)
        {
            float audioT = songT - warmup;
            if (audioT > profile.duration + 0.01f) break;

            float dist   = songT * speed;
            var   sample = path.GetSample(dist);

            // First event index at or after this song time
            int evtIdx = timeline?.Events.Length ?? 0;
            if (timeline != null)
            {
                for (int i = 0; i < timeline.Events.Length; i++)
                {
                    if (timeline.Events[i].eventTime >= songT) { evtIdx = i; break; }
                }
            }

            list.Add(new Checkpoint
            {
                index           = list.Count,
                songTime        = songT,
                audioTime       = Mathf.Max(0f, audioT),
                musicDistance   = dist,
                position        = SurfacePosition(sample, dist),
                rotation        = Quaternion.LookRotation(sample.tangent, Vector3.up),
                firstEventIndex = evtIdx,
            });
        }

        _checkpoints = list.ToArray();
        Debug.Log($"[Checkpoints] {_checkpoints.Length} checkpoints generated " +
                  $"every {interval}s ({profile.duration:F1}s song).");
    }

    // The MusicPath sample is the path's CENTERLINE — the actual walkable surface sits above
    // it by the frequency-driven relief (MusicWorldManager). A checkpoint teleport that used the
    // raw centerline could drop the player metres below the real mesh once maxFrequencyHeight
    // is non-trivial — this is THE authoritative surface query (same one the ground mesh and
    // collectibles use), evaluated at the path's centerline (lateralOffset = 0).
    private static Vector3 SurfacePosition(MusicPath.Sample sample, float distance)
    {
        var world = MusicWorldManager.Instance;
        return world != null ? world.SampleSurface(distance, 0f).position : sample.position;
    }

    // ── Tracking ──────────────────────────────────────────────────────────────

    /// <summary>Call every frame with the current MusicClock.SongTime.</summary>
    public void UpdateCurrent(float songTime)
    {
        if (!HasCheckpoints) return;
        while (_currentIdx + 1 < _checkpoints.Length &&
               _checkpoints[_currentIdx + 1].songTime <= songTime)
        {
            _currentIdx++;
            EventBus.Publish(new CheckpointReachedEvent { Index = _currentIdx });
        }
    }

    public void ResetToStart() => _currentIdx = 0;
}
