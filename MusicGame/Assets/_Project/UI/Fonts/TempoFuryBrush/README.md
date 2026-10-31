# Tempo Fury Brush — Unity

Font based on the approved v12 artwork and pair spacing. White artwork; use the normal component Color property to tint it.

## Assets

- **TempoFuryBrush-Regular.ttf**: TrueType font, assign to the Font field of a normal `UnityEngine.UI.Text` or legacy TextMesh. Also usable as the source for Unity font assets.
- **TempoFuryBrush Bitmap.asset**: native TMP Font Asset containing the exact approved bitmap silhouettes, bearings, accents and pair adjustments. Assign it directly to TextMeshProUGUI or TextMeshPro's Font Asset field. Best match to the HTML preview at the original display resolution.
- **TempoFuryBrush SDF.asset**: native, prepopulated scalable TMP Font Asset, derived from the same contours. Use for variable display sizes and TMP SDF material effects. Very fine raster edge detail can differ from the exact bitmap.
- **TempoFuryBrush Preview.prefab / .png**: visual examples of the three options, Catalan middle dot, cedilla, accents and countdown.
- **Source/**: original packed bitmap and all explicit glyph / kerning metrics. No runtime Python or custom text renderer is required.

## Usage

Assign the TTF to a regular Text or assign either TMP asset to a normal TMP component. Keep Font Style Normal and enable **Font Features → Kerning (`kern`)** in TMP. Use Character Spacing to compact or spread text. Negative spacing is allowed. Tint through Color; set it to white to preserve the source.

The family is uppercase display lettering. Lowercase input maps to the corresponding uppercase drawing, including accented letters. The source has 66 drawn characters and 113 mapped Unicode codepoints (including spaces and aliases). It is not a full Unicode typeface. TMP SDF has the project's existing Liberation Sans asset as a fallback for unsupported characters.

The TTF includes both `kern` and OpenType GPOS pair spacing tables. Unity's legacy UI Text does **not** apply the same pair kerning as TMP (verified with the CA pair). The normal Text component can display the font, but **TMP is the option that reproduces the approved spacing**. The bitmap / SDF TMP assets carry the explicit approved pair table, so do not generate a replacement asset with generic default metrics.

## Validation

The editor builder imports the font, creates native materials / atlas subassets, checks mapped characters and checks actual TMP origins against the pair metrics for CA, CO, ÇÓ, PA, A!, L· and ·L. `ValidationReport.json` records the results. A preview is rendered by Unity rather than simulated in HTML.

Menu: **MusicGame → UI → Fonts → Build Tempo Fury Brush**. Existing complete assets are reused; the command does not replace fonts assigned to existing game screens.
