// Flat, alpha-blended textured quad for a Horizon World mountain silhouette LAYER — no procedural
// geometry/noise at all. The PNG's alpha channel IS the silhouette shape; _Tint (already baked
// with brightness + this layer's own haze blend on the CPU side, see HorizonMountainLayers.cs)
// colors it. Two of these (far/near) stacked in front of the sky give the layered-depth mountain
// look described in the Horizon World visual spec.
Shader "MusicGame/HorizonMountainLayer"
{
    Properties
    {
        _MainTex("Silhouette (alpha = shape)", 2D) = "white" {}
        _Tint("Tint (brightness + haze already applied)", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float  _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return half4(tex.rgb * _Tint.rgb, tex.a * _Tint.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
