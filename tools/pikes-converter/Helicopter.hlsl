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
float timer,FlightIndex,RotorEnabled;
float3 Rotate(float4 q,float3 v){return v+2*cross(q.xyz,cross(q.xyz,v)+q.w*v);}
// SOURCE_FLIGHTS
struct Vertex {float3 position:POSITION;float2 uv:TEXCOORD0;float3 normal:NORMAL;};
struct Pixel {float4 position:SV_Position;float4 fog:COLOR1;float4 screen:TEXCOORD0;float4 normal:TEXCOORD1;float4 uv:TEXCOORD2;float3 toPoint:TEXCOORD3;};
Pixel main(Vertex i){
 Pixel o;float3 p=i.position,n=i.normal;
 if(FlightIndex<.5)Flight0(p,n);else if(FlightIndex<1.5)Flight1(p,n);else if(FlightIndex<2.5)Flight2(p,n);else Flight3(p,n);
 o.position=mul(viewProjection,float4(p,1));
 o.normal=float4(normalize(n),1);
 float frame=floor(frac(timer*6)*3);
 o.uv=float4(RotorEnabled>.5?(i.uv.x+frame)/3:i.uv.x,i.uv.y,0,0);
 o.screen=float4((o.position.xy+o.position.ww)*.5,o.position.zw);
 o.toPoint=p-eyePositionWS.xyz;
 float distance=length(o.toPoint);
 float hazePower=pow(saturate(dot(normalize(o.toPoint),sunDirection.xyz)),hazeParams2.w);
 float3 hazeColour=lerp(hazeParams.rgb,hazeParams2.rgb,hazePower);
 float hazeAmount=1-exp(max(distance-1/fogParams.z,0)*hazeParams.w);
 float baseAmount=saturate((distance-30)*fogParams.z)*fogParams.w;
 float4 fog=lerp(float4(fogColour.rgb,baseAmount),float4(hazeColour,hazeAmount),hazeAmount);
 o.fog=float4(fog.rgb*fog.w,1-fog.w);
 return o;
}
