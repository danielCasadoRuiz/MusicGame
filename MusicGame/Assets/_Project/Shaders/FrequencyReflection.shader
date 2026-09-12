// Flat fake "reflected light" fan lying on the water surface — NOT a mirrored copy of the
// horizon's geometry. Vertex color (rgb = that column's current color, a = radial fade shape ×
// column intensity × global opacity, all computed in FrequencyBackground.cs) is passed straight
// through; the only thing this shader adds is a cheap analytic (texture-free) shimmer so it
// reads as "light on moving water" rather than a flat static decal.
Shader "MusicGame/FrequencyReflection"
{
    Properties
    {
        _Distortion("Distortion Strength", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual  // explicit (never Always) — opaque road/player geometry always occludes this
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
            float _Distortion;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float n = sin(IN.positionWS.x * 0.7 + _Time.y * 1.3)
                        * sin(IN.positionWS.z * 0.9 - _Time.y * 0.8);
                float shimmer = saturate(1.0 + n * _Distortion);
                return float4(IN.color.rgb, IN.color.a * shimmer);
            }
            ENDHLSL
        }
    }
}
