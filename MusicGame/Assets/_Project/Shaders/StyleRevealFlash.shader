Shader "MusicGame/UI/StyleRevealFlash"
{
 Properties { [PerRendererData] _MainTex("Texture",2D)="white" {} _Color("Style colour",Color)=(.5,.2,1,1) _Progress("Progress",Float)=0 }
 SubShader
 {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct A{float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   fixed4 _Color;float _Progress;
   V vert(A i){V o;o.pos=UnityObjectToClipPos(i.vertex);o.uv=i.uv;o.color=i.color;return o;}
   fixed4 frag(V i):SV_Target
   {
    float2 p=(i.uv-.5)*float2(1.8,1);float r=length(p);
    float t=saturate(_Progress);float life=sin(t*3.14159)*(1-t);
    float glow=exp(-dot(p,p)*12)*.5;
    float ring=exp(-pow((r-lerp(.04,.65,t))/.018,2))*.4;
    float rays=pow(abs(cos(atan2(p.y,p.x)*6)),28)*smoothstep(.14,.24,r)*(1-smoothstep(.3,.8,r))*.28;
    // Every term fades to zero well inside the quad, even when the expanding wave reaches it.
    float edge=min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));
    float feather=smoothstep(.015,.19,edge)*(1-smoothstep(.36,.49,abs(i.uv.y-.5)));
    return fixed4(lerp(_Color.rgb,fixed3(.953,.918,.827),.25),saturate((glow+ring+rays)*life*feather)*i.color.a);
   }
   ENDCG
  }
 }
}
