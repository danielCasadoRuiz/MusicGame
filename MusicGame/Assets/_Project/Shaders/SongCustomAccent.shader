Shader "MusicGame/UI/SongCustomAccent"
{
    Properties
    {
        [PerRendererData] _MainTex("Button",2D)="white"{}
        _Accent("Style accent",Color)=(1,.72,.08,1)
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
    }
    SubShader
    {
        Tags{"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        Stencil{Ref[_Stencil] Comp[_StencilComp] Pass[_StencilOp] ReadMask[_StencilReadMask] WriteMask[_StencilWriteMask]}
        Cull Off Lighting Off ZWrite Off ZTest[unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask[_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct input{float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct output{float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;};
            sampler2D _MainTex;float4 _Accent,_ClipRect;
            output vert(input v){output o;o.vertex=UnityObjectToClipPos(v.vertex);o.world=v.vertex;o.color=v.color;o.uv=v.uv;return o;}
            fixed4 frag(output i):SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv);
                float left=.0208+.1458*i.uv.y,right=.8333+.1458*i.uv.y;
                float distance=min(min((i.uv.x-left)*1280,(right-i.uv.x)*1280),min((i.uv.y-.2)*320,(.8-i.uv.y)*320));
                float face=smoothstep(5,18,distance);
                float grain=dot(c.rgb,float3(.2126,.7152,.0722));
                c.rgb=lerp(c.rgb,_Accent.rgb*lerp(.68,1.04,grain),face*.97);
                c*=i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
