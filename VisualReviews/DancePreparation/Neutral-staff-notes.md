# Neutral staff + Dance reveal

Supersedes the first Dance-only waveform pilot.

Production assets:

- `MusicGame/Assets/_Project/Art/UI/Analysis/NeutralStaffSource.png`
- `MusicGame/Assets/_Project/Art/UI/Analysis/NeutralStaffAtlas.png`
- `MusicGame/Assets/_Project/Shaders/AnalysisStaffTint.shader`
- Existing `MusicGame/Assets/_Project/Art/UI/Analysis/DanceFrame.png`

The shared source was generated using the built-in image generation tool with the prompt below. The Unity editor bakes the loop and scan into a single grayscale atlas: 32 frames at 20 fps, 4 columns × 8 rows, 1024 × 320 per frame, top-left first. The atlas is 4096 × 2560. Runtime switches sprite references; it does not create images or deform the staff mesh. Textures have no mipmaps and no readable CPU copy.

Every PNG RGB channel is identical (verified R−G = G−B = 0 across the whole atlas). A UI material applies a dynamic tint to midtones while retaining warm ivory highlights. Initial tint comes from the current menu style; next-song selection carries the previous song's tint. Detection transitions to the actual song style.

Dance reveals once at the center of the staff with staggered per-letter impacts. Its lateral/bottom frame fades in at identification. At Runner readiness, the existing real HUD is raised above the cover and the same label flies directly into its HUD slot, changing to ivory with a dark outline. Metadata/staff fade away. The clock and playable countdown start after this handoff, then the cover is hidden. There is no intermediate style label next to the song.

The actual play check captures `01-Analysing.png`, `02-Dance-reveal.png`, `03-Hud-ready.png`, `04-Game-started.png` and records its checks in `play-result.txt`. Progress writes are suspended during the check; when run through a request in an existing editor, the check stops Play Mode without closing that editor.

## Exact source-art prompt

Production game UI animation SOURCE ASSET ONLY, transparent PNG landscape 3:1, ideally 2400x800. Reference only the NEUTRAL MUSICAL STAFF in the upper-left screen 01 of the supplied storyboard. Generate ONE reusable isolated flowing musical staff with five parallel elegant fine wavy lines and 6-8 simple luminous noteheads and stems attached naturally to the lines. Two gently undulating hills, diagonal flowing ribbons and a handful of hair-thin light trails; short tasteful clusters of light mist along lines, not a dense star field. An expressive yet technical sheet-music scanning aesthetic, not classical parchment. Frame the entire motif inside canvas x6%-94%, y18%-82% with fully transparent padding, so later animation has room. BOTH endpoints taper and fade out. Use strictly ACHROMATIC grayscale: white, light gray and charcoal values with real transparent alpha; ABSOLUTELY NO colored pixels, no purple, no cyan, no gold, no cream tint. Brightest noteheads and tiny selected filament glints pure white; main lines medium/light gray, subtle translucent trails darker gray. The grayscale hierarchy is essential: the game will dynamically tint midtones while keeping white highlights ivory. This is the static source for a seamless periodic sprite animation baked by the asset tool. Do NOT draw a scan beam, scanning is added by the sprite bake so it can move. Do not include the reference's Dance title, song metadata, progress bar, border, black background, numbers, text, labels or multiple storyboard frames. ONE beautiful isolated luminous grayscale musical-staff ribbon on true transparent background. Crisp detailed linework at output resolution, no photographic backdrop.
