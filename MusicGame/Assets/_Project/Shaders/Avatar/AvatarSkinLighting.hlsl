#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
half3 AvatarSkinLightFill(Light light,InputData data,SurfaceData surface,half weight)
{
    half nl=dot(data.normalWS,light.direction);
    half wrapped=saturate((nl+_SkinWrap)/(1+_SkinWrap));
    half extra=max(0,wrapped-saturate(nl));
    return surface.albedo*light.color*_SkinScatterColor.rgb*extra*_SkinSoftness*weight*
        light.distanceAttenuation*light.shadowAttenuation*surface.occlusion;
}
half4 AvatarSkinFragmentPBR(InputData inputData,SurfaceData surface,float2 uv)
{
    half4 color=UniversalFragmentPBR(inputData,surface);
    if(_SkinSoftness>.0001)
    {
        float2 atlasUV=(uv-_BaseMap_ST.zw)/_BaseMap_ST.xy;
        half4 region=SAMPLE_TEXTURE2D(_SkinRegionMap,sampler_SkinRegionMap,atlasUV);
        half weight=region.r*(1-region.g);
        half4 shadowMask=CalculateShadowMask(inputData);
        AmbientOcclusionFactor ao=CreateAmbientOcclusionFactor(inputData,surface);
        uint layers=GetMeshRenderingLayer();
        Light mainLight=GetMainLight(inputData,shadowMask,ao);
        #ifdef _LIGHT_LAYERS
        if(IsMatchingLightLayer(mainLight.layerMask,layers))
        #endif
        color.rgb+=AvatarSkinLightFill(mainLight,inputData,surface,weight);
        #if defined(_ADDITIONAL_LIGHTS)
        uint pixelLightCount=GetAdditionalLightsCount();
        #if USE_CLUSTER_LIGHT_LOOP
        [loop]for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light light=GetAdditionalLight(lightIndex,inputData,shadowMask,ao);
            #ifdef _LIGHT_LAYERS
            if(IsMatchingLightLayer(light.layerMask,layers))
            #endif
            color.rgb+=AvatarSkinLightFill(light,inputData,surface,weight);
        }
        #endif
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light=GetAdditionalLight(lightIndex,inputData,shadowMask,ao);
            #ifdef _LIGHT_LAYERS
            if(IsMatchingLightLayer(light.layerMask,layers))
            #endif
            color.rgb+=AvatarSkinLightFill(light,inputData,surface,weight);
        LIGHT_LOOP_END
        #endif
    }
    return color;
}
