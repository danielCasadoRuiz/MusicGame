# Announcer candidates (to listen to — NOT used by the game)

Each candidate was made the same way the final set would be made: Qwen3-TTS VoiceDesign creates the voice once (`*_reference.wav`), then the clone model says the two audition phrases with that identity:

- `*_tempo.wav` — "Three, two, one... Tempo!"
- `*_fury.wav` — "Fury!"

In Unity, select a file and press ▶ in the Inspector preview.

| Candidate | Persona (design description, short) | Measured pitch* |
|---|---|---|
| `female_a` | late 30s, warm contralto with chest resonance, low register | 368 |
| `female_b` | 40s, deep husky alto, a little grit, commanding | 388 |
| `female_c` | 30s, rich mezzo-contralto, stadium energy, relaxed low pitch | 356 |
| `male_b` | 40s, deep resonant bass-baritone with a slight rasp | 285 |
| `male_c` | 30s, warm powerful baritone, bright attack | 397 |
| *current* `announcer_m` (kept, provisional) | early 30s, resonant baritone | 290 |
| *rejected* `announcer_f` (out of the random pick) | early 30s, bright mezzo | 388 |

\* Median pitch (Hz) of the shouted reference measured with pYIN. The values are only comparable with each other: shouted speech pushes them up. Lower than the rejected voice (388) means a less high-pitched voice.

To adopt a candidate:
1. Copy its description and seed (`Tools/VoiceGen/announcer_candidates.py`) into an announcer `VoiceDefinition`.
2. Generate it with **Voice Production**. It produces the 6 clips, and only those.
3. Set `inRotation` on its set in `Resources/Audio/GameSfxLibrary`.

**Adopted 2026-10-24:** `female_a` → `announcer_f`, `male_c` → `announcer_m` (both in rotation). Per-line takes chosen with `Tools/VoiceGen/announcer_takes.py`.
