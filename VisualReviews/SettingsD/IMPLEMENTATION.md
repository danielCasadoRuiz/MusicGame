Settings D implemented in the production MainMenu prefab.

The angular panel and audio rows reuse StyleSkinLibrary.songCards textures; UV cropping preserves the texture scale and removes the baked card edge from the large panel. A separate two-unit native border follows the same silhouette. No new raster artwork or per-style panel textures were added. The existing menu button art is reused for BACK. Language/appearance options reuse the matching card textures with a small diagonal cut.

SettingsSkinPanel follows the visible home logo style and AppearancePreference; it keeps the existing four audio Slider objects and references, language selector, appearance buttons, and close-button callbacks. Language options use a settings-only prefab, leaving other language-selector consumers unchanged. AppearanceSetup reapplies the D configuration when it rebuilds an already styled main menu.

SettingsModalBlocker owns a screen-filling raycast veil behind the panel, soft light by day and dark by night. Native capture validation checks an actual UI raycast over the underlying home utility controls: the first hit must be SettingsBackdrop. The capture fixture resolves the automatic language label using the shipped English meaning without changing the saved language preference.

Captures are real Unity Canvas/camera renders at 1920x1080, using the production prefab in Dance, Acoustic, Catchy, Electronic, Soulful and Rock, each light/dark. Build/install: SettingsDSetup.Run. Capture only: SettingsDSetup.Capture. Appearance and launch-style review overrides are restored after rendering. Audio and purchases are untouched.
