Shader "MusicGame/UI/SongSelectionRim"
{
    Properties
    {
        [PerRendererData] _MainTex("UI texture",2D)="white"{}
        _Slant("Slant",Float)=72
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
            struct input{float4 vertex:POSITION;float4 colour:COLOR;float2 uv:TEXCOORD0;float4 shape:TEXCOORD1;};
            struct output{float4 vertex:SV_POSITION;float4 colour:COLOR;float2 uv:TEXCOORD0;float4 shape:TEXCOORD1;float4 world:TEXCOORD2;};
            float _Slant;float4 _ClipRect;
            output vert(input v){output o;o.vertex=UnityObjectToClipPos(v.vertex);o.world=v.vertex;o.colour=v.colour;o.uv=v.uv;o.shape=v.shape;return o;}
            fixed4 frag(output i):SV_Target
            {
                float2 p=(i.uv-.5)*i.shape.xy;
                p.x-=_Slant*p.y/max(1,2*i.shape.w);
                float2 q=abs(p)-i.shape.zw;
                float d=length(max(q,0))+min(max(q.x,q.y),0);
                float edge=1-smoothstep(.8,1.7,abs(d));
                float inset=(1-smoothstep(.45,1.15,abs(d+4.2)))*.65;
                float glow=exp(-pow(abs(d)/3.2,2))*.28;
                float alpha=max(max(edge,inset),glow)*i.colour.a;
                float3 rgb=lerp(i.colour.rgb,lerp(i.colour.rgb,float3(1,1,1),.35),edge);
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return fixed4(rgb,alpha);
            }
            ENDCG
        }
    }
}
