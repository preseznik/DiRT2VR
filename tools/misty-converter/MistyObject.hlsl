// Misty-only RGB normal materials, with source ambient on static scenery.
// Channel meanings verified against local ksPerPixelMultiMap(_AT) bytecode.
float2 DynamicAmbientOcclusionInvDim;
float preBakedShadows;
float4 MistyMaterial; // ksSpecular, ksSpecularEXP, useDetail, detailUVMultiplier
float4 MistyFresnel;  // fresnelC, fresnelEXP, fresnelMaxLevel, isAdditive
float4 MistySurface;  // multi-map, alpha test, sunSpecular, sunSpecularEXP
float4 MistyVisibility; // Conservative Daylight, Evening, Morning Fog visibility
float4 MistyLighting; // Authored ksAmbient, ksDiffuse
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
    float4 nightLightmapParam1, nightLightmapParam2, ambientColour;
    float2 trackReflectionScales, shadowBlend;
    float4 maskParams;
};
cbuffer RenderTargetConstantBuffer : register(b2) { float4 viewportDimensions; };
cbuffer AlphaRefConstantBuffer : register(b5) { float4 alphaRef; };
sampler2D TDiffuseAlphaMap : register(s0);
sampler2D TNormalMap : register(s1);
sampler2D TSpecularMap : register(s2);
sampler2D TShadowMask : register(s3);
sampler2D TDynamicAmbientOcclusionMask : register(s4);
sampler2D TMistySourceDiffuse : register(s5);
sampler2D TMistyObjectDetail : register(s6);
float3 safeNormal(float3 v) { return v*rsqrt(max(dot(v,v),1e-12)); }
#ifdef MISTY_NORMAL_PASS
struct Pixel {
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float4 velocity : TEXCOORD2;
    float3 tangent : TEXCOORD3;
    float3 binormal : TEXCOORD4;
};
float4 main(Pixel i) : SV_Target {
    clip(tex2D(TDiffuseAlphaMap,i.uv).a-.5);
    float3 detail=tex2D(TNormalMap,i.uv).rgb*2-1;
    float3 n=safeNormal(i.normal*detail.z+i.tangent*detail.x+i.binormal*detail.y);
    float2 v=i.velocity.yx/i.velocity.w;
    v*=rsqrt(max(1,dot(v,v)));
    return float4(v*.5+.5,n.xz*.5+.5);
}
#else
struct Pixel {
    float4 position : SV_Position;
    float4 fog : COLOR1;
    float4 screen : TEXCOORD0;
    float4 normal : TEXCOORD1;
    float4 uv : TEXCOORD2;
#ifndef MISTY_BOAT
    float3 tangent : TEXCOORD4;
    float3 binormal : TEXCOORD5;
#endif
    float3 viewVector : TEXCOORD3;
#ifdef MISTY_BOAT
    bool frontFace : SV_IsFrontFace;
#else
    float3 ambient : COLOR0;
#endif
};
float4 main(Pixel i) : SV_Target {
    float4 coverage=tex2D(TDiffuseAlphaMap,i.uv.xy);
    clip(coverage.a-alphaRef.x);
    float4 diffuse=tex2D(TMistySourceDiffuse,i.uv.xy);
    float3 maps=tex2D(TSpecularMap,i.uv.xy).rgb;
    float4 detail=tex2D(TMistyObjectDetail,i.uv.xy*MistyMaterial.w);
    if(MistyMaterial.z!=0) {
        diffuse=lerp(diffuse,diffuse*detail,1-diffuse.a);
        maps.r=lerp(maps.r,maps.r*detail.a,1-diffuse.a);
    }
    float3 nm=tex2D(TNormalMap,i.uv.xy).rgb*2-1;
    float3 n=safeNormal(i.normal.xyz);
#ifdef MISTY_BOAT
    n=i.frontFace?-n:n;
    // Native rigid-float vertex motion is retained; reconstruct its UV frame.
    float3 px=ddx(i.viewVector),py=ddy(i.viewVector);float2 ux=ddx(i.uv.xy),uy=ddy(i.uv.xy);
    float orientation=ux.x*uy.y-ux.y*uy.x>=0?1:-1;
    float3 t=(px*uy.y-py*ux.y)*orientation,b=(py*ux.x-px*uy.x)*orientation;
    t-=n*dot(n,t);b-=n*dot(n,b);
    if(min(dot(t,t),dot(b,b))>1e-12)n=safeNormal(n*nm.z+safeNormal(t)*nm.x+safeNormal(b)*nm.y);
#else
    n=safeNormal(n*nm.z+safeNormal(i.tangent)*nm.x+safeNormal(i.binormal)*nm.y);
#endif
    float2 screen=float2(i.screen.x,i.screen.w-i.screen.y)/i.screen.w;
    float4 shadow=tex2D(TShadowMask,screen+viewportDimensions.zw*maskParams.x);
    float fade=saturate(dot(i.viewVector,i.viewVector)*shadowBlend.x+shadowBlend.y);
    float visibility=lerp(shadow.x,preBakedShadows>.5?1:i.normal.w,fade);
#ifndef MISTY_BOAT
    visibility=min(visibility,saturate(i.normal.w)); // Permanent static sun occlusion survives native shadow fade.
#endif
    float facing=dot(n,sunDirection.xyz);
    float3 ambient=skylightColour.rgb*saturate((n.y+.3)/1.3)+backlightColour.rgb*saturate((.3-facing)/1.3)+ambientColour.rgb;
    float3 bakedAmbient=1;
#ifndef MISTY_BOAT
    bakedAmbient=saturate(i.ambient);
#endif
    ambient*=bakedAmbient*MistyLighting.x;
    float directVisibility=lerp(1,visibility,sunColour.w);
#ifndef MISTY_BOAT
    directVisibility=min(directVisibility,saturate(i.normal.w));
#endif
    float3 illumination=ambient*ambientOcclusionScales.w+sunColour.rgb*saturate(facing)*directVisibility*MistyLighting.y+shadow.yzw*maskParams.w;
    float3 lit=diffuse.rgb*illumination;
    float3 light=safeNormal(sunDirection.xyz), toEye=-safeNormal(i.viewVector);
    float3 preset=float3(dot(light,float3(.92718385,.37460659,0)),dot(light,float3(.97814760,.20791169,0)),dot(light,float3(-.98480775,.17364818,0)));
    // Unknown directions and any static occluder fail closed. Never use the
    // distance-faded native visibility for a restored direct highlight.
    float sunVisible=dot(step(.9999,preset),MistyVisibility.rgb)*saturate(shadow.x);
    float nh=saturate(dot(n,safeNormal(light+toEye)));
    float gloss=MistySurface.x!=0?maps.g*MistyMaterial.y+1:MistyMaterial.y;
    float specular=maps.r*MistyMaterial.x*pow(nh,max(1,gloss));
    specular+=maps.g*maps.b*MistySurface.z*pow(nh,max(1,maps.g*MistySurface.w+1));
    lit+=max(sunColour.rgb,0)*specular*sunVisible*saturate(facing);
    // AC's blue channel is environment reflectiveness. Approximate its broad
    // environment response with EGO's ambient/sky lighting, without a sun disk.
    float fresnel=min(MistyFresnel.z,MistyFresnel.x+pow(saturate(1-dot(n,toEye)),max(1,MistyFresnel.y)));
    float3 environment=max(ambient,0)*maps.b;
    if(MistyFresnel.w==1)lit+=environment*fresnel;
    else lit=lerp(lit,environment,fresnel);
    float ao=tex2D(TDynamicAmbientOcclusionMask,screen).x;
    return float4(max(lit*ao*i.fog.w+i.fog.rgb,0),coverage.a);
}
#endif
