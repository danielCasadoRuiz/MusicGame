Shader "MusicGame/UI/RectangularStyleButton"
{
    Properties
    {
        [PerRendererData] _MainTex ("Night", 2D) = "white" {}
        _DayTex ("Day", 2D) = "white" {}
        _Light ("Day weight", Range(0,1)) = 0
        _OtherTex ("Selected art", 2D) = "white" {}
        _StateMix ("Selected art weight", Range(0,1)) = 0
        _NativeUV ("Original button mapping",Range(0,1))=0
        _Emphasis ("Selected filter contrast", Range(0,1)) = 0
        _BodyTint ("Filter body tint", Color) = (1,1,1,1)
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
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
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 aspect:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; float aspect:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex, _DayTex, _OtherTex;
            fixed4 _Color;
            float _Light;
            float _StateMix;
            float _NativeUV;
            float _Emphasis;
            fixed4 _BodyTint;
            float4 _ClipRect;
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.aspect=v.aspect.x; o.color=v.color*_Color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float cap=.16;
                float edge=min(.3,cap*(1040.0/192.0)/max(1,i.aspect));
                float x=i.uv.x;
                x=x<edge?x/edge*cap:x>1-edge?1-(1-x)/edge*cap:cap+(x-edge)/(1-2*edge)*(1-2*cap);
                float2 source=float2((64+112*i.uv.y+1040*x)/1280.0,(64+192*i.uv.y)/320.0);
                source=lerp(source,i.uv,_NativeUV);
                fixed4 c=lerp(tex2D(_MainTex,source),tex2D(_OtherTex,source),_StateMix)*i.color;
                float body=smoothstep(.05,.13,i.uv.y)*smoothstep(.05,.13,1-i.uv.y)*smoothstep(.04,.16,x)*smoothstep(.04,.16,1-x);
                c.rgb=lerp(c.rgb,c.rgb*_BodyTint.rgb,body*_Emphasis);
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
