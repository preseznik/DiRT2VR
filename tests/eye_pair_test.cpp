#include "eye_pair.h"
#include "eye_blit.h"
#include "gpu_timer.h"
#include <cstdio>
#include <cstdlib>
using Microsoft::WRL::ComPtr;
#define CHECK(expression) do { if(!(expression)) { std::fprintf(stderr,"Failed line %d: %s\n",__LINE__,#expression); std::exit(1); } } while(0)

int main() {
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    CHECK(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)));
    D3D11_TEXTURE2D_DESC desc{}; desc.Width=16; desc.Height=8; desc.MipLevels=desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count=1; desc.BindFlags=D3D11_BIND_RENDER_TARGET;
    auto make=[&] { ComPtr<ID3D11Texture2D> result; CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&result))); return result; };
    auto source=make(); ComPtr<ID3D11RenderTargetView> target;
    CHECK(SUCCEEDED(device->CreateRenderTargetView(source.Get(),nullptr,&target)));
    EyePair pair;
    CHECK(FAILED(pair.Capture(source.Get(),1,10)));
    float red[]={1,0,0,1},blue[]={0,0,1,1};
    context->ClearRenderTargetView(target.Get(),red);
    CHECK(SUCCEEDED(pair.Capture(source.Get(),0,10))); CHECK(!pair.Ready(10));
    context->ClearRenderTargetView(target.Get(),blue);
    CHECK(SUCCEEDED(pair.Capture(source.Get(),1,10))); CHECK(pair.Ready(10)); CHECK(!pair.Ready(11));
    desc.BindFlags=0; desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    auto staging=make();
    for(unsigned eye=0;eye<2;++eye) {
        context->CopyResource(staging.Get(),pair.Texture(eye));
        D3D11_MAPPED_SUBRESOURCE mapped{};
        CHECK(SUCCEEDED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)));
        auto pixel=static_cast<unsigned char*>(mapped.pData);
        CHECK(pixel[0]==(eye==0?255:0) && pixel[2]==(eye==1?255:0));
        context->Unmap(staging.Get(),0);
    }
    EyeBlit blit; CHECK(blit.Initialize(device.Get()));
    D3D11_VIEWPORT originalViewport{2,3,7,5,0,1}; context->RSSetViewports(1,&originalViewport);
    auto originalTarget=target.Get(); context->OMSetRenderTargets(1,&originalTarget,nullptr);
    CHECK(blit.Draw(0,pair.Texture(0),target.Get(),16,8));
    ComPtr<ID3D11RenderTargetView> restoredTarget; context->OMGetRenderTargets(1,&restoredTarget,nullptr);
    CHECK(restoredTarget.Get()==originalTarget);
    D3D11_VIEWPORT restoredViewport{}; UINT count=1; context->RSGetViewports(&count,&restoredViewport);
    CHECK(restoredViewport.TopLeftX==2 && restoredViewport.Width==7);
    context->CopyResource(staging.Get(),source.Get());
    D3D11_MAPPED_SUBRESOURCE copied{}; CHECK(SUCCEEDED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&copied)));
    CHECK(static_cast<unsigned char*>(copied.pData)[0]==255 && static_cast<unsigned char*>(copied.pData)[2]==0);
    context->Unmap(staging.Get(),0);
    GpuTimer timer; CHECK(timer.Initialize(device.Get())); CHECK(timer.Begin(42));
    CHECK(!timer.Begin(43));
    context->ClearRenderTargetView(target.Get(),blue); timer.End();
    context->Flush(); // Test-only retirement; production Poll never flushes.
    std::vector<GpuTimer::Sample> timings;
    for(unsigned attempts=0;attempts<2000 && timings.empty();++attempts) { timings=timer.Poll(); if(timings.empty()) Sleep(1); }
    CHECK(timings.size()==1 && timings[0].frame==42 && timings[0].valid && timings[0].milliseconds>=0);
    CHECK(timer.Poll().empty());
    CHECK(SUCCEEDED(pair.Capture(source.Get(),0,11)));
    CHECK(FAILED(pair.Capture(source.Get(),1,12))); CHECK(!pair.Ready(11));
    desc.Width=32; desc.Usage=D3D11_USAGE_DEFAULT; desc.CPUAccessFlags=0;
    auto resized=make();
    CHECK(SUCCEEDED(pair.Capture(source.Get(),0,13)));
    CHECK(FAILED(pair.Capture(resized.Get(),1,13))); CHECK(!pair.Ready(13));
    CHECK(SUCCEEDED(pair.Capture(resized.Get(),0,14)));
    CHECK(SUCCEEDED(pair.Capture(resized.Get(),1,14))); CHECK(pair.Ready(14));
    CHECK(FAILED(pair.Capture(nullptr,0,15))); CHECK(!pair.Ready(14));
    std::puts("Eye copies independent; incomplete, stale and resized pairs rejected.");
}
