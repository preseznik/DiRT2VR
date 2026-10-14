// Restore the authored first frame of Spa's event screens. The GRID 2 wipe
// animation is not emulated; keep its atlas intact for a later animation port.
float ScreenBrightness;
float2 ScreenFrames;
sampler2D TDiffuseAlphaMap : register(s0);
struct Pixel {
    float4 position : SV_Position;
    float4 fog : COLOR1;
    float4 screen : TEXCOORD0;
    float4 normal : TEXCOORD1;
    float4 uv : TEXCOORD2;
    float3 eye : TEXCOORD3;
};
float4 main(Pixel i) : SV_Target {
    float2 uv=i.uv.xy/ScreenFrames;
    return float4(max(tex2D(TDiffuseAlphaMap,uv).rgb*ScreenBrightness*i.fog.w+i.fog.rgb,0),1);
}
