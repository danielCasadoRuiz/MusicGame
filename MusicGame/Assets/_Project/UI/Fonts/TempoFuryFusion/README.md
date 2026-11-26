# Tempo Fury Fusion

Original display lettering reinterpreted from the approved A/B concepts, shifted slightly toward B's chisel gestures while keeping A's compact proportions. This is a new glyph set, not a cleanup of TempoFuryBrush.

`TempoFuryFusion-Regular.ttf` is the editable outline font. `TempoFuryFusion SDF.asset` is the prebuilt TextMesh Pro font. The display face maps lowercase input to its matching uppercase design, as the previous display font did.

Coverage: 230 mapped characters / 148 glyphs, including all characters in the previous font, A–Z, digits, punctuation, grave/acute/circumflex/tilde/dieresis/ring/cedilla Latin forms, Catalan middle dot, inverted punctuation, curly quotes, dash variants and currency. Other scripts use the existing LiberationSans fallback. The font contains optical GPOS kerning; the TMP asset has the corresponding glyph-pair adjustments. Do not apply synthetic italics: the lean is drawn into the outlines.

Source master: `Source/GlyphMaster.png` (AI-generated original black-on-white glyph artwork). Accents are composed from the master accent forms. `Source/Metrics.json` records coverage and spacing. Rebuild outlines with `tools/typography/build_fusion.py` from the outer project root. Install and validate in Unity with **MusicGame → UI → Fonts → Install Tempo Fury Fusion**.

The old font remains as source history. Production UI references migrate to Fusion through the shared skin library and existing title components; the selected song headline also uses Fusion. Existing text colours, tier sizes and preview glow behaviour are retained.
