# HUD, pause and mobile controls

The runner HUD now includes the real TripleCombo and QuadCombo counters alongside Life and Special, using their distinct x3/x4 sprites. Counters read GameplayManager.ResourceCounts; collection and combat scoring rules are unchanged. The logo moves slightly left, the song/progress region moves right and the two separators have half-height strokes. All ten icons retain space for their counts.

The song title and style label now follow the same spatial transition to classical gold as the progress fill/border and icons. Combo icons also receive a gold finish. The classical pause glyph no longer draws the shader's side laurel/arcs, and a purely classical pause glyph remains visible even without a styled layer.

GameplayHUD subscribes to the actual PickupComboEvent and displays COMBO ×3 or COMBO ×4 below the HUD, in the title font, with a short impact/fade. Runner diagnostic combo/resource messages are shown only with the F1 debug overlay.

The touch controls use one shared neutral tintable family and an exclusive classical gold family, cross-fading with runner style progression. Their existing input scripts, hit rectangles and joystick math remain unchanged. The atlas is `MusicGame/Assets/_Project/Art/UI/TouchControls/Atlas.png`; its authored source is `Source.png` in the same folder. Built-in ImageGen generated it from the current game buttons; the exact prompt is `Touch-art-prompt.txt`. Packing strips low-alpha empty export residue uniformly.

Validation is in `result.txt`: actual GPU checks cover spatial curtain endpoints and the filled progress strip; nine rendered layouts cover Dance/Electronic/Classical at three progression shares; the real EventBus checks both combo toast texts. The combo event test uses an isolated editor preview and suspends no gameplay rules or player progress. This is not a new complete runner/fight playthrough or a physical touchscreen test.
