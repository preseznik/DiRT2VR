#include "hud_capture.h"
#include "eye_blit.h"
#include <d3dcompiler.h>
#include <array>
#include <cstdio>
#include <cstdlib>
#include <cstring>
using Microsoft::WRL::ComPtr;
#define CHECK(x) do { if(!(x)) { std::fprintf(stderr,"Failed line %d: %s\n",__LINE__,#x); std::exit(1); } } while(0)

int main() {
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    CHECK(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)));
    D3D11_TEXTURE2D_DESC desc{}; desc.Width=16; desc.Height=8; desc.ArraySize=desc.MipLevels=1;
    desc.SampleDesc.Count=1; desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    ComPtr<ID3D11Texture2D> back,output,staging;
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&back)));
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&output)));
    desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&staging)));
    ComPtr<ID3D11RenderTargetView> target,other;
    CHECK(SUCCEEDED(device->CreateRenderTargetView(back.Get(),nullptr,&target)));
    CHECK(SUCCEEDED(device->CreateRenderTargetView(output.Get(),nullptr,&other)));
    auto pixel=[&](ID3D11Texture2D* image,unsigned x) {
        context->CopyResource(staging.Get(),image);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        CHECK(SUCCEEDED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)));
        std::array<unsigned char,4> result{};
        std::memcpy(result.data(),static_cast<unsigned char*>(mapped.pData)+x*4,4);
        context->Unmap(staging.Get(),0); return result;
    };
    const char shader[]=R"(
float4 vs(uint id:SV_VertexID):SV_Position { return float4(float2((id<<1)&2,id&2)*float2(2,-2)+float2(-1,1),0,1); }
float4 ps():SV_Target { return float4(1,0,0,0.5); }
)";
    ComPtr<ID3DBlob> vs,ps,error;
    CHECK(SUCCEEDED(D3DCompile(shader,sizeof(shader),nullptr,nullptr,nullptr,"vs","vs_5_0",0,0,&vs,&error)));
    CHECK(SUCCEEDED(D3DCompile(shader,sizeof(shader),nullptr,nullptr,nullptr,"ps","ps_5_0",0,0,&ps,&error)));
    ComPtr<ID3D11VertexShader> vertex; ComPtr<ID3D11PixelShader> fragment;
    CHECK(SUCCEEDED(device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&vertex)));
    CHECK(SUCCEEDED(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&fragment)));
    context->VSSetShader(vertex.Get(),nullptr,0); context->PSSetShader(fragment.Get(),nullptr,0);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    D3D11_VIEWPORT viewport{0,0,8,8,0,1}; context->RSSetViewports(1,&viewport);
    D3D11_DEPTH_STENCIL_DESC depthDesc{}; depthDesc.DepthFunc=D3D11_COMPARISON_ALWAYS;
    ComPtr<ID3D11DepthStencilState> depth;
    CHECK(SUCCEEDED(device->CreateDepthStencilState(&depthDesc,&depth)));
    context->OMSetDepthStencilState(depth.Get(),7);
    D3D11_BLEND_DESC blendDesc{}; auto& rt=blendDesc.RenderTarget[0];
    rt.BlendEnable=TRUE; rt.SrcBlend=D3D11_BLEND_SRC_ALPHA; rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;
    rt.BlendOp=rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=rt.DestBlendAlpha=D3D11_BLEND_ZERO; rt.RenderTargetWriteMask=15;
    ComPtr<ID3D11BlendState> blend; CHECK(SUCCEEDED(device->CreateBlendState(&blendDesc,&blend)));
    float factors[]{0.2f,0.3f,0.4f,0.5f}; context->OMSetBlendState(blend.Get(),factors,0xffffffff);
    auto raw=target.Get(); context->OMSetRenderTargets(1,&raw,nullptr);
    const float blue[]{0,0,1,1}; context->ClearRenderTargetView(target.Get(),blue);
    HudCapture capture; CHECK(capture.Begin(back.Get(),10));
    capture.Draw(context.Get(),[&] { context->Draw(3,0); }); CHECK(capture.Draws()==1);
    ComPtr<ID3D11RenderTargetView> restoredTarget; ComPtr<ID3D11BlendState> restoredBlend;
    ComPtr<ID3D11DepthStencilState> restoredDepth; float restoredFactors[4]{}; UINT mask{},stencil{};
    context->OMGetRenderTargets(1,&restoredTarget,nullptr);
    context->OMGetBlendState(&restoredBlend,restoredFactors,&mask);
    context->OMGetDepthStencilState(&restoredDepth,&stencil);
    CHECK(restoredTarget.Get()==target.Get() && restoredBlend.Get()==blend.Get() && restoredDepth.Get()==depth.Get());
    CHECK(stencil==7 && mask==0xffffffff && std::memcmp(factors,restoredFactors,sizeof(factors))==0);
    CHECK(pixel(back.Get(),2)[0]==0); // duplicate did not touch the desktop image
    CHECK(capture.Draw(context.Get(),[] { CHECK(false); },false)); // second eye suppresses without redrawing
    CHECK(capture.Draws()==1);
    context->Draw(3,0); capture.End(true);
    auto hud=capture.Current(10); CHECK(hud && !capture.Current(9) && !capture.Current(11));
    auto p=pixel(hud,2); CHECK(p[0]==128 && p[1]==0 && p[2]==0 && p[3]==128);
    p=pixel(hud,14); CHECK((p==std::array<unsigned char,4>{}));
    p=pixel(back.Get(),2); CHECK(p[0]==128 && p[2]>=127 && p[2]<=128 && p[3]==0);
    EyeBlit blit; CHECK(blit.Initialize(device.Get()));
    CHECK(blit.Draw(2,hud,other.Get(),16,8,true));
    p=pixel(output.Get(),2); CHECK(p[0]==255 && p[1]==0 && p[2]==0 && p[3]==128);
    p=pixel(output.Get(),14); CHECK((p==std::array<unsigned char,4>{}));
    // Offscreen, default depth-writing and MRT draws must not contaminate the layer.
    CHECK(capture.Begin(back.Get(),11));
    auto foreign=other.Get(); context->OMSetRenderTargets(1,&foreign,nullptr);
    capture.Draw(context.Get(),[] { CHECK(false); });
    context->OMSetRenderTargets(1,&raw,nullptr); context->OMSetDepthStencilState(nullptr,0);
    capture.Draw(context.Get(),[&] { context->Draw(3,0); }); CHECK(capture.Draws()==1);
    desc.Usage=D3D11_USAGE_DEFAULT; desc.CPUAccessFlags=0; desc.BindFlags=D3D11_BIND_DEPTH_STENCIL;
    desc.Format=DXGI_FORMAT_D24_UNORM_S8_UINT;
    ComPtr<ID3D11Texture2D> depthImage; ComPtr<ID3D11DepthStencilView> dsv;
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&depthImage)));
    CHECK(SUCCEEDED(device->CreateDepthStencilView(depthImage.Get(),nullptr,&dsv)));
    CHECK(capture.Begin(back.Get(),11));
    context->OMSetRenderTargets(1,&raw,dsv.Get());
    capture.Draw(context.Get(),[] { CHECK(false); });
    context->OMSetDepthStencilState(depth.Get(),7);
    ID3D11RenderTargetView* multiple[]{raw,foreign}; context->OMSetRenderTargets(2,multiple,nullptr);
    capture.Draw(context.Get(),[] { CHECK(false); }); capture.End(true);
    CHECK(!capture.Current(11));
    context->OMSetRenderTargets(1,&raw,nullptr); CHECK(capture.Begin(back.Get(),12));
    capture.Draw(context.Get(),[&] { context->Draw(3,0); }); capture.End(false);
    CHECK(!capture.Current(12)); // pause/menu transition discards any captured HUD
    CHECK(!capture.Begin(nullptr,13)); CHECK(!capture.Current(13));
    std::puts("HUD alpha, untouched background, GPU state restoration, filtering and stale-frame rejection passed.");
}
