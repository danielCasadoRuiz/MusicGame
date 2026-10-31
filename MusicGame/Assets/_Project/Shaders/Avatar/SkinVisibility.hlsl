#ifndef MUSICGAME_SKIN_VISIBILITY_INCLUDED
#define MUSICGAME_SKIN_VISIBILITY_INCLUDED

// Per-avatar skin visibility, composed at runtime by BodyVisibilityMask from the body-coverage regions of
// every equipped segmented garment: white = covered skin, black = visible (max-combined, white wins).
// Sampled in the BODY's own UV0 (the mask is authored on the body UV layout, not on _BaseMap's tiling).
//
// Assigned per renderer through a MaterialPropertyBlock, so the material stays shared between avatars.
// Default "black" keeps the whole body visible when no mask has been assigned.
//
// Textures live outside UnityPerMaterial and the threshold is a compile-time constant, so this include
// leaves the material SRP Batcher compatible.
TEXTURE2D(_SkinVisibilityMask);
SAMPLER(sampler_SkinVisibilityMask);

// A texel is visible skin when its mask value is < this threshold. Masks are binary (0 / 1); bilinear
// filtering puts the cut half a texel inside the boundary between a visible and a hidden texel.
#define SKIN_VISIBILITY_THRESHOLD 0.5

// _BaseMap tiling/offset is applied to Varyings.uv by the URP passes; the mask needs the raw UV0 back.
// (URP's own UNDO_TRANSFORM_TEX is a no-op outside its debug-display variants, so it cannot be used here.)
float2 SkinVisibilityUVFromBaseUV(float2 baseUV)
{
    return (baseUV - _BaseMap_ST.zw) / _BaseMap_ST.xy;
}

// Discards the fragment when its skin is covered. Called first in every pass that writes color, depth,
// normals or shadows, so a covered region never shades, occludes or casts.
void SkinVisibilityClip(float2 bodyUV)
{
    half visibility = SAMPLE_TEXTURE2D_LOD(_SkinVisibilityMask, sampler_SkinVisibilityMask, bodyUV, 0).r;
    clip(SKIN_VISIBILITY_THRESHOLD - visibility);
}

#endif
