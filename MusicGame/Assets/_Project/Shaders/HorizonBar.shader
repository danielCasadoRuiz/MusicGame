// Unlit, opaque, vertex-color-only shader for the Horizon World's volumetric spectrum bars
// (SpectrumBars3D.cs). Color already carries the amplitude-gradient result + emission multiplier
// + a cheap per-face brightness bake (fake bevel) — the shader just outputs it untouched. Opaque
// (not transparent) so it correctly writes depth and occludes the reflection/water/sky behind it
// within the Horizon Camera's own render.
Shader "MusicGame/HorizonBar"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off // 4 faces per bar with mixed winding — simplest to just draw both sides, negligible cost at this vertex count

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
                float4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(IN.color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
