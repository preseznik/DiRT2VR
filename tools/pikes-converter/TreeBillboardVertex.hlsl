// Source tree_lowlod: eight directional atlas views blended around the tree's
// authored axes. Centres, orientation and card extents are baked per placement.
cbuffer PerFrameConstantBuffer : register(b1) {
    float4 sunDirection, sunColour, skylightColour, ambientOcclusionScales;
    float4 backlightColour, specularScales, specularDirection, specularColour;
    float4 fogColour, fogParams, hazeParams, hazeParams2;
};
cbuffer CameraParamsConstantBuffer : register(b3) {
    float4x4 view, projection, viewProjection, viewI, projectionI, viewIT;
    float4 eyePositionWS;
};
struct Vertex { float3 position:POSITION0; float2 uv:TEXCOORD0; float2 offset:TEXCOORD1; float2 detail:TEXCOORD2; float3 up:NORMAL0; float3 axisX:TANGENT0; };
struct Pixel {
    float4 position:SV_Position; float4 fog:COLOR0; float4 uv:TEXCOORD0;
    float3 axisX:TEXCOORD1; float3 up:TEXCOORD2; float3 axisZ:TEXCOORD3;
    float3 world:TEXCOORD4; float2 blendHeight:TEXCOORD5; float fade:TEXCOORD6;
};
Pixel main(Vertex i) {
    Pixel o;
    float3 up=normalize(i.up), axisX=normalize(i.axisX), axisZ=normalize(cross(axisX,up));
#ifdef BILLBOARD_SHADOW
    float3 towards=normalize(sunDirection.xyz);
#else
    float3 towards=normalize(eyePositionWS.xyz-i.position);
#endif
    float3 side=cross(up,towards);
    float3 right=dot(side,side)>0.000001?normalize(side):axisX;
    float3 p=i.position+right*i.offset.x+up*i.offset.y;
    float angle=atan2(dot(axisZ,towards),dot(axisX,towards));
    float sector=-angle*1.273239544735;
    float2 views=frac(ceil(sector+float2(9,10))*.125);
    o.uv=float4(i.uv+float2(views.x,0),i.uv+float2(views.y,0));
    o.blendHeight=float2(frac(sector),i.offset.y+abs(i.offset.y));
    o.axisX=axisX;o.up=up;o.axisZ=axisZ;o.world=p;
    o.position=mul(viewProjection,float4(p,1));
#ifdef BILLBOARD_SHADOW
    o.fade=1;
#else
    o.fade=1-i.detail.x*saturate((length(eyePositionWS.xyz-i.position)-600)/100);
#endif
    // Fully faded cards produce no rasterized pixels, including depth/normal passes.
    if(o.fade<=0)o.position=float4(0,0,0,1);
    float3 toPoint=p-eyePositionWS.xyz;
    float distance=max(length(toPoint),.0001);
    float hazePower=pow(saturate(dot(toPoint/distance,sunDirection.xyz)),hazeParams2.w);
    float3 hazeColour=lerp(hazeParams.rgb,hazeParams2.rgb,hazePower);
    float hazeAmount=1-exp(max(distance-1/fogParams.z,0)*hazeParams.w);
    float baseAmount=saturate((distance-30)*fogParams.z)*fogParams.w;
    float4 fog=lerp(float4(fogColour.rgb,baseAmount),float4(hazeColour,hazeAmount),hazeAmount);
    o.fog=float4(fog.rgb*fog.w,1-fog.w);
    return o;
}
