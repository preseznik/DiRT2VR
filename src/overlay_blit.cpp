#include "overlay_blit.h"
#include <d3dcompiler.h>
#include <cstring>

namespace vr {
using Microsoft::WRL::ComPtr;
bool OverlayBlit::Prepare(ID3D11Device* device) {
    if(device_.Get()!=device) {
        *this=OverlayBlit{};device_=device;
    }
    if(attempted_)return ready_;
    attempted_=true;
    ComPtr<ID3D11Device1> device1;ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&device1))) || FAILED(context.As(&context_)))return false;
    const D3D_FEATURE_LEVEL level=device->GetFeatureLevel();
    if(FAILED(device1->CreateDeviceContextState(0,&level,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),nullptr,&state_)))return false;
    constexpr char source[]=R"(
        Texture2D<float4> image:register(t0);
        SamplerState linearClamp:register(s0);
        struct V { float4 position:SV_Position; float2 uv:TEXCOORD0; };
        V vs(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1); return v; }
        float4 ps(V v):SV_Target { return image.SampleLevel(linearClamp,v.uv,0); })";
    ComPtr<ID3DBlob> vertex,pixel;
    if(FAILED(D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"vs","vs_5_0",0,0,&vertex,nullptr)) ||
       FAILED(D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"ps","ps_5_0",0,0,&pixel,nullptr)) ||
       FAILED(device->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&vs_)) ||
       FAILED(device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps_)))return false;
    // Premultiplied colour over the frame; the frame's alpha is left as it was.
    D3D11_BLEND_DESC blend{};auto& rt=blend.RenderTarget[0];
    rt.BlendEnable=TRUE;rt.SrcBlend=D3D11_BLEND_ONE;rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOp=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=D3D11_BLEND_ZERO;rt.DestBlendAlpha=D3D11_BLEND_ONE;rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;
    rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    D3D11_RASTERIZER_DESC raster{};raster.FillMode=D3D11_FILL_SOLID;raster.CullMode=D3D11_CULL_NONE;raster.DepthClipEnable=TRUE;
    D3D11_DEPTH_STENCIL_DESC depth{};depth.DepthFunc=D3D11_COMPARISON_ALWAYS;
    D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampler.MaxLOD=D3D11_FLOAT32_MAX;
    if(FAILED(device->CreateBlendState(&blend,&blend_)) || FAILED(device->CreateRasterizerState(&raster,&raster_)) ||
       FAILED(device->CreateDepthStencilState(&depth,&depth_)) || FAILED(device->CreateSamplerState(&sampler,&sampler_)))return false;
    return ready_=true;
}
bool OverlayBlit::Draw(ID3D11RenderTargetView* target,ID3D11Texture2D* image,float x,float y,float width,float height) {
    if(!target || !image || width<=0 || height<=0)return false;
    ComPtr<ID3D11Device> device;target->GetDevice(&device);
    if(!Prepare(device.Get()))return false;
    if(source_.Get()!=image) {
        view_.Reset();
        if(FAILED(device->CreateShaderResourceView(image,nullptr,&view_)))return false;
        source_=image;
    }
    ComPtr<ID3DDeviceContextState> previous;
    context_->SwapDeviceContextState(state_.Get(),&previous);
    auto* srv=view_.Get();auto* sampler=sampler_.Get();
    D3D11_VIEWPORT viewport{x,y,width,height,0,1};
    context_->IASetInputLayout(nullptr);context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->VSSetShader(vs_.Get(),nullptr,0);context_->PSSetShader(ps_.Get(),nullptr,0);
    context_->RSSetState(raster_.Get());context_->RSSetViewports(1,&viewport);
    context_->OMSetDepthStencilState(depth_.Get(),0);context_->OMSetBlendState(blend_.Get(),nullptr,0xffffffff);
    context_->PSSetShaderResources(0,1,&srv);context_->PSSetSamplers(0,1,&sampler);
    context_->OMSetRenderTargets(1,&target,nullptr);
    context_->Draw(3,0);
    ID3D11ShaderResourceView* none{};context_->PSSetShaderResources(0,1,&none);
    context_->OMSetRenderTargets(0,nullptr,nullptr);
    context_->SwapDeviceContextState(previous.Get(),nullptr);
    return true;
}
}
