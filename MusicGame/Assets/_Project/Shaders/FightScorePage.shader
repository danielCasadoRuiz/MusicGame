Shader "MusicGame/Fight/ScorePage"
{
 Properties {_BaseMap("Antique score page",2D)="white"{} _Heat("Burn",Range(0,1))=0 _Seed("Seed",Float)=0 _Opacity("Opacity",Range(0,1))=1 _AnimationTime("Time",Float)=0}
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;
   float _Heat,_Seed,_Opacity,_AnimationTime;
   CBUFFER_END
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   struct A{float4 p:POSITION;float2 uv:TEXCOORD0;float3 n:NORMAL;};
   struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 n:TEXCOORD1;};
   V vert(A a)
   {
    V o;float3 p=a.p.xyz;
    p.z+=sin(a.uv.y*8+_AnimationTime*13+_Seed*5)*.045*a.uv.x;
    o.p=TransformObjectToHClip(p);o.uv=a.uv;o.n=TransformObjectToWorldNormal(a.n);return o;
   }
   half4 frag(V i):SV_Target
   {
    half4 paper=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv*_BaseMap_ST.xy+_BaseMap_ST.zw);
    Light light=GetMainLight();float lit=.68+.32*abs(dot(normalize(i.n),light.direction));
    return half4(paper.rgb*lit,paper.a*_Opacity);
   }
   ENDHLSL
  }
 }
}
