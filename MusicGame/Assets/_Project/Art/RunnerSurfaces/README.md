# Runner surfaces â€” prototype

Open `RunnerSurfaceExamples.unity` or use **MusicGame > Runner > Build Surface Examples** to rebuild the presets and preview images. Assign one preset to **terrainSurfaceMaterial** under World Mesh â€” Surface in the runner level generation configuration. Leave empty for existing behaviour. The runner makes one owned material for the run, not one per surface layer.

## Modes
- VertexColor: original vertex-colour rendering, no surface atlas reads.
- SingleTexture: one atlas cell, tint strength 0 for natural colour or >0 for musical height colour. Set grid to 1Ã—1 for an ordinary full-image texture.
- HeightBlend: three chosen atlas cells, continuous organic masks and low/middle/high weight vectors. Weights refer to those three selected cells, not RGB colours. Identical weights at all three heights give an altitude-independent mixture. Three surface texture reads per pixel.
- Pieces: any rectangular atlas grid; deterministic selection per world-sized cell. Quarter turns, colour variation, scale range and large/small priority. Searches nine procedural footprints but samples only one surface texture. Optional softer/rough edges blend into the base colour. This is visually layered coverage, not displaced geometry or a packing solver; neighbours are not relocated.

Atlas indices start at the bottom left and proceed along each row. SurfaceAtlas uses grass=0, earth=1, paving=2, sand=3. PieceAtlas is an 8Ã—8 prototype. Source images are generated prototypes at their actual delivered resolution, not claimed 4K production assets. Set the grid to match the actual asset. Cell padding is an inset in source pixels; production atlases should have authored edge extrusion and compatible borders. Insetting alone does not guarantee seamless organic repetition or eliminate all distant mip bleeding.

Surface coordinates use lateral metres and accumulated path distance, plus the actual normalized musical terrain height. They do not reset when the ground window moves. Longitudinal distance follows the path baseline, not displaced surface arc length; steep slopes can stretch the texture. Variable path width can also distort mapping. Existing scanline UVs remain separate.

No transparent pass, extra surface renderer, normal map, displacement or texture-mask asset is added. The existing forward and shadow passes remain. Piece overlap has more arithmetic than regular tiling and should be profiled on target hardware. Atlas memory and compression should be selected per platform; example import deliberately uses uncompressed textures for visual review.

The five artistic options are represented by original vertex colours, single texture with tint 0, single texture with tint >0, height blend with tint >0, and height blend with tint 0. Base colour fills gaps between pieces; it is not a replacement for an organic coverage mask.

## Custom palette and weathering
Enable **Use custom three-colour height palette** to choose low/middle/high colours with the colour pickers, independent of texture blending. Otherwise existing musical vertex colours remain in use. **Height colour strength** controls the tint for textured modes; vertex-colour mode displays the palette directly.

**Colour transitions** contains lower transition centre/width and upper centre/width (normalized terrain height 0–1). Widths control soft or narrow transitions. **Colour blending: linear to smooth** changes the interpolation shape. **Colour boundary irregularity** adds continuous spatial variation controlled by organic patch size. These controls add no texture reads.

Optional broad weathering adds procedural stains. Organic texture distortion breaks ruler-straight texture lines. Piece position variation, missing-piece probability and edge irregularity produce rougher paving; gaps reveal the base colour, not a second sampled ground texture. These effects add arithmetic and are disabled by default.

Nine additional examples (12–20) explore ruins, warm mosaic, storm paving, overgrown ruins, desert remains, moss/ochre/chalk, twilight, autumn, and narrow colour bands.

## Space surfaces and bloom
SpaceAtlas contains alien plates=0, white station=1, industrial deck=2, cyan hex deck=3. Presets 21–26 use a single atlas cell and optional procedural HDR lighting (panel edges, guide rails, circuit grid, alien rings). TextureChroma can instead use saturated albedo details as an approximate emission mask, without another texture sample. This does not provide a separately authored emission mask.

Emission intensity, HDR colour, line width, size and pulse are independent controls. Intensity zero skips the emission pattern. Surface emission is added after diffuse lighting and before fog. It does not cast light onto surrounding geometry. The example scene includes PreviewBloom and enables camera post processing for space captures. Actual gameplay needs HDR/post processing and an enabled Bloom volume to show the halo; the shader alone emits brightness, not bloom. Bloom is a screen effect with an additional GPU cost. PreviewBloom is only saved in the example scene; no gameplay lighting setting is overwritten.

## Path families
Use MusicGame > Runner > Build Path Families after building the surface examples. Generates three interpretations per existing root preset: continuous, reclaimed/weathered, and expressive. All share the same curved 27 m preview mesh for comparison. Families/Galeria.html groups the three views by source style. Materials reuse existing atlases; no new source textures are added. These are alternative presets, not simultaneous layers or materials on a gameplay mesh. Preview geometry is separate from the live runner generator.
