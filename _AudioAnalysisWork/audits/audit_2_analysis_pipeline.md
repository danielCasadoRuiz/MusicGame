## Audio pre-analysis pipeline audit (read-only, no files edited)

Paths below are relative to `C:\Users\VISYON\Documents\MusicGame\MusicGame\Assets\_Project\Scripts`. "Measured" means I read it from files on disk. Nothing was timed in Unity.

### 1. Pipeline from end to end (CONFIRMED)

**Who starts it**
- `UI/SongAnalysisController.cs` adds an `AudioPreAnalyzer` component in `Awake` (line 29).
- `BeginAnalysis` runs from two places: the `GameFlowStateChangedEvent` handler (line 40) and from `OnEnable` when the state is already SongAnalysis (lines 47-48).
- Nothing stops it from running twice, so **duplicate analyses are possible**. There is no request id and no `StopCoroutine`.
- `WaitForClipThenAnalyze` (65-98) polls until `GameSession.SelectedSong.Clip != null`, up to 20 s. The state check (81) applies only while it waits.
- Once `Analyze` has started (line 97), **nothing cancels it**. If the player leaves the state or picks a new song, the old coroutine still publishes `SongProfileReadyEvent` and requests Gameplay.

**`AudioPreAnalyzer.Analyze` (`Audio/PreAnalysis/AudioPreAnalyzer.cs`)**
- Everything runs as main-thread coroutines. There are no threads, Jobs or Burst. "Background" in the comments only means yielding between frames.
- **Cache hit (40-69):**
  - `SongCache.TryLoad` reads the file synchronously: `File.ReadAllText` plus `JsonUtility.FromJson` (`SongCache.cs:17`).
  - `RunOfflineAnalyzers` then runs synchronously (44).
  - It re-tags if needed (49-56), publishes `PreAnalysisStartedEvent{IsCacheHit=true}` and calls `onTagsReady`.
  - **`yield return new WaitForSeconds(2f)` (line 65; constant at line 16)** follows. This is an artificial wait, and the comment calls it "purely cosmetic".
- **Cache miss (72-247), in this order:**
  1. `GetClipData`: `clip.GetData` into a new interleaved buffer (77, 302-307).
  2. `MixToMono` (79).
  3. **Semantic tagging runs before the FFT loop** (88-95). This is confirmed and intentional, so the style can be detected early.
  4. The rhythmic FFT loop (134-198) yields every 150 frames and publishes `PreAnalysisProgressEvent`.
  5. Onsets, BPM and segments (209-211).
  6. `SongCache.Save`, a synchronous pretty-printed JSON write (243; `SongCache.cs:37` passes `prettyPrint: true`).
  7. `RunOfflineAnalyzers` (245), then the publish.
- **Progress:** real progress exists only for the FFT loop. Tagging sends no progress events. `UI/AnalyzingScreenController.cs` fills the gap with simulated progress: an asymptotic ease (lines 75-81, 283-292) and a fixed 1.6 s fill on a cache hit.
- **Extra fixed waits:**
  - `AfterStyleFlash` waits `StyleFlashSeconds = 1.4f` before it publishes `MusicStyleRevealFinishedEvent` (78, 241-244).
  - `SongAnalysisController.FinishAnalysis` (lines 116-131) waits for the reveal plus `ThemeTransitionController.Duration` (default 0.4 s) plus 0.15 s, with a 4 s timeout.
  - If the early tags are not conclusive, the style is published only when the profile is ready. Another 1.4 s flash then starts after the analysis.
  - `RunnerSceneBootstrap.cs:59-67` re-publishes the profile one frame after the Runner scene loads.

**`SentisMusicTagger` (`Audio/SemanticTagging/SentisMusicTagger.cs`)**
- **Created and disposed on every analysis:**
  - `Resources.Load` of the model and the filterbank, then `ModelLoader.Load` (66-78).
  - `new Worker(_model, BackendType.CPU)` (79).
  - Disposed per call in `AudioPreAnalyzer.cs:317-326`.
- **Preprocessing, all on the main thread:**
  - Linear resample to 16 kHz (224-241), then reflect pad (245-255).
  - About 11k STFT frames (n_fft 512, hop 256). Each frame calls `OfflineFFT.ComputePowerSpectrum`, which allocates and recomputes its window.
  - A dense 96×257 mel matmul per frame (136-143): about 24.7k MACs per frame, roughly 278M MACs for a 3-minute song.
  - It yields every 300 frames (145).
- **Inference:**
  - All patches go in one batch, about 60 × 187 × 96 (160-162).
  - `_worker.Schedule(input)` is followed immediately by `PeekOutput().DownloadToArray()` (170-175). That is a **blocking** completion of the whole CPU job chain on the main thread.
- Timing is already logged (`LogResult`, 207-220: inference ms and total ms). Run it on the target device first: it splits the tagger's cost.

**`Core/AudioSystemBootstrapper.cs`**
- This is the live pipeline only: Sampler, Analyzer, detectors.
- It subscribes one lambda per detector to `SongProfileReadyEvent` and never unsubscribes (lines 45, 61-67). The code itself flags this as debt.

### 2. FFT and onset configuration (CONFIRMED)

**Effective values**
- `Configs/AudioAnalysisConfig.asset`: `spectrumSize: 1024`, `visualBandCount: 24`, `advancedSimilarity: 0`, `advancedSemanticTagging: 1`.
- Window = 1024 samples, hop = 512 (50% overlap) (AudioPreAnalyzer 97-100).
- The catalog is at 48 kHz (cache: `sampleRate 48000`, `analysisHopTime 0.010667`). That gives a 21.3 ms window, a 10.67 ms hop and **46.9 Hz bins**.
- The hop and the onset neighbourhood are counted in samples and frames, not seconds. Their time resolution therefore changes with the sample rate.
- The source rate is used as-is (`clip.frequency`); nothing is resampled for the rhythm branch.

**`OfflineFFT.Compute`**
- Blackman-Harris, symmetric (n-1 denominator).
- **The window is recomputed on every call, with 3 cosines per sample** (`OfflineFFT.cs:91-119`).
- **Every call allocates a window copy, `imag` and `magnitude`** (7-17). The caller also allocates `chunk` every frame (AudioPreAnalyzer 136).
- That is about 4 arrays per frame, roughly 14 KB × 19k frames, which comes to about 270 MB of garbage per song.
- Twiddle factors use a recurrence. Output is |X|/n over n/2 bins: the Nyquist bin is dropped and window gain is not compensated.

**Bin mapping**
- Offline: bin = round(f·N/sr) (`ComputeBandEnergy`, 367-376).
- Sub Bass (20-60 Hz) becomes bins 0-1, so it **includes the DC bin**.
- The log-spaced visual bands start at 20 Hz. By my arithmetic at 48 kHz, roughly VB1-VB3 collapse onto bin 1, and VB0 and VB4 overlap it. So the lowest visual bands are near-duplicates.

**Runtime convention differs**
- `AudioSampler.cs:17-25` calls `GetSpectrumData` with a 1024-element array. That returns 1024 bins over 0..Nyquist (about 23.4 Hz per bin).
- `AudioAnalyzer.cs:62-70` maps with `minHz*len/nyquist`.
- Bin width and magnitude normalization are therefore different from the offline FFT. Live detectors that compare live values with `profile.GetBandEnergyAt` (Kick/Snare/HiHat detectors) mix two scales.

**Onset detection (`DetectOnsets`, 378-400)**
- One global flux: half-wave rectified, on linear magnitudes over all bins, with no log compression and no per-band split.
- Threshold = mean + 1.5σ over ±21 frames (about ±224 ms at 48 kHz), plus a local-maximum test.
- There is **no noise floor, no minimum inter-onset interval and no duplicate suppression**. The search is O(N·43) and sits outside the yield loop.
- The time reported is `i*hopTime`, which is the **start** of the window. The window centre is half a window later (about 10.7 ms).
- **Hypothesis:** this biases onsets early. It needs to be measured against clicks at known times.

**BPM (402-430)**
- A histogram of consecutive inter-onset intervals with ×0.5/1/1.5/2 multiples, 60-200 BPM, 80 bins, rounded.
- No phase is estimated.

**Classification (`Gameplay/GameplayTimeline.cs:992-1048`)**
- Spectral centroid below 350 Hz → Kick. Above 3500 Hz with flatness above 0.5 → HiHat.
- Otherwise it compares 6-band ratios against each band's song average, using a flatness gate below 0.35 and a margin test.
- Each onset gets exactly one label.
- The centroid lookup uses `RoundToInt` (line 1015), but `SongProfile.Sample` uses `FloorToInt` (`SongProfile.cs:194`). The two can disagree by one frame.
- Candidates (`GameplayTimeline.cs:631-660`):
  - Onsets below `averageEnergy * energyThreshold` are dropped.
  - Each type has a minimum spacing of min(cfg, 0.6 × a 16th note).
  - The beat-grid fill (662-690) is synthetic, at confidence 0.15.

### 3. SongProfile fields, sizes, cache and consumers

**Sizes (measured on `Gym Bros - Dyin' Alive`, 205.96 s, 48 kHz)**
- 19,306 frames per array.
- 24 visual bands and 6 classification bands.
- `chromaFlat` holds 231,672 values.
- 861 onsets and 52 segments.
- About 907k floats in total, about **3.6 MB as raw float32**.

**Cache (`Audio/PreAnalysis/SongCache.cs`)**
- Location: `%USERPROFILE%/AppData/LocalLow/DefaultCompany/MusicGame/SongCache/<clipName>_<samples>.json`.
- Pretty-printed JsonUtility: **24-56 MB per song; 20 files = 701 MB** (measured).
- That is roughly 9× the binary size, all parsed and written synchronously on the main thread.

**Identity and versioning**
- `clipName == clip.name && clipSamples == clip.samples` (`SongProfileData.cs:55-56`). Nothing else.
- There is no format version, no pipeline or parameter version and no audio hash.
- `musicTagModelVersion` (`SentisMusicTagger.ModelVersion = 1`) is the only version field.
- A config change (spectrumSize, bands, visualBandCount) does **not** invalidate the cache. It only re-runs when the visual bands are missing or chroma is missing (AudioPreAnalyzer 39-41).

**What is cached and what is recomputed**
- Cached raw fields: energy, centroid, flux, onsets, bands, visual bands, segments, flatness, chroma, raw voice, tags.
- Derived fields are **recomputed on every load** by `RunOfflineAnalyzers` (259-298):
  - Loudness → `loudnessDb`.
  - Timbral → `intensity`, `aggressiveness`, `timbralChange`.
  - Dynamics → gradient, `buildupCurve`, impact/drop/buildupStart times, the 4 zone types.
  - Structure → `density`, `tempoStability`, `beatConfidence`, `complexity`, `danceability`.
  - Harmony → `melodicContour`, `totalChroma`, key/mode.
  - Voice → smoothed `voiceProbability`, `vocalZones`.
  - Similarity (disabled).

**In-place mutation BUG (confirmed in code)**
- `VoiceAnalyzer.cs:25-28` replaces `profile.voiceProbability` with a smoothed clone.
- On the **cache-hit re-tag path**, `RunOfflineAnalyzers` runs first (line 44) and `SongCache.Save` runs after it (55). The **already-smoothed** voice data is saved as raw, so the next load smooths it twice.
- The miss path is correct: it saves before the analyzers (243 then 245).
- `ToProfile` shares array references with the parsed DTO. No consumer was found writing into profile arrays: `MusicEnvironmentController` only writes its own `_chromaEMA`.
- One `SongProfile` instance stays in `GameSession.Profile` across runs, so any future in-place write would leak into the next run.

**Consumers, by field**

| Field(s) | Consumers |
|---|---|
| `energyEnvelope` / `GetEnergyAt` / `averageEnergy` | GameplayTimeline, SnakeWayWorldGenerator, MusicStyleResolver, SongSimilarityVector, AudioContextProvider, live Onset/Peak detectors |
| `spectralCentroid` | GameplayTimeline.ClassifyOnset, SongSimilarityVector |
| `spectralFlux` | Only AudioPreAnalyzer, which builds onsets from it. Nobody else |
| `onsetTimes` | GameplayTimeline only (plus Structure analyzer via ctx) |
| `bandEnvelopes` | GameplayTimeline; live Kick/Snare/HiHat detectors via `GetBandEnergyAt` |
| `visualBandEnvelopes` | MusicWorldManager (lines 254, 299, 336 uses the Catmull-Rom `GetVisualBandEnergyAtSmooth`, 750-788), AudioDebugVisualizer |
| `segments` / `GetSegmentAt` | AudioContextProvider (live context), SimilarityAnalyzer |
| `spectralFlatness` | ClassifyOnset, SongSimilarityVector |
| `chromaFlat` / `GetChromaAt` | MusicEnvironmentController, HarmonyAnalyzer |
| `voiceProbability` | MusicStyleResolver, SongSimilarityVector |
| `vocalZones` / `IsVocalAt` | GameplayTimeline |
| `intensity` | GameplayTimeline, CameraFollow, MusicEnvironmentController, SnakeWayWorldGenerator, MusicStyleResolver, similarity |
| `buildupCurve` | GameplayTimeline, MusicEnvironmentController, SnakeWayWorldGenerator |
| `density`, `timbralChange` | SnakeWayWorldGenerator, MusicStyleResolver, similarity |
| impact/drop/buildupStart times | GameplayTimeline, SnakeWayWorldGenerator, MusicStyleResolver |
| key/mode | MusicEnvironmentController, MusicStyleResolver, similarity |
| `estimatedBPM` | GameplayTimeline, BeatDetector, AudioContextProvider, similarity |
| `musicTags` | MusicStyleResolver, GameSession, the HUDs, TagBasedMusicStyleClassifier, similarity |

- **No consumer found:** `loudnessDb` (similarity only), `energyGradient`, `silenceZones`, `breakZones`, `crescendoZones`, `decrescendoZones`, `melodicContour`, `totalChroma`, `similarSections`. Their getters are also unused.

**Other readers of the SongCache JSON**
- `Song/Similarity/SongSimilarityCache.cs:110-133` scans the directory and parses the JSON lazily.
- `Editor/DevTools/SongSimilarityBake.cs:107-121` reads it too, and its fallback runs the analysis coroutine synchronously.
- So the personal cache is currently a de facto input to the editor bake.

### 4. Inference Engine (CONFIRMED)

- `Packages/manifest.json` has `com.unity.ai.inference: 2.6.1` (package.json version 2.6.1 in `Library/PackageCache/com.unity.ai.inference@9a123aee5df7`) and Addressables 2.3.1.
- APIs available in that package:
  - `Tensor.ReadbackAndCloneAsync()` returns `Awaitable<Tensor<T>>` (`Runtime/Core/TensorGeneric.cs:154`, `Tensor.cs:235`).
  - `Tensor.ReadbackRequest()` / `IsReadbackRequestDone()` (`Tensor.cs:187-203`).
  - `Worker.ScheduleIterable(input)` lets the schedule be split layer by layer across frames (`Worker.cs:388`).
- On the CPU backend (`Backends/CPU/BurstTensorData.cs`):
  - Compute runs as Burst jobs. `Download`/`DownloadToArray` call `fence.Complete()`, which blocks (274-286).
  - `DownloadAsync` polls `fence.IsCompleted` with `Task.Yield()`, then `Awaitable.MainThreadAsync()` (290-306). This is non-blocking.
  - `IsReadbackRequestDone` returns true once the fence has completed (324-330). `ReadbackRequest` is a no-op on CPU.
- The non-blocking pattern is: Schedule (or ScheduleIterable) on the main thread, then poll `IsReadbackRequestDone()` once per frame or `await ReadbackAndCloneAsync()`, then dispose the clone.
- The Worker and tensors stay on the main thread. Only the mel and STFT preprocessing should move to a worker thread.

### 5. Where the ~10 s probably goes (candidates, not timed)

- **(a) Clip readiness.**
  - `AddressableSongSource.cs:37-57` hands over the clip right after `LoadAssetAsync`, with `LoadAudioData()` merely started.
  - The 20 Music WAVs are `loadType 0` (DecompressOnLoad) with `loadInBackground 0`. The 60 composer MP3s are `loadType 1` (CompressedInMemory) with `loadInBackground 1`. All have `preloadAudioData 0`.
  - `GetData` on a clip that is CompressedInMemory or still Loading may decode synchronously or misbehave. **Hypothesis to verify.**
  - No `Addressables.Release` is visible in this file.
- **(b)** `clip.GetData`, the interleaved copy and `MixToMono`: about 40 MB stereo plus 20 MB mono for a 3.5-minute song at 48 kHz.
- **(c)** Tagger preprocessing on the main thread: resample, pad, about 11k allocating FFTs, a 278M-MAC mel loop.
- **(d)** `ModelLoader.Load`, Worker creation, and the blocking `DownloadToArray` on inference.
- **(e)** The rhythm FFT loop: about 19k frames, 4 allocations and about 3k cosines per frame, yielding every 150 frames.
- **(f)** `DetectOnsets`, `RunOfflineAnalyzers`, and the synchronous **32 MB+ pretty JSON write**.
- **(g) Cache hit:** a synchronous **32-56 MB JSON read and parse**, then derived analyzers, then the **fixed 2 s**.
- **(h)** UI holds: the 1.4 s style flash (twice when not conclusive), theme Duration + 0.15 s, the 4 s reveal timeout.
- **(i)** Runner scene load, the one-frame republish, and world and timeline generation in the `SongProfileReadyEvent` handlers (MusicWorldManager, GameplayManager).

**Where to put instrumentation**
- A `Stopwatch` plus `ProfilerMarker` around each of (a)-(i), plus `GC.GetTotalMemory` and `Profiler.GetTotalAllocatedMemoryLong` before and after.
- Run it for a cache miss and a cache hit, on a composer MP3 and on a Music WAV.
- Also log the main-thread duration of each coroutine step (the longest gap between yields), because the yields hide stalls rather than parallelising work.

### 6. Recommended design

1. **Capture step, on the main thread.**
   - Ensure `clip.loadState == Loaded` asynchronously.
   - Call `GetData` once, then take an immutable snapshot of the config: an `AnalysisParams` struct with Hz bands, window, hop, version numbers and a params hash.
   - Allocate a `requestId` and a `CancellationToken` from a single owner service, which de-duplicates requests by songId and revision.
2. **Rhythm branch, in `Task.Run`.**
   - Mono mixdown happens here.
   - Use a precomputed window table and a precomputed twiddle and bit-reverse table, with per-worker scratch buffers.
   - Process frames in chunks of about 1-2k, in parallel with bounded concurrency (`Math.Min(ProcessorCount-1, 2..4)`). Each chunk writes its own frame range of preallocated output arrays.
   - Flux, onset and adaptive-threshold passes then run sequentially over the finished spectra.
   - Report progress through `Interlocked` counters, read by the main thread.
3. **Semantic branch.**
   - STFT, mel and patching run in a worker `Task`, overlapping the rhythm branch.
   - Inference runs on the main thread with `ScheduleIterable` or `Schedule`, then awaits `ReadbackAndCloneAsync`.
   - Cache the loaded `Model` across songs; create and dispose the `Worker` per request inside `try/finally`.
4. **Merge on the main thread.**
   - Discard the result if `requestId` is stale or cancelled.
   - Build the complete `SongProfile` and run the derived analyzers (pure functions, which could also run on the worker).
   - Never save partial results. Keep raw and derived data separate, and copy any array before smoothing it.
5. **Publishable profile.**
   - A binary `ScriptableObject` or a raw `.bytes` asset with a header: songId, an audio content hash over the PCM or source file, import settings, format version, pipeline version, params hash and model version.
   - Body as float32 arrays, about 3.6 MB per song, plus the derived arrays.
   - Ship one Addressable per song, keyed by songId.
   - A valid published profile skips FFT and inference entirely and has no cosmetic delay. The UI shows "loading profile" or "downloading" truthfully.
   - The same pipeline drives the Editor batch tool through a synchronous, cancellable `Run(pcm, params, token)` entry point, without coroutines.
6. **Fix along the way:** onset time = window centre (or a measured offset), sample-rate-independent windows in seconds, DC bin excluded, a consistent frame-index rounding rule, and band magnitude normalization documented against `GetSpectrumData`.
