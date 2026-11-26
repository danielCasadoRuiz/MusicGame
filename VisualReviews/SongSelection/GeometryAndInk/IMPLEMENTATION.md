Song selection geometry, footer and title-colour correction.

Selected card surface grows uniformly by 1.08. Surface, rim and shadow derive the same slant from that scale; their edge slope remains equal to the unselected card. The frame tracks the current animated surface bounds, including intermediate frames. The rim/shadow shaders read slant encoded in each mesh instead of a shared fixed slant. The inner rim is a constant signed-distance inset.

Footer buttons share a bottom baseline and 32-unit gaps. BACK is alpha=1. BACK and YOUR SONG are both 82 high, PLAY remains 104 high. YOUR SONG fills the remaining left-hand footer span. Its existing skin button texture is divided into fixed-height-scaled left/right ends and an extendable centre; no new raster assets.

Choose Your Song alone uses a per-skin texture palette saved in StyleSkinLibrary.songHeadingInks. Day samples the darker centre of the authored selected-song stage; night samples the selected menu-button centre. Very dark night samples retain hue with a raised brightness floor. Other menu titles retain their existing colour policy.

Runner and Fighter pause titles explicitly use GameSession.DetectedGameStyle. Existing gameplay style/classical blend weights on pause buttons remain intact. Validation sets home skin Acoustic and song Electronic and checks both production pause prefabs.

Verified with eight actual Unity Canvas/camera renders at 1920x1080 across Groove, Dance, Electronic and Acoustic in light/dark. Geometry validation checks card aspect, edge slopes, frame bounds/encoded shear, matching shadow, footer baseline/gaps/opacities and extended endcap components. See song-selection-polish-validation.txt at the outer project root. No song metadata, game progress or purchase state is changed.

Footer CTA revision: order is BACK / YOUR SONG / PLAY. The 1720-unit footer keeps 32-unit gaps and a common bottom baseline. YOUR SONG and PLAY are both 624x104; BACK is 408x68, keeping the native button aspect. The custom-song face reuses the existing button texture with a shared accent shader and one owned material instance; borders, alpha and texture grain are retained, and label ink is chosen for contrast. Pale accents fall back to the style palette. Groove night title/CTA use the warmer gold #FBB916 sampled from the selected button x800..1000/y100..230. No additional bitmap assets. Eight native renders and footer geometry/CTA checks passed.
