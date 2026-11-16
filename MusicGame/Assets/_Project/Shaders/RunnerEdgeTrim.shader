Shader "MusicGame/RunnerEdgeTrim"
{
 Properties {_BaseColor("Charcoal",Color)=(.025,.029,.035,1) _Grain("Fine grain",Range(0,1))=.25}
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Grain;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 normal:NORMAL;float3 surface:TEXCOORD1;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 surface:TEXCOORD2;};
   V vert(A a){V o;o.world=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normal);o.surface=a.surface;return o;}
   half4 frag(V i):SV_Target
   {
    float2 grainUV=i.surface.xy*850;
    float grain=frac(sin(dot(floor(grainUV),float2(127.1,311.7)))*43758.5453);
    float3 base=_BaseColor.rgb*lerp(1-_Grain,1+_Grain,grain);
    float3 n=normalize(i.normal);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    float diffuse=saturate(dot(n,light.direction));
    float3 halfVector=normalize(light.direction+GetWorldSpaceNormalizeViewDir(i.world));
    float spec=pow(saturate(dot(n,halfVector)),72)*.13*light.shadowAttenuation;
    return half4(base*(SampleSH(n)+light.color*diffuse*light.shadowAttenuation+.28)+light.color*spec,1);
   }
   ENDHLSL
  }
 }
}
