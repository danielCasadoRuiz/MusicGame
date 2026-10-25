# Phase P1 (SYNC) report — 2026-10-17

Scope: P1 only. The new analysis core is not integrated, Addressables content is untouched, there are no player builds, nothing is committed, and `AppConfig.testForceOpponentId` is unchanged.

## Changed files (`MusicGame/Assets/_Project/Scripts/...`)

**New files**
- `Gameplay/MusicTime.cs`: the time-domain conversions and the cursor helpers.
- `Gameplay/MusicClockCore.cs`: the pure state machine behind MusicClock. It also defines `MusicClockState`, `MusicTimeDiscontinuityReason` and `AudioPlaybackReading`.
- `Gameplay/MusicalPulseDeduplicator.cs`: the camera-kick deduplication.
- `Audio/Core/ClipTimeContinuity.cs`: detects clip-time jumps that nobody announced.
- `Editor/Tests/MusicSyncTests.cs`: the edit-mode NUnit tests. They live in Assembly-CSharp-Editor, so no asmdef was needed.
- `Editor/DevTools/MusicSyncProbe.cs`: a play-mode probe with a synthetic clip and a real AudioSource.
- `Editor/DevTools/RunnerSyncIntegrationProbe.cs`: a play-mode probe of the real game flow.

**Modified files**
- Clock and gameplay: `Gameplay/MusicClock.cs` (rewritten as a wrapper over the core), `Gameplay/GameplayManager.cs`, `Gameplay/GameplayEvents.cs`, `Gameplay/GameplayTimeline.cs` (conversions only), `Gameplay/MusicEnvironmentController.cs`.
- World and camera: `World/FallRespawnSystem.cs`, `World/CheckpointSystem.cs`, `World/MusicWorldManager.cs`, `Player/CameraFollow.cs`.
- Environment modules: `World/Horizon/HorizonWorld.cs`, `World/Horizon/SpectrumBars3D.cs`, `World/Environment/RunnerEnvironmentController.cs`, `World/Environment/RunnerEnvironmentTypes.cs`, `Editor/Environment/RunnerEnvironmentCheck.cs`.
- Live audio: `Audio/Core/AudioSampler.cs`, `Audio/Core/AudioAnalyzer.cs`, `Audio/Core/AudioData.cs` (`timestamp` replaced by `clipTime`), `Audio/Detectors/*` (all 8 detectors plus `IAudioDetector`), `Core/AudioSystemBootstrapper.cs`.

## MusicClock: API and states

MusicClock remains the single authority for time and distance.

**States (`MusicClockState`)**

| State | Behaviour |
|---|---|
| `Stopped` | Frozen. |
| `Warmup` | Advances with game time, starting at `StartClipTime`. It is capped at `PlaybackStartSongTime = warmup + start`. |
| `Playing` | `SongTime = warmup + clip`. The clip position is read from `timeSamples` and filtered: it snaps when the error is above 0.15 s and eases otherwise. |
| `Held` | Fall-respawn. Frozen. |
| `Stalled` | The audio stopped without explanation. Frozen. It resyncs automatically when the audio plays again. |
| `Paused` | An overlay on top of any other state. `Resume` returns to the interrupted state. |
| `Ended` | The clip reached its natural end. Frozen at the end. |
| `Farewell` | Advances with `Time.deltaTime`. |

The clock **never** falls back to wall-clock time.

**API**
- Lifecycle:
  - `Initialize(source, warmup, unitsPerSecond, startClipTime = 0)`
  - `StartPlayback(clipTime, reason)`: seeks with `timeSamples`, calls `Play()`, snaps the clock and publishes the event.
  - `Hold()`, `Pause()`, `Resume()`, `ForceUpdate()`, `BeginManualAdvance()`, `Stop()`.
- End test: `ReachedPlaybackEnd(endClipTime)`. It is true when the raw clip time is at or past `endClipTime` while Playing, or when the state is Ended.
- Time and distance properties:
  - `SongTime`, `ClipTime` (= SongTime − warmup, filtered), `AudioClipTime` (raw, from `timeSamples`)
  - `MusicDistance`, `WarmupTime`, `StartClipTime`, `PlaybackStartSongTime`
- State properties:
  - `State`, `EffectiveState` (the state ignoring a pause)
  - `HasPlaybackStarted`, `IsInWarmup`, `NaturalEndDetected`
  - `Generation` (incremented on every discontinuity)
- Converters: `ToClipTime(songTime)` and `ToSongTime(clipTime)`.

**Natural end.** In Playing, once the source has been observed playing since the last StartPlayback, the first frame where it is not playing counts as the end only if the last position observed while playing, plus the real time elapsed since then, is at least the clip length minus 0.1 s. Pause, hold and seek all leave the Playing state first, so they can never be mistaken for an end.

Measured fact: after a real natural end, the stopped source reported its position as 0, as 384000/384000, and as **the position playback was last started from**, on different runs. Its own position is therefore not used as evidence.

## Each requirement and how it was resolved

- **Fall-respawn**
  - `FallRespawnSystem` calls `Hold()` before `Pause()`. The respawn uses `StartPlayback(clip, Seek)` and `ResyncCursors(fallSongTime)`.
  - A fall during the warmup respawns in place and does not touch the audio.
  - If the song ends during the fall fade, the player respawns in place. The music is not restarted inside the farewell.
- **Mid-song start**
  - The clock starts at the offset. `ResyncCursors(PlaybackStartSongTime)` moves the spawn, reveal, pulse and macro cursors forward without firing anything.
  - The player is placed at the clock's distance, and the ground is rebuilt there.
  - Restart uses the same start offset.
- **Load wait after the warmup:** the warmup cap holds `SongTime` at the handoff point. There is no overrun and no snap back.
- **Natural end → farewell:** GameplayManager uses `_clock.ReachedPlaybackEnd(SongPlayEnd)` for the end and `AudioClipTime` for the fade. `!isPlaying` on its own is never treated as the end.
- **Time helpers (`MusicTime`)**
  - `SongToClip`, `ClipToSong`, `OnsetToEventTime`, `SongToDistance`, `DistanceToSong`, `SamplesToSeconds`, `SecondsToSampleIndex`, `FirstEventAtOrAfter`, `FirstMacroAtOrAfter`, `ClampClipTime`.
  - They are used in GameplayManager, GameplayTimeline, CheckpointSystem, MusicWorldManager, FallRespawnSystem and the clock.
- **Environment**
  - MusicEnvironmentController reads `ClipTime` and does nothing until `HasPlaybackStarted`. It resets its chroma smoothing when the song changes.
  - Extra bug found and fixed: `SpectrumBars3D` also passed `SongTime` to the clip-time-indexed `NormalizedBandValue`, so the bars ran 3 s ahead of the audio. The environment context now carries `ClipTime` as well.
  - CameraFollow's energy height uses `ClipTime` as well.
- **Live detectors**
  - AudioSampler captures the clip time from `timeSamples` in the same frame as `GetSpectrumData`. Every detector queries SongProfile with `AudioData.clipTime`.
  - History is updated during cooldowns.
  - `IAudioDetector.Reset()` exists, and the first frame after a reset only primes the detector.
  - AudioAnalyzer resets the detectors and re-primes its own smoothing on any of:
    - `MusicTimeDiscontinuityEvent`
    - `PlayerRespawnedEvent`
    - `GameStartedEvent`
    - an unannounced clip-time jump (backwards by more than 20 ms, or forwards by more than the frame delta + 100 ms)
    - a song change, via the bootstrapper
  - The bootstrapper now uses one stored handler, which is unsubscribed in `OnDestroy`. AudioAnalyzer also unsubscribes.
- **Camera kick**
  - `BeatPulseEvent` now carries `EventIndex` (the timeline index) and `EventTime` (SongTime domain).
  - CameraFollow deduplicates with `MusicalPulseDeduplicator`:
    - The same index twice adds nothing.
    - Another event within `kickMergeWindow` (serialized, default 0.03 s) of an applied moment adds only the extra strength (max, not sum).
    - Distinct times always add in full, even when they arrive in the same frame.
  - There is no delay. The deduplicator resets on every discontinuity.
  - Spawn, reveal, pulse and collection keep their separate code paths.

## Tests run (all in batch, Unity closed beforehand, never two instances)

1. **Compile.** Command: `Unity.exe -batchmode -nographics -projectPath <proj> -quit -logFile Logs/p1_compile1.log`. Result: 0 errors. Every later run also compiled cleanly. One probe edit had a CS0136 error; it was fixed, and that run never entered play mode.
2. **Edit-mode tests.** Command: `Unity.exe -batchmode -nographics -projectPath <proj> -runTests -testPlatform EditMode -testFilter MusicSyncTests -testResults Logs/p1_editmode_results.xml -logFile Logs/p1_editmode.log`. Result: **24/24 passed** (21 tests, one of them parameterised with 3 cases, plus the others).
   - Earlier iterations: 20/21. The failing test was a test bug: edit mode does not send `OnDestroy`. Then 22/22, before the natural-end evidence was revised.
3. **Synthetic play-mode probe.** Command: `Unity.exe -batchmode -nographics -projectPath <proj> -executeMethod MusicSyncProbe.RunFromCommandLine -syncProbeOut Logs/p1_syncprobe_report_N.txt`. Result: **31/31 on 3 consecutive runs** (`Logs/p1_syncprobe_report_{1,2,3}.txt`).
   - Coverage: warmup cap and load wait, handoff, ClipTime vs `timeSamples` (< 60 ms; measured 3–8 ms), pause, hold/respawn seek, unexplained stop (Stalled), auto resync, unannounced external seek (analyzer reset), restart with mid-start at 5 s, natural end → Ended → Farewell, and no time jump vs game time (max deviation 2–10 ms).
   - Audio does run in `-batchmode -nographics` (48 kHz, DSP buffer 256x4).
   - Earlier iterations failed and were fixed:
     - The jump metric was compared against editor wall time, which is wrong. It now compares against `Time.time`.
     - The natural end was missed twice because of the position evidence described above.
4. **Real-flow integration probe.** Command: `Unity.exe -batchmode -projectPath <proj> -executeMethod RunnerSyncIntegrationProbe.RunFromCommandLine -runnerProbeOut Logs/p1_runnerprobe_report.txt`. It boots Core, goes through Song Selection, plays the first playable song (60 s range) and drives the real GameplayManager. Result: **18/18, 0 errors/exceptions logged**.
   - Warmup: no pulses during the warmup, and the handoff happens at 3.000.
   - Pause: frozen, no pulses.
   - Forced fall: the clock was Held and frozen with no pulses, then respawned exactly at the fall moment (9.998). No pre-fall burst: the earliest pulse after the respawn has eventTime 10.093.
   - Restart: back to Warmup, with no pulses during the countdown.
   - End: the range end produced SongFinished at clip 60.011, then Farewell, then GameEnded after 5 s.
   - Pulse timing: 308 pulses, each within 33 ms of its own `eventTime − lead`.
   - **Save:** backed up just before the run to `.claude/jobs/982d9202/tmp/save_backup_20261017_024109`. The run wrote the save, so it was restored from that backup; the hashes match the originals. The probe-written copy is archived in `tmp/probe_written_save_20261017_024109`.
   - The synthetic probe runs never touched the save. Each of those runs was still backed up first.

## Verified vs not verified

**Verified in Unity**
- Everything listed in the test results above.
- The full natural-end path (`timeSamples` reaching the clip end) on a real AudioSource with a synthetic clip.

**Not verified**
- The natural end of a *real song* in the Runner flow. The 60 s manual range ends before the clip does, so the real flow exercised only the range end. The clip-end path is covered by the synthetic probe and the unit tests.
- A mid-song start inside the real Runner flow. `manualPlayRangeStartSeconds` is 0 and the config asset was not modified. This path is covered by the synthetic probe and the edit-mode tests.
- A fall during the warmup, and a song that ends during a fall fade. The code paths exist but no test ran them.
- Whether the camera kick feels better (no visual or by-ear check), and the live detectors' output quality.
- Mobile devices and IL2CPP.

## Open issues

- **Live vs offline spectrum scales.** These still do not match (`GetSpectrumData` at 1024 bins vs offline magnitude/n), so the profile-adapted thresholds in the live detectors are not comparable. They only feed AudioDebugVisualizer (which is enabled in AudioAnalysisConfig). Decide in P2B/P4 whether to drop the adaptation or normalise both.
- **Live capture latency.** `GetSpectrumData` reflects audio that was already output, and DSP buffer latency is not compensated. This was not measured, and per the spec it must be kept separate from detector calibration and master/playback alignment.
- **BeatDetector.** It still anchors its phase arbitrarily (it now works in clip time). It is a debug heuristic, not a beat tracker.
- **External seeks.** An external seek while Playing makes the clock snap silently (a large error triggers a snap). It publishes no discontinuity event, but AudioAnalyzer catches the jump itself. Gameplay code should always seek through `StartPlayback`.
- **Restart during the very first warmup.** A restart requested during the warmup of the very first `GenerateAndStart` can still leave that coroutine's `StartPlayback` pending. This is pre-existing and not addressed.
- **External pause near the end.** An external pause within roughly 0.1 s of the clip end, done without going through the clock, would be read as a natural end. This is inherent to Unity's API, because a paused and a stopped AudioSource look the same.
- **`AudioContextProvider`.** It still reads `_source.time` (clip domain, correct, but unused).

## What P2B and P4 must know

- **Time domains.** Profile, onset and detection times are **clip time**. Timeline and macro `eventTime` are `MusicTime.OnsetToEventTime(onset, warmup)`. Use the `MusicTime` and `MusicClock` helpers and never hand-write `± warmup`. At runtime, read `MusicClock.ClipTime` (filtered) or `AudioClipTime` (raw `timeSamples`).
- **Cursors.** Any new cursor or list consumer must place its cursor with `MusicTime.FirstEventAtOrAfter` / `FirstMacroAtOrAfter`, or with `GameplayManager.ResyncCursors(songTime)`, so it never fires earlier events.
- **Reset hooks.** `MusicTimeDiscontinuityEvent { Reason: NewRun | PlaybackStarted | Seek | Resync, FromSongTime, ToSongTime, ToClipTime, Generation }` is published by MusicClock. Any stateful per-moment consumer resets on it. `IAudioDetector.Reset()` and `AudioAnalyzer.ResetState(reason)` exist. The analyzer resets on that event, `PlayerRespawnedEvent`, `GameStartedEvent`, the song change and unannounced jumps.
- **Pulses.** `BeatPulseEvent` now carries `EventIndex` and `EventTime`. P4 diagnostics and audible clicks should use these identities. Deduplicate cumulative effects with `MusicalPulseDeduplicator` (by time and identity, with no frame grouping and no delay).
- **Starting and seeking audio.** Always go through `MusicClock.StartPlayback(clipTime, reason)`. Before stopping the audio on purpose, call `Hold()` first. For the end, use `ReachedPlaybackEnd(end)` or `NaturalEndDetected`.
- **Tests.** Extend `Editor/Tests/MusicSyncTests.cs`. Edit-mode tests in Assembly-CSharp-Editor work without an asmdef. Re-run the two probes after any change to the clock or gameplay. The Runner probe writes the save: back it up before running and restore it afterwards.
