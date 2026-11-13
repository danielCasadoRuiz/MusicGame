#include "AvatarNailContours.generated.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
TEXTURE2D(_SkinRegionMap);
SAMPLER(sampler_SkinRegionMap);
float3 AvatarSkinAnchor(float value)
{
    float t=saturate(value)*6;
    float3 a,b;
    if(t<1){a=float3(239,206,186);b=float3(225,183,155);}
    else if(t<2){a=float3(225,183,155);b=float3(202,159,135);}
    else if(t<3){a=float3(202,159,135);b=float3(175,122,88);}
    else if(t<4){a=float3(175,122,88);b=float3(140,87,59);}
    else if(t<5){a=float3(140,87,59);b=float3(104,60,40);}
    else {a=float3(104,60,40);b=float3(68,39,27);}
    return SRGBToLinear(lerp(a,b,t>=6?1:frac(t))/255.0);
}
float3 AvatarSkinToneColor(float3 source,half4 region)
{
    float3 reference=SRGBToLinear(float3(202,159,135)/255.0);
    float depth=saturate((_SkinTone-1.0/3.0)*1.5);
    float pale=saturate((1.0/3.0-_SkinTone)*3);
    float3 target=AvatarSkinAnchor(_SkinTone);
    float3 palm=AvatarSkinAnchor(saturate(_SkinTone-depth*.25));
    target=lerp(target,palm,region.b*depth*.6);
    float oldLum=dot(target,float3(.2126,.7152,.0722));
    target*=exp2(_SkinUndertone*float3(.09,.005,-.09));
    target*=oldLum/max(dot(target,float3(.2126,.7152,.0722)),.0001);
    float3 detail=log2(max(source,.0001)/reference);
    float luminance=dot(detail,float3(.2126,.7152,.0722));
    float3 relative=luminance*(1+depth*.3-pale*.1)+(detail-luminance)*(1-depth*.35+pale*.1);
    float3 skin=target*exp2(relative);
    // Lip pigment responds less strongly than general skin pigment, retaining its own chroma.
    float ratio=oldLum/max(dot(reference,float3(.2126,.7152,.0722)),.0001);
    float3 lip=source*pow(max(ratio,.0001),.55);
    float3 result=lerp(source,skin,region.r);
    result=lerp(result,lip,region.g);
    return saturate(result);
}
inline void InitializeStandardLitSurfaceData(float2 uv,out SurfaceData surface)
{
    InitializeAvatarIrisSurfaceData(uv,surface);
    float2 nailUV=(uv-_BaseMap_ST.zw)/_BaseMap_ST.xy;
    if(_SkinToneEnabled>.5)
    {
        float2 atlasUV=(uv-_BaseMap_ST.zw)/_BaseMap_ST.xy;
        half4 region=SAMPLE_TEXTURE2D(_SkinRegionMap,sampler_SkinRegionMap,atlasUV);
        surface.albedo=AvatarSkinToneColor(surface.albedo,region);
    }
    // Natural mode leaves original nail albedo, normal and smoothness untouched.
    if(_NailMode<.5)return;
    float2 nailRegion=AvatarPrecomputedNailRegion(nailUV);
    int nailId=(int)round(nailRegion.x)-1;
    half4 polish=half4(0,0,0,0);
    if(_NailMode<1.5)
    {
        if(nailId>=0 && nailId<5){polish=_NailGroupColor0;polish.a*=_NailGroup0Enabled;}
        else if(nailId<10 && nailId>=5){polish=_NailGroupColor1;polish.a*=_NailGroup1Enabled;}
        else if(nailId<15 && nailId>=10){polish=_NailGroupColor2;polish.a*=_NailGroup2Enabled;}
        else if(nailId<20 && nailId>=15){polish=_NailGroupColor3;polish.a*=_NailGroup3Enabled;}
    }
    else
    {
    if(nailId==0) polish=_NailColor0;
    else if(nailId==1) polish=_NailColor1;
    else if(nailId==2) polish=_NailColor2;
    else if(nailId==3) polish=_NailColor3;
    else if(nailId==4) polish=_NailColor4;
    else if(nailId==5) polish=_NailColor5;
    else if(nailId==6) polish=_NailColor6;
    else if(nailId==7) polish=_NailColor7;
    else if(nailId==8) polish=_NailColor8;
    else if(nailId==9) polish=_NailColor9;
    else if(nailId==10) polish=_NailColor10;
    else if(nailId==11) polish=_NailColor11;
    else if(nailId==12) polish=_NailColor12;
    else if(nailId==13) polish=_NailColor13;
    else if(nailId==14) polish=_NailColor14;
    else if(nailId==15) polish=_NailColor15;
    else if(nailId==16) polish=_NailColor16;
    else if(nailId==17) polish=_NailColor17;
    else if(nailId==18) polish=_NailColor18;
    else if(nailId==19) polish=_NailColor19;
    }
    half coverage=nailRegion.y;
    half weight=coverage*polish.a;
    // Preserve subtle existing nail variation while using the chosen pigment.
    half lum=dot(surface.albedo,half3(.2126,.7152,.0722));
    surface.albedo=lerp(surface.albedo,polish.rgb*clamp(.85+lum*.3,.85,1.1),weight);
    surface.smoothness=lerp(surface.smoothness,.72h,weight);
    surface.metallic=lerp(surface.metallic,0,weight);
}
