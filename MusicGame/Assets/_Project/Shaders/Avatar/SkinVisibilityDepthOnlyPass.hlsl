#ifndef MUSICGAME_SKIN_VISIBILITY_DEPTH_ONLY_PASS_INCLUDED
#define MUSICGAME_SKIN_VISIBILITY_DEPTH_ONLY_PASS_INCLUDED

// URP 17.3 DepthOnlyPass.hlsl with UV0 always interpolated (URP only does it for alpha-test variants)
// so covered skin writes no depth (depth prepass / depth priming / _CameraDepthTexture).
// Keep in sync with the URP version when the package is upgraded.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif
#include "SkinVisibility.hlsl"

struct Attributes
{
    float4 position     : POSITION;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv           : TEXCOORD0; // raw body UV0
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings SkinVisibilityDepthOnlyVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.uv = input.texcoord;
    output.positionCS = TransformObjectToHClip(input.position.xyz);
    return output;
}

half SkinVisibilityDepthOnlyFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SkinVisibilityClip(input.uv);

    #if defined(_ALPHATEST_ON)
        Alpha(SampleAlbedoAlpha(TRANSFORM_TEX(input.uv, _BaseMap), TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    return input.positionCS.z;
}

#endif
