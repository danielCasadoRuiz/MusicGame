# Dance preparation pilot

Implemented the selected lower-left layout using the existing TempoFuryBrush title font and the menu's supporting font. No percentage; one preparation screen from next-song selection through the real readiness barrier. The new surrounding artwork is authored only for Dance. Other styles retain their existing artwork while using the shared layout.

Assets:

- `MusicGame/Assets/_Project/Art/UI/Analysis/DanceFrame.png`: transparent surrounding artwork.
- `MusicGame/Assets/_Project/Art/UI/Analysis/DanceWaveSource.png`: transparent painterly waveform source.
- `MusicGame/Assets/_Project/Art/UI/Analysis/DanceWaveAtlas.png`: 24 frames, 6 columns × 4 rows, each 512 × 192, top-left first; 16 fps, 1.5-second loop. No duplicate endpoint frame. Baked once by `DancePreparationSetup`, not generated during play. Sprite changes only at runtime. Textures have no mipmaps or readable CPU copy.

Artwork generated with the built-in image generation tool, using the approved four-proposal board as reference. The animation atlas is a deterministic periodic deformation of the generated waveform, built in the Unity editor. No CLI/API image-generation fallback was used.

Validation:

- Actual Unity renders at 1920×1080, 1280×720 and 2560×1080.
- Actual published Dance song selected and prepared through the real content pipeline.
- All 24 animation sprites observed during preparation.
- No visible percent or large analysis heading.
- Preparation remains visible until GameStarted.
- Next-song selection reuses the same view, clears the previous title and does not instantiate the old NextSongTransitionView.
- Progress writes suspended during the play check.
- Adjacent-frame RGBA differences across the atlas range 16.12–18.13; last-to-first seam is 17.09, within that range.

Exact art prompts follow.

## Frame prompt

Production game UI asset, transparent PNG landscape 16:9, ideally 1920x1080. Use ONLY the bottom-left proposal 03 of the reference image as art direction. Create ONLY its outer painterly Dance framing artwork as a transparent overlay. Remove ALL text, labels, numbers, progress lines, central waveform, black background and contact sheet gutters. Preserve the purple/lilac and ivory dry-brush streaks, torn expressive brush edges, violet halftone dots and subtle diagonal slash accents of the selected bottom-left screen. Long elegant lower-left and lower-right L-shaped sweeps extend up both side edges and horizontally along the bottom. Left art climbs into upper-left; right art rises to mid-right. Bottom paint only occupies bottom 12-16% with tapering narrow trails toward center. Side paint occupies outer 12-17% tapering inward. Keep entire central area (x20%-80%, y12%-82%) completely transparent and empty for content. Do not fill the center with dark paint. No hard rectangular boundary or black matte; genuinely transparent alpha including gaps between hairs/paint flecks. No text of any kind. Full-bleed decoration touches left, bottom, right edges, balanced asymmetrical wide framing. This is the asset to overlay on a near-black screen, not a screenshot of the UI.

## Waveform source prompt

Production game UI animation SOURCE asset, one single horizontal violet painterly sound ribbon, transparent PNG, wide 4:1 canvas. Reference ONLY the graceful small waveform floating in the upper-middle of proposal 03 (bottom-left) of the supplied contact sheet. Generate ONLY that waveform as a separate reusable transparent asset, not a screen or contact sheet. One elegant continuous horizontally flowing musical wave, two rising crests and two soft troughs, beginning and ending at the same central horizontal baseline. Several hair-thin luminous lilac/violet translucent brush-filament trails layered around a slightly brighter central painted line; expressive dry-brush texture, sparse tiny halftone flecks near the ends. Purple palette, understated rather than neon bloom. Long thin horizontally composed shape, central stroke amplitude occupies about 35% of image height with plenty of transparent padding top/bottom for future animation deformation. Shape fades gracefully at left and right endpoints, occupies x8%-92%. No text, no numbers, no icon, no frame, no border, no progress bar, no labels, no black background. Preserve real alpha transparency. This source will be deformed into seamless periodic sprite frames by the game asset build step, so don't add multiple different tiles: ONE waveform only.
