#include "eye_blit.h"
#include <d3dcompiler.h>
#include <cstring>
using Microsoft::WRL::ComPtr;

bool EyeBlit::Initialize(ID3D11Device* device) {
    if(!device || device_) return false;
    ComPtr<ID3D11Device1> device1;
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&device1))) || FAILED(context.As(&context_))) return false;
    auto level=device->GetFeatureLevel();
    if(FAILED(device1->CreateDeviceContextState(0,&level,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),nullptr,&state_))) return false;
    const char* shader=R"(
Texture2D image:register(t0); SamplerState linearClamp:register(s0);
struct V { float4 position:SV_Position; float2 uv:TEXCOORD; };
V vs(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1); return v; }
float4 ps(V v):SV_Target {
    float3 c=image.Sample(linearClamp,v.uv).rgb;
    return float4(lerp(c/12.92,pow((c+0.055)/1.055,2.4),step(0.04045,c)),1);
}
float4 alphaPs(V v):SV_Target {
    float4 texel=image.Sample(linearClamp,v.uv);
    float3 c=texel.a>0.00001 ? saturate(texel.rgb/texel.a) : 0;
    return float4(lerp(c/12.92,pow((c+0.055)/1.055,2.4),step(0.04045,c)),texel.a);
})";
    ComPtr<ID3DBlob> vertex,pixel,alpha,error;
    if(FAILED(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"vs","vs_5_0",0,0,&vertex,&error)) ||
       FAILED(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"ps","ps_5_0",0,0,&pixel,&error)) ||
       FAILED(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"alphaPs","ps_5_0",0,0,&alpha,&error))) return false;
    if(FAILED(device->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&vs_)) ||
       FAILED(device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps_)) ||
       FAILED(device->CreatePixelShader(alpha->GetBufferPointer(),alpha->GetBufferSize(),nullptr,&alphaPs_))) return false;
    D3D11_SAMPLER_DESC sample{}; sample.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sample.AddressU=sample.AddressV=sample.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP; sample.MaxLOD=D3D11_FLOAT32_MAX;
    D3D11_RASTERIZER_DESC raster{}; raster.FillMode=D3D11_FILL_SOLID; raster.CullMode=D3D11_CULL_NONE; raster.DepthClipEnable=TRUE;
    D3D11_DEPTH_STENCIL_DESC depth{}; depth.DepthFunc=D3D11_COMPARISON_ALWAYS;
    if(FAILED(device->CreateSamplerState(&sample,&sampler_)) || FAILED(device->CreateRasterizerState(&raster,&raster_)) ||
       FAILED(device->CreateDepthStencilState(&depth,&depth_))) return false;
    device_=device; return true;
}
bool EyeBlit::Draw(unsigned eye,ID3D11Texture2D* source,ID3D11RenderTargetView* target,unsigned width,unsigned height,bool alpha) {
    if(!device_ || eye>=sources_.size() || !source || !target || !width || !height) return false;
    if(sources_[eye].Get()!=source) {
        ComPtr<ID3D11ShaderResourceView> view;
        if(FAILED(device_->CreateShaderResourceView(source,nullptr,&view))) return false;
        sources_[eye]=source; views_[eye]=view;
    }
    ComPtr<ID3DDeviceContextState> previous;
    context_->SwapDeviceContextState(state_.Get(),&previous);
    context_->OMSetRenderTargets(1,&target,nullptr); context_->OMSetDepthStencilState(depth_.Get(),0);
    context_->OMSetBlendState(nullptr,nullptr,0xffffffff);
    D3D11_VIEWPORT viewport{0,0,static_cast<float>(width),static_cast<float>(height),0,1};
    context_->RSSetViewports(1,&viewport); context_->RSSetState(raster_.Get());
    context_->IASetInputLayout(nullptr); context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->VSSetShader(vs_.Get(),nullptr,0); context_->PSSetShader(alpha ? alphaPs_.Get() : ps_.Get(),nullptr,0);
    auto view=views_[eye].Get(); auto sampler=sampler_.Get();
    context_->PSSetShaderResources(0,1,&view); context_->PSSetSamplers(0,1,&sampler);
    context_->Draw(3,0);
    context_->OMSetRenderTargets(0,nullptr,nullptr);
    view=nullptr; context_->PSSetShaderResources(0,1,&view);
    context_->SwapDeviceContextState(previous.Get(),nullptr);
    return true;
}
