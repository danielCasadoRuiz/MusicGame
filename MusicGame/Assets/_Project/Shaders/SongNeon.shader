Shader "MusicGame/UI/SongNeon"
{
    Properties
    {
        [PerRendererData] _MainTex("UI texture",2D)="white"{}
        _GlowRadius("Glow radius",Float)=7
        _ShadowOnly("Shadow only",Float)=0
        _Slant("Shadow slant",Float)=0
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
            float _GlowRadius,_ShadowOnly,_Slant;float4 _ClipRect;
            output vert(input v){output o;o.vertex=UnityObjectToClipPos(v.vertex);o.world=v.vertex;o.colour=v.colour;o.uv=v.uv;o.shape=v.shape;return o;}
            fixed4 frag(output i):SV_Target
            {
                float2 p=(i.uv-.5)*i.shape.xy;
                float authoredSlant=i.shape.x-i.shape.y+2*i.shape.w-2*i.shape.z;
                p.x-=authoredSlant*p.y/max(1,2*i.shape.w);
                float rounding=_ShadowOnly>.5?0:min(i.shape.z,i.shape.w)*.85;
                float2 q=abs(p)-max(float2(.05,.05),i.shape.zw-rounding);
                float d=length(max(q,0))+min(max(q.x,q.y),0)-rounding;
                float haze=exp(-pow(max(d,0)/max(1,_GlowRadius),2)*1.7);
                float core=1-smoothstep(-.65,.65,d);
                float3 rgb;float alpha;
                if(_ShadowOnly>.5){rgb=i.colour.rgb;alpha=haze*i.colour.a;}
                else
                {
                    float halo=haze*.82;
                    rgb=lerp(i.colour.rgb,lerp(i.colour.rgb,float3(1,1,1),.94),core);
                    alpha=saturate(core+halo*(1-core))*i.colour.a;
                }
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return fixed4(rgb,alpha);
            }
            ENDCG
        }
    }
}
