// GRID 2 dry terrain albedo equations with the DiRT 2 lighting/fog interface.
// Reference: the installed games' disassembly retained in fidelity-inspect.
float2 DynamicAmbientOcclusionInvDim;
float4 Map1UVScaleAndOffset,Map2UVScaleAndOffset,Map3UVScaleAndOffset,Map4UVScaleAndOffset;
float4 TrackFresnelParameters,TrackSpecularParameters;
float DetailBlendPower,DetailBlendScale,BlendMaskSoftness;
float2 BlendMaskUVScale;
float EdgeSCurveSteepness,EdgeSCurveShift,EdgeBlendMaskUScale,EdgeBlendMaskVScale,terrainXFade;
float ColourMapScale;
float4 TColourMapChannelScale,TColourMapChannelMin;
float4 SourceLighting; // indirect intensity, direct intensity, light wrap, baked shadow enabled
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection,sunColour,skylightColour,ambientOcclusionScales;
    float4 backlightColour,specularScales,specularDirection,specularColour;
    float4 fogColour,fogParams,hazeParams,hazeParams2;
    float4 nightLightmapParam1,nightLightmapParam2,ambientColour;
    float2 trackReflectionScales,shadowBlend;
    float4 maskParams;
};
cbuffer RenderTargetConstantBuffer : register(b2) { float4 viewportDimensions; };
cbuffer CameraParamsConstantBuffer : register(b3) {
    float4x4 view,projection,viewProjection,viewI,projectionI,viewIT;
    float4 eyePositionWS;
};
sampler2D TShadowMask : register(s0);
sampler2D TDynamicAmbientOcclusionMask : register(s1);
#ifndef SPA_SIMPLE
sampler2D TDiffuseSpecMap2 : register(s2);
sampler2D TDiffuseSpecMap3 : register(s3);
#ifdef SPA_ROAD
sampler2D TBlendMap3 : register(s4);
#else
sampler2D TBlendMap2 : register(s4);
#endif
#endif
sampler2D TAmbientOcclusion : register(s5);
#if defined(SPA_EDGE) || defined(SPA_ROAD) || defined(SPA_SIMPLE)
sampler2D TDiffuseSpecMap1 : register(s6);
#endif
sampler2D TColourMap : register(s8);
struct Pixel {
    float4 position : SV_Position;
    float4 screen : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float4 uv : TEXCOORD2;
    float4 world : TEXCOORD3;
    float4 tangent : TEXCOORD4;
    float4 binormal : TEXCOORD5;
    float4 colour : COLOR0;
    float4 fog : COLOR1;
};
float2 WorldUV(float2 xz,float4 m) { return float2(xz.x/m.x+m.z,1-(xz.y/m.y+m.w)); }
float2 MeshUV(float2 uv,float4 m) { return float2(uv.x/m.x+m.z,1-((1-uv.y)/m.y+m.w)); }
float2 ScaledMeshUV(float2 uv,float4 m) { return float2(uv.x*m.x+m.z,1-((1-uv.y)*m.y+m.w)); }
float4 Albedo(Pixel i) {
#if defined(SPA_ROAD) || defined(SPA_SIMPLE)
    float4 texel=tex2D(TDiffuseSpecMap1,ScaledMeshUV(i.uv.xy,Map1UVScaleAndOffset));
#ifdef SPA_ROAD
    float2 uv3=ScaledMeshUV(i.uv.zw,Map3UVScaleAndOffset);
    float2 mask=tex2D(TBlendMap3,uv3).rg;
    // GRID 2 encodes the third layer's channel choice AND strength in green.
    // Green 0.5 disables it; endpoints select red or green at full strength.
    float blend=(i.colour.g<=.5?mask.r:mask.g)*abs(2*i.colour.g-1);
    texel=lerp(lerp(texel,tex2D(TDiffuseSpecMap2,ScaledMeshUV(i.uv.xy,Map2UVScaleAndOffset)),i.colour.b),tex2D(TDiffuseSpecMap3,uv3),blend);
#endif
#else
#ifdef SPA_TRACK
    float2 uv2=MeshUV(i.uv.xy,Map2UVScaleAndOffset),uv3=MeshUV(i.uv.xy,Map3UVScaleAndOffset);
    float2 maskUV=uv2*BlendMaskUVScale;
#else
    float2 uv2=WorldUV(i.world.xz,Map2UVScaleAndOffset),uv3=WorldUV(i.world.xz,Map3UVScaleAndOffset);
    float2 maskUV=float2(uv2.x*BlendMaskUVScale.x,1-(1-uv2.y)*BlendMaskUVScale.y);
#endif
#ifdef SPA_EDGE
    float mask=tex2D(TBlendMap2,maskUV).b;
#else
    float mask=tex2D(TBlendMap2,maskUV).g;
#endif
    float blend=saturate(saturate(mask*(1+BlendMaskSoftness)-.5*BlendMaskSoftness)*i.colour.g);
    float4 texel=lerp(tex2D(TDiffuseSpecMap2,uv2),tex2D(TDiffuseSpecMap3,uv3),blend);
#ifdef SPA_EDGE
    float2 uv1=float2(i.uv.x*Map1UVScaleAndOffset.x+Map1UVScaleAndOffset.z,1-((1-i.uv.y)*Map1UVScaleAndOffset.y+Map1UVScaleAndOffset.w));
    float edgeMask=tex2D(TBlendMap2,float2(uv2.x*EdgeBlendMaskUScale,1-(1-uv2.y)*EdgeBlendMaskVScale)).g;
    float edge=max(i.colour.r,i.colour.g);
    float curve=pow(abs(2*(edge-.5)),8)+edgeMask;
    float edgeBlend=1/(1+exp(-(EdgeSCurveSteepness*edge*curve-.5*EdgeSCurveSteepness+EdgeSCurveShift)));
    texel=lerp(tex2D(TDiffuseSpecMap1,uv1),texel,edgeBlend);
#endif
#endif
    float2 lightUV=float2(i.tangent.w,i.binormal.w);
    texel.rgb*=(tex2D(TColourMap,lightUV).rgb*TColourMapChannelScale.rgb+TColourMapChannelMin.rgb)*ColourMapScale;
    return texel;
}
float4 main(Pixel i) : SV_Target {
    float4 texel=Albedo(i);
#ifdef SPA_ALBEDO_TEST
    return texel;
#else
    // GRID 2 GIS stores coloured indirect illumination in RGB and sunlight
    // visibility in alpha. Alpha must NEVER occlude the indirect term, and
    // indirect light is additive illumination, not a multiplier on albedo.
    float4 gi=tex2D(TAmbientOcclusion,float2(i.tangent.w,i.binormal.w));
    float3 delta=i.world.xyz-eyePositionWS.xyz;
    float distanceSquared=dot(delta,delta);
    float3 eyeDirection=delta*rsqrt(max(distanceSquared,1e-8));
    float fade=saturate(distanceSquared*shadowBlend.x+shadowBlend.y);
    float facing=dot(eyeDirection,i.normal);
    float fresnel=lerp(TrackFresnelParameters.x,TrackFresnelParameters.y,min(pow(max(1+facing,0),TrackFresnelParameters.z),1))*TrackSpecularParameters.x;
    float3 reflection=eyeDirection-2*facing*i.normal;
    // Spa's source day profile disables GIS ambient occlusion.
    float3 specular=specularColour.rgb*pow(saturate(dot(reflection,specularDirection.xyz)),TrackSpecularParameters.y)*texel.a*fresnel;
    float2 screen=float2(i.screen.x,i.screen.w-i.screen.y)/i.screen.w;
    float dynamicAO=tex2D(TDynamicAmbientOcclusionMask,screen).r;
    float4 shadow=tex2D(TShadowMask,screen+viewportDimensions.zw*maskParams.x);
    float visibility=lerp(shadow.r,lerp(1,gi.a,SourceLighting.w),fade);
    float sunVisibility=1+sunColour.w*(visibility-1);
    float3 normalLight=(maskParams.w==0?shadow.gba*2-1:0)*(.5*visibility+.5);
    float sunFacing=dot(i.normal,sunDirection.xyz);
    float3 light=gi.rgb*SourceLighting.x;
    light+=saturate(sunFacing*(1-SourceLighting.z)+SourceLighting.z)*sunColour.rgb*sunVisibility*SourceLighting.y;
    light=max(light+shadow.gba*maskParams.w+normalLight,0);
    float3 lit=(texel.rgb*light+specular*sunVisibility)*dynamicAO;
    return float4(max(lit*i.fog.w+i.fog.rgb,0),1);
#endif
}
