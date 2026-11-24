Shader "MusicGame/UI/SongCardAppearance"
{
    Properties
    {
        [PerRendererData] _MainTex("Colour",2D)="white"{}
        _NormalTex("Light",2D)="white"{}
        _SelectedTint("Colour tint",Color)=(1,1,1,1)
        _LightSurface("Light surface",Range(0,1))=1
        _Completed("Completed",Range(0,1))=0
        _Stage("Painted stage",Range(0,1))=0
        _StageMask("Paint mask",2D)="white"{}
        _StageCrop("Stage crop",Vector)=(1,1,0,0)
        _StageWindow("Authored band window",Vector)=(0,1,0,0)
        _StageBase("Stage primary",Color)=(.2,.05,.5,1)
        _StageAccent("Stage accent",Color)=(1,.05,.7,1)
        _StageSecondary("Stage secondary",Color)=(0,.8,1,1)
        _StageRecolour("Adapt stage palette",Range(0,1))=0
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip",Float)=0
    }
    SubShader
    {
        Tags{"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="False"}
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
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct input{float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct output{float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;};
            sampler2D _MainTex,_NormalTex,_StageMask;
            fixed4 _SelectedTint;
            float _LightSurface;
            float _Completed;
            float _Stage;
            float4 _StageCrop,_StageWindow;
            fixed4 _StageBase,_StageAccent,_StageSecondary;
            float _StageRecolour;
            float4 _ClipRect;
            output vert(input v){output o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;return o;}
            fixed4 frag(output i):SV_Target
            {
                float2 colouredUV=.5+(i.uv-.5)*float2(.96,.91);
                fixed4 c=lerp(tex2D(_MainTex,colouredUV)*_SelectedTint,tex2D(_NormalTex,i.uv),_LightSurface)*i.color;
                float grey=dot(c.rgb,float3(.299,.587,.114));
                c.rgb=lerp(c.rgb,grey.xxx,_Completed*.45);
                c.rgb=lerp(c.rgb,float3(.45,.45,.45),_Completed*.14);
                if(_Stage>.5)
                {
                    // Each skin supplies its own finished surface and silhouette; align its opaque centre under the text.
                    float2 artworkUV=_StageWindow.z>.5?float2(i.uv.x,lerp(_StageWindow.x,_StageWindow.y,i.uv.y)):.5+(i.uv-.5)/_StageCrop.xy;
                    fixed4 art=tex2D(_MainTex,artworkUV);
                    float strength=max(art.r,max(art.g,art.b));
                    float magenta=saturate((art.r-art.g)*2);
                    float cyan=saturate((art.g-art.r)*2);
                    float3 baseTone=lerp(_StageBase.rgb*.35,float3(.018,.022,.035),.38)*(.25+strength*1.6);
                    float3 mapped=lerp(baseTone,_StageAccent.rgb*(.38+.62*strength),magenta);
                    mapped=lerp(mapped,_StageSecondary.rgb*(.42+.58*strength),cyan);
                    c=fixed4(lerp(art.rgb,mapped,_StageRecolour),art.a)*i.color;
                }
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
