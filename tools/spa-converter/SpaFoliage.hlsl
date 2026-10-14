// Source card centres are baked into world space; ST1 is the expansion vector.
// Keep the native object_simple pixel/depth interfaces and automatic buffers.
float4x4 model, modelViewProj, modelView, prevViewProj, prevModel;
float2 DynamicAmbientOcclusionInvDim;
float preBakedShadows;
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
    float4 nightLightmapParam1, nightLightmapParam2, ambientColour;
    float2 trackReflectionScales, shadowBlend; float4 maskParams;
};
cbuffer RenderTargetConstantBuffer : register(b2) { float4 viewportDimensions; };
cbuffer CameraParamsConstantBuffer : register(b3) {
    float4x4 view, projection, viewProjection, viewI, projectionI, viewIT;
    float4 eyePositionWS;
};
struct Vertex { float3 position:POSITION0; float2 uv:TEXCOORD0; float2 offset:TEXCOORD1; float3 normal:NORMAL0; };
struct Pixel {
    float4 position:SV_Position;
#if defined(SPA_VELOCITY)
    float4 velocity:TEXCOORD0; float2 uv:TEXCOORD1;
#elif defined(SPA_NORMAL_DEPTH)
    float3 normal:TEXCOORD0; float2 uv:TEXCOORD1; float4 velocity:TEXCOORD2;
#elif defined(SPA_SHADOW)
    float2 uv:TEXCOORD0;
#else
    float4 fog:COLOR1; float4 screen:TEXCOORD0; float4 normal:TEXCOORD1;
    float4 uv:TEXCOORD2; float3 eye:TEXCOORD3;
#endif
};
Pixel main(Vertex i) {
    Pixel o=(Pixel)0;
    float3 p=i.position, n;
#ifdef SPA_FACING
    // GRID 2 facing_foliage uses both camera axes. Instance scale has already
    // been applied to ST1 once, just as it is to the baked centre.
    p+=mul((float3x3)viewI,float3(i.offset,0));
    n=normalize(mul((float3x3)viewI,float3(0,0,1)));
#else
    // treesheet_foliage offsets are already in world metres. Stay upright.
    float3 towards=eyePositionWS.xyz-p;
    float2 horizontal=towards.xz;
    horizontal=dot(horizontal,horizontal)>1e-8?normalize(horizontal):float2(0,1);
    float3 right=float3(horizontal.y,0,-horizontal.x);
    p+=right*i.offset.x+float3(0,i.offset.y,0);
    n=float3(horizontal.x,0,horizontal.y);
#endif
    o.position=mul(viewProjection,float4(p,1));
#if defined(SPA_VELOCITY) || defined(SPA_NORMAL_DEPTH)
    float4 previous=mul(prevViewProj,float4(p,1));
    o.velocity=float4((o.position.xy*previous.w-previous.xy*o.position.w)*viewportDimensions.xy*float2(.025,-.025),
        -mul(view,float4(p,1)).z,previous.w*o.position.w);
    o.uv=i.uv;
#ifdef SPA_NORMAL_DEPTH
    o.normal=n;
#endif
#elif defined(SPA_SHADOW)
    o.uv=i.uv;
#else
    float3 toPoint=p-eyePositionWS.xyz;
    float distance=max(length(toPoint),1e-6);
    float hazePower=pow(saturate(dot(toPoint/distance,sunDirection.xyz)),hazeParams2.w);
    float3 hazeColour=lerp(hazeParams.rgb,hazeParams2.rgb,hazePower);
    float hazeAmount=1-exp(max(distance-1/fogParams.z,0)*hazeParams.w);
    float baseAmount=saturate((distance-30)*fogParams.z)*fogParams.w;
    float4 fog=lerp(float4(fogColour.rgb,baseAmount),float4(hazeColour,hazeAmount),hazeAmount);
    o.fog=float4(fog.rgb*fog.w,1-fog.w);
    o.screen=float4((o.position.xy+o.position.w)*.5,o.position.zw);
    o.normal=float4(n,1);o.uv=float4(i.uv,.5,.5);o.eye=toPoint;
#endif
    return o;
}
