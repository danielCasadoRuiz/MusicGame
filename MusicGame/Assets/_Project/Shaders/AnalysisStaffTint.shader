Shader "MusicGame/UI/AnalysisStaffTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Grayscale sprite", 2D) = "white" {}
        _Tint ("Midtone tint", Color) = (0.7,0.6,0.8,1)
        _Highlight ("Ivory highlights", Color) = (0.953,0.918,0.827,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            fixed4 _Tint, _Highlight;
            float4 _ClipRect;
            v2f vert(appdata v)
            { v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 sprite=tex2D(_MainTex,i.uv);
                // Tint midtones, while bright notes and the scan retain their ivory highlights.
                float light=smoothstep(.80,.98,sprite.r);
                fixed3 rgb=lerp(_Tint.rgb,_Highlight.rgb,light)*lerp(.5,1,sprite.r);
                fixed alpha=sprite.a*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return fixed4(rgb*i.color.rgb,alpha);
            }
            ENDCG
        }
    }
}
