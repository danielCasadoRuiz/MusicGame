# Phase P3 — published catalog, remote Addressables, readiness barrier (2026-10-22)

The work started on 2026-10-17 and was cut off by the usage limit. It was resumed and finished on 2026-10-22.

Measurements were taken in the editor (Mono) on an i7-12650H, using real built bundles served over local HTTP (`LocalContentServer` profile). Nothing was measured on mobile.

Nothing was committed and no player build was made. One Addressables content build was made, with the `LocalContentServer` profile, into `ServerData/`.

## 1. What exists now

### Bake
`Tools > MusicGame > Content > Songs > Bake Published Profiles` (`SongCatalogBake`) analyses every playable song from its **lossless WAV master**, using the same pipeline as the runtime. Its output:
- one profile per song: `Assets/_Project/Content/SongProfiles/<songId>.mgsp.bytes`;
- the remote index `Assets/_Project/Content/SongCatalogIndex.asset` (identity, display name, order, revisions, similarity);
- the shipped similarity fallback.

How it decides what to do per song:
- **REUSED** when the profile is current;
- **REBOUND** when only the playback import changed;
- **ANALYSED** otherwise.

It also offers **Force Rebuild All**, shows a progress bar with cancel, and writes a report. A failed song is left out of the index, which blocks publishing.

The personal analysis cache is never read.

Last runs:
- 20 analysed: 19 on the first run, plus `song_010` after the padding rule below;
- the next run reused all 20.

### Playback representation
- **Format:** Vorbis quality 1.0, unchanged. The encoded data is identical, so there is no double lossy step and no MP3. The masters are untouched.
- **Load type:** `CompressedInMemory` with `loadInBackground`; it used to be `DecompressOnLoad`.
- **Where it is set:** `RemoteSongContentSetup` applies the policy.
- **Revision:** the load type is not part of the playback revision, because it does not change the samples.

Measured on 3 songs, averages:

| Variant | Bundle | Clip RAM | Prepare | Seek call (main thread) | SNR vs master |
|---|---|---|---|---|---|
| vorbis100 DecompressOnLoad (old) | 9.2 MB | 32.2 MB | 1,095 ms | 0.004 ms | 37–38 dB |
| **vorbis100 CompressedInMemory (chosen)** | 9.2 MB | 9.2 MB | 8 ms | 0.004 ms | same data |
| vorbis100 Streaming | 9.2 MB | 0.2 MB | 3 ms | **22–24 ms** | same data |
| vorbis70 CompressedInMemory | 4.6 MB | 4.6 MB | 27 ms | 0.003 ms | 25–28 dB |
| ADPCM CompressedInMemory | 9.0 MB | 9.0 MB | 64 ms | 0.012 ms | 30–33 dB |
| PCM | 32 MB | 32 MB | | | lossless |

- **Streaming** was rejected: its seek and restart calls block the main thread for 10–24 ms.
- **vorbis70** would halve the download, but SNR is not a measure of audible quality. It needs a listening test by the user before it can be adopted.

### Master ↔ playback alignment
Every song is measured by the bake, at the start, middle and end, with cross-correlation:
- **Lag:** 0 samples on all 20 songs.
- **Gain:** +0.06 dB.
- **SNR:** 37–39 dB.
- **Padding:** Unity pads `song_008` by +1 frame and `song_010` by +2 frames **at the end**. The padding is silent (peak 0.0000) and the lag at the end is still 0.

Rules:
- Tail padding of at most 64 frames is accepted only with a zero lag at all three points.
- Trimming, start padding, drift, resampling or a gain mismatch above 0.5 dB fail the bake.

The time conversion is the identity. No offset exists anywhere, and the runtime refuses any profile whose header carries one.

### Profile header (format 2)
The header binds:
- the master identity (its bytes plus the normalisation policy);
- the pipeline, params and tag-model versions;
- the playback revision: master plus the effective import settings plus the Unity major.minor;
- the played clip's frames, channels and rate;
- the analysis gain, the conversion and the measured alignment.

At runtime the check is cheap: header ↔ index ↔ the loaded clip's samples, channels and frequency. No PCM is decoded and nothing is hashed on the device.

### Remote content
The old local `Songs` group (one 263 MB PackTogether bundle shipped in the player) became **`Remote_Songs`**: remote, `PackSeparately`, LZ4, updatable.

**Built layout**, verified by `SongContentLayoutCheck`, which opens every bundle of the last build:
- **one bundle per song audio**: 8.0–17.9 MB;
- **one per profile**: 2.6–6.1 MB;
- **one for the index**: 7.5 KB;
- nothing in local bundles.

Size: 330.7 MB in total; the largest song download is 24 MB.

**Why audio and profile are separate bundles:** they are bound by revision. A profile-only change never re-downloads the audio. Both share the download label `Song_<songId>`.

**Addresses:**
- audio `SongAudio/<songId>`;
- profile `SongProfile/<songId>`;
- index `SongCatalogIndex`.

Addresses are **ASCII on purpose**. Addressables 2.3.1's binary catalog wrote `VÅÅV - Dancig Queer` as `V??V - Dancig Queer`: a key whose characters are all ≤ U+00FF is stored as single bytes. That song could never have loaded remotely. The friendly name is now `displayName` in the index, and the UI, next-song screen and progression screen use it. The validator rejects non-ASCII addresses.

**Remote index:** identity, order, revisions and similarity for each song. A content update can add songs without a new player.

**Validation before any content or player build** (`SongContentValidator`, wired into `ContentUpdateTools` and `IPreprocessBuildWithReport`). It checks:
- profile vs current master, playback revision and versions;
- index consistency;
- the same decoded audio on Windows, Android and iOS (no platform override);
- group layout and ASCII addresses;
- that nothing in a build scene or `Resources` references a master, profile or the index.

### Runtime
`CatalogSongLoader` handles a catalog song in these stages:
1. index → download (real bytes and progress);
2. audio and profile loaded in parallel;
3. header validation;
4. profile built on a worker (raw + light derived);
5. clip data loaded.

There is no FFT, no inference and no analysis cache. The style comes from the profile's published tags, and the model is never loaded.

**Errors** are typed and none of them triggers an analysis:
- `DownloadFailed`;
- `LoadFailed`;
- `IncompatibleContent`;
- `IndexUnavailable`;
- `Cancelled`.

**Lease:**
- `GameSession` owns the playback clip handle;
- it is released when another song becomes the selection;
- the preview keeps its own handle.

## 2. The readiness barrier — what authorises the countdown and the music
`SongPreparation` holds explicit states, each one ended by a real completion:

ResolvingSelection → Downloading → LoadingContent → ValidatingProfile → BuildingProfile → PreparingAudio → ContentReady → ResolvingStyle → ApplyingTheme → LoadingScene → PreparingWorld → PreparingConsumers → PreparingAudio → **Ready**

**The countdown (`GameStartedEvent` + `MusicClock.Initialize`) starts only when all of these hold, in the same frame, for the current selection AND while the flow is still Gameplay:**
- the content is adopted (profile valid for the index, the app and the loaded clip);
- the theme transition has settled (style visual loaded and colour transition finished);
- the Runner scene is loaded;
- the world path is built;
- the timeline, pools and checkpoints are created;
- **the environment modules are instantiated** (horizon and music-reactive bars; these were not awaited before — fixed);
- the Runner AudioSource holds this selection's clip with `loadState == Loaded`.

The music starts exactly `warmupTime` later (`StartPlayback`). The clock does not exist or stays Stopped before Ready, as verified at every stage event. No timer replaces a completion.

- **Bounded waits:** download stall 30 s, load 60 s, theme 15 s, Runner 30 s. Each one fails explicitly; none of them continues.
- **One remaining presentation beat:** the ~1.4 s "style detected" caption on the Analyzing screen. It already existed and is the dominant part of the start time; see the separate times below. I did not remove it without asking.

Two real holes were closed in this phase:
1. The environment modules were not awaited.
2. Going back to Song Selection while the Runner scene was still loading let the late preparation **start the countdown anyway**. `GameplayManager` now also requires the flow to be Gameplay.

## 3. Measured times
Editor, local HTTP server at 8 MB/s for the first play.

| | First play (download 15.3 MB) | Same song again (cached) | Another song after a superseded one |
|---|---|---|---|
| Download | 1,602–1,838 ms | — | 84 ms |
| Index + size query | 12–16 ms | 6 ms | 6 ms |
| Load + validate + profile build + audio | 79–121 ms (load 30–40, validate 3–6, profile 39–63 on a worker, audio 7–12) | 50 ms | 81 ms |
| Style + theme + reveal caption | 1,414 ms | 1,406 ms | 1,406 ms |
| Runner scene | 558–704 ms | 361 ms | 429 ms |
| World + consumers + audio | 60–66 ms | 35 ms | 59 ms |
| Ready → countdown | 2 ms | 0.3 ms | 0.4 ms |
| **Total Play → countdown** | **3.9–4.0 s** | **1.86 s** | 2.07 s |
| Clip runtime memory | 11.7 MB | 11.7 MB | 13.0 MB |

- Mono heap stays at 770–865 MB in the editor, with no growth across 9 selections.
- Total allocated memory stays at 352–394 MB.

## 4. Tests

### Edit-mode
**44/44 pass:**
- 24 `MusicSyncTests`;
- 14 `SongAnalysisTests`;
- 6 new `PublishedContentTests`:
  - every compatibility mismatch is an explicit reason;
  - the published profiles are bound to the current masters and round-trip byte-identically;
  - the index revision follows every entry;
  - ASCII addresses;
  - the Phase 4 reuse key.

### `CatalogContentProbe` (new)
Play mode, real built bundles, clean install (bundle cache and remote catalog cleared). **76/76 pass, 0 unexpected errors:**
- remote index and listing (20 songs);
- first play with a real download (bytes, monotonic progress), every stage in order;
- every consumer ready **at** the countdown;
- clip `CompressedInMemory`;
- the same song from cache (no download stage);
- cancel during the download (slow server, 1 MB/s), during ApplyingTheme (content adopted) and during the Runner scene load: nothing published, no countdown, correct leases;
- the previous song's clip is actually unloaded;
- a superseded selection publishes nothing;
- a tampered profile revision and an absent profile give `IncompatibleContent`;
- a server answering 503 gives `DownloadFailed`; the retry succeeds once the server answers again;
- at the end only the current selection is leased;
- **analyses 0, model loads 0, Sentis workers 0** over the whole session.

The 20 error logs inside the three deliberate failure scenarios are expected (Addressables logs several lines per failed bundle).

### `RunnerSyncIntegrationProbe -builtContent -cleanInstall`
The full Runner flow with built bundles. **18/18 pass:**
- warmup;
- handoff;
- pause;
- fall → held → respawn at the fall moment, with no burst;
- restart;
- end of the play range → farewell → GameEnded;
- every pulse at its own musical moment.

### 128 ms issue
Still open and not reproduced.

- **Instrumentation:** `PulseTimingDiagnostics` records expected vs actual musical time, frame time, clock correction and concurrent operations.
- **This run, with built content:** 386 pulses, mean late 0.8 ms, worst 46 ms, worst frame 24 ms, 0 outliers > 50 ms.
- No limit was raised and no offset was added.

## 5. First-use cost (~350 ms)
- **Catalog songs:** the Sentis model and Worker are never created; the counters stayed at 0. The stall can no longer happen on the catalog path.
- **Imported files:** it still happens on the first import of a session, during the Analyzing screen, before Ready — never during the countdown or the music. It was not pre-warmed, since imports will probably be retired.

## 6. Contract for Phase 4 (registered, not implemented)
`Gameplay/BonusLayout/BonusLayoutContract.cs` defines:
- `ProfileRevision` and `BonusLayoutKey`, made of:
  - the song and profile revision;
  - the campaign position (level + match);
  - continuous difficulty, quantised to 0.001;
  - the generator version;
  - a geometry/movement hash;
  - the lap only when it changes difficulty;
  - **no attempt number**;
- `PlacedBonus` and `BonusLayoutRecord`;
- `IBonusLayoutStore`, `IBonusLayoutGenerator` and `IBonusLayoutProvider` (GetOrCreate: generated the first time, saved, then loaded).

Rules:
- The music profile never contains a bonus distribution.
- Combinations are never precomputed.

## 7. Pending or not verified
- **Mobile:** IL2CPP, a real device, RAM, thermals, Vorbis decode CPU, and the Android/iOS content builds. Only Windows content was built; the validator checks that the import settings for Android and iOS give the same samples.
- **A real content update:** not exercised end to end (change → update build → old client gets the new catalog → index re-read → new audio/profile). The pieces exist (`ContentCatalogUpdatedEvent` → index re-read; the validator blocks an inconsistent build).
- **Natural clip end with built content:** the Runner probe ends at the play range, because the manual range is 60 s. The natural end was verified in P1 with `MusicSyncProbe`, not with remote content.
- **Mid-song start with built content:** covered in P1 only.
- **Profile size:** profiles add 76 MB, 23 % of the song download, and are lossless. Compression was not evaluated.
- **vorbis70:** halves the audio download; needs a listening test.
- **Unity shutdown in batch mode:** Unity now hangs ("Cleanup mono") or segfaults on exit after the probes finish; the reports are already written. Run the probes with a timeout.
- **`ServerData/` (1.6 GB):** not in `.gitignore`. It also holds older remote bundles from 15–16 October that are not mine.
- **`ContentDownloadManager` fix:** a synchronously completed download task stayed cached as "in flight"; fixed.

## 8. Files

### New
- `Song/Catalog/*`
- `Gameplay/BonusLayout/BonusLayoutContract.cs`
- `Gameplay/PulseTimingDiagnostics.cs`
- `Editor/Audio/MasterAudio.cs`
- `Editor/Audio/PlaybackAlignment.cs`
- `Editor/Content/SongCatalogBake.cs`
- `Editor/Content/SongContentValidator.cs`
- `Editor/Content/RemoteSongContentSetup.cs`
- `Editor/Content/SongContentLayoutCheck.cs`
- `Editor/DevTools/AudioImportComparison.cs`
- `Editor/DevTools/CatalogContentProbe.cs`
- `Editor/DevTools/BuiltContentPlayMode.cs`
- `Editor/Tests/PublishedContentTests.cs`
- `Assets/_Project/Content/` (20 profiles + index)

### Modified
- `GameplayManager` (barrier)
- `RunnerEnvironmentController` (`IsBuilt`)
- `SongAnalysisController`
- `GameSession` (lease)
- `ProfileBinary` (format 2; the harness copy is synced)
- `SongCatalog` (`PlayableSongCatalogSO`)
- `SongSelectionController`
- `MainMenuController`
- `NextSongTransitionController`
- `RandomNextSongSelector` (it compared a songId with an address)
- `SongPreviewPlayer` and `SongPreviewConfigSO`
- `ProgressionScreenController`
- `ContentDownloadManager`
- `ContentUpdateTools`
- `RunnerSyncIntegrationProbe` (`-builtContent`)
- the 20 `.wav.meta` (load type)
- the Addressables groups

### Removed
- `Song/AddressableSongSource.cs`

### Reports
`Logs/SongCatalogBake3.txt`, `SongContentValidator.txt`, `SongContentLayout.txt`, `CatalogContentProbe.txt`, `RunnerSyncIntegrationProbe.txt`.

The format comparison data lives in `~/.claude/jobs/982d9202/tmp/fmt/*.tsv`.
