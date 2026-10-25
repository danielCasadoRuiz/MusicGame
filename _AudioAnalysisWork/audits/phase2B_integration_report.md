# Phase P2B — analysis core integration (2026-10-17)

The analysis core is now part of the Unity project, with one pipeline shared by the runtime and editor tools. I measured it inside Unity against the old pipeline, and the P1 tests and probes pass again.

Constraints I kept:
- Nothing was committed, no player build or Addressables build was run, and `AppConfig.testForceOpponentId` is unchanged.
- The legacy JSON `SongCache` (~700 MB) is left untouched.
- The user's save was backed up before every play-mode run. The Runner probe writes the save, so after each of its three runs I restored it from that run's backup. The SHA-256 hashes match the originals: `5344dbe6…` for `player_progress.json` and `11ad3162…` for `.bak`.

Logs are in `audits/p2b_logs/`.

## 1. Changed files

All paths are under `MusicGame/Assets/_Project/Scripts/`.

### New files
- **`Audio/Analysis/Core/*.cs`** — the 12 core files. They are byte-identical to `_AudioAnalysisWork/analysis_core/Core`, and that harness copy was updated too (see §8).
- **`Audio/Analysis/SongAnalysisSnapshot.cs`**
  - `SongAnalysisSnapshot`: an immutable copy of the main-thread settings.
  - `DerivedAnalysisSettings`.
  - `AnalysisConcurrency`: the global gate, the worker budget and the Sentis chunk size.
- **`Audio/Analysis/SongAudioIdentity.cs`** — `SongAudioIdentity`, `SongAudioIdentitySource` and `SongAudioIdentityKind`.
- **`Audio/Analysis/SongProfileSerializer.cs`**
  - `SongProfileFactory`: raw result → `SongProfile`.
  - `SongProfileDerived`: the derived pass.
  - `SongProfileSerializer`: header, write/read, compatibility checks.
  - `SongProfileCache`: the binary cache on disk.
- **`Audio/Analysis/SongAnalysisPipeline.cs`**
  - `SongAnalysisJob`: the single pipeline.
  - `IPipelineWait` and `WorkItem`.
  - `SongAnalysisInput`, `SongAnalysisOutcome` and `SongAnalysisTimings`.
  - The blocking editor entry points.
- **`Editor/Audio/EditorSongAudioIdentity.cs`** — the editor identity: hash of the source asset bytes plus the import settings that shape the decoded PCM.
- **`Editor/Tests/SongAnalysisTests.cs`** — 14 edit-mode tests.
- **`Editor/DevTools/AnalysisBenchmarkProbe.cs`** — play-mode benchmark plus lifecycle checks on real songs.

### Rewritten files
- **`Audio/PreAnalysis/AudioPreAnalyzer.cs`** — now the runtime request service.
- **`Audio/SemanticTagging/SentisMusicTagger.cs`** — the model is cached and inference runs in chunks without blocking.
- **`UI/SongAnalysisController.cs`** — request ownership, cancellation and the failure path.
- **`UI/AnalyzingScreenController.cs`** — the progress bar now shows real progress only.

### Modified files
- **`Audio/PreAnalysis/SongProfile.cs`**
  - New fields: `onsetEvents`, `beatTimes`, `beatPhase`, `bpmConfidence`, the identity fields, `voiceProbabilitySmoothed` and `VoiceForFeatures`.
  - The raw-versus-derived contract is documented in the class.
- **`Audio/PreAnalysis/SongProfileData.cs`** — documented as a read-only legacy format, kept only for old dev diagnostics.
- **`Audio/AdvancedAnalysis/*`** — the analyzers take `DerivedAnalysisSettings`, and `VoiceAnalyzer` writes to its own new field.
- **`Audio/Core/AudioEvents.cs`**
  - `SongProfileReadyEvent.SelectionGeneration`.
  - `RequestId` on `PreAnalysisStarted` and `PreAnalysisProgress`.
- **`Core/GameSession.cs`**
  - `SelectionGeneration`: setting `SelectedSong` bumps it and drops the old `Profile`.
  - Profiles from another selection are rejected.
- **`Core/RunnerSceneBootstrap.cs`** — the republish carries the generation.
- **`Song/SongSelectionController.cs`** — clears the selection before a catalog song loads. Before this, the analysis could start on the *previous* song's clip.
- **`Song/Similarity/SongSimilarityCache.cs`** — the lazy fallback now reads the binary cache.
- **`Song/Similarity/SongSimilarityVector.cs`** and **`Audio/MusicStyle/MusicStyleResolver.cs`** — read `VoiceForFeatures`, i.e. the smoothed voice curve, as before.
- **`Editor/DevTools/SongSimilarityBake.cs`** — uses `SongAnalysisPipeline.AnalyzeClipBlocking`.
- **`Gameplay/GameplayTimeline.cs`** — stable sort of the candidates before pattern grouping.
- **`Editor/DevTools/RunnerSyncIntegrationProbe.cs`** — diagnostics only: the pulse-timing check now lists the outliers.
- **`Audio/MusicStyle/TagBasedMusicStyleClassifier.cs`** — comment only.

### Removed files
- `Audio/PreAnalysis/SongCache.cs` (the JSON cache).
- `Audio/PreAnalysis/OfflineFFT.cs`.
- `Audio/SemanticTagging/IMusicTagger.cs`.

## 2. Architecture

### Threads

| Work | Where it runs |
|---|---|
| Snapshot of the config (`SongAnalysisSnapshot.Create`), `Resources` loads, Sentis model cache, Worker creation and Schedule, `AudioClip` access, event publishing | Main thread |
| PCM capture | Main thread, time-sliced: `GetData` in 64k-frame chunks into one reused buffer, mixed directly to mono, about 4 ms per frame. The return value is checked. |
| Identity hashing, cache probe, tag preprocessing, rhythm core, serialize, cache write, `SongProfileFactory` + derived pass | `Task.Run` workers |

Sentis inference runs on Unity's job workers. The main thread only polls `IsReadbackRequestDone`; it never calls `DownloadToArray` on an unfinished tensor.

### Concurrency budget
- `AnalysisConcurrency.Gate` lets only **one** analysis do heavy work at a time, at runtime or in the editor.
- Worker budget: desktop min(4, cores/2), mobile 2.
- Order of work:
  1. Tag preprocessing uses the full budget.
  2. The rhythm core then **overlaps** inference with budget − 1 workers.
- Inference runs in **chunks of patches**: desktop 4, mobile 2 (`AnalysisConcurrency.TagPatchesPerChunk`).
  - The CPU backend keeps every intermediate tensor of a scheduled batch alive until the batch completes, about 44 MB of native memory per patch (measured).
  - With the first non-blocking version, a whole-song batch peaked at **+2.3 / 3.0 / 5.3 GB native**.
  - Chunked peaks: **+89 MB** (2 patches), **+177 MB** (4), +355 MB (8), +709 MB (16).
  - Tags are bitwise-identical for every chunk size (bench check plus an edit-mode test).
- Inference Engine 2.6.1 quirk: `Tensor(shape, T[], dataStartIndex)` copies everything after the offset, so each chunk is copied into an exact-size buffer.

### Request lifecycle (`AudioPreAnalyzer`)
- **`Begin(request, config)`**
  - Calling it again for the same clip and the same selection generation **joins** the running request.
  - Otherwise it calls `Cancel()` on the previous request **first**, gives the new one a new id, takes the snapshot and starts the job.
- **Publishing:** every global event and every callback is gated by `IsCurrent`: it must be the active request, not cancelled, and its `SelectionGeneration` must equal `GameSession.SelectionGeneration`. A stale request is therefore invalidated before it can publish anything. `Update()` also cancels a request whose selection has moved on.
- **Progress:** monotonic (the job keeps a lock-protected max). `Update()` reads it and publishes it on the main thread.
- **Cancellation:**
  - The token stops the worker stages within one chunk.
  - Running inference is abandoned: no new chunk is scheduled, and the worker is disposed by `SentisMusicTagger.PumpDeferred` once its jobs are done, so the main thread never blocks.
  - `OnDisable` disposes the job iterators, which releases the gate.
- **Results:**
  - A failure calls `OnFailed`, and `SongAnalysisController` logs it and goes back to Song Selection. A partial profile is never published.
  - The complete profile is published **once** (`SongProfileReadyEvent{Profile, SelectionGeneration}`).
  - The final style is published once (`_stylePublished`).
  - The fake 2 s delay is gone.
- **Lifetimes:**
  - Model: loaded once and kept for the app lifetime (a few MB).
  - Worker: one per request.
  - Input tensors: one per chunk, disposed after readback.
  - PCM: the mono copy is dropped before compose.
  - `GameSession` drops the previous `Profile` when the selection changes.
  - Addressables clip handles are **not** changed; that is P3 (lease).

### Events
- `PreAnalysisStartedEvent{IsCacheHit, RequestId}` fires after the cache probe.
- `PreAnalysisProgressEvent{Progress, RequestId}` carries real progress.
- `SongProfileReadyEvent{Profile, SelectionGeneration}`.
- `MusicStyleDetectedEvent` and `MusicStylePendingEvent` are unchanged.
- `AnalyzingScreenController`:
  - The bar now shows `max(real progress)` only; the asymptotic and timed fills are removed.
  - The 1.4 s "Style detected" caption stays as the reveal moment. It is not progress.

## 3. Binary format, identity and versions
- **Format:** `ProfileBinary` "MGSP", `FormatVersion = 1`, little-endian, CRC-32 over the body, tagged sections (unknown sections are skipped).
- **Header fields:**

  | Field | Meaning |
  |---|---|
  | `formatVersion` | `ProfileBinary.FormatVersion` |
  | `pipelineVersion` | `AnalysisVersions.Pipeline`, **now 3** |
  | `paramsHash` | `AnalysisParams.Hash` of `SongAnalysisSnapshot.ToSettings(AppConfig.audioAnalysis)` |
  | `tagModelVersion` | `SentisMusicTagger.ModelVersion = 1`; 0 means no tags |
  | `tagReuseKey` | audio + model + `MusicnnPreprocessor.PreprocessingVersion` + filterbank hash + topN/minScore |
  | `songId` | the song key |
  | `audioIdentity` | see below |
  | `clipSamples`, `clipChannels`, `clipFrequency` | the analysed clip |
  | `flags` | |
  | `bodyLength`, `bodyCrc32` | |

- **Identity (`SongAudioIdentity`)** — no longer the clip name plus the sample count:
  - **Imported files:** FNV-64 of the file bytes. The key is `import_<hash>`.
  - **Editor project assets:** FNV of the source bytes plus the import settings that shape the PCM: force-to-mono, normalize, compression format, quality, and the sample-rate setting/override for the active target platform.
  - **Fallback** (a player catalog song before P3, or a clip loaded from a bundle): hash of the decoded mono PCM. This is local to the device.
  - File hashes are memoised for the session.
- **Cache:** `persistentDataPath/SongProfileCache/<songKey>_<audioIdentity>.mgsp`, next to the untouched legacy `SongCache/`. Writing a new revision removes that song's older revisions.
- **Partial reuse:**
  - If the rhythm part is current but the tags are stale, only the tags are recomputed and the raw data is rewritten unchanged.
  - The reverse case works the same way.
- **Size:** 2.6 MB for a 154 s song; the old JSON was 22.7 MB.

## 4. Contract between raw and derived data
- **Persisted:** only the raw core result plus the tags.
  - `SongProfileSerializer.Write` runs before any derived pass.
  - Raw arrays are never mutated or replaced. `VoiceAnalyzer` now writes `voiceProbabilitySmoothed`, a new array; it no longer overwrites `voiceProbability`.
- **Derived:** recomputed on every build and every load by `SongProfileDerived.Compute`, using the immutable `DerivedAnalysisSettings`.
  - Because it is recomputed from raw each time, smoothing can never apply twice.
  - Cost, measured in Unity: **39–127 ms** per song on a worker thread.
- **Callers:** voice consumers read `VoiceForFeatures` (smoothed, as before).
- **Ownership:** each profile owns fresh arrays (a new analysis or a fresh binary read), so no asset is ever mutated in place.

## 5. Measured numbers

**Conditions**
- Hardware: i7-12650H, 16 logical cores, 32 GB RAM.
- Unity 6000.3.2f1 in editor play mode, batch mode with -nographics, Mono (**not** IL2CPP or mobile).
- The empty scene sat under `Scenes/Debug`, so AppBootstrap did not boot.
- Clips are the real catalog imports: Vorbis q1, DecompressOnLoad, 48 kHz stereo.
- Desktop worker budget 4 (3 while overlapping inference).
- Allocations use the profiler counter "GC Allocated In Frame" summed per frame. Unity's Mono has no `GC.GetTotalAllocatedBytes`, so allocations in the **old** run are n/a.
- "Peak GC heap use" is sampled every 1 ms and includes uncollected garbage.
- Native peak is sampled once per frame. The old pipeline did its inference inside a single blocking frame, so its native peak could not be observed; by construction it ran the same whole-batch inference.

### Cache miss (full analysis)

| | Low Megas 154 s | Gym Bros 206 s | Older New 360 s |
|---|---|---|---|
| Old total | 14.56 s | 19.01 s | 33.72 s |
| Old max main-thread frame | **2.73 s** | **3.30 s** | **7.31 s** |
| Old peak GC heap use | +398 MB | +419 MB | +554 MB |
| New total (warm) | 5.99 s (8 patches), 6.76 s (4 patches) | 9.48 s | 17.34 s |
| New time to tags | | 4.83 s | 8.77 s |
| New max main-thread frame | 56 ms (8), 25.5 ms (4) | 66 ms | 83 ms |
| New GC allocated | ~69 MB | 95.7 MB | 158.6 MB |
| New peak GC heap use | +55 MB | +80 MB | +120 MB |
| New peak native | +177 MB (4) / +355 MB (8) | +355 MB (8) | +355 MB (8) |

- The Gym and Older runs used 8 patches per chunk, which was the desktop default at the time. Desktop now uses 4 (Low Megas: 25.5 ms max frame, +177 MB native).
- Mono heap growth on a miss was 0–98 MB. Boehm never shrinks the heap.

### Cache hit

| | Low Megas | Gym Bros | Older New |
|---|---|---|---|
| Old | 2,002 ms total (JSON read + derived 303 ms + fixed 2 s; the 303 ms stall shortened the scaled `WaitForSeconds`) | ~2.1–2.4 s (JSON 449 ms + 2 s) | ~2.1–2.4 s (JSON 706 ms + 2 s) |
| Old max main-thread frame | 303 ms | 449 ms | 706 ms |
| New total | **69 ms** | **82 ms** | **107 ms** |
| New max frame | 1.8 ms | 4.6 ms | 2.9 ms |
| New GC allocated | 6.7 MB | 9.0 MB | 15.5 MB |

### Stage breakdown (Gym, miss)

| Stage | Time | Notes |
|---|---|---|
| Identity (source WAV hash, worker) | 344 ms | |
| Capture | 743 ms | max slice 14 ms |
| Tag preprocessing | 359 ms | |
| Inference | 3,373 ms wall | main-thread schedule 124 ms total, ≤ 25 ms per frame |
| Rhythm | 7,935 ms | decimate 1,886 / features 2,619 / onset ODF 3,245 / pick 17 / tempo 166 ms |
| Compose + derived | 57 ms | |
| Serialize | 16 ms | |
| Cache write | 10 ms | |

### Resident PCM
- The clip's own native data: **28.2 / 37.7 / 65.8 MB**, held by the clip while it is loaded.
- One transient mono `float[]` of the same size, from capture to the end of rhythm.
- The old pipeline held an interleaved stereo copy plus a mono copy: 3× the clip size.

### Other measurements
- **First-use costs:**
  - The first Sentis Worker in a process cost ~350 ms of main thread with a warm Burst cache.
  - After an editor recompilation it cost **22.5 s** (cold Burst JIT, editor only).
- **Mono versus .NET 8:** the core is about 7× slower under the editor's Mono than in the .NET 8 harness (Gym rhythm 7.9 s versus 0.96 s). IL2CPP was not measured.
- **Editor blocking entry point** (`AnalyzeClipBlocking`, no cache), Low Megas: 5.29 s. It blocks the main thread by design (editor tools only).

## 6. Tests and results

### Edit-mode tests
`-runTests -testPlatform EditMode -testFilter "MusicSyncTests|SongAnalysisTests"`: **38/38 pass**, 24 from P1 and 14 from P2B.

The 14 P2B tests cover:
- the snapshot and the params hash;
- identity: same length with different audio gives a different hash, and the import file hash works;
- the derived pass never mutates raw data and does not compound;
- binary round-trip and header binding;
- the re-tag path keeps the raw voice and reuses the rhythm, followed by a full cache hit;
- streaming-clip `GetData` failure gives an explicit error;
- a silent clip is never analysed silently;
- chunked capture equals full `GetData` + core mixdown, byte for byte;
- the first full window is analysed and an attack at t=0 is detected;
- no BPM without evidence;
- `onsetTimes` equals the events, ascending;
- sequential and parallel runs inside Unity give identical bytes;
- chunked inference equals a single batch;
- blocking cancellation releases the gate.

### `AnalysisBenchmarkProbe`
**29/29 checks pass**:
- one publish per request;
- monotonic progress ending at 1;
- the raw voice stays raw;
- a cache hit is reported as a hit;
- tags are identical across chunk sizes 2/4/8/16;
- editor-blocking bytes equal runtime bytes;
- binary round-trip;
- a duplicate request joins the running one;
- a superseded request publishes nothing and the newest publishes once;
- cancel: nothing published, gate released 2 ms after `Cancel()`, deferred inference drained 11 ms later.

An intermediate run also showed that the failure path works: an inference exception went to `OnFailed`, nothing was published, and nothing hung.

### P1 regression
- **`MusicSyncProbe`:** 31/31 pass. The save was untouched.
- **`RunnerSyncIntegrationProbe`** (the real flow through the new controller):
  - **Run 1, cache miss:** 17/18. The failing check was pulse timing: one outlier at 128 ms against a 100 ms limit.
  - **Run 2, cache hit:** 18/18, worst pulse 12 ms.
  - **Run 3, cache miss again:** 18/18, worst 20 ms.
  - The outlier did not reproduce. I added outlier details to the check message so the cause is visible if it happens again.
  - The save was restored after each run (hashes verified). The probe-written copy is in `.claude/jobs/982d9202/tmp/probe_written_save_20261017_125005`.

### Harness after the core change
- `suite` exit 0.
- `tempo` 18/24 exact (was 17), 24/24 up to octave.
- `real`: BPM, confidence and phase identical on all 20 songs.
- `check_netstandard.sh` OK.

### Compilation
Every compile in the batch runs had 0 errors; only pre-existing warnings remain.

## 7. Old bugs

| Bug | Status |
|---|---|
| Identity = name + samples | **Fixed.** Content and settings hash instead (§3). Tested. |
| Re-tag path saved smoothed voice as raw | **Fixed.** Raw is written before the derived pass, and `VoiceAnalyzer` writes its own field. Tested, including a partial cache reuse. |
| `clip.GetData` result ignored | **Fixed.** Checked on every chunk. Streaming clips, failure and digital silence all give explicit errors. Tested. |
| First full window skipped | **Fixed by the core.** Centred frames, (N−1)/hop+1 frames, attack at t=0 detected. Tested. |
| Arbitrary BPM without evidence | The old code returned 120 by default. **The new core still produced one: 200.9 BPM from a single isolated hit.** Fixed in the core: a tempo needs an interior autocorrelation peak and at least 4 events (pipeline 3). The estimate is now 0 with no beats. Tested. Real songs are unchanged. |
| Candidates unsorted before patterns | **Fixed** with a trivial stable insertion sort in `GameplayTimeline.Generate`. It changes layouts only where grid items used to sit out of order. Not unit-tested (private method). The selection policy itself belongs to P4. |

## 8. How the new events map to `GameplayTimeline`
- `onsetTimes` holds **every** detected event's calibrated attack time, ascending, one per `OnsetEvent`, with nothing filtered out.
- `ClassifyOnset` still labels each time from the frame features. Those frames are now centred, so the frame chosen by `RoundToInt` is the one centred nearest to the onset.
- Families and confidences sit in `onsetEvents` and are **not consumed** yet; that is P4.
- `estimatedBPM` is not rounded, and is 0 when there is no evidence. Every consumer already guards `> 0`.
- Density is higher: 1,269 events versus about 1,011 legacy onsets on the probe song. Thinning still uses the existing spacing and density rules. The gameplay effect was not judged by eye.

## 9. Verified versus not verified

**Verified in Unity** (editor, Mono, desktop):
- everything in §5 and §6;
- the real Song Selection → analysis → Runner flow, with a cache miss and a cache hit.

**Not verified:**
- IL2CPP, mobile, and real device memory and thermals;
- importing a local file end to end (only the identity is unit-tested with temporary files; `LocalAudioClipLoader` was not run);
- cancelling inside the real UI flow (only in the bench);
- the look of the progress bar (no capture);
- the GPU backend;
- `SongSimilarityBake` with the new pipeline (it compiles but was not run).

## 10. Open issues
1. The first Sentis Worker in a process stalls the main thread for ~350 ms (warm), or 22.5 s right after an editor recompile (cold Burst JIT). Options: pre-warm at boot, or `ScheduleIterable`.
2. The core under Mono is about 7× slower than under .NET 8. Measure IL2CPP. Burst/Jobs for the core is a possible later step.
3. The clip's PCM stays resident (DecompressOnLoad, 28–66 MB), and `AddressableSongSource` still never releases its handle. Both are P3.
4. The binary cache has no size cap; imports accumulate one entry per distinct file. The Mono heap grows by up to ~100 MB during a miss and does not shrink.
5. The header has no fields yet for the playback representation, import platform or time conversion (spec "NOU REQUISIT"). P3 must extend the format and bump `FormatVersion`.
6. Similarity vectors baked from the old analysis stay in place until a forced rebake. The lazy similarity fallback cannot verify the audio revision.
7. The live-detector spectrum scale mismatch from P1 is still open.
8. Each bench run creates `Scenes/Debug/__AnalysisBench.unity`. I deleted it after my runs; delete it by hand if a run crashes.
9. Free space on C: dropped to 3.3 GB during the session. My outputs are about 10 MB; `pagefile.sys` (47 GB) was modified while I was idle.

## 11. What P3 needs

### Editor entry points
Both live in `SongAnalysisPipeline` and must run on the main thread. Both are synchronous and cancellable, and report progress about every 50 ms on the calling thread.

```csharp
SongAnalysisOutcome AnalyzeClipBlocking(AudioClip clip, string songKey, AudioAnalysisConfig config, CancellationToken ct,
    Action<float> progress = null, SongAnalysisCacheMode cache = ReadWrite, SongAudioIdentity? identity = null)
SongAnalysisOutcome AnalyzePcmBlocking(float[] mono, int sampleRate, int sourceChannels, SongAudioIdentity identity,
    SongAnalysisSnapshot snapshot, CancellationToken ct, Action<float> progress = null, SongAnalysisCacheMode cache = None)
```

- **Outcome fields:** `Profile`, `Raw` (`AnalysisResult`), `Tags`, `Header`, `ProfileBytes` (the publishable binary), `Identity`, `CacheHit` / `RhythmReused` / `TagsReused`, `Timings`.
- **Gate:** both entry points take the global gate (waiting up to 60 s), so do not call them while a play-mode analysis is running.
- **Snapshot:** `SongAnalysisSnapshot.Create(config, workerBudget = 0)`, main thread. It exposes `.Params.Hash` and `.TagReuseKey(audioId)`.

### Serialization
- `SongProfileSerializer.MakeHeader(identity, snapshot, samples, channels, frequency, hasTags)`, `Write(header, raw, tags)` and `Read(bytes, out header, out tags)`.
- Compatibility checks: `RhythmCompatible(...)` and `TagsCompatible(...)`.
- `ProfileBinary.ReadHeader(bytes, out headerSize)`: cheap, no body parse.
- Runtime load without FFT or inference: `SongAnalysisPipeline.LoadProfile(bytes, new DerivedAnalysisSettings(config), out header)`. It rebuilds raw + derived, about 40–130 ms measured.

### Identity
- `new SongAudioIdentity(kind, songKey, hash)`.
- `EditorSongAudioIdentity.Resolve(clip)` returns (source path, settings hash); `EditorSongAudioIdentity.SettingsHash(AudioImporter)`.
- `SongAudioIdentitySource{Kind=SourceAsset, FilePath, SettingsHash}.Resolve()` hashes the file on a worker.
- For master WAVs, P3 should compute its own identity (master bytes) and pass it in.

### Where the versions live
- `AnalysisVersions.Pipeline = 3` and the params hash: `Audio/Analysis/Core/AnalysisParams.cs`. The params come from `SongAnalysisSnapshot.ToSettings(AppConfig.audioAnalysis)`.
- `ProfileBinary.FormatVersion = 1`: `Core/ProfileBinary.cs`.
- `SentisMusicTagger.ModelVersion = 1`: `Audio/SemanticTagging/SentisMusicTagger.cs`.
- `MusicnnPreprocessor.PreprocessingVersion = 1` and `FilterbankHash`: `Core/MusicnnPreprocessor.cs`.
- Keep the harness copy at `_AudioAnalysisWork/analysis_core/Core` in sync with any core change, and re-run `dotnet bin/harness.dll suite` (it must exit 0).
