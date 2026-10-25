Shader "MusicGame/Art/SceneryBackdrop"
{
    Properties
    {
        [NoScaleOffset] _MainTex("Existing scenario panorama",2D)="grey"{}
        _Tint("Tint",Color)=(0.5,0.5,0.5,1)
        _Exposure("Exposure",Range(0,8))=1
        _Rotation("Rotation",Range(0,360))=0
        _HorizontalFov("Image horizontal coverage",Range(30,360))=100
        _VerticalFov("Image vertical coverage",Range(30,180))=60
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
            float _Rotation,_HorizontalFov,_VerticalFov;
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
                float u=.5+degrees(atan2(direction.x,direction.z))/max(_HorizontalFov,1);
                // Mirror repeats keep the supplied single-view landscape continuous around the arena.
                u=1-abs(frac(u*.5)*2-1);
                float v=saturate(.5+degrees(asin(clamp(direction.y,-1,1)))/max(_VerticalFov,1));
                half3 color=tex2D(_MainTex,float2(u,v)).rgb;
                color*= _Tint.rgb*unity_ColorSpaceDouble.rgb*_Exposure;
                return half4(color,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
