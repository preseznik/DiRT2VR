// Imported static scenery is already in world space. Keep the native object
// projection, fog and pixel ABI, adding only the source ambient vertex colour.
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
    float4 nightLightmapParam1, nightLightmapParam2, ambientColour;
    float2 trackReflectionScales, shadowBlend;
    float4 maskParams;
};
cbuffer CameraParamsConstantBuffer : register(b3) {
    float4x4 view, projection, viewProjection, viewI, projectionI, viewIT;
    float4 eyePositionWS;
};
struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float3 normal : NORMAL;
    float3 tangent : TANGENT;
    float3 binormal : BINORMAL;
    float4 ambient : COLOR0;
};
struct Pixel {
    float4 position : SV_Position;
    float4 fog : COLOR1;
    float4 screen : TEXCOORD0;
    float4 normal : TEXCOORD1;
    float4 uv : TEXCOORD2;
    float3 tangent : TEXCOORD4;
    float3 binormal : TEXCOORD5;
    float3 viewVector : TEXCOORD3;
    float3 ambient : COLOR0;
};
float3 safeNormal(float3 v) { return v*rsqrt(max(dot(v,v),1e-12)); }
Pixel main(Vertex i) {
    Pixel o;
    o.position=mul(viewProjection,float4(i.position,1));
    o.screen=float4((o.position.xy+o.position.ww)*.5,o.position.zw);
    float3 light=safeNormal(sunDirection.xyz);
    float3 preset=step(.9999,float3(dot(light,float3(.92718385,.37460659,0)),dot(light,float3(.97814760,.20791169,0)),dot(light,float3(-.98480775,.17364818,0))));
    // Decode the three fixed-sun bits before interpolation. Packed colour Z is
    // alpha; RGB remains the source ambient shading. No extra vertex stream.
    float bits=round(i.ambient.z*255);
    float3 sunlight=fmod(floor(bits/float3(1,2,4)),2);
    o.normal=float4(safeNormal(i.normal),dot(sunlight,preset));
    o.uv=float4(i.uv,i.uv1);
    o.tangent=safeNormal(i.tangent);o.binormal=safeNormal(i.binormal);
    o.viewVector=i.position-eyePositionWS.xyz;
    float distance=length(o.viewVector);
    float haze=1-exp(max(0,distance-1/fogParams.z)*hazeParams.w);
    float facing=pow(saturate(dot(safeNormal(o.viewVector),sunDirection.xyz)),hazeParams2.w);
    float4 fog=lerp(float4(fogColour.rgb,saturate((distance-30)*fogParams.z)*fogParams.w),
                    float4(lerp(hazeParams.rgb,hazeParams2.rgb,facing),haze),haze);
    o.fog=float4(fog.rgb*fog.w,1-fog.w);
    // EGO swaps the packed uint_color_argb before its B8G8R8A8 input layout:
    // the shader receives G,R,A,B. Decode RGB; alpha is not ambient light.
    o.ambient=i.ambient.yxw;
    return o;
}
