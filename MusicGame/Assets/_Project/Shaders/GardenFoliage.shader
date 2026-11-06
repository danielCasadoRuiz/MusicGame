Shader "MusicGame/Art/GardenFoliage"
{
 Properties { _BaseColor("Tint", Color)=(1,1,1,1) _BaseMap("Base",2D)="white"{} _Cutoff("Cutoff",Float)=0 _Cull("Cull",Float)=0 }
 SubShader {
 Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
 Pass {
 Name "ForwardLit" Tags {"LightMode"="UniversalForward"} Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile _ _ADDITIONAL_LIGHTS
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.c=i.c;return o;}
 half4 frag(V i, bool front:SV_IsFrontFace):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);half3 n=normalize(i.n)*(front?1:-1);Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));half3 illumination=SampleSH(n)+sun.color*(saturate(dot(n,sun.direction))+.12)*sun.shadowAttenuation;
 #ifdef _ADDITIONAL_LIGHTS
 uint count=GetAdditionalLightsCount();for(uint j=0;j<count;j++){Light light=GetAdditionalLight(j,i.world);illumination+=light.color*saturate(dot(n,light.direction))*light.distanceAttenuation*light.shadowAttenuation;}
 #endif
 return half4(i.c.rgb*illumination,1);
 }
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
