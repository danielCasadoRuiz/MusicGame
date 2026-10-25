# Phase P4 — bonuses from the detections, stored layouts, difficulty, diagnostics (2026-10-23)

Built in the Windows editor. Nothing committed; no player build was made. The content from P3 was reused as is: profiles and bundles are unchanged.

## How it works

### Selection (`GameplayTimeline.SelectDetectedCandidates`)
- **Source:** `profile.onsetEvents`.
- **Gates:**
  - strength ≥ 0.08;
  - the music not near-silent;
  - not on top of a spawned Impact (90 ms).
- **Priority:** strength × (0.6 + 0.4 × best family confidence), +0.15 when the event is on a tracked beat.
- **Ranking:** built greedily under a 160 ms minimum spacing. It is the song's single playable ranking.
- **Difficulty:** keeps the first `Share(d)` of that ranking, from 25 % at d = 0 to 100 % at d = 1. The result is smooth and nested: a harder layout is a superset of an easier one.
- **One object per detection:**
  - type = the most confident family ≥ 0.5, otherwise a generic `Onset`;
  - contributors = all the detection's labels ("Kick+HiHat").
- **Randomness** (the layout seed) only decides heights, lateral offsets, patterns and off-track picks, through the existing emitters.
- **Legacy path:** a profile without `onsetEvents` keeps the old classifier.
- **`TimelineEvent`** now carries `origin` (Detected / SyntheticGrid / Structural / Resource / Legacy) and `onsetIndex`.

### Synthetic fill
`selection.syntheticFill` is **off by default**. When on, it adds only tracked beats (`beatTimes`, never a 60/BPM grid) in gaps of at least 0.7 s. Those items are RingType Beat, sourceFeature "Grid", origin SyntheticGrid.

### Difficulty
Difficulty is continuous, from 0 to 1.

**Campaign** (`CampaignConfigSO.RunnerDifficultyAt`):
- Each level gets a slice of 0.15 → 0.85, and its matches step up inside it: L1 runs 0.15 → 0.29, …, L4#6 is 0.82.
- Per-level overrides: `difficultyFirstMatch` / `difficultyLastMatch`.
- `lapDifficultyStep` defaults to 0. While it is 0, laps share layouts and the lap is not part of the key.

**Free play:** `collectibles.freePlayDifficulty`; −1 derives it from Normal (0.3) / Hard (0.6).

**Heights:** `HeightsAt(d)` interpolates between `normalHeights` (at 0.3) and `hardHeights` (at 0.6), so the old tuning is exactly preserved at its two anchors.

Song_008 (154 s), detected bonuses per campaign position:

| L1#1 | L1#5 | L2#1 | L2#5 | L3#1 | L3#6 | L4#1 | L4#6 |
|---|---|---|---|---|---|---|---|
| 260 | 335 | 354 | 429 | 448 | 526 | 541 | 620 |

Each match adds +15–19 moments over the previous one.

### No player accessibility options (user decision, 2026-10-23)
Difficulty, jumps and off-track bonuses depend ONLY on the config and the campaign progression. No menu or control exists.

The former settings now live in `collectibles.devLayoutOverrides`, which is a DEVELOPMENT tool:
- the master switch `enabled` is off;
- outside the editor and development builds it is ignored entirely (`DevOverrides` returns null);
- while inactive it is excluded from the layout key.

### Stored layouts (`BonusLayoutProvider`, `FileBonusLayoutStore`)
- **Key:**
  - profile revision (master, playback, body CRC, pipeline, params);
  - position ("L2#3" / "free");
  - difficulty (×1000);
  - generator version (`RunnerPickupLayout.GeneratorVersion` = **3**);
  - config hash (core + collectibles + levelGeneration, with diagnostics and object references excluded);
  - lap only when it changes the difficulty;
  - **no attempt number**.
- **Seed:** derived from the key.
- **First play:** generated and saved complete in `persistentDataPath/BonusLayouts/<song>_<revision>_<keyhash>.mgbl` ("MGBL" v1, full key verified, CRC-32).
- **Later plays:** loaded and never regenerated.
- **Missing file:** rebuilt identically.
- **Corrupt file or a foreign key:** rebuilt and reported as `Rebuilt`.
- **New profile revision of a song:** a new key; the old revision's files are pruned.
- **Saves:** untouched. The old per-match `pickupSeed` stays in saves but no longer decides anything.
- **Where it runs:** inside the readiness barrier (`PreparingConsumers`), so it always finishes before the countdown.

Measured in the editor:
- generate: 15–51 ms;
- load: 5–11 ms;
- about 20 KB per layout.

### Diagnostics
`BonusDiagnostics` plays optional clicks: Off / Detections / SelectedBonuses (synthetic = double click) / SyntheticOnly / ExecutedPulses.
- **F7** cycles the mode in play.
- The starting mode is `collectibles.diagnostics.clickMode`, default Off.
- Clicks are scheduled from the music's clip time via `PlayScheduled`, and re-sync after pause, seek, respawn and restart.
- The F1 HUD has a **BONUS LAYOUT** section: outcome + key, selection counts, click mode.
- The log prints per-run counts.

## Tests
- **Edit mode: 52/52 + 14/14** (on the final code: `BonusLayoutTests` 8 + `PublishedContentTests` 6). The new `BonusLayoutTests` run on the real published `song_008`:
  - repeatability, byte-identical;
  - variety between positions, with the same selection;
  - generated → loaded → deleted → rebuilt identically → corrupt → `Rebuilt`;
  - key versus config, diagnostics, revision and attempt;
  - pruning of stale revisions;
  - difficulty strictly rising per match across the 22 campaign positions, nested, spacing respected, the Normal/Hard anchors kept;
  - dev overrides: off by default; while inactive they change neither the layout nor the key; when enabled in the editor, spacing / no off-track / no jumps / cap work;
  - one object per detection, no duplicate next to an Impact;
  - exact sync: event time = warmup + attack time, with no offset;
  - synthetic fill off by default and, when on, only tracked beats in real gaps.
- **`CatalogContentProbe`** (real remote bundles, clean install): **77/77**. Includes the new check that the second play of the same combination **loads** the stored layout (same key).
- **`RunnerSyncIntegrationProbe`** (full Runner flow): **18/18**. 123 pulses, worst 2 ms, no outlier; the layout was loaded in 11 ms.
- The P1/P2 suites (`MusicSyncTests`, `SongAnalysisTests`) passed on the first P4 compile (52/52). They were not rerun after the selection retune, because they do not touch it.

## Not verified / pending
- Listening by the user: the selection rules are heuristics, so only a real listen tells whether the bonuses sit "on the music".
- `hihat` is rare at low difficulty, because strong kicks and snares rank first.
- The synthetic fill is off and its musical value is unjudged.
- Mobile was not tested.
- No cap on the number of layout files (about 20 KB each, pruned per song revision).
- The click timing has about one DSP buffer of error. It is a comparison aid, not a measurement.
