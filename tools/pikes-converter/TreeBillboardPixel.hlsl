sampler2D TDiffuseAlphaMap : register(s0);
#if !defined(BILLBOARD_DEPTH)
sampler2D TNormalMap : register(s6);
#endif
#if !defined(BILLBOARD_DEPTH) && !defined(BILLBOARD_NORMAL)
sampler2D TTreeSkyLightMapWS : register(s1);
sampler2D TShadowMask : register(s7);
float4 SkyMapMapping;
float3 TreeAmbientColour;
float TreeDiffuseScale,TreeShadowing,TreeLightWrapping;
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
    float4 nightLightmapParam1, nightLightmapParam2, ambientColour;
    float2 trackReflectionScales, shadowBlend;
    float4 maskParams;
};
cbuffer RenderTargetConstantBuffer : register(b2) { float4 viewportDimensions; };
#endif
struct Pixel {
    float4 position:SV_Position; float4 fog:COLOR0; float4 uv:TEXCOORD0;
    float3 axisX:TEXCOORD1; float3 up:TEXCOORD2; float3 axisZ:TEXCOORD3;
    float3 world:TEXCOORD4; float2 blendHeight:TEXCOORD5; float fade:TEXCOORD6;
};
float4 main(Pixel i):SV_Target {
    // World-space stippling is stable across frames and shared by both eyes.
    float threshold=frac(dot(floor(i.world*8),float3(.754877666,.569840296,.438289)));
    if(i.fade<1)clip(i.fade-threshold-.00001);
    float4 texel=lerp(tex2Dbias(TDiffuseAlphaMap,float4(i.uv.xy,0,-1)),tex2Dbias(TDiffuseAlphaMap,float4(i.uv.zw,0,-1)),i.blendHeight.x);
    clip(texel.a-.35);
#ifdef BILLBOARD_DEPTH
    return float4(.5,.5,0,0);
#else
    // Source atlas normals store local XYZ in A,B,G, with Z reversed.
    float3 local=lerp(tex2D(TNormalMap,i.uv.xy).abg,tex2D(TNormalMap,i.uv.zw).abg,i.blendHeight.x)*2-1;
    float3 n=normalize(i.axisX*local.x+i.up*local.y-i.axisZ*local.z);
#ifdef BILLBOARD_NORMAL
    return float4(.5,.5,n.xz*.5+.5);
#else
    float4 sky=tex2D(TTreeSkyLightMapWS,i.world.xz*SkyMapMapping.xy+SkyMapMapping.zw);
    float3 ambient=sky.rgb*TreeAmbientColour;
    float sourceSun=lerp(1,i.blendHeight.y>sky.a*255?1:0,TreeShadowing);
    float visibility=1+sunColour.w*(tex2D(TShadowMask,i.position.xy*viewportDimensions.zw+maskParams.x*viewportDimensions.zw).r-1);
    float facing=saturate(dot(n,normalize(sunDirection.xyz+n*TreeLightWrapping)));
    float3 light=lerp(ambient,sunColour.rgb*TreeDiffuseScale,facing*visibility*sourceSun);
    return float4(max(texel.rgb*max(light,0)*i.fog.w+i.fog.rgb,0),1);
#endif
#endif
}
