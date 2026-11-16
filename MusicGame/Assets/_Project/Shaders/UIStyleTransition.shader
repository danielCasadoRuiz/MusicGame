Shader "MusicGame/UI/StyleTransition"
{
 Properties
 {
  [PerRendererData] _MainTex("Sprite",2D)="white"{}
  _ClassicTex("Classic sprite",2D)="white"{}
  _StyleColor("Style colour",Color)=(1,1,1,1)
  _ClassicColor("Classic colour",Color)=(1,1,1,1)
  _UseClassicTexture("Use classic sprite",Float)=0
  _GoldClassic("Gold finish",Float)=0
  _Laurel("Gold laurel",Float)=0
  _FixedBlend("Fixed blend (-1 = curtain)",Float)=-1
  _Mapping("Reference viewport x: minimum, inverse width",Vector)=(0,1,0,0)
  _Edge("Edge",Float)=1
  _Softness("Softness",Float)=.2
  _StencilComp("Stencil comparison",Float)=8
  _Stencil("Stencil",Float)=0
  _StencilOp("Stencil operation",Float)=0
  _StencilWriteMask("Stencil write mask",Float)=255
  _StencilReadMask("Stencil read mask",Float)=255
  _ColorMask("Colour mask",Float)=15
  [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Alpha clip",Float)=0
 }
 SubShader
 {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend One OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct A {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;float x:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
   sampler2D _MainTex,_ClassicTex;
   float4 _StyleColor,_ClassicColor,_Mapping,_ClipRect;
   float _UseClassicTexture,_GoldClassic,_Laurel,_FixedBlend,_Edge,_Softness;
   V vert(A a)
   {
    V o;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.local=a.vertex;o.vertex=UnityObjectToClipPos(a.vertex);o.color=a.color;o.uv=a.uv;
    // Clip-space position is authoritative even when uGUI batches/rebases vertices.
    float screenX=(o.vertex.x/o.vertex.w)*.5+.5;
    o.x=(screenX-_Mapping.x)*_Mapping.y;return o;
   }
   float3 Gold(float3 source,float y)
   {
    float light=max(source.r,max(source.g,source.b));
    float metal=saturate(light*.72+y*.20+.10*sin(y*31));
    float3 gold=lerp(float3(.12,.045,.008),float3(1,.62,.10),smoothstep(.08,.72,metal));
    return lerp(gold,float3(1,.95,.69),smoothstep(.73,1,metal));
   }
   float Laurel(float2 uv)
   {
    float a=0;
    [unroll]for(int j=0;j<8;j++)
    {
     float theta=(-1.15+j*.30);
     float2 centre=float2(.5+.31*cos(theta),.5+.34*sin(theta));
     float2 d=uv-centre;
     float2 axis=float2(-sin(theta),cos(theta));
     float2 p=float2(dot(d,axis),dot(d,float2(axis.y,-axis.x)));
     a=max(a,1-smoothstep(.72,1,length(p/float2(.052,.019))));
     centre.x=1-centre.x;d=uv-centre;axis.x=-axis.x;
     p=float2(dot(d,axis),dot(d,float2(axis.y,-axis.x)));
     a=max(a,1-smoothstep(.72,1,length(p/float2(.052,.019))));
    }
    return a;
   }
   fixed4 frag(V i):SV_Target
   {
    float4 source=tex2D(_MainTex,i.uv);
    float4 normal=source*_StyleColor;
    float4 classic=lerp(source,tex2D(_ClassicTex,i.uv),_UseClassicTexture)*_ClassicColor;
    classic.rgb=lerp(classic.rgb,Gold(classic.rgb,i.uv.y),_GoldClassic);
    if(_Laurel>.5)
    {
     float leaf=Laurel(i.uv);classic.rgb=lerp(classic.rgb,Gold(float3(.9,.9,.9),i.uv.y),leaf);
     classic.a=max(classic.a,leaf*.92);
    }
    float k=_FixedBlend>=0?saturate(_FixedBlend):smoothstep(_Edge,_Edge+max(.0001,_Softness),i.x);
    // Blend premultiplied layers once: no double exposure or dim seam at the frontier.
    normal.rgb*=normal.a;classic.rgb*=classic.a;
    float4 color=lerp(normal,classic,k);
    color.rgb*=i.color.rgb*i.color.a;color.a*=i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
    color*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    #ifdef UNITY_UI_ALPHACLIP
    clip(color.a-.001);
    #endif
    return color;
   }
   ENDCG
  }
 }
}
