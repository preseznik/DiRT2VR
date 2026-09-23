#include "hud_capture.h"
#include <array>
#include <cstring>
using Microsoft::WRL::ComPtr;

bool HudCapture::Begin(ID3D11Texture2D* back,uint64_t frame) {
    active_=ready_=false; draws_=0;
    if(!back) return false;
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    if(desc.SampleDesc.Count!=1 || desc.ArraySize!=1 || desc.MipLevels!=1) return false;
    ComPtr<ID3D11Device> device; back->GetDevice(&device);
    if(back_.Get()!=back) {
        desc.Usage=D3D11_USAGE_DEFAULT; desc.CPUAccessFlags=desc.MiscFlags=0;
        desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> image; ComPtr<ID3D11RenderTargetView> target;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&image)) || FAILED(device->CreateRenderTargetView(image.Get(),nullptr,&target))) return false;
        image_=image; target_=target; back_=back; width_=desc.Width; height_=desc.Height;
        blend_.Reset(); depth_.Reset();
    }
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    const float clear[4]{}; context->ClearRenderTargetView(target_.Get(),clear);
    frame_=frame; active_=true; return true;
}
void HudCapture::End(bool cockpit) { ready_=active_ && cockpit && draws_!=0; active_=false; }
ID3D11Texture2D* HudCapture::Current(uint64_t frame) const {
    return ready_ && frame==frame_ ? image_.Get() : nullptr;
}
bool HudCapture::Draw(ID3D11DeviceContext* context,const std::function<void()>& draw,bool capture) {
    if(!active_ || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return false;
    std::array<ComPtr<ID3D11RenderTargetView>,8> targets;
    ID3D11RenderTargetView* raw[8]{}; ComPtr<ID3D11DepthStencilView> dsv;
    context->OMGetRenderTargets(8,raw,&dsv);
    for(unsigned i=0;i<8;++i) targets[i].Attach(raw[i]);
    if(!targets[0]) return false;
    for(unsigned i=1;i<8;++i) if(targets[i]) return false;
    ComPtr<ID3D11Resource> resource; targets[0]->GetResource(&resource);
    if(resource.Get()!=back_.Get()) return false;
    ComPtr<ID3D11DepthStencilState> originalDepth; UINT stencil{};
    context->OMGetDepthStencilState(&originalDepth,&stencil);
    D3D11_DEPTH_STENCIL_DESC depth{};
    if(originalDepth) originalDepth->GetDesc(&depth);
    else { depth.DepthEnable=TRUE; depth.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ALL; depth.DepthFunc=D3D11_COMPARISON_LESS; }
    // Without a depth target the game's default depth state has no effect.
    if(dsv && depth.DepthEnable && depth.DepthWriteMask!=D3D11_DEPTH_WRITE_MASK_ZERO) return false;
    depth.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ZERO; depth.StencilWriteMask=0;
    ComPtr<ID3D11BlendState> originalBlend; float factors[4]{}; UINT mask{};
    context->OMGetBlendState(&originalBlend,factors,&mask);
    D3D11_BLEND_DESC blend{};
    if(originalBlend) originalBlend->GetDesc(&blend);
    else {
        auto& rt=blend.RenderTarget[0]; rt.SrcBlend=D3D11_BLEND_ONE; rt.DestBlend=D3D11_BLEND_ZERO;
        rt.BlendOp=D3D11_BLEND_OP_ADD; rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    }
    auto& rt=blend.RenderTarget[0];
    if(!(rt.RenderTargetWriteMask&7)) return false; // stencil-only masks remain game-owned.
    if(!capture) return true; // second eye: HUD already captured once this frame
    if(!rt.BlendEnable) { rt.SrcBlend=D3D11_BLEND_ONE; rt.DestBlend=D3D11_BLEND_ZERO; rt.BlendOp=D3D11_BLEND_OP_ADD; }
    rt.BlendEnable=TRUE; rt.SrcBlendAlpha=D3D11_BLEND_ONE; rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;
    rt.BlendOpAlpha=D3D11_BLEND_OP_ADD; rt.RenderTargetWriteMask|=D3D11_COLOR_WRITE_ENABLE_ALPHA;
    ComPtr<ID3D11Device> device; context->GetDevice(&device);
    if(!blend_ || std::memcmp(&blend,&blendDesc_,sizeof(blend))) {
        ComPtr<ID3D11BlendState> replacement;
        if(FAILED(device->CreateBlendState(&blend,&replacement))) return false;
        blend_=replacement; blendDesc_=blend;
    }
    if(!depth_ || std::memcmp(&depth,&depthDesc_,sizeof(depth))) {
        ComPtr<ID3D11DepthStencilState> replacement;
        if(FAILED(device->CreateDepthStencilState(&depth,&replacement))) return false;
        depth_=replacement; depthDesc_=depth;
    }
    auto target=target_.Get();
    context->OMSetRenderTargets(1,&target,dsv.Get());
    context->OMSetBlendState(blend_.Get(),factors,mask);
    context->OMSetDepthStencilState(depth_.Get(),stencil);
    // Run before the original draw, with stencil/depth writes disabled, so a UI
    // mask is consumed in the same state without modifying the game's buffer.
    draw(); ++draws_;
    context->OMSetRenderTargets(8,raw,dsv.Get());
    context->OMSetBlendState(originalBlend.Get(),factors,mask);
    context->OMSetDepthStencilState(originalDepth.Get(),stencil);
    return true;
}
