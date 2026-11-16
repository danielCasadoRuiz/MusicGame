#ifndef RUNNER_SURFACE_INCLUDED
#define RUNNER_SURFACE_INCLUDED
TEXTURE2D(_SurfaceAtlas);
SAMPLER(sampler_SurfaceAtlas);
TEXTURE2D(_FinishAtlas);
SAMPLER(sampler_FinishAtlas);
float SurfaceHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
float SurfaceNoise(float2 p)
{
    float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(SurfaceHash(i),SurfaceHash(i+float2(1,0)),f.x),lerp(SurfaceHash(i+float2(0,1)),SurfaceHash(i+1),f.x),f.y);
}
float RunnerFinishBlend(float2 pathMetres)
{
    if(_FinishSurfaceEnabled<.5)return 0;
    if(pathMetres.y>=_FinishDistance)return 1;
    if(_FinishBlendMeters<=0)return 0;
    float progress=saturate((pathMetres.y-(_FinishDistance-_FinishBlendMeters))/_FinishBlendMeters);
    UNITY_BRANCH if(progress<=0)return 0;
    // Stationary two-scale islands: only coverage grows towards the finish.
    // Early coverage stays sparse; the same pattern survives streaming and retries.
    float2 p=pathMetres/max(_FinishPatchSize,.1)+float2(17.3,53.7)+_SurfaceSeed*.071;
    float field=SurfaceNoise(p)*.72+SurfaceNoise(p*3.9+31)*.28;
    field=smoothstep(.2,.8,field);
    float coverage=pow(progress,1.6);
    float softness=max(_FinishPatchSoftness,.005);
    float threshold=lerp(-softness,1+softness,coverage);
    return smoothstep(field-softness,field+softness,threshold);
}
// Mirrored coordinates are continuous at every tile edge, even for non-seamless source art.
// Two offset phases, selected by a continuous field, break up the mirrored repetition.
half3 SurfaceCellSample(TEXTURE2D_PARAM(atlas, atlasSampler), float4 texelSize,
    float2 atlasGrid, float padding, float2 uv, float index, float2 dx, float2 dy)
{
    float2 grid=max(floor(atlasGrid),1);
    index=clamp(floor(index),0,grid.x*grid.y-1);
    float2 cell=float2(fmod(index,grid.x),floor(index/grid.x));
    float2 inset=min(max(padding,0)*texelSize.xy*grid,.24);
    float2 span=1-2*inset;
    float2 phase=frac(uv*.5)*2;
    float2 mirrored=1-abs(phase-1);
    float2 direction=1-2*step(1,phase);
    return SAMPLE_TEXTURE2D_GRAD(atlas,atlasSampler,(cell+inset+mirrored*span)/grid,
        dx*direction*span/grid,dy*direction*span/grid).rgb;
}
half3 SurfaceTiledSample(TEXTURE2D_PARAM(atlas, atlasSampler),float4 texelSize,
    float2 grid,float padding,float2 uv,float index,float2 dx,float2 dy,float seed,bool organic)
{
    half3 first=SurfaceCellSample(TEXTURE2D_ARGS(atlas,atlasSampler),texelSize,grid,padding,uv,index,dx,dy);
    UNITY_BRANCH if(!organic)return first;
    float mix=smoothstep(.25,.75,SurfaceNoise(uv*.59+float2(seed*.017,seed*.031)));
    half3 second=SurfaceCellSample(TEXTURE2D_ARGS(atlas,atlasSampler),texelSize,grid,padding,
        uv+float2(.417,.673),index,dx,dy);
    return lerp(first,second,mix);
}
half3 AtlasSample(float2 uv,float index,float2 dx,float2 dy)
{
    return SurfaceTiledSample(TEXTURE2D_ARGS(_SurfaceAtlas,sampler_SurfaceAtlas),_SurfaceAtlas_TexelSize,
        _AtlasGrid.xy,_Padding,uv,index,dx,dy,_SurfaceSeed,_SurfaceMode>1.5 && _SurfaceMode<2.5 && !(_EmissionPattern>5.5 && _SurfaceEmissionStrength>0));
}
half3 FinishAtlasSample(float2 uv,float index,float2 dx,float2 dy)
{
    return SurfaceTiledSample(TEXTURE2D_ARGS(_FinishAtlas,sampler_FinishAtlas),_FinishAtlas_TexelSize,
        _FinishGrid.xy,_FinishPadding,uv,index,dx,dy,_FinishSurfaceSeed,_FinishMode>1.5);
}
half3 RunnerFinishSurface(float3 coords,float2 pathDx,float2 pathDy)
{
    float2 uv=coords.xy/max(_FinishMeters,.01);
    float2 dx=pathDx/max(_FinishMeters,.01),dy=pathDy/max(_FinishMeters,.01);
    half3 col;
    if(_FinishMode<1.5) col=FinishAtlasSample(uv,_FinishIndex,dx,dy);
    else
    {
        float2 patches=coords.xy/max(_FinishLayerPatchSize,0.01)+float2(_FinishSurfaceSeed*.137,_FinishSurfaceSeed*.319);
        float n=(SurfaceNoise(patches)*.65+SurfaceNoise(patches*3.7+57)*.35)*2-1;
        float h=coords.z+n*_FinishPatchAmount;
        float a=saturate((h-_FinishHeightStops.x)/max(_FinishHeightStops.y-_FinishHeightStops.x,0.001));
        float b=saturate((h-_FinishHeightStops.y)/max(_FinishHeightStops.z-_FinishHeightStops.y,0.001));
        a=a*a*(3-2*a); b=b*b*(3-2*b);
        float3 w=lerp(lerp(_FinishLowWeights.xyz,_FinishMidWeights.xyz,a),_FinishHighWeights.xyz,b);
        w=max(w,0); w/=max(dot(w,float3(1,1,1)),0.0001);
        // A continuous spatial selector produces patches instead of a uniform colour mixture.
        float selector=SurfaceNoise(patches+float2(39.4,17.2))*.7+SurfaceNoise(patches*4.1+73)*.3;
        float width=max(_FinishBlendWidth*0.5,0.005);
        float c0=1-smoothstep(w.x-width,w.x+width,selector);
        float c2=smoothstep(w.x+w.y-width,w.x+w.y+width,selector);
        float3 masks=float3(c0,max(0,1-c0-c2),c2);
        masks*=step(float3(.0001,.0001,.0001),w);
        masks/=max(dot(masks,float3(1,1,1)),0.0001);
        float3 tiling=max(_FinishLayerTiling.xyz,.01);
        col=FinishAtlasSample(uv*tiling.x,_FinishLayerIndices.x,dx*tiling.x,dy*tiling.x)*masks.x
           +FinishAtlasSample(uv*tiling.y,_FinishLayerIndices.y,dx*tiling.y,dy*tiling.y)*masks.y
           +FinishAtlasSample(uv*tiling.z,_FinishLayerIndices.z,dx*tiling.z,dy*tiling.z)*masks.z;
        // Optional fourth construction state: sample only near the exposed foundation.
        UNITY_BRANCH if(_FinishFoundation.x>.5 && h<_FinishFoundation.z+_FinishFoundation.w*.5)
        {
            float foundation=1-smoothstep(_FinishFoundation.z-_FinishFoundation.w*.5,_FinishFoundation.z+_FinishFoundation.w*.5,h);
            col=lerp(col,FinishAtlasSample(uv,_FinishFoundation.y,dx,dy),foundation);
        }
    }
    if(_FinishWeatherStrength>0)
    {
        float stains=SurfaceNoise(coords.xy*.65+19)*.65+SurfaceNoise(coords.xy*2.4+77)*.35;
        col*=lerp(1,lerp(.42,1.12,stains),_FinishWeatherStrength);
    }
    return col;
}
half3 RunnerSurface(float3 coords, half3 vertexColor)
{
    if (_CustomHeightPalette > 0.5)
    {
        float h=coords.z;
        if (_TintNoise > 0) h+=(SurfaceNoise(coords.xy/max(_PatchSize,0.01)+83)-0.5)*2*_TintNoise;
        float2 centres=float2(_TintTransitions.x,_TintTransitions.z);
        float2 widths=max(float2(_TintTransitions.y,_TintTransitions.w),0.001);
        // Keep transitions ordered even when entered in reverse in the Inspector.
        if(centres.x>centres.y) {centres=centres.yx; widths=widths.yx;}
        float2 t=saturate((h-centres)/widths+0.5);
        t=lerp(t,t*t*(3-2*t),_TintSmoothness);
        vertexColor=lerp(lerp(_LowHeightColor.rgb,_MidHeightColor.rgb,t.x),_HighHeightColor.rgb,t.y);
    }
    UNITY_BRANCH if (_SurfaceMode < 0.5) return vertexColor;
    float2 uv=coords.xy/max(_Meters,0.01);
    if (_UVWarp > 0 && _SurfaceMode < 2.5)
        uv+=(float2(SurfaceNoise(coords.xy*.7),SurfaceNoise(coords.xy*.7+51))-.5)*_UVWarp;
    float2 dx=ddx(uv),dy=ddy(uv);
    half3 col;
    UNITY_BRANCH if (_SurfaceMode < 1.5)
        col=AtlasSample(uv,_LayerIndices.x,dx,dy);
    else if (_SurfaceMode < 2.5)
    {
        float2 patches=coords.xy/max(_PatchSize,0.01)+float2(_SurfaceSeed*.137,_SurfaceSeed*.319);
        float n=(SurfaceNoise(patches)*.65+SurfaceNoise(patches*3.7+57)*.35)*2-1;
        float h=coords.z+n*_PatchAmount;
        float a=saturate((h-_HeightStops.x)/max(_HeightStops.y-_HeightStops.x,0.001));
        float b=saturate((h-_HeightStops.y)/max(_HeightStops.z-_HeightStops.y,0.001));
        a=a*a*(3-2*a); b=b*b*(3-2*b);
        float3 w=lerp(lerp(_LowWeights.xyz,_MidWeights.xyz,a),_HighWeights.xyz,b);
        w=max(w,0); w/=max(dot(w,float3(1,1,1)),0.0001);
        // A continuous spatial selector produces patches instead of a uniform colour mixture.
        float selector=SurfaceNoise(patches+float2(39.4,17.2))*.7+SurfaceNoise(patches*4.1+73)*.3;
        float width=max(_BlendWidth*0.5,0.005);
        float c0=1-smoothstep(w.x-width,w.x+width,selector);
        float c2=smoothstep(w.x+w.y-width,w.x+w.y+width,selector);
        float3 masks=float3(c0,max(0,1-c0-c2),c2);
        masks*=step(float3(.0001,.0001,.0001),w);
        masks/=max(dot(masks,float3(1,1,1)),0.0001);
        float3 tiling=max(_LayerTiling.xyz,.01);
        col=AtlasSample(uv*tiling.x,_LayerIndices.x,dx*tiling.x,dy*tiling.x)*masks.x
           +AtlasSample(uv*tiling.y,_LayerIndices.y,dx*tiling.y,dy*tiling.y)*masks.y
           +AtlasSample(uv*tiling.z,_LayerIndices.z,dx*tiling.z,dy*tiling.z)*masks.z;
        // Optional fourth construction state: sample only near the exposed foundation.
        UNITY_BRANCH if(_Foundation.x>.5 && h<_Foundation.z+_Foundation.w*.5)
        {
            float foundation=1-smoothstep(_Foundation.z-_Foundation.w*.5,_Foundation.z+_Foundation.w*.5,h);
            col=lerp(col,AtlasSample(uv,_Foundation.y,dx,dy),foundation);
        }
    }
    else
    {
        // Search neighbouring footprints, but sample only the winning piece.
        // Maximum scale is bounded so this 3x3 neighbourhood covers every candidate.
        float2 cell=floor(uv), chosen=cell, local=frac(uv);
        float priority=-100, coverage=0, chosenScale=1;
        [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
        {
            float2 id=cell+float2(x,y);
            float scale=lerp(clamp(_PieceScale.x,0.25,1.8),clamp(_PieceScale.y,0.25,1.8),SurfaceHash(id+71));
            float2 offset=(float2(SurfaceHash(id+111),SurfaceHash(id+222))-.5)*2*_PieceJitter;
            float2 q=(uv-id-0.5-offset)/scale+0.5;
            float edge=min(min(q.x,q.y),min(1-q.x,1-q.y));
            edge+=(SurfaceNoise(coords.xy*5+id)-0.5)*_EdgeRoughness;
            float aa=max(fwidth(uv.x)+fwidth(uv.y),0.001)/scale;
            float mask=smoothstep(0,max(_EdgeSoftness,aa),edge);
            if(SurfaceHash(id+333)<_MissingPieces) mask=0;
            float rank=(_OverlapOrder<0.5 ? scale : -scale)+SurfaceHash(id+9)*0.0001;
            if(mask>0 && rank>priority) { priority=rank; chosen=id; local=q; coverage=mask; chosenScale=scale; }
        }
        float turn=_RotatePieces>0.5 ? floor(SurfaceHash(chosen+31)*4) : 0;
        float2 gx=dx/chosenScale,gy=dy/chosenScale;
        [unroll] for(int r=0;r<3;r++) if(r<turn) { local=float2(1-local.y,local.x); gx=float2(-gx.y,gx.x); gy=float2(-gy.y,gy.x); }
        float count=max(1,floor(_AtlasGrid.x)*floor(_AtlasGrid.y));
        float idx=floor(SurfaceHash(chosen)*count);
        half3 piece=AtlasSample(saturate(local)*0.99999,idx,gx,gy);
        piece*=1+(SurfaceHash(chosen+43)*2-1)*_ColorVariation;
        col=lerp(_BaseColor.rgb,piece,coverage);
    }
    if (_WeatherStrength > 0)
    {
        float stains=SurfaceNoise(coords.xy*.65+19)*.65+SurfaceNoise(coords.xy*2.4+77)*.35;
        col*=lerp(1,lerp(.42,1.12,stains),_WeatherStrength);
    }
    return col*lerp(half3(1,1,1),vertexColor,saturate(_TintStrength));
}
half3 RunnerSurfaceEmission(float2 metres, half3 albedo, float terrainHeight)
{
    UNITY_BRANCH if (_SurfaceEmissionStrength <= 0) return 0;
    float2 uv=metres/max(_EmissionScale,.01);
    float2 q=frac(uv), edge=min(q,1-q);
    float width=max(_EmissionWidth,.002);
    float aa=max(fwidth(uv.x)+fwidth(uv.y),.001);
    float mask=0;
    if(_EmissionPattern<.5) mask=1-smoothstep(width,width+aa,min(edge.x,edge.y));
    else if(_EmissionPattern<1.5)
    {
        float rail=abs(abs(metres.x)-3.2)/max(_EmissionScale,.01);
        mask=1-smoothstep(width,width+aa,rail);
        mask*=step(.15,frac(uv.y*.5));
    }
    else if(_EmissionPattern<2.5)
    {
        float horizontal=(1-smoothstep(width,width+aa,abs(q.y-.5)))*step(q.x,.75);
        float vertical=(1-smoothstep(width,width+aa,abs(q.x-.75)))*step(.5,q.y);
        mask=max(horizontal,vertical)*step(.23,SurfaceHash(floor(uv)));
    }
    else if(_EmissionPattern<3.5)
    {
        float d=abs(length((q-.5)*float2(1,1.25))-.33);
        mask=(1-smoothstep(width,width+aa,d))*step(.1,abs(q.x-.5));
    }
    else
    {
        float chroma=max(albedo.r,max(albedo.g,albedo.b))-min(albedo.r,min(albedo.g,albedo.b));
        mask=smoothstep(.16,.45,chroma);
    }
    float pulse=lerp(1,.65+.35*sin(_Time.y*1.8+metres.y*.4),_EmissionPulse);
    // Short inset light strips follow the deck panel scale. No extra texture samples / geometry.
    UNITY_BRANCH if(_EmissionPattern>5.5)
    {
        mask*=smoothstep(.45,.85,max(albedo.r,max(albedo.g,albedo.b)));
        float2 panelUV=metres/max(_Meters,.01)*float2(2,3);
        float2 id=floor(panelUV);
        float seed=SurfaceHash(id+_SurfaceSeed);
        float2 p=frac(panelUV);
        // Different orientations, inset positions, lengths and installed segments per panel.
        if(seed>.5)p=p.yx;
        if(SurfaceHash(id+31)>.5)p.x=1-p.x;
        float inset=lerp(.1,.26,SurfaceHash(id+67));
        float lengthEnd=lerp(.48,.86,SurfaceHash(id+91));
        float panelAA=max(fwidth(panelUV.x)+fwidth(panelUV.y),.001);
        float stripWidth=max(_EmissionWidth,.006);
        float horizontal=(1-smoothstep(stripWidth,stripWidth+panelAA,abs(p.y-inset)))
            *smoothstep(.14,.18,p.x)*(1-smoothstep(lengthEnd,lengthEnd+.04,p.x));
        float vertical=(1-smoothstep(stripWidth,stripWidth+panelAA,abs(p.x-(1-inset))))
            *smoothstep(.34,.38,p.y)*(1-smoothstep(.77,.81,p.y));
        float shortStrip=(1-smoothstep(stripWidth,stripWidth+panelAA,abs(p.y-.83)))
            *smoothstep(.28,.32,p.x)*(1-smoothstep(.45,.49,p.x));
        float installed=step(.16,SurfaceHash(floor(panelUV)+19));
        vertical*=step(.4,SurfaceHash(id+43));
        shortStrip*=step(.65,SurfaceHash(id+59));
        float neon=max(horizontal,max(vertical,shortStrip))*installed;
        // Bright fitted strips belong mainly to the finished upper deck.
        neon*=smoothstep(.3,.6,terrainHeight);
        return (albedo*mask+_SurfaceEmissionColor.rgb*neon)*(_SurfaceEmissionStrength*pulse);
    }
    // Authored-colour mode follows the sampled circuit colours, including blended wear.
    half3 emissionColor=_EmissionPattern>4.5?albedo:_SurfaceEmissionColor.rgb;
    return emissionColor*(_SurfaceEmissionStrength*mask*pulse);
}
#endif
