// Full float UV arithmetic keeps the tiny iris stable on mobile half-precision GPUs.
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    InitializeAvatarBaseSurfaceData(uv, surface);
    float2 atlasUV = (uv - _BaseMap_ST.zw) / _BaseMap_ST.xy;
    float2 r = (atlasUV - _IrisRightUV.xy) / max(_IrisRightUV.z, 0.00001);
    float2 l = (atlasUV - _IrisLeftUV.xy) / max(_IrisLeftUV.z, 0.00001);
    float radius = sqrt(min(dot(r,r), dot(l,l)));
    // Preserve the black pupil and feather only the iris/sclera boundary.
    half mask = _IrisEnabled * smoothstep(0.34,0.38,radius) * (1.0-smoothstep(0.97,1.025,radius));
    half fibers = dot(surface.albedo, half3(0.2126,0.7152,0.0722));
    half3 tint = lerp(_IrisInnerColor.rgb, _IrisMainColor.rgb,
        smoothstep(0.36, max(0.38,_IrisInnerRadius),radius));
    tint = lerp(tint, _IrisOuterColor.rgb,
        smoothstep(_IrisOuterRadius,1.025,radius));
    surface.albedo = lerp(surface.albedo, saturate(tint * fibers * _IrisDetailGain), mask * (1.0 - _IrisBaked));
    half eyeball = _IrisEnabled * (1.0-smoothstep(2.65,2.72,radius));
    surface.smoothness = lerp(surface.smoothness,_EyeSmoothness,eyeball);
}

