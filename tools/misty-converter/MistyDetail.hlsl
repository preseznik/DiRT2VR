// Native terrain vertex ABI and shared lighting buffers. Only albedo composition
// and grass coverage are new; native screen shadows, SSAO and vertex fog remain.
float2 DynamicAmbientOcclusionInvDim;
float4 Map1UVScaleAndOffset;
float4 Map2UVScaleAndOffset;
float4 Map3UVScaleAndOffset;
float4 Map4UVScaleAndOffset;
float4 TrackFresnelParameters;
float4 TrackSpecularParameters;
float DetailBlendPower;
float DetailBlendScale;
float BlendMaskSoftness;
float2 BlendMaskUVScale;
float EdgeSCurveSteepness;
float EdgeSCurveShift;
float EdgeBlendMaskUScale;
float EdgeBlendMaskVScale;
float terrainXFade;
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
    float4 nightLightmapParam1, nightLightmapParam2, ambientColour;
    float2 trackReflectionScales, shadowBlend;
    float4 maskParams;
};
cbuffer RenderTargetConstantBuffer : register(b2) { float4 viewportDimensions; };
cbuffer CameraParamsConstantBuffer : register(b3) {
    float4x4 view, projection, viewProjection, viewI, projectionI, viewIT;
    float4 eyePositionWS;
};
// Legacy combined samplers keep texture and sampler reflection names identical,
// as required by EGO's SHADERINPUTDEFINITION bindings.
sampler2D TShadowMask : register(s0);
sampler2D TDynamicAmbientOcclusionMask : register(s1);
sampler2D TDiffuseSpecMap2 : register(s2);
sampler2D TDiffuseSpecMap3 : register(s3);
sampler2D TBlendMap2 : register(s4);
sampler2D TAmbientOcclusion : register(s5);
sampler2D TMistyDetail : register(s6);
sampler2D TMistyDetailA : register(s7);
sampler2D TMistyNormal : register(s8);
struct Pixel {
    float4 position : SV_Position;
    float4 screen : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float4 unused : TEXCOORD2;
    float4 world : TEXCOORD3;
    float4 uv0 : TEXCOORD4;
    float4 uv1 : TEXCOORD5;
    float4 colour : COLOR0;
    float4 fog : COLOR1;
};
float2 detailUV(float3 p,float4 mapping,float2 uv) {
    return p.xz*mapping.xy+mapping.zw;
}
float4 main(Pixel i) : SV_Target {
    float2 uv=float2(i.uv0.w,i.uv1.w);
    float3 p=i.world.xyz, n=normalize(i.normal);
    float3 albedo;
#ifdef MISTY_GRASS
    float4 texel=tex2D(TDiffuseSpecMap2,uv);
    clip(texel.a-.38);
    float distanceFade=saturate((Map2UVScaleAndOffset.x-length(p-eyePositionWS.xyz))/35);
    // Stable screen-door fade avoids alpha-sort errors between crossing cards.
    float noise=frac(52.9829189*frac(dot(floor(i.position.xy),float2(.06711056,.00583715))));
    clip(distanceFade-noise);
    albedo=texel.rgb*i.colour.rgb;
#else
    float4 weights=tex2D(TBlendMap2,uv);
    float3 first=tex2D(TDiffuseSpecMap2,detailUV(p,Map2UVScaleAndOffset,uv)).rgb;
    float3 second=tex2D(TDiffuseSpecMap3,detailUV(p,Map3UVScaleAndOffset,uv)).rgb;
    float3 third=tex2D(TMistyDetail,detailUV(p,Map4UVScaleAndOffset,uv)).rgb;
    float3 fourth=tex2D(TMistyDetailA,detailUV(p,Map1UVScaleAndOffset,uv)).rgb;
    float3 base=tex2D(TAmbientOcclusion,uv).rgb;
    albedo=first*weights.r+second*weights.g+third*weights.b+fourth*weights.a;
    // Retain authored broad colour instead of treating it as three AO channels.
    albedo*=base*DetailBlendScale;
    if(any(BlendMaskUVScale!=0)) {
        // Reconstruct the source UV frame: the stock terrain vertex program does
        // not carry AC tangents, but world/UV derivatives retain their orientation.
        float3 px=ddx(p),py=ddy(p);float2 ux=ddx(uv),uy=ddy(uv);
        float orientation=ux.x*uy.y-ux.y*uy.x>=0?1:-1;
        float3 tangent=(px*uy.y-py*ux.y)*orientation;
        float3 bitangent=(py*ux.x-px*uy.x)*orientation;
        tangent=normalize(tangent-n*dot(n,tangent));
        bitangent=normalize(bitangent-n*dot(n,bitangent));
        float3 detail=tex2D(TMistyNormal,uv*BlendMaskUVScale).rgb*2-1;
        n=normalize(tangent*detail.x+bitangent*detail.y+n*detail.z);
    }
#endif
    float2 screen=float2(i.screen.x,i.screen.w-i.screen.y)/i.screen.w;
    float ao=tex2D(TDynamicAmbientOcclusionMask,screen).x;
    float4 shadow=tex2D(TShadowMask,screen+viewportDimensions.zw*maskParams.x);
    float cameraDistance=length(p-eyePositionWS.xyz);
    float shadowFade=saturate(cameraDistance*cameraDistance*shadowBlend.x+shadowBlend.y);
    float lightVisibility=lerp(shadow.x,1,shadowFade);
    float sunVisibility=lerp(1,lightVisibility,sunColour.w);
    float3 baked=shadow.yzw*maskParams.w;
    float3 bounce=maskParams.w==0 ? (shadow.yzw*2-1)*(lightVisibility*.5+.5) : 0;
    float facing=dot(n,sunDirection.xyz);
    float3 illumination=sunColour.rgb*saturate(facing)*sunVisibility+baked;
    illumination+=skylightColour.rgb*saturate((n.y+.3)/1.3);
    illumination+=backlightColour.rgb*saturate((.3-facing)/1.3)+ambientColour.rgb;
    illumination=max(illumination+bounce,0);
    float3 lit=albedo*illumination*ao;
    // Explicitly dry: no diffuse-alpha specular term, including hidden-sun views.
    return float4(max(lit*i.fog.w+i.fog.rgb,0),1);
}
