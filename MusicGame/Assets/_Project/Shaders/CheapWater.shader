// Cheap fake-water surface for the Frequency Background: single flat unlit plane, dark tint,
// analytic (texture-free) scrolling ripple via two sine fields, and a view-angle fresnel rim for
// a "wet/reflective" read — no normal maps, no planar reflection, no RenderTexture, no realtime
// reflection probe. Actual reflected bars are separate faded geometry (see FrequencyBackground.cs)
// sitting just under this plane; this shader only needs to look dark/glossy enough to sell it.
Shader "MusicGame/CheapWater"
{
    Properties
    {
        _BaseColor("Water Color", Color) = (0.015, 0.03, 0.07, 0.92)
        _FresnelColor("Fresnel Color", Color) = (0.12, 0.28, 0.35, 1)
        _FresnelPower("Fresnel Power", Float) = 5
        _RippleScale("Ripple Scale (Water Noise Scale)", Float) = 10
        _RippleSpeed("Ripple Speed", Float) = 0.08
        _RippleStrength("Ripple Strength (Water Wave Strength)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Back
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _FresnelColor;
            float  _FresnelPower;
            float  _RippleScale;
            float  _RippleSpeed;
            float  _RippleStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _RippleSpeed;
                float2 uv1 = IN.uv * _RippleScale + float2(t, t * 0.6);
                float2 uv2 = IN.uv * _RippleScale * 1.7 - float2(t * 0.7, t * 0.35);
                float ripple = sin(uv1.x * 6.283) * sin(uv1.y * 6.283)
                             + sin(uv2.x * 6.283 + 1.7) * sin(uv2.y * 6.283);
                ripple *= _RippleStrength;

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fres = pow(1.0 - saturate(dot(normalize(IN.normalWS), viewDir)), _FresnelPower);

                float3 rgb = _BaseColor.rgb + ripple + _FresnelColor.rgb * fres;
                return float4(rgb, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
