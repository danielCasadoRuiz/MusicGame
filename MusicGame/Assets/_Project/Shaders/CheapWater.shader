// Stylized water surface for the Horizon World: dark, near-flat, TWO independently tiling/
// scrolling normal maps combined for a subtle microwave ripple (falls back to a flat default
// normal if no textures are assigned — see HorizonWater.cs), a Blinn-Phong specular highlight +
// Fresnel rim from the main directional light, and REFRACTION of whatever opaque geometry sits
// behind/below it (URP's _CameraOpaqueTexture) — this is what makes the mirrored reflection bars
// (SpectrumBars3D) sitting just below the water plane read as a wobbly, distorted reflection
// instead of a perfect duplicate. No RenderTexture/second camera involved.
Shader "MusicGame/CheapWater"
{
    Properties
    {
        _BaseColor("Water Color", Color) = (0.015, 0.03, 0.07, 0.92)
        _NormalMapA("Normal Map A", 2D) = "bump" {}
        _NormalMapB("Normal Map B", 2D) = "bump" {}
        _TilingA("Tiling A", Float) = 6
        _ScrollA("Scroll A", Vector) = (0.035, 0.015, 0, 0)
        _TilingB("Tiling B", Float) = 17
        _ScrollB("Scroll B", Vector) = (-0.012, 0.028, 0, 0)
        _NormalStrength("Normal Strength", Range(0,2)) = 0.4
        _FresnelColor("Fresnel Color", Color) = (0.12, 0.28, 0.35, 1)
        _FresnelPower("Fresnel Power", Float) = 5
        _SpecularPower("Specular Power", Range(4,256)) = 48
        _SpecularIntensity("Specular Intensity", Float) = 0.6
        _HorizonTint("Horizon Tint Color", Color) = (0.9, 0.5, 0.4, 1)
        _HorizonTintStrength("Horizon Tint Strength", Range(0,1)) = 0.25
        _RefractionStrength("Refraction Strength", Range(0,0.2)) = 0.08
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float2 uv           : TEXCOORD4;
            };

            TEXTURE2D(_NormalMapA); SAMPLER(sampler_NormalMapA);
            TEXTURE2D(_NormalMapB); SAMPLER(sampler_NormalMapB);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float  _TilingA; float2 _ScrollA;
            float  _TilingB; float2 _ScrollB;
            float  _NormalStrength;
            float4 _FresnelColor;
            float  _FresnelPower;
            float  _SpecularPower;
            float  _SpecularIntensity;
            float4 _HorizonTint;
            float  _HorizonTintStrength;
            float  _RefractionStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS   = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.bitangentWS = cross(OUT.normalWS, OUT.tangentWS) * IN.tangentOS.w;
                OUT.uv          = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uvA = IN.uv * _TilingA + _ScrollA * _Time.y;
                float2 uvB = IN.uv * _TilingB + _ScrollB * _Time.y;
                float3 nTanA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA, sampler_NormalMapA, uvA));
                float3 nTanB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapB, sampler_NormalMapB, uvB));
                float3 nTan  = normalize(float3((nTanA.xy + nTanB.xy) * _NormalStrength, 1.0));

                float3x3 tbn = float3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                float3 normalWS = normalize(mul(nTan, tbn));

                float3 viewDir  = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float  fres     = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);

                Light mainLight = GetMainLight();
                float3 halfDir  = normalize(viewDir + mainLight.direction);
                float  spec     = pow(saturate(dot(normalWS, halfDir)), _SpecularPower) * _SpecularIntensity;

                float horizonFacing = 1.0 - saturate(abs(viewDir.y) * 4.0);
                float3 horizonTint  = _HorizonTint.rgb * horizonFacing * _HorizonTintStrength;

                // ── Refraction: distorts whatever opaque geometry (the mirrored reflection bars,
                // see SpectrumBars3D) is sitting behind/below this surface — offset the screen UV
                // by the SAME tangent-space normal wobble already driving the ripple, so the
                // distortion always reads as "this water surface" doing it, never a separate effect.
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float2 refractedUV = screenUV + nTan.xy * _RefractionStrength;
                float3 sceneColor = SampleSceneColor(refractedUV);

                float3 rgb = _BaseColor.rgb * 0.4 + sceneColor * 0.6
                           + _FresnelColor.rgb * fres
                           + mainLight.color * spec
                           + horizonTint;

                return float4(rgb, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
