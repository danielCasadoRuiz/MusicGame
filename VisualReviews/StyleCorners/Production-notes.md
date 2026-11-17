# Freeform analysis backgrounds

SUPERSEDED: the user rejected this repeated-patch composition. Current approved artwork and implementation are documented in `../AllStyleBackgrounds/Production-notes.md`.

The design uses the full screen apart from the central style/song typography. Eight irregular fragments are distributed across the upper/lower regions and both sides, with varied rotations, sizes and restrained opacities. The artwork has fine internal details and open holes rather than L-shaped panels, square patches or large repeated symbols. Every fragment retains its original aspect ratio.

## Assets and references

- Current source: `MusicGame/Assets/_Project/Art/UI/Analysis/AnalysisFreeformSource.png`.
- Runtime atlas: `MusicGame/Assets/_Project/Art/UI/Analysis/AnalysisCornersAtlas.png`.
- Reference: the game's existing `StyleButtons/<style>/selected.png` backgrounds, collected in `Button-selected-references.png`.
- Exact prompts: `Freeform-prompts.txt`. Generation and background extraction both used built-in ImageGen.
- Original button assets and older motif assets remain intact. Earlier corner/quadrant proposals are not used by the new packer.

Order: Electronic, Dance, Rock, Heavy; Acoustic, Cinematic, Groove, Funky; Soulful, Jazzy, Catchy, Energetic; Chill, Dreamy, Dark, Experimental. Classical and Unknown retain their undecorated treatment.

## Integration

`StyleCornerSetup` isolates all 16 cells, trims export padding and centers each drawing in a square without stretching it. It packs a 2048px atlas with 8px transparent compression gutters. Low-alpha export haze (alpha <= 0.08) is cleared uniformly so the empty background remains transparent in Unity's linear UI blending. Atomic replacement avoids overwriting a mapped texture file.

`AnalysisPresentation` uses eight images sharing this single atlas. A smaller per-fragment scale retains fine details; varied orientations and weights spread the artwork across the available screen while keeping the central typography clear. The neutral staff animation, style-letter reveal, typography autosizing and direct HUD handoff are unchanged.

Verification is recorded in `latest-validation.txt`; Unity captures use `<Style>-1920x1080.png`, with additional Dance 720p and ultrawide captures.
