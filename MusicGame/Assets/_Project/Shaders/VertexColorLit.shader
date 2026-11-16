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

        [Enum(VertexColor,0,SingleTexture,1,HeightBlend,2,Pieces,3)] _SurfaceMode("Surface", Float) = 0
        _SurfaceAtlas("Surface texture / atlas", 2D) = "white" {}
        _AtlasGrid("Atlas columns, rows", Vector) = (1,1,0,0)
        _Padding("Inset per cell (source pixels)", Float) = 8
        _Meters("Texture / piece size (metres)", Float) = 2
        [Toggle] _CustomHeightPalette("Use custom three-colour height palette", Float) = 0
        _LowHeightColor("Low altitude colour", Color) = (0.2,0.45,0.25,1)
        _MidHeightColor("Middle altitude colour", Color) = (0.8,0.65,0.4,1)
        _HighHeightColor("High altitude colour", Color) = (0.9,0.85,0.7,1)
        _TintTransitions("Colour transitions: lower centre/width, upper centre/width", Vector) = (0.25,0.5,0.75,0.5)
        _TintSmoothness("Colour blending: linear to smooth", Range(0,1)) = 1
        _TintNoise("Colour boundary irregularity", Range(0,0.4)) = 0
        _WeatherStrength("Broad weathering / stains", Range(0,1)) = 0
        _UVWarp("Organic texture distortion", Range(0,0.2)) = 0
        _PieceJitter("Piece position variation", Range(0,0.25)) = 0
        _MissingPieces("Missing piece probability", Range(0,0.8)) = 0
        _TintStrength("Height colour strength", Range(0,1)) = 1
        _BaseColor("Base / joint colour", Color) = (0.22,0.18,0.12,1)
        _LayerIndices("Low, middle, high atlas indices (bottom left first)", Vector) = (0,1,2,0)
        _Foundation("Extra foundation: enabled, cell, transition height, width", Vector) = (0,0,0.125,0.09)
        _LayerTiling("Low / middle / high texture scale multipliers", Vector) = (1,1,1,0)
        _SurfaceSeed("Stable mask pattern seed", Float) = 0
        _LowWeights("Low altitude: layer weights", Vector) = (1,0,0,0)
        _MidWeights("Middle altitude: layer weights", Vector) = (0,1,0,0)
        _HighWeights("High altitude: layer weights", Vector) = (0,0,1,0)
        _HeightStops("Low, middle, high normalized heights", Vector) = (0,0.5,1,0)
        _PatchSize("Organic patch size (metres)", Float) = 3
        _PatchAmount("Organic boundary irregularity", Range(0,1)) = 0.35
        _BlendWidth("Organic transition softness", Range(0.01,1)) = 0.25
        _PieceScale("Piece scale min, max", Vector) = (0.9,1.1,0,0)
        [Enum(LargeOnTop,0,SmallOnTop,1)] _OverlapOrder("Overlap priority", Float) = 0
        _EdgeSoftness("Piece edge softness", Range(0,0.3)) = 0.01
        _EdgeRoughness("Piece edge irregularity", Range(0,0.3)) = 0
        _RotatePieces("Random quarter turns", Range(0,1)) = 1
        _ColorVariation("Piece colour variation", Range(0,0.3)) = 0.08
        [HDR] _SurfaceEmissionColor("Surface emission colour (HDR)", Color) = (0,1,1,1)
        _SurfaceEmissionStrength("Surface emission intensity", Range(0,12)) = 0
        [Enum(PanelEdges,0,GuideRails,1,CircuitGrid,2,AlienRings,3,TextureChroma,4,TextureColor,5,TextureAndNeon,6)] _EmissionPattern("Light pattern", Float) = 0
        _EmissionScale("Light pattern size (metres)", Float) = 2
        _EmissionWidth("Light line width (fraction)", Range(0.002,0.15)) = 0.015
        _EmissionPulse("Light pulse amount", Range(0,1)) = 0
        [HideInInspector] _FinishMode("Finish Mode", Float) = 1
        [HideInInspector] _FinishSurfaceSeed("Finish SurfaceSeed", Float) = 0
        [HideInInspector] _FinishPatchAmount("Finish PatchAmount", Float) = 0.15
        [HideInInspector] _FinishBlendWidth("Finish BlendWidth", Float) = 0.1
        [HideInInspector] _FinishWeatherStrength("Finish WeatherStrength", Float) = 0.08
        [HideInInspector] _FinishLayerPatchSize("Finish LayerPatchSize", Float) = 2.4
        [HideInInspector] _FinishLayerIndices("Finish LayerIndices", Vector) = (0,1,2,0)
        [HideInInspector] _FinishLayerTiling("Finish LayerTiling", Vector) = (1,1,1,0)
        [HideInInspector] _FinishHeightStops("Finish HeightStops", Vector) = (.125,.375,.625,0)
        [HideInInspector] _FinishLowWeights("Finish LowWeights", Vector) = (1,0,0,0)
        [HideInInspector] _FinishMidWeights("Finish MidWeights", Vector) = (0,1,0,0)
        [HideInInspector] _FinishHighWeights("Finish HighWeights", Vector) = (0,0,1,0)
        [HideInInspector] _FinishFoundation("Finish Foundation", Vector) = (0,0,0,0)
        [HideInInspector] _FinishSurfaceEnabled("Finish transition enabled", Float) = 0
        [HideInInspector] _FinishAtlas("Classical finish atlas", 2D) = "white" {}
        [HideInInspector] _FinishGrid("Finish atlas grid", Vector) = (1,1,0,0)
        [HideInInspector] _FinishIndex("Finish atlas cell", Float) = 0
        [HideInInspector] _FinishMeters("Finish texture metres", Float) = 5
        [HideInInspector] _FinishPadding("Finish atlas padding", Float) = 3
        [HideInInspector] _FinishDistance("Finish path distance", Float) = 0
        [HideInInspector] _FinishBlendMeters("Finish anticipation distance", Float) = 100
        [HideInInspector] _FinishPatchSize("Finish patch size", Float) = 1.8
        [HideInInspector] _FinishPatchSoftness("Finish patch edge softness", Float) = 0.07
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)

                float _CustomHeightPalette, _TintSmoothness, _TintNoise, _WeatherStrength;
                float4 _LowHeightColor, _MidHeightColor, _HighHeightColor, _TintTransitions;
                float _UVWarp, _PieceJitter, _MissingPieces;
                float _SurfaceMode, _Padding, _Meters, _TintStrength;
                float4 _AtlasGrid, _BaseColor, _LayerIndices;
                float4 _LayerTiling, _Foundation;
                float _SurfaceSeed;
                float4 _LowWeights, _MidWeights, _HighWeights, _HeightStops;
                float _PatchSize, _PatchAmount, _BlendWidth;
                float4 _PieceScale;
                float _OverlapOrder, _EdgeSoftness, _EdgeRoughness, _RotatePieces, _ColorVariation;
                float4 _SurfaceAtlas_TexelSize;
                float4 _SurfaceEmissionColor;
                float _SurfaceEmissionStrength, _EmissionPattern, _EmissionScale, _EmissionWidth, _EmissionPulse;
                float _FinishMode, _FinishSurfaceSeed, _FinishPatchAmount, _FinishBlendWidth, _FinishWeatherStrength, _FinishLayerPatchSize;
                float4 _FinishLayerIndices, _FinishLayerTiling, _FinishHeightStops, _FinishLowWeights, _FinishMidWeights, _FinishHighWeights, _FinishFoundation;
                float _FinishSurfaceEnabled, _FinishIndex, _FinishMeters, _FinishPadding;
                float _FinishDistance, _FinishBlendMeters, _FinishPatchSize, _FinishPatchSoftness;
                float4 _FinishGrid, _FinishAtlas_TexelSize;
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

            #include "RunnerSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 surfaceUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                float3 surfaceUV : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color       = IN.color;
                OUT.uv          = IN.uv;
                OUT.surfaceUV = IN.surfaceUV;
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
                // Derivatives are evaluated before the patch-dependent branches, keeping
                // atlas mip selection stable along irregular island boundaries.
                float2 finishDx=ddx(IN.surfaceUV.xy), finishDy=ddy(IN.surfaceUV.xy);
                float finishBlend = RunnerFinishBlend(IN.surfaceUV.xy);
                half3 surfaceColor;
                UNITY_BRANCH if(finishBlend >= 1) surfaceColor = RunnerFinishSurface(IN.surfaceUV,finishDx,finishDy);
                else
                {
                    surfaceColor = RunnerSurface(IN.surfaceUV, IN.color.rgb);
                    UNITY_BRANCH if(finishBlend > 0) surfaceColor = lerp(surfaceColor,RunnerFinishSurface(IN.surfaceUV,finishDx,finishDy),finishBlend);
                }
                half3 baseColor = surfaceColor * lightAmount;

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
                half3 finalColor = baseColor + playheadCol * _PlayheadEmission * lineMask
                    + RunnerSurfaceEmission(IN.surfaceUV.xy, surfaceColor, IN.surfaceUV.z) * (1-finishBlend);
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
