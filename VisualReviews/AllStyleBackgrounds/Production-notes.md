# Full-screen style background collection

All 17 named styles now have a complete 16:9 background in `MusicGame/Assets/_Project/Resources/UI/AnalysisBackdrops/`. The 16 new designs use their own existing selected-button artwork plus the approved Dance composition as references. Dance is preserved unchanged. Unknown remains the neutral analysis state.

Each PNG is 1672x941, opaque, compressed at high quality, without mipmaps. The runtime loads only the active style by resource name and releases it when the presentation hides or changes style. The image covers the viewport by cropping UVs, preserving proportions. The neutral staff animation, style reveal, autosizing and HUD handoff are retained.

Generation used built-in ImageGen. Exact per-style prompts are saved alongside this document as `<Style>-prompt.txt`; `Generation-manifest.json` records source and destination paths. Original button assets remain intact.

Validation: Unity compiled the runtime/editor changes; all 17 assets were selected correctly in actual preparation previews. 51 rendered previews cover 1280x720, 1920x1080 and 2560x1080. Each preview also explicitly invokes the presentation's hide handler and checks its background reference is released (edit-mode preview scenes do not dispatch normal MonoBehaviour lifecycle callbacks). The 1080p collection was visually inspected in `All-styles-in-Unity.jpg`. No new full gameplay-transition test was run for this artwork extension.
