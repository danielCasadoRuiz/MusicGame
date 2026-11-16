Shader "MusicGame/BonusGlowUnlit"
{
 Properties
 {
  _BaseMap("Texture",2D)="white"{}
  _BaseColor("Base colour",Color)=(1,1,1,1)
  _GlowStrength("Glow intensity",Float)=2.5
  _FlowSpeed("Flow speed",Float)=0.8
  _PulseAmount("Pulse amount",Range(0,1))=0.15
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass
  {
   Name "BonusUnlit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;half4 _BaseColor;
   float _GlowStrength,_FlowSpeed,_PulseAmount;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
   V vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   half4 frag(V i):SV_Target
   {
    half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
    // Authored albedo is the colour authority. No type/configuration tint and no
    // saturation remap: both surface and emission preserve each texel's RGB ratios.
    half peak=max(max(albedo.r,albedo.g),albedo.b);
    half3 textureHue=albedo/max(peak,.002);
    half facet=.88+.12*saturate(dot(normalize(i.normalWS),normalize(half3(.35,.75,-.55))));
    half3 base=albedo*facet;
    half rim=pow(1-saturate(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS))),2.5);
    half flow=pow(saturate(.5+.5*sin(i.positionWS.y*9-_Time.y*_FlowSpeed*6.283)),10);
    half pulse=1-_PulseAmount*(.5+.5*sin(_Time.y*2.3));
    half3 glow=textureHue*(.18+rim*.48+flow*.18)*max(0,_GlowStrength)*pulse;
    return half4(MixFog(base+glow,i.fog),1);
   }
   ENDHLSL
  }
 }
}
