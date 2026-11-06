Shader "MusicGame/Art/WagnerFlame"
{
 Properties { [HDR]_BaseColor("Fire intensity",Color)=(2.5,2.5,2.5,1) }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
  Pass {
   Blend SrcAlpha One
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   half4 _BaseColor;
   struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   V vert(A i) {V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
   half4 frag(V i):SV_Target {
    float y=i.uv.y;float sway=sin(y*9-_Time.y*7)*.09*y+sin(y*17+_Time.y*5)*.035;
    float x=abs(i.uv.x-.5+sway)*2;
    float width=lerp(.86,.04,pow(y,.8));
    float body=smoothstep(width,width*.2,x)*smoothstep(0,.1,y)*pow(saturate(1-y),.65);
    float core=pow(saturate(1-x*2.2),2)*(1-y);
    half3 color=lerp(half3(1,.12,.008),half3(1,.73,.13),core);
    color=lerp(color,half3(1,.96,.65),core*core);
    return half4(color*_BaseColor.rgb*i.color.rgb,body*i.color.a*.9);
   }
   ENDHLSL
  }
 }
}
