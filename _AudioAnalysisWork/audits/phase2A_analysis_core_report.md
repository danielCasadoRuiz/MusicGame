I've built and validated the Phase 2A analysis core as pure C# in the scratchpad; nothing under the Unity project was edited. Every number below was measured by the harness with that harness's code and parameters. Unity was running during all the runs, so timings are noisy; I report the minimum of 3 runs. The real songs are the source WAVs (48 kHz stereo, peak-normalised to imitate the importer's Normalize), not the Vorbis q1 clips the game actually analyses. My port of the legacy analysis still reproduces the existing Unity cache: onset counts match within 0.6 % and BPM is identical on all 20 songs.

**Where things are** (`C:\Users\VISYON\AppData\Local\Temp\claude\C--Users-VISYON-Documents-MusicGame\01b5d66e-0d92-4d05-bf4e-5b4523f7d7c5\scratchpad\analysis_core\`)
- `Core\*.cs` (12 files, about 2,700 lines) can be copied unchanged into `Assets/_Project/Scripts/Audio/Analysis`. They use namespace `MusicGame.Audio.Analysis`, no UnityEngine, no unsafe code, no LINQ in hot loops. `check_netstandard.sh` compiles them alone against Unity 6000.3.2f1's netstandard 2.1 reference with C# 9: zero warnings.
- `Harness\*.cs` holds the test and benchmark program. `build.sh` builds it and `dotnet bin\harness.dll <cmd>` runs it; commands are synth, calib, tempo, equiv, real, featcmp, gridalign, sweep, profile, bench, tagger, format, halfcheck, misc.
- `results\*.txt` has the saved output of every run quoted here.

## Core API
- **Settings:** fill `AnalysisSettings` (mutable, defaults are the recommended values) on the main thread, then call `AnalysisParams.Create(settings)`. The result is immutable and carries `.Hash`, a stable 64-bit hash that includes `AnalysisVersions.Pipeline` (=1). `AnalysisParams.Default` is also available. Heuristic constants inside the classifier are code, covered by bumping `AnalysisVersions.Pipeline`.
- **Running:** `AnalysisRunOptions { maxWorkers (default min(4, cores/2); ≤1 = sequential), chunkFrames, cancellation, progress }`. Progress is called from worker threads and is monotonic.
- **Analysis:** `MusicAnalyzer.MixToMono(interleaved, channels)` (same formula as today) and `MusicAnalyzer.Analyze(mono, sampleRate, params, options)` return an `AnalysisResult`. Cancelling throws `OperationCanceledException`; there are no partial results.
- **`AnalysisResult` contents:**
  - Frame arrays: `energy, centroid, flux, flatness, voice` (raw), `bands[6][]`, `visualBands[24][]`, `chroma` (frame·12), `hopTime`, `frameCount`.
  - Song values: `averageEnergy`, `maxEnergy`, `estimatedBpm` (float, not rounded), `bpmConfidence`, `beatPhase`, `beatTimes[]`, `segments` (`AnalysisSegment`, level enum in legacy order).
  - Onsets: `onsets[]` (`OnsetEvent`) and `OnsetTimes()`.
  - Band layouts: `classificationBandSpecs`, `visualBandSpecs`.
  - Diagnostics: `timings` per stage (not persisted).
- **`OnsetEvent`:**
  - Fields: `time` (clip time, calibrated to the attack), `strength` (0..1), `families`, `origin`, and `kick/snare/hihat/genericConfidence`.
  - `families` is a multi-label flags field (Kick | Snare | HiHat | Generic); Generic is set only when no family reaches 0.5.
  - `origin` is `Detected`; `SyntheticGrid` is reserved for Phase 4.
  - The confidences are heuristic scores, not probabilities.
- **Tagger preprocessing:**
  - Set-up: `MusicnnPreprocessor(MusicnnPreprocessor.ParseFilterbank(bytes))`, which takes the bytes of the existing TextAsset `Resources/MusicTagging/mel_filterbank_96x257`.
  - Building the batch: `BuildBatch(mono, sr, opt, out numPatches)` returns the model input `[patches, 187, 96]` directly, or null if the song is too short; `PatchCount()` gives the number in advance.
  - Versioning: `TagReuseKey(audioId, modelVersion, topN, minScore)`, `PreprocessingVersion`, `FilterbankHash`.
- **Binary profile:**
  - `ProfileBinary.Write(header, result, tags, halfPrecision = false)`, `ReadHeader(bytes, out size)` (cheap) and `Read(bytes, out header, out tags, verifyCrc = true)`.
  - `ProfileHeader` holds magic "MGSP", formatVersion (1), pipelineVersion, paramsHash, tagModelVersion, tagReuseKey, songId, audioIdentity, clip samples/channels/frequency, flags, body length and body CRC32.
  - `header.IsCompatible(params, audioId, samples, freq)` tells whether a stored profile can be reused.
  - The body is made of tagged sections (SCAL, BNDS, FRAM, ONST, BEAT, SEGS, TAGS); readers skip unknown ones. Every read creates fresh arrays, so a session can never mutate a shared asset.
- **Hashing:** `StableHash` (64-bit FNV-1a, with `HashPcm`).
- Lower-level classes are public too: `RealFftPlan`, `WindowTables`, `LowRateSignal`, `FeatureLayout`/`FeatureExtractor`, `OnsetLayout`/`OnsetDetector`, `TempoEstimator`, `BeatTracker`, `StructureAnalyzer`, `Crc32`, `HalfConv`. `OnsetDetector.DebugEvidence` is a [ThreadStatic] diagnostics sink, null in production.

## What the core does and why
- **FFT:** real FFT with precomputed tables, scratch buffers per worker, work split into chunks with bounded concurrency. Verified against a naive DFT (relative error about 1e-7).
- **Frame features keep today's formulas and scales.** Window 21.3 ms, hop 10.67 ms, Blackman-Harris, magnitude/windowLength, defined in seconds so 44.1 kHz gets the same time resolution. Energy, centroid, flux, flatness, voice and chroma correlate 1.0000 with legacy (mean ratio 1.000). Three things change on purpose:
  - **Frames are centred:** frame f is the window centred on f·hop. The old frames were about 10.7 ms early, because they were timestamped at the window start.
  - **The 6 classification bands:** DC is excluded and band edges use overlap weighting. Correlation with legacy is 0.976–1.000.
  - **The low-band collapse is fixed:**
    - Before, visual bands 1–3 were 100 % identical frame by frame.
    - Visual bands up to 250 Hz now use an 85 ms window on a 16× decimated copy of the signal. That copy is zero-phase, computed once and shared.
    - Band count and log layout are unchanged, but the means of bands 0–7 change (0.14–1.03× legacy). The world mesh normalises each band, so this should not matter.
- **Onsets:**
  - **Evidence, three band groups:**
    - Low 30–150 Hz (42.7 ms window, on the decimated signal), Mid 150–4000 Hz (21.3 ms) and High 5–16 kHz (10.7 ms); hop 5.33 ms; flux lag 10.7 ms; log compression γ=1000.
    - Detection uses log-compressed positive flux, a threshold that adapts to the local mean (−250 ms to +50 ms; delta Low 0.20, Mid/High 0.06), local-max peak picking (±30 ms), a noise floor (−70 dB) and duplicate suppression per group (60/40/30 ms). Peak position is refined by parabolic interpolation.
  - **Accepting an event:**
    - Group peaks within 30 ms are merged into one event.
    - An event must add real power (rise ≥ 1.5 dB) and that power must be within 35 dB of the whole mix. This rejects the spectral splatter when a sound ends.
  - **Families:** chosen from how much power each band gains, how broadband the attack is, and how long it rings. A kick rings 45–250 ms and a held bass note longer. Hi-hat must gain clearly more in 5–16 kHz than in a 2–4.5 kHz reference band; snare requires the mid band to carry a real share of the added power.
  - **Duplicates:** a second label of the same family is dropped inside 90 ms for kick/snare and 45 ms for hi-hat/generic.
- **Timing calibration.** I measured each group's delay with synthetic attacks and subtract it: Low −9.6 ms, Mid 0, High +2.8 ms.

  | | Bias | Spread |
  |---|---|---|
  | Legacy, window-start timestamps | −7 to −12 ms | sd ≈ 3 ms, range −17 to −2 |
  | New, raw peaks before correction | Low −9.6 ms, Mid ≈ 0, High +2.8 ms | sd < 0.8 ms |
  | New, final events | 0.1–2.9 ms mean \|error\| (clicks worst, −2.9 ms at 44.1 kHz) | |
- **Why these window and hop sizes** (sweep over 42 synthetic cases plus 6 real songs):

  | Variant | Result |
  |---|---|
  | Onset hop 10.7 ms | 7 misses on fast hi-hats, 2× timing error |
  | 512-sample window for every band | 336 false positives (the low band fires on everything) |
  | 1024 for every band | 54 false positives |
  | 2048 for every band | 46 misses (fast hi-hats) |
  | Low band at 1024 | 54 false positives (kick–bass beating) |
  | Low band at 4096 | timing error up to 20 ms |
  | Flux lag 5.3 or 21 ms instead of 10.7 ms | family accuracy drops |
  | Relative-gain gate −35 dB instead of −40 dB | removes the last synthetic false positive; real songs change by 3 of 8,903 events |
- **Tempo:**
  - BPM: autocorrelation of the onset evidence with a broad prior (60–200 BPM, centred at 120, σ = 1 octave). A weighted fit then refines tempo and phase together.
  - Beats: a dynamic-programming beat tracker (Ellis-style, tightness 100) produces `beatTimes` that follow drift.
  - Segments are computed as today.

## Results
- **Synthetic onset tests, 21 cases at both 44.1 and 48 kHz: 41/42 pass, with 0 misses and 0 false positives.**
  - Cases covered: kick/snare/hi-hat-like bursts, clicks, kick+hat and kick+snare together, tonal plucks (all Generic), sustained bass (one event), a pad crescendo, −36 dB then −6 dB with a noise floor, silence gaps, 16th hats at 180 BPM, 32nd hats at 120 BPM, a full drum loop with bass and noise, kick at t=0, kick 5 ms before the end, 500 samples, 10 samples and empty input.
  - The pass rule also fails any wrong extra family label.
  - Mean |timing error| is 0.1–2.9 ms. The soft-attack bass (20 ms fade-in) is the worst case at up to 9.4 ms.
  - The one failure: at 44.1 kHz, 1 of 12 simultaneous kick+snare events also gets a HiHat label.
  - Legato bass (no energy rise) is detected 2/8 at 48 kHz and 3/8 at 44.1 kHz. That is by design (no energy rise means no attack), so the case is reported for information only.
  - A kick 5 ms before the end is labelled Generic, because its ring can't be measured.
- **Tempo, 24 synthetic patterns (84–174 BPM, offset 0.237 s):**
  - 24/24 correct up to octave, 17/24 exact (±0.01 BPM); octave choice remains open, e.g. 174 is reported as 87.
  - Phase is within ±2 ms in 24/24, and the tracker finds 1198/1198 beats within 15 ms.
  - Legacy BPM was wrong in most of these cases, e.g. 172 reported for 84 BPM.
- **Tempo on real songs** (no ground truth). I compared each BPM by the share of kick/snare events that land within 25 ms of its beats:
  - legacy BPM: 10–27 % (close to chance);
  - new constant grid: 9–52 %;
  - tracked beats: 31–67 %, stable from the first third of the song to the last.

  The constant grid drifts or changes tempo on several songs, so Phase 4 should use `beatTimes`, not 60/BPM.
- **Real songs, all 20:**
  - New events: 29,902 vs 21,713 legacy onsets, about 330–490 per minute.
  - Families: Kick 10,565, Snare 9,866, HiHat 4,164, Generic 10,122; about 15 % of events carry more than one label.
  - Legacy ClassifyOnset gave Kick 13.7 %, Snare 0, HiHat 0.3 %, Onset 85.9 %.
- **Sequential vs parallel:** bitwise identical for 1/2/4/8/16 workers and chunk sizes 64–4096, on synthetic audio at both rates and on a real song.
- **Cancellation and progress:** cancelling returns `OperationCanceledException` within 0–37 ms; progress is monotonic and ends at 1.0.
- **Speed and memory per song** (Gym 206 s / Older 360 s / Low Megas 154 s):

  | | Wall time | Allocated | Peak managed heap |
  |---|---|---|---|
  | Legacy loop (sequential) | 1036 / 1719 / 692 ms | 269 / 470 / 201 MB | +15 to +18 MB |
  | New core, sequential | 1105 / 1921 / 868 ms | 11 / 19.5 / 8.3 MB | ≈ allocated (the output arrays) |
  | New core, 4 workers | 521 / 702 / 417 ms | 11 / 19.5 / 8.3 MB | ≈ allocated |
  | New core, 2 workers (Gym) | 700 ms | 11 MB | ≈ 11 MB |

  Sequentially the new core takes about as long as legacy while doing much more work. Decimating the low band roughly halved both main passes. The time in Unity's coroutine, which yields every 150 frames, was not measured.
- **Tagger preprocessing port:**
  - Accuracy vs a verbatim port of the current code: max |Δ| 1.5e-4 in log10 units (values span about 6), mean 8e-7, same patch counts; parallel equals sequential bitwise. Tested at 48/44.1/22.05/16 kHz, a too-short input and 2 real songs.
  - Cost per song: 376–405 ms and 103–121 MB allocated before, 72–85 ms sequential and 5–6 MB after, about 30 ms with 4 workers.
  - Only 2 % of the filterbank values are non-zero, so it is stored sparsely. Resampling and reflect padding are done on the fly, and only frames that end up in a patch are computed, straight into the batch.
- **Binary format:**
  - **Size:** 3.6–5.0 MB per song vs 31–44 MB of current pretty JSON (about 8.7× smaller). For 4 songs: 16.6 MB vs 144 MB.
  - **Speed:**
    - Binary: read 3–7 ms with the CRC check, 0.5–1.8 ms without; header only < 1 µs; write 4–16 ms.
    - JSON, timed with System.Text.Json as a stand-in for JsonUtility: parse 110–224 ms, write 134–211 ms.
  - **Robustness:** round-trip is lossless. A corrupted body byte, truncation, wrong magic and a future version are all rejected, and the compatibility check catches other audio or other params.
  - **Compression:** Deflate/Brotli shave off only about 11 %, so extra compression isn't worth it.
  - **Half precision (evaluated, not adopted):** halves the size; max relative error is 4.9e-4 on most arrays but 1–2.8 % on tiny band values.

## What Phase 2B must do
1. **Copy the core.** Copy `Core\*.cs` unchanged. Build `AnalysisSettings` from AudioAnalysisConfig: bands, visualBandCount, and the flags flatness = advanced && (timbre || voice || structure), chroma = advanced && harmony, voice = advanced && advancedVoice. These all change results, so they are part of the hash.
2. **Main thread vs worker.** On the main thread: `clip.GetData`, read the filterbank bytes, snapshot the params. On a worker (Task.Run): MixToMono, `Analyze`, `BuildBatch`. Inference stays on the main thread:
   - load the Model once and cache it;
   - Schedule, then read back with `ReadbackAndCloneAsync` (or poll `IsReadbackRequestDone`) instead of `DownloadToArray`;
   - overlap inference with the rhythm analysis.
3. **Runtime wrapper.** It needs:
   - a request id;
   - cancellation when the song changes or the state is left;
   - one analysis per song at a time;
   - publishing on the main thread only once the profile is complete;
   - a single final style publish;
   - removal of the 2 s fake delay.
4. **Map into SongProfile.**
   - Direct mappings: `onsetTimes = OnsetTimes()` (all detections), `estimatedBPM = estimatedBpm` (not rounded), `voiceProbability = voice` kept raw. VoiceAnalyzer and the other derived analyzers must work on copies; today VoiceAnalyzer smooths it in place (double smoothing).
   - New fields: onsetEvents, beatTimes, beatPhase, bpmConfidence.
   - Consumers must know that frames are now centre-aligned, and that frame count is (N−1)/hop+1 instead of (N−W)/hop.
5. **SongCache.** Switch to the binary format. Identity:
   - **Published songs:** hash of the source asset bytes plus importer settings. Decoded Vorbis PCM can differ per platform, so `HashPcm` should only be an in-memory fallback.
   - **Imported songs:** hash of the file bytes.

   Reuse a profile when `IsCompatible(...)` passes. Reuse tags when `tagReuseKey` matches, even after a rhythm-pipeline change.
6. **Re-measure in Unity** on the real clips: time, main-thread stalls, load times. Then run the synthetic tests as edit-mode tests.

## Still uncertain
- Family accuracy on real songs is unknown because there are no annotations; I only have the distribution. Some songs get very few hi-hats (hah 49, Backup Boys 54). That may be real, or the hi-hat test (the 5–16 kHz gain must clearly exceed the 2–4.5 kHz gain) may be too strict. This needs the A/B listening test in Phase 4.
- Tempo octave and metre stay ambiguous. WHAT_ moved from 112 to 84 BPM after the low band switched to the decimated signal, and legacy says 164. `bpmConfidence` is a heuristic.
- The calibration comes from synthetic sounds; real attacks vary, and soft attacks land 5–9 ms late.
- Event density is about 6–8 per second, so the gameplay selection in Phase 4 must thin it out.
- Timings are from a desktop with Unity running; mobile is not measured.
