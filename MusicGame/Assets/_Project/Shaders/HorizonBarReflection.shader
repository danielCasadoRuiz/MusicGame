// Mirrored "reflection bar" geometry sitting below the Horizon World's water plane (see
// SpectrumBars3D — one real bar always has exactly one of these, same X/Z, mirrored Y, same
// mesh). Deliberately unlit and OPAQUE (must be opaque so it's captured by URP's
// _CameraOpaqueTexture, which CheapWater.shader samples+distorts to sell the reflection — a
// transparent reflection bar would be invisible to that refraction sample). Reads darker/dimmer
// than the real bar (color already scaled down in C#) and fades toward a dark "deep water" color
// the further below the water surface it sits, in WORLD space (not the bar's own local height),
// so a tall bar's deep bottom fades the same amount as a short bar's — physically consistent
// regardless of individual bar height.
Shader "MusicGame/HorizonBarReflection"
{
    Properties
    {
        _Color("Color (per-instance via MaterialPropertyBlock)", Color) = (1,1,1,1)
        _FadeDistance("Fade Distance (world units below water)", Float) = 3
        _FadeColor("Deep Fade Color", Color) = (0.02, 0.02, 0.05, 1)
        _WaterLevelWorldY("Water Level World Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float  _FadeDistance;
            float4 _FadeColor;
            float  _WaterLevelWorldY;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float depthBelowWater = max(0.0, _WaterLevelWorldY - IN.positionWS.y);
                float fadeT = saturate(depthBelowWater / max(0.01, _FadeDistance));
                float3 rgb = lerp(_Color.rgb, _FadeColor.rgb, fadeT);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
