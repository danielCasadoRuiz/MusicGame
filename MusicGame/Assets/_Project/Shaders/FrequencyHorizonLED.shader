// Reproduces the old 2D LED-equalizer look (per-block green/yellow/red, small black border,
// off blocks transparent), just curved into a static arc mesh instead of flat screen-space bars.
// Geometry never deforms — every LED cell's position is fixed forever (see
// FrequencyBackground.RebuildHorizonTopology); only vertex COLOR (rgb = that row's tier color
// possibly with emission already summed in, a = 1 lit / 0 off) changes per frame. One shader,
// one material, one mesh.
Shader "MusicGame/FrequencyHorizonLED"
{
    Properties
    {
        _BorderSize("Border Size (0..0.5, fraction of cell)", Range(0, 0.45)) = 0.08
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
                float2 uv         : TEXCOORD0; // local 0..1 within THIS cell only
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            float _BorderSize;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                bool border = IN.uv.x < _BorderSize || IN.uv.x > 1.0 - _BorderSize
                           || IN.uv.y < _BorderSize || IN.uv.y > 1.0 - _BorderSize;
                if (border) return float4(0, 0, 0, 0.85);
                return float4(IN.color.rgb, IN.color.a);
            }
            ENDHLSL
        }
    }
}
