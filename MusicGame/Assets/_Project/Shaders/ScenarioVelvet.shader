Shader "MusicGame/Art/ScenarioVelvet"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        [Normal] _BumpMap("Normal",2D)="bump"{}
        _BumpScale("Normal strength",Float)=0.65
        _MetallicGlossMap("Metallic smoothness",2D)="white"{}
        _Metallic("Metallic",Range(0,1))=0
        _Smoothness("Smoothness",Range(0,1))=0.55
        _EmissionMap("Emission",2D)="white"{}
        [HDR] _EmissionColor("Emission color",Color)=(0,0,0,0)
        _VelvetColor("Velvet fabric",Color)=(1,0.015,0.025,1)
        _VelvetBoost("Fabric brightness",Float)=1
        _Cull("Cull",Float)=0
        _Cutoff("Cutoff",Range(0,1))=0.5
        [HideInInspector] _Surface("Surface",Float)=0
        [HideInInspector] _OcclusionMap("Occlusion",2D)="white"{}
        [HideInInspector] _OcclusionStrength("Occlusion strength",Range(0,1))=1
        [HideInInspector] _SpecColor("Specular",Color)=(0.2,0.2,0.2,1)
    }
    SubShader
    {
        Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode"="UniversalForward"}
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 _VelvetColor;
            half _VelvetBoost;
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;};
            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(input.normalOS,input.tangentOS);
                output.positionCS=p.positionCS;output.positionWS=p.positionWS;
                output.normalWS=n.normalWS;output.tangentWS=half4(n.tangentWS,input.tangentOS.w*GetOddNegativeScale());
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);output.fog=ComputeFogFactor(p.positionCS.z);
                return output;
            }
            half4 Fragment(Varyings input):SV_Target
            {
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv,surface);
                // Recolor cool/neutral fabric while retaining the warm gold atlas regions.
                half fabric=smoothstep(0.60h,0.95h,surface.albedo.b/max(surface.albedo.r,0.001h));
                half luminance=dot(surface.albedo,half3(0.2126h,0.7152h,0.0722h));
                surface.albedo=lerp(surface.albedo,luminance*_VelvetColor.rgb*_VelvetBoost,fabric);
                surface.metallic=lerp(surface.metallic,0.0h,fabric);
                surface.smoothness=lerp(surface.smoothness,0.3h,fabric);
                InputData data=(InputData)0;
                data.positionWS=input.positionWS;
                half3 n=normalize(input.normalWS);
                half3 t=normalize(input.tangentWS.xyz);
                half3 b=input.tangentWS.w*cross(n,t);
                data.normalWS=NormalizeNormalPerPixel(TransformTangentToWorld(surface.normalTS,half3x3(t,b,n)));
                data.tangentToWorld=half3x3(t,b,n);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                data.fogCoord=input.fog;data.vertexLighting=VertexLighting(input.positionWS,data.normalWS);
                data.bakedGI=SampleSH(data.normalWS);
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask=half4(1,1,1,1);
                half4 color=UniversalFragmentPBR(data,surface);
                color.rgb=MixFog(color.rgb,data.fogCoord);
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
