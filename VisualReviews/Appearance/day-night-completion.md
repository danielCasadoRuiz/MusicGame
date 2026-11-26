# Day/night background completion

Built-in image generation edits, using the existing night backgrounds as references. Saved new day assets only; originals retained.

Assets: `MusicGame/Assets/_Project/Art/UI/MenuBackgrounds/Day/{Dance,Dark,Heavy,Rock}.png`.

Prompts: retain the exact minimal abstract identity, fine diagonal brushing and restrained edge accents; change the charcoal night field to a genuinely light daytime surface, no objects, floor, lettering, frames or UI. Dance: lavender/silver with magenta curves. Heavy: pale steel with graphite and crimson corners. Rock: silver/ivory with crimson edge streaks. Dark was revised per user instruction: keep sombre medium smoky graphite/slate grey, brighter than night but not white/pale silver; muted violet and wine haze, fine diagonal grain, calm centre. Dark keeps light ink in day mode.

Original generated files:
- Dance: C:\Users\VISYON\.codex\generated_images\01a1f9ce-28d3-7041-a7b9-36c347326170\exec-89e9cc70-c05e-4c3e-ba05-c237ad8e4ed6.png
- Dark: C:\Users\VISYON\.codex\generated_images\01a1f9ce-28d3-7041-a7b9-36c347326170\exec-70acf0d1-ddb5-400a-8066-ca4d10de715c.png
- Heavy: C:\Users\VISYON\.codex\generated_images\01a1f9ce-28d3-7041-a7b9-36c347326170\exec-6fab7cba-82fd-4ac3-97e3-d7e21c4a74e1.png
- Rock: C:\Users\VISYON\.codex\generated_images\01a1f9ce-28d3-7041-a7b9-36c347326170\exec-2ff20460-fc91-4eb6-92c2-80357886e0f5.png

Audit: `background-audit-after.tsv`. All sixteen night/day pairs have distinct textures and day is brighter than night. Dark's day remains intentionally darker than all others. Card surfaces reverse in day/night using the same normal/selected texture pair; shaders blend both surfaces and ink during the existing .65s preference transition.

