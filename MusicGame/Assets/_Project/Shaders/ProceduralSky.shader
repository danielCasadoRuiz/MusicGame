// Procedural gradient skybox for the Horizon World — vertical navy/purple/pink/coral gradient
// (deformed by cheap low-frequency 2D noise so it doesn't read as a flat linear ramp), an
// independent horizon glow band, and a simple procedural sun (disc + soft outer glow). Assigned
// as a per-camera Skybox override (ProceduralSkyController) on the Horizon Camera only — never
// touches RenderSettings.skybox or the gameplay Main Camera.
//
// Standard "Skybox/Procedural"-style technique: Unity draws the skybox using its own cube mesh
// with the camera at the origin, so treating the (unit-cube) object-space position as a view
// direction is exactly right — no camera/view matrices needed here at all.
Shader "MusicGame/ProceduralSky"
{
    Properties
    {
        _ZenithColor   ("Zenith Color",   Color) = (0.05, 0.04, 0.14, 1)
        _UpperColor    ("Upper Color",    Color) = (0.20, 0.08, 0.38, 1)
        _LowerColor    ("Lower Color",    Color) = (0.75, 0.25, 0.45, 1)
        _HorizonColor  ("Horizon Color",  Color) = (0.95, 0.55, 0.35, 1)
        _BelowColor    ("Below Horizon Color (seen through / refracted by the water)", Color) = (0.95, 0.55, 0.35, 1)
        // Music-varied gradient, blended in by distance from the sun (higher / sides / behind).
        _ZenithFar     ("Zenith Color (far from sun)",  Color) = (0.05, 0.04, 0.14, 1)
        _UpperFar      ("Upper Color (far from sun)",   Color) = (0.20, 0.08, 0.38, 1)
        _LowerFar      ("Lower Color (far from sun)",   Color) = (0.75, 0.25, 0.45, 1)
        _HorizonFar    ("Horizon Color (far from sun)", Color) = (0.95, 0.55, 0.35, 1)
        _FarNearSun    ("Variation right at the sun", Range(0,1)) = 0.12
        _SunKeepRadius ("Fixed-colour radius around the sun (radians)", Float) = 0.35
        _GradientStart ("Gradient Start Height", Float) = -1
        _GradientEnd ("Gradient End Height", Float) = 1
        _NoiseScale    ("Noise Scale", Float) = 1.5
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.25
        _GlowColor     ("Horizon Glow Color", Color) = (1, 0.6, 0.5, 1)
        _GlowIntensity ("Horizon Glow Intensity", Float) = 1
        _SunColor      ("Sun Color", Color) = (1, 0.85, 0.7, 1)
        _SunDirection  ("Sun Direction", Vector) = (0, 0.2, 1, 0)
        _SunSize       ("Sun Size", Range(0.001, 0.2)) = 0.03
        _SunGlowSize   ("Sun Glow Size", Range(0,1)) = 0.25
        _SunGlowIntensity ("Sun Glow Intensity", Float) = 1.2
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 viewDir : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
            float4 _ZenithColor, _UpperColor, _LowerColor, _HorizonColor, _GlowColor, _SunColor, _SunDirection, _BelowColor;
            float4 _ZenithFar, _UpperFar, _LowerFar, _HorizonFar;
            float _FarNearSun, _SunKeepRadius;
            float _GradientStart, _GradientEnd;
            float _NoiseScale, _NoiseStrength, _GlowIntensity, _SunSize, _SunGlowSize, _SunGlowIntensity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.viewDir = IN.positionOS.xyz; // skybox cube is centered on the camera — direction, not a world position
                return OUT;
            }

            float Hash21(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.viewDir);
                float h = dir.y; // -1 (down) .. 1 (up)

                float n = (ValueNoise(dir.xz * _NoiseScale + h * 2.0) - 0.5) * _NoiseStrength;
                float t = saturate((h - _GradientStart) / max(0.01, _GradientEnd - _GradientStart) + n);

                // Smooth 4-colour gradient: smoothstep inside each segment so the slope never jumps
                // at a colour stop (linear segments read as hard bands, especially upper→zenith).
                float s1 = smoothstep(0.0, 0.33, t);
                float s2 = smoothstep(0.33, 0.66, t);
                float s3 = smoothstep(0.66, 1.0, t);
                float3 col = lerp(lerp(lerp(_HorizonColor.rgb, _LowerColor.rgb, s1), _UpperColor.rgb, s2), _ZenithColor.rgb, s3);
                float3 far = lerp(lerp(lerp(_HorizonFar.rgb,   _LowerFar.rgb,   s1), _UpperFar.rgb,   s2), _ZenithFar.rgb,   s3);
                // Only a patch around the sun keeps the style's fixed colours (_SunKeepRadius, radians);
                // everywhere else — including the sky straight ahead above it — follows the music.
                float sunAngle = acos(clamp(dot(dir, normalize(_SunDirection.xyz)), -1.0, 1.0));
                float keep = 1.0 - smoothstep(_SunKeepRadius * 0.5, _SunKeepRadius * 2.0, sunAngle);
                col = lerp(far, col, keep * (1.0 - _FarNearSun));
                // Below the horizon the sky is only ever seen through the water: fade to the water colour.
                col = lerp(col, _BelowColor.rgb, saturate(-h * 10.0));

                // Horizon glow — brightest right at the horizon line (h == 0), fading with |h|.
                float glow = exp(-abs(h) * 6.0) * _GlowIntensity;
                col += _GlowColor.rgb * glow;

                // Procedural sun — small hard disc plus a much wider soft falloff glow.
                float3 sunDir = normalize(_SunDirection.xyz);
                float sunDot = dot(dir, sunDir);
                float sunDisc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, sunDot);
                float glowExp = 1.0 / max(_SunGlowSize, 0.001);
                float sunGlow = pow(saturate(sunDot), glowExp) * _SunGlowIntensity;
                col += _SunColor.rgb * (sunDisc + sunGlow);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
