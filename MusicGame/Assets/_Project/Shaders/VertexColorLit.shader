// URP shader for the procedural music-terrain: main-light diffuse + shadows (cast AND
// receive), multiplied by the per-vertex musical colour (green/yellow/red from
// MusicWorldManager). _AmbientFloor keeps shadowed areas from going pure black so the musical
// gradient stays readable there too (see MusicRunnerLevelConfig.terrainAmbientFloor).
//
// Playhead scanline: a transversal glowing band tracking the current song position, added on
// top of the lit vertex color (MusicWorldManager.UpdatePlayheadGlobals/MusicRunnerLevelConfig's
// own doc has the full design). _StartMusicDistance/_EndMusicDistance are PER-CHUNK (set via
// MaterialPropertyBlock, one ground mesh instance at a time — never a second Material), every
// other _Playhead*/_FreqTex value is GLOBAL (Shader.SetGlobalX, shared by whichever chunk(s)
// are currently rendered, no per-chunk CPU work at all).
Shader "MusicGame/VertexColorLit"
{
    Properties
    {
        _AmbientFloor("Ambient Floor", Range(0,1)) = 0.35
        _StartMusicDistance("Start Music Distance (per-chunk)", Float) = 0
        _EndMusicDistance("End Music Distance (per-chunk)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half  _AmbientFloor;
                float _StartMusicDistance;
                float _EndMusicDistance;
            CBUFFER_END

            // Globals — shared across every chunk, pushed once per frame by
            // MusicWorldManager.UpdatePlayheadGlobals (never touched via Properties/Inspector).
            float _PlayheadMusicDistance;
            float _PlayheadEnabled;
            float _PlayheadLineWidth;
            float _PlayheadEmission;
            float _PlayheadUseFreqColors;
            half4 _PlayheadSingleColor;
            TEXTURE2D(_FreqTex);
            SAMPLER(sampler_FreqTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color       = IN.color;
                OUT.uv          = IN.uv;
                // Gameplay Fog (see GameplayFogController) — RenderSettings.fog driven, NEVER
                // touches the separate Horizon World (its shaders don't sample fog at all).
                OUT.fogFactor   = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 normalWS = normalize(IN.normalWS);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half lit   = ndotl * mainLight.shadowAttenuation;

                // Never lets the musical vertex colour drop below _AmbientFloor of itself,
                // regardless of shadow/angle — keeps green/yellow/red legible everywhere.
                half lightAmount = max(_AmbientFloor, lit);
                half3 baseColor = IN.color.rgb * lightAmount;

                // ── Playhead scanline ────────────────────────────────────────────────────
                // Deliberately NOT saturated: uv.y is always within [0,1] for this chunk, so if
                // the true playhead sits outside THIS chunk's [start,end], playhead01 lands well
                // outside [0,1] too and the distance/smoothstep below naturally zeroes the mask —
                // no branch, no "is this the active chunk" check needed.
                float chunkLength = max(_EndMusicDistance - _StartMusicDistance, 0.0001);
                float playhead01  = (_PlayheadMusicDistance - _StartMusicDistance) / chunkLength;
                float halfWidthUV = (_PlayheadLineWidth / chunkLength) * 0.5;
                float d = abs(IN.uv.y - playhead01);
                float lineMask = (1.0 - smoothstep(halfWidthUV * 0.6, halfWidthUV, d)) * _PlayheadEnabled;

                half3 freqColor   = SAMPLE_TEXTURE2D(_FreqTex, sampler_FreqTex, float2(IN.uv.x, 0.5)).rgb;
                half3 playheadCol = lerp(_PlayheadSingleColor.rgb, freqColor, _PlayheadUseFreqColors);

                // Additive HDR glow on top of the lit terrain — _PlayheadEmission > 1 pushes it
                // past 1.0 so Bloom (URP Volume) picks it up; with no Bloom it's just a brighter
                // flat-colored band, still fully correct, just not glowing.
                half3 finalColor = baseColor + playheadCol * _PlayheadEmission * lineMask;
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
