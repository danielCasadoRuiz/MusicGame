# Approved Dance full-screen background

The user approved the unified background inspired directly by `StyleButtons/Dance/selected.png`. The final artwork is `MusicGame/Assets/_Project/Art/UI/Analysis/DanceBackdrop.png`, generated with built-in ImageGen; exact instructions are in `Prompt.txt`.

The presentation now draws this one opaque texture across the viewport and crops its UVs to cover different aspect ratios without stretching it. The central typography remains clear. The existing reveal controls its entrance; the staff animation, text reveal, typography autosizing and HUD handoff are unchanged.

The repeated freeform patches have been removed from the runtime layout. This approved direction has now been extended to all 17 named styles; see `../AllStyleBackgrounds/Production-notes.md`. The runtime uses the per-style Resources background and releases it when hidden. The rejected generated source files remain as unused working artifacts.

Validation: runtime and editor scripts compiled in Unity; Dance rendered and visually inspected at 1920x1080, 1280x720 and 2560x1080. Electronic rendered to verify restoration of the original fallback. Prefab references point to the single Dance backdrop and original motif atlas, with the rejected corner atlas cleared. Existing obsolete API warnings remain; no new compilation errors were found. This was a UI layout check, not a new full gameplay transition test.
