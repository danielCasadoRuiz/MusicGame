## Audit: song content, Addressables and pre-analysis (read-only; no files edited)

Paths below are relative to `C:\Users\VISYON\Documents\MusicGame\MusicGame\Assets\`. Scripts are under `_Project/Scripts/`.

### 1. How song content flows today

**Catalog discovery (all of it is local).**
- The song list comes from `Addressables.LoadResourceLocationsAsync("Song", typeof(AudioClip))`. Three places do this:
  - `Song/SongSelectionController.cs:157-172`, started from `Start` (line 81).
  - `Song/SongProgression.cs:37-46`.
  - `Fight/NextSongTransitionController.cs:145`.
- None of them waits for `ContentDownloadManager.Initialization` or its catalog update (`Content/ContentDownloadManager.cs:112-136`, 141-170). If a remote catalog update lands late, the list built at startup can be stale.
- Each location is turned into a `songId` by `SongCatalog.IdForAddress(PrimaryKey)` (`Song/PlayableSongCatalogSO.cs:56-60`). The lookup table comes from `Resources.Load<AppConfigSO>("AppConfig").songCatalog` (lines 39-53). That asset ships inside the player.
- A location with no entry in that table is skipped with a warning (`SongProgression.cs:58-61`). **So a song added later through a remote content update can never get a songId, progression, or a pending run.** The `songId → address` mapping is only in the player-shipped SO.
- The SO stores `songId` / `address` / `editorGuid` as strings (`PlayableSongCatalog.asset`): 20 entries, `nextIdNumber: 21`. The GUIDs are plain strings, not object references, so they do not drag audio into the build.

**Loading a song.**
- `AddressableSongSource.Load` (`Song/AddressableSongSource.cs:37-65`) calls `LoadAssetAsync<AudioClip>(location)` and `LoadAudioData()`, then hands the clip to `SelectedSongInfo`. **It never releases the handle.** The class doc admits this (lines 16-18).
- The same source is created on every path:
  - SELECT SONG: `SongSelectionController.cs:411`
  - CONTINUE: `UI/MainMenuController.cs:282`
  - Next song after a fight: `NextSongTransitionController.cs:167`
- Each new song therefore leaks one fully decoded clip. It is about 20-70 MB of PCM, because runner songs use DecompressOnLoad (see section 4).

**Preview.**
- `SongPreviewPlayer` (`Song/SongPreviewPlayer.cs:57-105`) keeps its own handle and releases it in `Stop()` (line 70).
- Addressables reference-counts handles, so releasing the preview does not unload a clip that gameplay loaded through a different handle.
- The safe part today exists only because gameplay never releases. Once gameplay starts releasing, it must hold its own handle for the whole session/run lifetime: Runner, then Fight, then Fight Again. `MatchResultController.cs:500-535` and `RunnerSceneBootstrap.cs:46-48` re-apply `SelectedSong.Clip`.

**Flow from selection to gameplay start.**
1. `OnPlayClicked` (`SongSelectionController.cs:376-423`) moves to the `SongAnalysis` state immediately.
2. It calls `RunAssignment.CreatePendingRun` or `TryRestorePending` (`Song/RunAssignment.cs:124-209`), then `SongSelectionService.SelectSong` (`Song/SongSelectionService.cs:29-51`), which sets `GameSession.SelectedSong`.
3. `UI/SongAnalysisController.cs:110-142` polls until `SelectedSong.Clip` exists, with a 20 s timeout. It then always calls `AudioPreAnalyzer.Analyze`.
4. `SongProfileReadyEvent` fills `GameSession.Profile` (`Core/GameSession.cs:313-319`, which also calls `SongSimilarityCache.Store`).
5. `RunnerSceneBootstrap` re-publishes the profile.

**How a catalog song gets its SongProfile today.**
- It is always "analysed": `AudioPreAnalyzer.Analyze` runs for every song.
- The only shortcut is `SongCache` (`Audio/PreAnalysis/SongCache.cs:6-7`): a JSON file at `persistentDataPath/SongCache/<clip.name>_<clip.samples>.json`.
  - The identity check is name + sample count only (`SongProfileData.cs:56-57`). There is no songId, audio hash, pipeline version or config hash.
  - On a cache hit it still recomputes the derived analyzers (`AudioPreAnalyzer.cs:44`, 252-300).
  - It re-tags only if the tag model version changed (lines 49-55).
  - It then waits a fake 2 s (`CacheHitFakeDelaySeconds`, lines 16 and 65).
- On a clean install every catalog song gets a full FFT plus Sentis analysis.

### 2. Addressables setup

**Songs group: local, not remote.**
- Both build and load paths point at the local variables (`AssetGroups/Schemas/Songs_BundledAssetGroupSchema.asset:36-40`): build path id `8d7d43…` = Local.BuildPath, load path id `7468e2…` = `{RuntimePath}/[BuildTarget]`.
- The Local load path has the same value in all four profiles.
- `m_BundleMode: 0` means PackTogether (set in code at `SongAddressablesSetup.cs:71-76`).
- `ContentUpdateGroupSchema` has `m_StaticContent: 1` (marked static, i.e. "Cannot Change Post Release").
- **Measured: the built bundle `Library/com.unity.addressables/aa/Windows/StandaloneWindows64/songs_assets_all_….bundle` is 263 MB, one monolithic bundle in StreamingAssets.** Today the songs ship inside the player.

**Other groups.**
- `Remote_Arenas` and `Remote_Enemies_*` use the Remote variables with PackSeparately.

**Profiles** (`AddressableAssetSettings.asset:83-160`).

| Profile | Remote load path |
|---|---|
| Default | (not checked) |
| ProductionCCD | CCD URL with placeholders `YOUR-CCD-PROJECT-ID` / `YOUR-CCD-BUCKET-ID` |
| LocalContentServer | `http://localhost:8765/[BuildTarget]`, built to `ServerData/[BuildTarget]` |
| DevelopmentLocal (**active**, line 232) | the local runtime path |

**Settings.**
- `BuildRemoteCatalog: 1`, and the remote catalog paths are set.
- `DisableCatalogUpdateOnStart: 0`.
- `CheckForContentUpdateRestrictionsOption: 0` in the asset, although `ContentDeliverySetup.cs:205` sets it to ListUpdatedAssetsWithRestrictions.
- `m_BuildAddressablesWithPlayerBuild: 0`.
- `ContentDeliverySetup.cs:208-213` forces every group whose name does not start with `Remote_` to StaticContent=true. **Re-running it would keep "Songs" static/Core.** Renaming the group to `Remote_Songs` fits that convention.

**Labels.** "Song" exists. The `Category_*`, `Q_*` and `Core` labels belong to the quality/content system.

**Content update tooling** (`Editor/Content/ContentUpdateTools.cs`).
- Build Release Content: `CleanPlayerContent`, `BuildPlayerContent`, then archives the content state to `ContentState/<platform>/<version>/addressables_content_state.bin` (lines 65-86).
- Build Content Update: moves changed static entries into a remote PackSeparately "Content Update <v>" group (lines 90-128).
- Batch entry points: `-executeMethod ContentUpdateTools.Build*FromCommandLine -profile X`.
- **There is no pre-build validation hook** (grep found no `IPreprocessBuildWithReport`, `BuildPlayerProcessor` or `BuildScript` callbacks).

**ContentDownloadManager** provides `EnsureContentAvailableAsync`, `GetDownloadSizeAsync`, `ClearCacheAsync` and `PreloadContentAsync`, with retries (lines 199-270). It is already usable for "download this song before loading".

**Leaks into the player build.**
- No scene, prefab or Resources object references any of the 20 Music GUIDs; only the two catalog SOs contain them, as strings.
- AppConfig (Resources) references `songCatalog`, `songSimilarity` (19 KB) and `audioAnalysis`. Those are lightweight.
- The real leak is the local Songs group itself.

### 3. Bake tools and SongCache

**`Editor/DevTools/SongSimilarityBake.cs`**
- Iterates `PlayableSongScope.FindPlayableClipGuids()` (`PlayableSongScope.cs:22-30`) and maps songIds through `editorGuid`.
- Reuse key: `AssetDatabase.GetAssetDependencyHash(path)`, which covers source file + import settings, plus `SongSimilarityVector.CurrentVersion` (lines 69-72).
- Profile source order:
  1. the personal `persistentDataPath` SongCache JSON (lines 103-121; the path is hard-coded again here);
  2. a full in-Editor `AudioPreAnalyzer.Analyze` run synchronously through `RunToEnd`, with the WaitForSeconds skipped (lines 126-156).
- It stores only `SongSimilarityVector`s in `Configs/Song/SongSimilarityCatalog.asset`. **The full profile is thrown away.** It depends on the personal SongCache, so its results vary by machine.
- It has a progress bar but no cancellation, and it reports reused / from-cache / analysed / failed counts.
- `SongSimilarityCache` (`Song/Similarity/SongSimilarityCache.cs:7-11`, 110-131) also falls back lazily to SongCache files at runtime.

**SongCache size (measured).** `C:\Users\VISYON\AppData\LocalLow\DefaultCompany\MusicGame\SongCache` holds 20 files, 701 MB in total, 23.8-55.7 MB each. The files are pretty-printed JSON (`SongCache.cs:37`, `ToJson(...,true)`).
- Sample song "All But Water": 20,135 frames at a 10.67 ms hop, 48 kHz.
- Per frame it stores energy, centroid, flux, flatness and voice (5 floats), 6 bands, 24 visual bands and 12 chroma values: about 47 floats per frame, roughly 3.8 MB per song as raw float32.
- Plus 966 onsets.
- A binary format would be about 6-15x smaller than this JSON.

### 4. Import settings and catalog size

**Runner songs** (`_Project/Audio/Music`, 20 WAV files, 870 MB of source, 28-69 MB each). All 20 metas use:
- `loadType: 0` (DecompressOnLoad)
- Vorbis, `quality: 1`
- `sampleRateSetting: 0` (preserve)
- `preloadAudioData: 0`, `loadInBackground: 0`
- `normalize: 1`, `3D: 1`
- no platform overrides

DecompressOnLoad is needed by `clip.GetData` in runtime analysis (`AudioPreAnalyzer.cs:304`).

**Composer tracks** (`_Project/Audio/Composers`, 60 MP3s, 321 MB):
- `loadType: 1` (CompressedInMemory), Vorbis, `loadInBackground: 1`.

**Playable songs:** 20, matching 20 addresses in `AssetGroups/Songs.asset` and 20 songIds.

### Risks (confirmed)
1. Songs ship locally as one 263 MB static bundle. Playing any song requires the whole bundle.
2. A remote update cannot add songs, because the songId table lives only in the Resources SO.
3. The gameplay handle is never released: a leak per consecutive song.
4. Cache identity is name + samples, which can collide; there is no versioning or config hash.
5. Song lists are queried without awaiting the catalog update.
6. A bake depends on the developer's personal cache.
7. `normalize: 1` and Vorbis decoding change the PCM. A profile must be computed from the imported clip and bound to its import hash.
8. There is no publish gate.

### Recommended design

**1. Published profile asset per song.**
- New `SongProfileAsset : ScriptableObject` in `Assets/_Project/Content/SongProfiles/<songId>.asset`. It holds a header plus the raw arrays as one blob: `byte[]` float32 (or a TextAsset `.bytes`), no JSON.
- Header fields:
  - `songId`
  - `audioSourceSha256` (file bytes)
  - `importHash` (`GetAssetDependencyHash`, which covers importer and normalize)
  - `clipSamples`, `channels`, `frequency`
  - `formatVersion`, `pipelineVersion`
  - `analysisConfigHash`: a hash of the canonical serialization of the result-affecting `AudioAnalysisConfig` fields
  - `tagModelVersion` and its own `tagsInputHash`, so tags can be reused when only rhythm changes
- Store raw arrays only. Derived analyzers recompute on load into a fresh `SongProfile`, never mutating the shared asset's arrays, or else clone them. Measure the cost.
- Generate it from a single bake tool that writes both the profile asset and the similarity vector from one analysis. That replaces the SongCache step in `SongSimilarityBake`.
- The bake tool should support: missing/stale only, force, cancel (`DisplayCancelableProgressBar`), and a report.

**2. Bundle layout.**
- Rename or recreate the group as `Remote_Songs` (Remote build/load paths, `StaticContent=false`, PackSeparately).
- Use a pair of addresses per song: `song/<songId>/audio` and `song/<songId>/profile`, with labels `Song` and `Song_<songId>`.
- Recommendation: put the audio and its profile in the same bundle for each song, either a per-song group or PackTogetherByLabel. They then download, cache, invalidate and update atomically, so a new audio file can never pair with an old profile, and you get one download per song.
- The cost is that a profile-only fix re-downloads the audio. That is acceptable because profile changes come with pipeline bumps, which already affect every song.
- Consider CompressedInMemory or Streaming for catalog audio once runtime analysis is no longer needed for it. Keep DecompressOnLoad only for local-file imports, and verify that preview seeking still works.

**3. Validation before a content build.**
- Run an Editor validator from both `ContentUpdateTools.Build*` and an `IPreprocessBuildWithReport`. For each songId in the catalog:
  - the profile asset exists and its `songId` matches;
  - its `audioSourceSha256` / `importHash` match the current clip;
  - `formatVersion` / `pipelineVersion` / `configHash` are current;
  - both entries sit in a Remote group with matching labels;
  - no Music asset is in a local group or referenced from scenes or Resources.
- Fail the build on any error.

**4. Runtime load path.**
- Add a `CatalogSongSource` with a cancellation token and a request id.
- `EnsureContentAvailableAsync("Song_<id>")` with progress (download is reported separately). Then start the audio and profile loads in parallel.
- Validate the header against the loaded clip (samples, frequency, songId).
- Publish `SongProfileReadyEvent` from the existing `SongAnalysisController` flow, skipping `AudioPreAnalyzer`, the fake delay, FFT and Sentis.
- Hold the handles in a session-scoped owner, e.g. `GameSession.SongContentLease`. Release it only when a different song is assigned or the session ends, never on preview stop.
- On error, report "download failed" (retry) separately from "invalid content" (back to selection). Never fall back to silent analysis.
- Local imports keep the `AudioPreAnalyzer` + SongCache path. Upgrade the cache key to songId/path + hash + versions.

**5. Remote catalog discovery.**
- Move the songId index into the remote content: a small `SongIndexAsset` per song (songId, displayName, addresses), or one remote `SongIndex` asset, labelled `SongIndex` in a non-static remote group.
- Enumerate after `ContentDownloadManager.Initialization` (catalog updated) instead of reading AppConfig.
- Keep a tiny player-shipped fallback index only for offline first launch, or none at all.
- Keep `PlayableSongCatalogSO` as the Editor-side source of truth for id allocation.
- Make `SongCatalog` resolve from the loaded remote index, so new songs added by a content update get ids, progress and similarity data. The similarity vectors would also need to move to remote content, or sit in each per-song index asset.

**Pending/unverified:** no Unity run, no bundle build, no device test. CCD credentials are still placeholders.
