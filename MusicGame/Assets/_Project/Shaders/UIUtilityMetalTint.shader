Shader "MusicGame/UI/UtilityMetalTint"
{
 Properties
 {
  [PerRendererData] _MainTex("Sprite",2D)="white"{}
  _Tint("Style tint",Color)=(1,1,1,1)
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
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend One OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct A {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct V {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   sampler2D _MainTex;
   float4 _Tint,_ClipRect;
   V vert(A a)
   {
    V o;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.local=a.vertex;o.vertex=UnityObjectToClipPos(a.vertex);o.color=a.color;o.uv=a.uv;return o;
   }
   fixed4 frag(V i):SV_Target
   {
    float4 source=tex2D(_MainTex,i.uv);
    // Retain the authored bevel's light/shade, replacing the original gold hue rather than multiplying it.
    float light=max(source.r,max(source.g,source.b));
    float3 metal=_Tint.rgb*lerp(.30,1.05,smoothstep(.12,.87,light));
    metal=lerp(metal,float3(1,1,1),smoothstep(.88,1,light)*.65);
    float alpha=source.a*_Tint.a*i.color.a;
    float4 color=float4(metal*i.color.rgb*alpha,alpha);
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
