using System.Collections;
using UnityEngine;

/// <summary>
/// Detects when the player falls off the path and handles the full respawn flow:
///   fall detected → audio fade-out → wait → seek to the EXACT fallen-at songTime → fade-in → resume
///
/// No discrete checkpoints — the player always resumes at the precise musical moment they fell
/// at (MusicClock.SongTime captured the instant the fall triggers), never a fixed earlier point.
///
/// Created dynamically by GameplayManager — call Initialize() after AddComponent.
/// </summary>
public class FallRespawnSystem : MonoBehaviour
{
    public static FallRespawnSystem Instance { get; private set; }

    // Set by Initialize()
    private AudioSource     _audio;
    private PlayerController _player;
    private GameplayManager  _manager;
    private MusicRunnerCoreConfig _config;

    private bool _active;
    private bool _processing;

    // ── Factory / Init ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(AudioSource audio, PlayerController player,
                           GameplayManager manager, MusicRunnerCoreConfig config)
    {
        _audio   = audio;
        _player  = player;
        _manager = manager;
        _config  = config;
    }

    public void Activate()   => _active = true;
    public void Deactivate() { _active = false; _processing = false; }

    // ── Manual restart (Pause menu → Restart Song) ────────────────────────────

    /// <summary>
    /// Reuses the exact same RestartSong() coroutine the automatic post-fall path uses, so
    /// there is a single reset routine instead of two divergent ones. Also reuses
    /// PauseController's OWN Resume() (not a copy of its logic) to guarantee this is entered in
    /// exactly the same unpaused state the pause-menu's "Restart Song" button already relies on
    /// (Time.timeScale, AudioSource, MusicClock, and — importantly — PauseController's own
    /// `_paused` flag itself, which only Resume() clears; leaving it true would pop the PAUSED
    /// overlay right back up the instant this restart finishes and _ended clears). Resume() is a
    /// no-op if we weren't actually paused (the common end-screen case), so this is always safe.
    /// </summary>
    public void RestartSongManually()
    {
        if (_processing) return;
        StartCoroutine(RestartSongManuallyRoutine());
    }

    private IEnumerator RestartSongManuallyRoutine()
    {
        _processing = true;
        Debug.Log("[RESTART] RestartSongManuallyRoutine begin");

        // Whatever caller got us here (end screen, pause menu), always start from the SAME
        // known-good unpaused state pause menu's own Restart already depends on — see doc above.
        PauseController.Instance?.Resume();

        if (_manager != null) _manager.SuppressSongEnd = true;

        // Not mid-fall-fade, so the AudioSource is still at its normal volume — use that as
        // the fade-in target instead of the fall path's captured pre-fade-out volume.
        float targetVol = _audio.volume;

        // try/finally: guarantees _processing (and SuppressSongEnd) can never get stuck true —
        // a single uncaught exception here used to brick every future Restart click silently,
        // with no way to recover short of restarting the whole game.
        try
        {
            _manager?.ResetRunState();
            yield return StartCoroutine(RestartSong(targetVol));
        }
        finally
        {
            if (_manager != null) _manager.SuppressSongEnd = false;
            _processing = false;
        }
    }

    // ── Fall detection ────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_active || _processing || _player == null || _config == null) return;
        if (!_config.enableFallOffPath) return;

        // PlayerController is the single authority for this (computed from the same path sample
        // it moves against) — read its result instead of recomputing it here against a possibly
        // different sample/frame. Being laterally outside the track (IsAtLateralLimit) no longer
        // triggers this by itself — only actually sinking below the track's own surface plane
        // does, so a jump toward a bonus near/outside the edge can still be corrected mid-air
        // with air control instead of dying the instant the lateral clamp is reached.
        if (_player.IsBelowTrackSurface)
            TriggerFall();
    }

    // ── Trigger ───────────────────────────────────────────────────────────────

    public void TriggerFall()
    {
        if (_processing) return;
        _processing = true;

        // Captured HERE, synchronously, before any fade/coroutine delay — the audio (and
        // MusicClock, which derives from it) keeps advancing during the fade-out below, so this
        // is the only point that actually reflects "where the player was when they fell".
        float fallSongTime = MusicClock.Instance?.SongTime ?? 0f;

        Debug.Log($"[FALL] pos={_player.transform.position:F1}  y={_player.transform.position.y:F2}  " +
                  $"fallSongTime={fallSongTime:F2}  playerDistance={_player.ActualDistance:F1}");
        _player.EnterFallState();
        EventBus.Publish(new PlayerFellEvent { FallSongTime = fallSongTime });
        StartCoroutine(FallSequence(fallSongTime));
    }

    // ── Respawn flow ──────────────────────────────────────────────────────────

    private IEnumerator FallSequence(float fallSongTime)
    {
        float origVol = _audio.volume;

        // Tell GameplayManager to ignore isPlaying==false while we own the audio
        if (_manager != null) _manager.SuppressSongEnd = true;

        // Fade out
        float t = 0f, fadeOut = _config.fallFadeOutDuration;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            _audio.volume = Mathf.Lerp(origVol, 0f, t / fadeOut);
            yield return null;
        }
        _audio.volume = 0f;
        _audio.Pause();

        yield return new WaitForSeconds(Mathf.Max(0f, _config.checkpointRespawnDelay));

        yield return StartCoroutine(RespawnAtSongTime(fallSongTime, origVol));

        if (_manager != null) _manager.SuppressSongEnd = false;
        _processing = false;
    }

    /// <summary>
    /// Respawns the player at the EXACT songTime they fell at — no discrete checkpoint involved.
    /// Structurally the same flow the old checkpoint-based respawn used (resync audio/clock/
    /// ground BEFORE teleporting, so nothing can briefly disagree with the new position), just
    /// driven by an arbitrary songTime instead of a precomputed Checkpoint.
    /// </summary>
    private IEnumerator RespawnAtSongTime(float songTime, float targetVol)
    {
        float audioTime = Mathf.Max(0f, songTime - _config.warmupTime);

        // Resync audio/clock BEFORE the player repositions itself — so by the time
        // PlayerController.SnapToCanonicalPosition() reads MusicClock.MusicDistance below, it
        // already reflects the fall's songTime. Without this ordering, the ground window (still
        // centered wherever the player fell) could be far from the respawn point for up to a
        // frame, and MusicClock briefly still reflects the pre-fall distance — either can show up
        // as a fall-through/hitch right after respawn depending on script execution order, which
        // this removes entirely.
        _audio.time = audioTime;
        _audio.Play();
        MusicClock.Instance?.ForceUpdate();

        // Read back (not re-derived from songTime*speed — PlayerController.CanonicalDistance is
        // the only place that formula lives) purely to size the ground-rebuild window.
        float musicDistance = MusicClock.Instance?.MusicDistance ?? 0f;
        Debug.Log($"[RESPAWN] songTime={songTime:F2}  dist={musicDistance:F1}");
        MusicWorldManager.Instance?.RebuildNow(musicDistance);

        // Single authority for "where the player belongs" — no distance formula or surface-
        // sampling duplicated here (see PlayerController.SnapToCanonicalPosition's own doc).
        _player.SnapToCanonicalPosition();
        // Explicit, not inferred — CameraFollow doesn't guess a teleport happened by comparing
        // distances; it's told. Was already done in RestartSong() but missing here, which is
        // exactly why the camera used to visibly glide back to center after a fall respawn.
        CameraFollow.Instance?.SnapToPlayer();
        Debug.Log($"[PLAYER RESET] forwardOffset={_player.ForwardOffset:F2}  lateralOffset={_player.LateralOffset:F2}  " +
                  $"verticalVelocity={_player.VerticalVelocity:F2}");

        _manager.ReturnAllActiveToPool();
        _manager.SetNextEventIndex(FindFirstEventIndexAtOrAfter(songTime));
        _manager.ResyncMacroIndex(songTime);

        yield return StartCoroutine(FadeIn(targetVol, _config.checkpointMusicFadeInDuration));

        // -1 = "a normal mid-song fall-respawn happened" — distinct from the full-restart
        // signal (CheckpointIndex==0) GameplayHUD/PauseController un-freeze on; a fall can only
        // happen mid-run anyway, so neither of those is showing at this point regardless.
        EventBus.Publish(new PlayerRespawnedEvent { CheckpointIndex = -1 });
    }

    /// <summary>First timeline event at or after the given songTime — same lookup
    /// CheckpointSystem used to precompute per-checkpoint, done on demand here instead since
    /// there's no fixed set of respawn points to precompute it for anymore.</summary>
    private int FindFirstEventIndexAtOrAfter(float songTime)
    {
        var events = _manager != null ? _manager.Timeline?.Events : null;
        if (events == null) return 0;
        for (int i = 0; i < events.Length; i++)
            if (events[i].eventTime >= songTime) return i;
        return events.Length;
    }

    private IEnumerator RestartSong(float targetVol)
    {
        Debug.Log("[RESPAWN] RestartSong (full restart — end screen / pause menu)");

        _audio.Stop();
        _audio.time = 0f;

        _manager.ReturnAllActiveToPool();
        _manager.SetNextEventIndex(0);
        _manager.ResyncMacroIndex(0f);
        CheckpointSystem.Instance?.ResetToStart();
        // 0 warmup — a manual restart plays back immediately (the player already knows the
        // game; this isn't the first-ever level generation, which is the only place the normal
        // config.warmupTime count-in lead-in makes sense). Passing the real warmupTime here
        // would make SongTime hard-snap to that offset the instant audio plays below, ahead of
        // where the player was just teleported (distance 0) — see ReinitializeClock's doc.
        _manager.ReinitializeClock(0f);

        if (MusicWorldManager.Instance?.Path != null)
        {
            MusicWorldManager.Instance.RebuildNow(0f);
            // Single authority for "where the player belongs" — MusicClock.MusicDistance is
            // already 0 here (ReinitializeClock(0f) above), so this places the player at the
            // path start exactly like before, without duplicating the sample/surface logic.
            _player.SnapToCanonicalPosition();
            CameraFollow.Instance?.SnapToPlayer();
            Debug.Log($"[PLAYER RESET] forwardOffset={_player.ForwardOffset:F2}  lateralOffset={_player.LateralOffset:F2}  " +
                      $"verticalVelocity={_player.VerticalVelocity:F2}");
        }

        // No wait here anymore — a manual restart (end screen / pause menu) must go the instant
        // it's clicked, not after a hidden countdown where nothing visibly happens (that dead
        // time was what made Restart look like it needed a second click: the first click's
        // effect only became visible ~warmupTime later, right as an impatient second click
        // landed). Audio plays back immediately at distance 0.
        _audio.Play();
        MusicClock.Instance?.ForceUpdate();

        // Re-arms the main Update() loop/PlayerController/fall-detection — a no-op if the game
        // was already running (the old automatic no-checkpoint-fall path), but ESSENTIAL when
        // this restart was requested from the END SCREEN, where the song already naturally
        // ended and stopped all three of these.
        _manager?.ResumeRunning();

        yield return StartCoroutine(FadeIn(targetVol, _config.checkpointMusicFadeInDuration));

        Debug.Log("[RESTART] RestartSong complete, publishing PlayerRespawnedEvent(0)");
        EventBus.Publish(new PlayerRespawnedEvent { CheckpointIndex = 0 });
    }

    /// <summary>
    /// Unscaled delta time — this fade-in is the LAST step of the manual-restart flow (end
    /// screen / pause menu) before PlayerRespawnedEvent(0) fires and un-freezes the end-screen
    /// UI. If it used scaled Time.deltaTime, any moment where Time.timeScale isn't exactly 1
    /// here would stall this loop forever, silently keeping the results screen up with no way
    /// to tell why — exactly the failure mode the WaitForSecondsRealtime warmup wait above was
    /// already written to avoid. This closes the same gap for the fade-in.
    /// </summary>
    private IEnumerator FadeIn(float targetVol, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _audio.volume = Mathf.Lerp(0f, targetVol, t / dur);
            yield return null;
        }
        _audio.volume = targetVol;
    }
}
