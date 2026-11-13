TEXTURE2D(_IrisDetailMap);
SAMPLER(sampler_IrisDetailMap);
float2 AvatarIrisCoordinate(float2 atlasUV)
{
    float2 right=(atlasUV-_IrisRightUV.xy)/max(_IrisRightUV.z,0.00001);
    float2 left=(atlasUV-_IrisLeftUV.xy)/max(_IrisLeftUV.z,0.00001);
    return dot(right,right)<dot(left,left)?right:left;
}
// Float UV arithmetic keeps the small circles stable on mobile/XR GPUs.
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    InitializeAvatarBaseSurfaceData(uv, surface);
    float2 atlasUV = (uv - _BaseMap_ST.zw) / _BaseMap_ST.xy;
    float2 irisPoint=AvatarIrisCoordinate(atlasUV);
    float radius=length(irisPoint);
    float pupil=max(.1,_IrisPupilRadius);
    float feather=max(max(.015,_IrisEdgeFeather),fwidth(radius)*.65);
    half mask=_IrisEnabled*smoothstep(pupil,pupil+.04,radius)*
        (1-smoothstep(1+feather*.25,1+feather*1.5,radius))*(1-_IrisBaked);
    if(mask>0.0001)
    {
    float gradientRadius=length(irisPoint-_IrisVariation.xy)+sin(irisPoint.x*9.7+_IrisVariation.z)*sin(irisPoint.y*8.3+_IrisVariation.z*.71)*_IrisDistortion*.5;
    half3 tint = lerp(_IrisInnerColor.rgb, _IrisMainColor.rgb,
        smoothstep(pupil,max(pupil+.04,_IrisInnerRadius),gradientRadius));
    tint=lerp(tint,_IrisOuterColor.rgb,smoothstep(_IrisOuterRadius,1.025,gradientRadius));
    tint=lerp(_IrisDefaultColor.rgb,tint,_IrisUseGradient);
    half atlasFiber=dot(surface.albedo,half3(.2126,.7152,.0722));
    half fiber=atlasFiber;
    if(_IrisDetailEnabled>.5)
    {
        float radial=saturate((radius-pupil)/max(.1,1-pupil));
        float2 center=lerp(_IrisDetailPupil.xy,_IrisDetailOuter.xy,radial);
        // Stay within opaque fibers: bilinear filtering must not pull in transparent black.
        float sourceRadius=lerp(_IrisDetailPupil.z*1.025,_IrisDetailOuter.z*.985,radial);
        half4 detail=SAMPLE_TEXTURE2D(_IrisDetailMap,sampler_IrisDetailMap,center+irisPoint/max(radius,.0001)*sourceRadius);
        // Transparent pixels are coverage, never black pigment.
        fiber=lerp(.5h,dot(detail.rgb,half3(.2126,.7152,.0722)),smoothstep(.08,.9,detail.a));
    }
    // Contrast around the selected pigment, rather than multiplying it toward black.
    fiber=clamp(1+(fiber*_IrisDetailGain-1)*.55,.55,1.35);
    surface.albedo=lerp(surface.albedo,saturate(tint*fiber),mask);
    }
    half eyeball = _IrisEnabled * (1.0-smoothstep(2.65,2.72,radius));
    surface.smoothness = lerp(surface.smoothness,_EyeSmoothness,eyeball);
}

