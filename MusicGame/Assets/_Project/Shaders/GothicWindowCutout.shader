Shader "MusicGame/Art/GothicWindowCutout"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        [Normal] _BumpMap("Normal",2D)="bump"{}
        _BumpScale("Normal strength",Float)=.65
        _MetallicGlossMap("Metallic smoothness",2D)="white"{}
        _Metallic("Metallic",Range(0,1))=0
        _Smoothness("Smoothness",Range(0,1))=.5
        _EmissionMap("Emission",2D)="white"{}
        [HDR] _EmissionColor("Emission color",Color)=(0,0,0,0)
        _OcclusionMap("Occlusion",2D)="white"{}
        _OcclusionStrength("Occlusion strength",Range(0,1))=1
        _SpecColor("Specular",Color)=(.2,.2,.2,1)
        _Cull("Cull",Float)=0
        _Cutoff("Cutoff",Range(0,1))=.5
        [HideInInspector] _Surface("Surface",Float)=0
        [HideInInspector] _WindowCenter0("Opening 0 Center",Vector)=(0,0,0,0)
        [HideInInspector] _WindowRight0("Opening 0 Right",Vector)=(0,0,0,0)
        [HideInInspector] _WindowUp0("Opening 0 Up",Vector)=(0,0,0,0)
        [HideInInspector] _WindowNormal0("Opening 0 Normal",Vector)=(0,0,0,0)
        [HideInInspector] _WindowShape0("Opening 0 Shape",Vector)=(0,0,0,0)
        [HideInInspector] _WindowCenter1("Opening 1 Center",Vector)=(0,0,0,0)
        [HideInInspector] _WindowRight1("Opening 1 Right",Vector)=(0,0,0,0)
        [HideInInspector] _WindowUp1("Opening 1 Up",Vector)=(0,0,0,0)
        [HideInInspector] _WindowNormal1("Opening 1 Normal",Vector)=(0,0,0,0)
        [HideInInspector] _WindowShape1("Opening 1 Shape",Vector)=(0,0,0,0)
        [HideInInspector] _WindowCenter2("Opening 2 Center",Vector)=(0,0,0,0)
        [HideInInspector] _WindowRight2("Opening 2 Right",Vector)=(0,0,0,0)
        [HideInInspector] _WindowUp2("Opening 2 Up",Vector)=(0,0,0,0)
        [HideInInspector] _WindowNormal2("Opening 2 Normal",Vector)=(0,0,0,0)
        [HideInInspector] _WindowShape2("Opening 2 Shape",Vector)=(0,0,0,0)
        [HideInInspector] _WindowCenter3("Opening 3 Center",Vector)=(0,0,0,0)
        [HideInInspector] _WindowRight3("Opening 3 Right",Vector)=(0,0,0,0)
        [HideInInspector] _WindowUp3("Opening 3 Up",Vector)=(0,0,0,0)
        [HideInInspector] _WindowNormal3("Opening 3 Normal",Vector)=(0,0,0,0)
        [HideInInspector] _WindowShape3("Opening 3 Shape",Vector)=(0,0,0,0)
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        float4 _WindowCenter0;
        float4 _WindowRight0;
        float4 _WindowUp0;
        float4 _WindowNormal0;
        float4 _WindowShape0;
        float4 _WindowCenter1;
        float4 _WindowRight1;
        float4 _WindowUp1;
        float4 _WindowNormal1;
        float4 _WindowShape1;
        float4 _WindowCenter2;
        float4 _WindowRight2;
        float4 _WindowUp2;
        float4 _WindowNormal2;
        float4 _WindowShape2;
        float4 _WindowCenter3;
        float4 _WindowRight3;
        float4 _WindowUp3;
        float4 _WindowNormal3;
        float4 _WindowShape3;
        void ClipOpening(float3 p,float4 center,float4 right,float4 up,float4 normal,float4 shape)
        {
            if(center.w<.5)return;
            float3 d=p-center.xyz;
            float x=abs(dot(d,right.xyz))/max(right.w,.0001),y=dot(d,up.xyz),depth=abs(dot(d,normal.xyz));
            float arc=sqrt(saturate(1-(x+1)*(x+1)*.25))/sqrt(.75);
            if(depth<normal.w&&x<1&&y>0&&y<up.w+shape.x*arc)clip(-1);
        }
        void ClipWindows(float3 p)
        {
            ClipOpening(p,_WindowCenter0,_WindowRight0,_WindowUp0,_WindowNormal0,_WindowShape0);
            ClipOpening(p,_WindowCenter1,_WindowRight1,_WindowUp1,_WindowNormal1,_WindowShape1);
            ClipOpening(p,_WindowCenter2,_WindowRight2,_WindowUp2,_WindowNormal2,_WindowShape2);
            ClipOpening(p,_WindowCenter3,_WindowRight3,_WindowUp3,_WindowNormal3,_WindowShape3);
        }
        struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;UNITY_VERTEX_OUTPUT_STEREO};
        V Vertex(A i)
        {
            V o=(V)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(i.normalOS,i.tangentOS);
            o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,i.tangentOS.w*GetOddNegativeScale());
            o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
        }
        half3 SurfaceNormal(V i,SurfaceData surface)
        {half3 n=normalize(i.normalWS),t=normalize(i.tangentWS.xyz),b=i.tangentWS.w*cross(n,t);return NormalizeNormalPerPixel(TransformTangentToWorld(surface.normalTS,half3x3(t,b,n)));}
        half4 Forward(V i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ClipWindows(i.positionWS);SurfaceData surface;InitializeStandardLitSurfaceData(i.uv,surface);
            InputData data=(InputData)0;data.positionWS=i.positionWS;data.normalWS=SurfaceNormal(i,surface);data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
            data.shadowCoord=TransformWorldToShadowCoord(i.positionWS);data.fogCoord=i.fog;data.vertexLighting=VertexLighting(i.positionWS,data.normalWS);data.bakedGI=SampleSH(data.normalWS);
            data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);data.shadowMask=half4(1,1,1,1);
            half4 color=UniversalFragmentPBR(data,surface);color.rgb=MixFog(color.rgb,data.fogCoord);return color;
        }
        half4 Depth(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ClipWindows(i.positionWS);return 0;}
        half4 DepthNormal(V i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ClipWindows(i.positionWS);SurfaceData s;InitializeStandardLitSurfaceData(i.uv,s);half3 n=SurfaceNormal(i,s);
            #if defined(_GBUFFER_NORMALS_OCT)
            return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
            #else
            return half4(n,0);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags {"LightMode"="UniversalForwardOnly"} Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Forward
            #pragma multi_compile_instancing
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment Depth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            V ShadowVertex(A i)
            {
                V o=Vertex(i);float3 direction=_LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction=normalize(_LightPosition-o.positionWS);
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction));
                #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Depth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"} ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment DepthNormal
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
