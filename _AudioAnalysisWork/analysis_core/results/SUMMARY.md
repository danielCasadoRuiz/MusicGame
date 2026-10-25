# Analysis core — test-fix pass after Codex review (2026-10-17, pipeline version 2)

Run: `build.sh`, then `dotnet bin\harness.dll suite` (also `synth`, `stimuli`, `margins`). `synth`/`stimuli`/`suite` exit 1 on any mandatory failure (negative control: strict suite on the old detector returned 1).

## Codex findings
1. Extra families ignored → strict verdict on every matched event in every case: required ⊆ predicted ⊆ required ∪ justified-optional; Generic counts as extra when a family is required; missing/extra families counted separately.
2. Exit code always 0 → fixed.
3. Stimulus end transients → end fades 30 ms (pad 300 ms); detector-independent stimulus validator (HP 2 kHz energy rises in 2 ms frames + 3-band rises ±40 ms around every sound end) reports 0 artefacts in current stimuli. The old 44.1 kHz pluck "false positive" was such an artefact.

## Synthetic suite (44.1 and 48 kHz)
20 mandatory cases per rate: 0 failed, 0 omissions, 0 extra events, 0 missing families, 0 extra families, 0 stimulus artefacts.
Timing max |error| 4.49 ms on sharp attacks (limit 6 ms); soft-bass 7.18 ms (44.1k) / 9.43 ms (48k) (limit 15 ms, 20 ms fade-in).
Justified relaxations: hats under snare hits (hat 5.3 dB below snare in 5–16 kHz) → HiHat optional; kick 5 ms before end of audio → Kick or Generic. Legato bass = informational (no sharp attack).

## 44.1 kHz Kick+Snare → extra HiHat
Root cause: hi-hat test compared total power of a ~10.9 kHz-wide band vs a ~2.5 kHz-wide reference → flat noise looked +6.4 dB high-heavy from bandwidth alone. Fix in the detector: power per Hz + explaining-away (confident Snare requires a co-occurring hat +1…+7 dB brighter per Hz; otherwise ramp −4…+2 dB). `AnalysisVersions.Pipeline` = 2.
Margins (threshold 0.5): Kick required ≥0.83 / not-permitted ≤0.25; Snare 1.00 / 0.33; HiHat 0.73 (44.1k), 0.50 (48k) / 0.31–0.33.
Known knife-edge: soft 32nd hat 62 ms after a 6 dB louder one → exactly 0.50 (coverage test; a 3 dB criterion fixed it but raised Snare labels on real songs by 37 % without ground truth, reverted).

## Other
- Sequential vs parallel: bitwise identical (1–16 workers, chunks 64–4096; synthetic + real song).
- Cancellation: OperationCanceledException, no result, 1–68 ms after Cancel(); progress monotonic, ends at 1.0.
- Tempo unchanged: 24/24 correct up to octave, 17/24 exact, phase ±15 ms 24/24.
- Real songs: label distribution only (NOT accuracy): HiHat 4,164 → 3,802, Kick/Snare unchanged, 29,913 events.
- bench/format/tagger/featcmp/calib/fftcheck outputs are from pipeline 1 (family heuristics only changed). Old outputs: `archive_pipeline1/`.
- All timings: external .NET on desktop, not Unity / mobile. Synthetic tests do not prove instrument identification in real mixes — that needs the Phase 4 A/B listening test.
- Core still compiles against netstandard 2.1 / C# 9 (`check_netstandard.sh` OK).

## P2B change (pipeline version 3) — 2026-10-17
- TempoEstimator: a tempo is only reported for a PERIODICITY PEAK (interior local maximum of the onset-novelty autocorrelation; the prior chooses between peaks) and with ≥ 4 events. Before, one isolated hit produced 200.9 BPM (the autocorrelation decays with lag → the edge of the search range won). Now estimatedBpm = 0, no beats.
- `suite` exit 0 (all mandatory parts). `tempo`: 18/24 exact (was 17/24: halftime 120 now 120 instead of 60), 24/24 up to octave, phase 24/24, tracked beats 1227/1227 (`tempo_pipeline3.txt`). `real`: BPM, confidence and phase identical on all 20 songs, events unchanged (`real_pipeline3.txt`).
