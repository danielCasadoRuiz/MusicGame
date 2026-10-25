AUDIT REPORT: sync, live detectors and timeline (read-only; no project files edited)

Root: C:\Users\VISYON\Documents\MusicGame\MusicGame\Assets\_Project\Scripts. Line numbers refer to files under that root. I did not run Unity. Onset counts come from a Python copy of the C# logic, run over the 20 cached profiles in %USERPROFILE%\AppData\LocalLow\DefaultCompany\MusicGame\SongCache with the current asset values (script: scratchpad\onsetaudit\audit.py). Those caches may predate the current pipeline.

## 1. Time domains and conversion formulas

- **Clip / SongProfile time `c`** (no warmup; same as `audioSource.time`).
  - `SongProfile.Sample` uses `floor(c / analysisHopTime)` (SongProfile.cs:191-196). `ClassifyOnset` reads the centroid with `round(...)` (GameplayTimeline.cs:1008), so its centroid and band/flatness lookups can be one frame apart.
  - Offline analysis: window W = 1024, hop = 512 (AudioPreAnalyzer.cs:97-100), i.e. 10.67 ms at 48 kHz.
  - An onset is stamped `i*hop`, the frame start (AudioPreAnalyzer.cs:397). The new audio that raises flux in frame i sits in [i·hop+W/2, i·hop+W]. **Hypothesis to measure with clicks:** onsets are reported about 10–21 ms early.
- **MusicClock `S`** (MusicClock.cs:131-169):
  - During warmup, `S = Time.time − wallStart`, running 0 to warmup.
  - During playback, `S = warmup + audioSource.time`, smoothed (snap above 0.15 s, otherwise drift correction).
  - So `c = S − warmup`, `MusicDistance = S · playerSpeed`, `eventTime = warmup + onsetTime`, `eventDistance = eventTime · speed` (GameplayTimeline.cs:30-31, :698-699).
  - Respawn seeks to `audio.time = max(0, S_fall − warmup)` (FallRespawnSystem.cs:168). Checkpoints use `audioT = songT − warmup` (CheckpointSystem.cs:82).
- **Live capture:** `data.timestamp = Time.time` (AudioAnalyzer.cs:51), seconds since app start. It has no relation to `c` or `S`.
- **Scheduling** (GameplayManager.cs:343-408; assets warmup 3 s, speed 10, spawnLookAhead 1.5, reveal 2 / 4 units, pulseLeadTime 0.05):
  - Spawn when `S ≥ eventTime − 1.5 s` (distance test).
  - Visual reveal (`RingController.Pulse(0)`, no event) when `S ≥ eventTime − 0.2 s` in third person, `− 0.4 s` in first person.
  - Musical pulse (`Pulse(strength)` plus `BeatPulseEvent`) when `S ≥ eventTime − pulseLeadTime`.
  - Macro events when `S ≥ eventTime`.
  - Collection is a physical trigger at the player's `ActualDistance` (includes surge). `TimingError = |S − eventTime|` (RingController.cs:119-120) feeds `ScoreFor` (GameplayManager.cs:203).
- **Correct consumers:** CameraFollow.cs:397 and MusicWorldManager.cs:374 both convert with `S − warmup`. AudioContextProvider.cs:19 uses `_source.time`, but nothing calls it.

## 2. Confirmed problems (from code; the fall-respawn one is reasoned from code, not reproduced)

1. **Live detectors look up SongProfile with the wrong time.** The `Time.time` timestamp is passed to `GetBandEnergyAt` / `GetEnergyAt` in KickDetector.cs:36, SnareDetector.cs:41, HiHatDetector.cs:36, OnsetDetector.cs:40 and PeakDetector.cs:27. Once `Time.time` passes the song length, the lookup clamps to the last frame.
   - Even with the right time, the scales don't match. Live data is `GetSpectrumData` (1024 bins up to Nyquist). Offline data is `OfflineFFT` magnitude/n (512 bins; OfflineFFT.cs:14-16). OnsetDetector also compares full-spectrum flux against RMS energy × 2.
   - BeatDetector.cs:27-43 runs its own beat phase on `Time.time`, unrelated to the music.
2. **Detector state goes stale during cooldowns.** Kick (26 vs 29-31), HiHat (26 vs 29-31) and Snare (27 vs 30-33) return during cooldown before updating `_prevValue`. The first frame after a cooldown therefore measures delta against a stale value.
3. **Detector state is never reset on discontinuities.** No detector reacts to seek, respawn or restart. AudioSampler.cs:24 skips frames while audio is paused, so the first frame after a seek compares against pre-seek `_prevValue`, `_prevSpectrum` (OnsetDetector) and `_nextBeatTime` (BeatDetector).
   - Related: the `SongProfileReadyEvent` lambdas registered at AudioSystemBootstrapper.cs:45 are never unsubscribed, which the code itself notes (61-67).
4. **The environment reads SongProfile 3 s early.** MusicEnvironmentController.cs:138-139 and :153 (`SampleFeatures`, :172-198) pass `clock.SongTime` straight to `GetIntensityAt`, `GetBuildupAt` and `GetChromaAt`. That reads `c + warmup` (warmup = 3 s). It is the same bug MusicWorldManager.cs:367-374 documents as fixed for terrain.
5. **MusicClock jumps forward during fall-respawn.** FallSequence pauses the audio and waits 0.5 s (FallRespawnSystem.cs:151-153) without pausing MusicClock. `Tick` then takes the wall-clock branch, `S = Time.time − wallStart` (MusicClock.cs:142-152).
   - Each earlier fall rewinds the audio by about fade + delay (1.1 s) but not the wall clock, so the forward jump grows with every fall.
   - `GameplayManager.Update` keeps running, so spawns, `BeatPulseEvent` bursts and `MacroEventOccurredEvent` fire for the skipped span before `SetNextEventIndex` / `ResyncMacroIndex` reset them (GameplayManager.cs:909-926).
6. **Starting mid-song (`SongPlayStart > 0`) causes a time jump.** `Initialize` has no start offset (MusicClock.cs:63). Warmup runs `S` from 0 to 3. At `Play` (GameplayManager.cs:311-313; also restart at FallRespawnSystem.cs:274-277), `S` jumps to `warmup + P`.
   - The player is placed at `(warmup+P)·speed` (GameplayManager.cs:280) but then follows `MusicDistance` from 0 (PlayerController.cs:135, 205).
   - At the jump, every event before P has its activation, pulse and macro fire in one frame, because the loops have no lower bound.
   - This is latent today: `manualPlayRangeStartSeconds` = 0 (MusicRunnerCoreConfig.cs:133).
7. **Possible stall at natural song end (needs runtime check).** End detection needs `audioSource.time ≥ SongPlayEnd` (GameplayManager.cs:452). When `SongPlayEnd == duration`, which happens for songs ≤ 61 s with the 60 s range, or always with `useManualPlayRange` off, Unity stops the clip and resets `time`. The end may then never be detected, and MusicClock falls back to wall-clock time.
8. **Small warmup handoff step.** If the clip is still loading after warmup (GameplayManager.cs:309-310), wall-clock `S` runs past `warmup` and then snaps back to `warmup + 0`.

**Checked and fine:** pause / resume (PauseController.cs:181-200 sets timeScale 0, pauses audio and clock; `Resume` forces a resync); the respawn ordering of seek → `ForceUpdate` → snap; the farewell manual advance.

## 3. Who consumes the live detectors and pulse events

- **Peak / Onset / Beat / Kick / Snare / HiHat events:** only AudioDebugVisualizer (subscribes at :56-61). It only shows a fading yellow label in `OnGUI` (:85-95).
  - `enableVisualizer: 1` in Configs/AudioAnalysisConfig.asset:83, so that label is visible in the normal configuration.
- **`EnergyUpdatedEvent`, `FrequencyBandsUpdatedEvent`, `AudioDataReadyEvent`:** no subscribers.
- **Conclusion:** live detectors drive no production bonus, score, camera or environment. AudioSystemBootstrapper.cs:4-10 says the same.
- **`BeatPulseEvent`** (published at GameplayManager.cs:886, musical types only): its only consumer is CameraFollow.cs:332, an additive camera kick capped at 1.5 × `kickStrength`. Coinciding events (an Impact plus a confident Kick at the same instant, or patterns) each publish, so camera kicks stack up to that cap.
- **Score** comes only from `RingCollectedEvent` (GameplayManager.cs:203).
- **Environment** listens to `MacroEventOccurredEvent` (MusicEnvironmentController.cs:104-115). `GameplayDebugHUD` subscribes to ring-collected and macro events.

## 4. GameplayTimeline: grid, candidate reduction and classification

- **The beat grid (GameplayTimeline.cs:659-685) is not musically aligned.**
  - `period = 60/BPM × beatGridSubdivision`, and the asset sets subdivision 0.7 (MusicRunnerLevelConfig.asset:48), which is not a musical subdivision.
  - Phase is fixed at t = period from clip 0; nothing aligns it to the music.
  - BPM is a histogram of onset intervals, rounded (AudioPreAnalyzer.cs:402-430).
  - A grid point is skipped only if a candidate is within 0.28 × period.
  - It is already switchable (`useBeatGrid`, MusicRunnerLevelConfig.cs:188). Grid items are tagged `RingType.Beat`, `sourceFeature` "Beat", confidence 0.15.
  - But `ClassifyOnset` also returns `RingType.Beat` when band data is missing (:1020), so real onsets and synthetic grid items can't be told apart.
- **Where onsets are dropped** (20 cached songs, 21,704 analysed onsets):
  - 1,254 (5.8%) by the energy gate (:637, threshold 0.4 × average).
  - 1,175 (5.4%) by same-type minimum spacing (:641, `min(0.2, 0.6 × sixteenth note)`).
  - 19,275 survive as candidates.
  - None are lost to thinning. `AmbiguousConfidence` 0.4 (:977) equals `confidenceKeepThreshold` 0.4 (collectibles asset :42), so every real onset bypasses thinning (:140). Only grid filler is thinned, and only grid filler can be absorbed near an Impact (:129-130).
  - The grid adds 5,032 synthetic candidates, about 21% of the total before thinning.
  - At runtime, only events inside the play window spawn. The 60 s manual range is on by default (MusicRunnerCoreConfig.cs:131-136; GameplayManager.cs:357).
- **Classification gives one label per onset and is almost never Snare or HiHat:** Kick 2,648 (13.7%), Snare 0, HiHat 64 (0.3%), generic Onset 16,563 (85.9%).
  - Cause: on a sampled song, spectral flatness at onsets has p90 0.23 and max 0.32. The HiHat rule requires > 0.5 (:1014), and anything below 0.35 is sent to Onset (:1039).
- **Ordering bug:** the micro candidate list is onsets followed by grid items and is never sorted before pattern grouping (:115-195). The adjacency check `kept[i+run].time − kept[i+run-1].time ≤ 0.9` (:187) passes on the negative difference at the boundary, and onsets are never grouped with interleaved grid items. Events are sorted only later (:215).
- **Onset detection itself** (AudioPreAnalyzer.cs:378-399): flux over all bins with no log compression, threshold mean + 1.5σ over ±21 frames (about ±0.22 s), local maximum, no minimum interval.

## 5. Existing tests and diagnostics

- **Sync / timing:** GameplayDebugHUD "SYNC AUDIT" (:351-361, reads `GameplayTimeline.SyncDebugInfo` :53-69), the per-pickup timing error line (:55) and per-type average timing error (:323).
- **Other tools:** RestartDiagnostic (Editor/DevTools), PickupPlacementDiagnostic, PickupGroundReachDiagnostic (layout determinism :339-370), and RunnerScoringTests section G (reads persistentDataPath SongCache, :223-289).
- **Missing:** com.unity.test-framework 1.6.0 is installed, but there are no NUnit tests. Nothing tests onset accuracy, time-domain conversion, detectors, pause / seek / respawn sync, or pulse duplication.

## 6. Fix recommendations (MusicClock stays the authority)

1. **Define the live timestamps explicitly:**
   - `clipTime = source.timeSamples / clip.frequency`, captured in AudioSampler next to `GetSpectrumData`.
   - `songTime = MusicClock.SongTime`.
   - Detectors query SongProfile only with `clipTime`. Optionally subtract the DSP buffer latency (`AudioSettings.GetDSPBufferSize`), but only after measuring.
2. **Fix the detectors:**
   - Add `Reset()` on `IAudioDetector`, called on GameStarted, PlayerRespawned, restart, and any jump in `clipTime` greater than about 0.1 s.
   - Update previous state every frame, and check cooldown only at the moment of firing.
   - Either drop the profile-adapted thresholds or normalise both spectra to the same scale; they aren't comparable today.
   - Unsubscribe the bootstrapper lambdas.
3. **MusicEnvironmentController:** query with `SongTime − warmup` (or an explicit helper on MusicClock, e.g. `ClipTime`), and don't sample during warmup. One conversion helper should replace the hand-written `− warmup` in every consumer.
4. **MusicClock:**
   - Add a "hold" state: once audio has played, a non-playing source freezes `S` instead of falling back to the wall clock. Or call `Pause()` / `Resume()` around the respawn delay.
   - Add a start offset to `Initialize`, so warmup runs from P to warmup + P.
   - Fast-forward the event, pulse and macro cursors to the start time.
   - Detect a natural clip end via `!isPlaying && timeSamples == 0 && was playing`.
5. **Timeline:**
   - Sort `micro` before thinning and pattern grouping.
   - Give synthetic grid items their own `origin` field instead of reusing `RingType.Beat`.
   - Make the grid subdivision a musical enum (1, 1/2, 1/4), phase-aligned to the detected onsets, with real BPM and phase validation.
   - Keep `useBeatGrid` and report counts with and without it.
   - Keep all detections in the profile and select at generation time.
   - Separate "is there an attack?" (band-wise flux) from family labels with a multi-label heuristic confidence. Note that the flatness gates currently send about 86% of onsets to generic Onset.
6. **Pulse dedup:** merge `BeatPulseEvent`s that land in the same frame or within about 30 ms into one, taking the maximum strength, before CameraFollow. Leave `pulseLeadTime` (0.05) as the only pulse offset; reveal and spawn stay separate.
7. **Tests:**
   - Synthetic click and burst signals with known onset times, run through `AudioPreAnalyzer.Analyze`, to measure the onset time bias (expected −10 to −21 ms).
   - An editor test of the time conversions and of MusicClock across pause, respawn, start offset and natural end.
   - A per-song diagnostic listing detections, selected bonuses, pulses fired and synthetic-vs-real counts.
