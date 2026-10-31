#ifndef MUSICGAME_SKIN_VISIBILITY_PASSES_INCLUDED
#define MUSICGAME_SKIN_VISIBILITY_PASSES_INCLUDED

// Fragment entry points of "MusicGame/Avatar/Skin Visibility Lit": each one runs the skin visibility test
// and then the unmodified URP 17.3 Lit implementation of the same pass. Include AFTER the URP pass file.

#include "SkinVisibility.hlsl"

#if defined(MUSICGAME_SKIN_VISIBILITY_FORWARD)
void SkinVisibilityLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    SkinVisibilityClip(SkinVisibilityUVFromBaseUV(input.uv));
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#if defined(MUSICGAME_SKIN_VISIBILITY_GBUFFER)
GBufferFragOutput SkinVisibilityGBufferPassFragment(Varyings input)
{
    SkinVisibilityClip(SkinVisibilityUVFromBaseUV(input.uv));
    return LitGBufferPassFragment(input);
}
#endif

#if defined(MUSICGAME_SKIN_VISIBILITY_DEPTH_NORMALS)
// The pass file only interpolates uv for alpha-test/normal-map/parallax/detail variants;
// the shader defines REQUIRES_UV_INTERPOLATOR before including it so uv always exists here.
void SkinVisibilityDepthNormalsFragment(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    SkinVisibilityClip(SkinVisibilityUVFromBaseUV(input.uv));
    DepthNormalsFragment(input, outNormalWS
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#endif
