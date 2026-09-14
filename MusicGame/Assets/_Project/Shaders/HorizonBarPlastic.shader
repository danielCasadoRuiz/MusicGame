// "Milky white plastic shell lit from within by a saturated LED" look for the Horizon World's
// spectrum bars — a hand-written simplified Lit shader (real diffuse + specular from the main
// light, so the cube still reads as a genuine 3D PBR-ish object), NOT stock URP Lit: stock Lit
// has no Fresnel/rim/transmission hooks, and this look specifically needs those to sell "light
// escaping a translucent shell" without real Subsurface Scattering.
//
// KEY DESIGN — LEDColor is NEVER the BaseColor:
//   BaseColor  = lerp(_PlasticColor, _LEDColor, _PlasticTintStrength)   — mostly off-white/milky,
//                only lightly tinted by the LED hue, and lit normally (diffuse + specular).
//   Rim        = _LEDColor * fresnel term * _RimStrength                — LED "escaping" at edges.
//   Transmission = _LEDColor * back-facing term * _TransmissionStrength — fake SSS: a soft LED
//                glow on the side FACING AWAY from the main light, as if it shines through the
//                shell (classic stylized wrap/back-light trick, no real SSS).
//   Emission  = _LEDColor * _EmissionAmount                             — the ACTUAL glow driving
//                Bloom, kept as a SEPARATE additive term so cranking it never desaturates
//                BaseColor toward white — the whole reason this shader exists instead of the old
//                "BaseColor = LEDColor, Emission = BaseColor * bigNumber" approach.
//
// _LEDColor/_EmissionAmount are per-instance (set via MaterialPropertyBlock by SpectrumBars3D —
// never a Material instance per bar). Every other property here is shared/static art-direction.
Shader "MusicGame/HorizonBarPlastic"
{
    Properties
    {
        _PlasticColor("Plastic Base Color", Color) = (0.85, 0.85, 0.88, 1)
        _PlasticTintStrength("Plastic Tint Strength", Range(0,1)) = 0.18
        _Smoothness("Smoothness", Range(0,1)) = 0.45
        _AmbientFloor("Ambient Floor", Range(0,1)) = 0.3
        _RimPower("Rim Power", Float) = 2.5
        _RimStrength("Rim Strength", Range(0,2)) = 0.5
        _TransmissionStrength("Transmission Strength", Range(0,2)) = 0.4
        _LEDColor("LED Color (per-instance)", Color) = (1,1,1,1)
        _EmissionAmount("Emission Amount (per-instance)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _PlasticColor;
            float  _PlasticTintStrength;
            float  _Smoothness;
            float  _AmbientFloor;
            float  _RimPower;
            float  _RimStrength;
            float  _TransmissionStrength;
            float4 _LEDColor;
            float  _EmissionAmount;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = normalize(IN.normalWS);
                half3 viewDir  = normalize(GetWorldSpaceViewDir(IN.positionWS));
                Light mainLight = GetMainLight();

                half NdotL = dot(normalWS, mainLight.direction);

                // Half-Lambert "wrap" diffuse — softer falloff than a hard clamp, reads slightly
                // translucent instead of a flat-shaded hard terminator.
                half wrapNdotL = saturate(NdotL * 0.5 + 0.5);
                half lightAmount = max(_AmbientFloor, wrapNdotL);

                half3 baseColor = lerp(_PlasticColor.rgb, _LEDColor.rgb, _PlasticTintStrength);
                half3 litColor  = baseColor * lightAmount * mainLight.color;

                // Specular — kept WHITE/light-tinted (dielectric plastic highlight, not colored by
                // the LED) so it still reads as a real lit surface, not a glowing decal.
                half3 halfDir  = normalize(viewDir + mainLight.direction);
                half specPower = lerp(8.0, 128.0, _Smoothness);
                half spec      = pow(saturate(dot(normalWS, halfDir)), specPower) * _Smoothness;
                half3 specular = mainLight.color * spec;

                // Rim — LED escaping at grazing angles.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _RimPower);
                half3 rim = _LEDColor.rgb * fresnel * _RimStrength;

                // Fake transmission — a soft LED glow on the side FACING AWAY from the main light,
                // as if it shines through the milky shell. No real SSS, just a back-facing term.
                half backLight = pow(saturate(-NdotL), 1.5);
                half3 transmission = _LEDColor.rgb * backLight * _TransmissionStrength;

                // Emission — the ONLY term that actually drives Bloom, deliberately separate from
                // BaseColor so raising it never washes the surface color toward white.
                half3 emission = _LEDColor.rgb * _EmissionAmount;

                half3 rgb = litColor + specular + rim + transmission + emission;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
