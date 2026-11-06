Shader "MusicGame/Art/SeamlessPanorama360"
{
    Properties
    {
        [NoScaleOffset] _MainTex("Existing scenario panorama",2D)="grey"{}
        _Tint("Tint",Color)=(0.5,0.5,0.5,1)
        _Exposure("Exposure",Range(0,8))=1
        _Rotation("Rotation",Range(0,360))=0
        _HorizontalFov("Image horizontal coverage",Range(30,360))=100
        _VerticalFov("Image vertical coverage",Range(30,180))=60
        _VerticalOffset("Image vertical framing",Range(-.5,.5))=0
        [HideInInspector] _Mapping("Mapping",Float)=1
        [HideInInspector] _ImageType("Image type",Float)=1
        [HideInInspector] _Layout("Layout",Float)=0
        [HideInInspector] _MirrorOnBack("Mirror",Float)=1
    }
    SubShader
    {
        Tags{"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation,_HorizontalFov,_VerticalFov,_VerticalOffset;
            struct appdata{float4 vertex:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f{float4 vertex:SV_POSITION;float3 direction:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            v2f vert(appdata input)
            {
                v2f output;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex=UnityObjectToClipPos(input.vertex);output.direction=input.vertex.xyz;
                return output;
            }
            half4 frag(v2f input):SV_Target
            {
                float3 direction=normalize(input.direction);
                float angle=radians(_Rotation);float c=cos(angle),s=sin(angle);
                direction.xz=float2(c*direction.x-s*direction.z,s*direction.x+c*direction.z);
                float u=frac(.5+atan2(direction.x,direction.z)/6.28318530718);
                // One horizontal panorama per full turn; no mirrored landscape repetitions.
                // A narrow symmetric edge filter guarantees identical values at the wrap.
                float edge=.5*(1-smoothstep(0,.025,min(u,1-u)));
                float v=saturate(.5+degrees(asin(clamp(direction.y,-1,1)))/max(_VerticalFov,1)-_VerticalOffset);
                half3 color=lerp(tex2D(_MainTex,float2(u,v)).rgb,tex2D(_MainTex,float2(1-u,v)).rgb,edge);
                color*= _Tint.rgb*unity_ColorSpaceDouble.rgb*_Exposure;
                return half4(color,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
