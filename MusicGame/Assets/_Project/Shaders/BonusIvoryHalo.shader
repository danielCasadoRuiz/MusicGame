Shader "MusicGame/BonusIvoryHalo"
{
 Properties { _HaloColor("Halo colour / opacity",Color)=(1,1,1,.6) }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off ZTest LEqual Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _HaloColor;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
   V vert(A i)
   {
    V o;
    float3 centre=TransformObjectToWorld(float3(0,0,0));
    float size=max(length(GetObjectToWorldMatrix()._m00_m10_m20),max(length(GetObjectToWorldMatrix()._m01_m11_m21),length(GetObjectToWorldMatrix()._m02_m12_m22)));
    // Move away from the camera so the opaque bonus always covers its own backing.
    centre-=GetWorldSpaceNormalizeViewDir(centre)*size*.27;
    float3 world=centre+(UNITY_MATRIX_I_V._m00_m10_m20*i.positionOS.x+UNITY_MATRIX_I_V._m01_m11_m21*i.positionOS.y)*size;
    o.positionCS=TransformWorldToHClip(world);o.uv=i.uv;return o;
   }
   half4 frag(V i):SV_Target
   {
    float2 p=(i.uv-.5)*2;float radius=length(p);
    // A broad feather, rather than a narrow outer cutoff. Keep the light centre readable.
    float falloff=exp(-3.5*dot(p,p))*(1-smoothstep(.42,1,radius));
    return half4(_HaloColor.rgb,_HaloColor.a*falloff);
   }
   ENDHLSL
  }
 }
}
