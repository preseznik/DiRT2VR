#include "water_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
#include <fstream>
#include <string>
using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
int ReflectionSlot(uint64_t shader) {
    switch(shader) {
    case 0x08192877abdf068aull: case 0x7a14b9be22409ae0ull: return 2;
    case 0x122478b22e69d9faull: case 0x55275a604e5a93deull: return 1;
    case 0x2039c720ca94a148ull: case 0x70409804398940cfull: return 4;
    case 0x2e9232fbf00db676ull: case 0x5205714abff8646eull: return 5;
    case 0x218b4f025a0c0b4bull: return -2; // environment-only variant
    default: return -1;
    }
}
}
void TraceWaterInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye) {
    const int reflection=ReflectionSlot(shader);
    if(!LoggingEnabled() || reflection==-1 || eye<1 || eye>2 || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return;
    static uint64_t lastFrame=~uint64_t{};
    static std::array<unsigned,2> counts{};
    static uint64_t savedBytes{};
    if(lastFrame!=frame) { counts={}; savedBytes=0; lastFrame=frame; }
    if(counts[eye-1]>=4) return; // bounded, even when a route has many patches
    const auto draw=counts[eye-1]++;
    const auto prefix="water-"+std::to_string(frame)+"-eye-"+std::to_string(eye)+"-draw-"+std::to_string(draw);
    auto meta=TraceFile(Output()/(prefix+".txt"));
    meta << "shader=" << std::hex << shader << std::dec << " reflection_slot=" << reflection << '\n';
    ComPtr<ID3D11Device> device; context->GetDevice(&device);
    // Capture globals, per-frame and camera inputs for both shader stages.
    for(unsigned stage=0;stage<2;++stage) for(unsigned slot=0;slot<4;++slot) {
        ComPtr<ID3D11Buffer> source,copy;
        if(stage) context->PSGetConstantBuffers(slot,1,&source); else context->VSGetConstantBuffers(slot,1,&source);
        if(!source) continue;
        D3D11_BUFFER_DESC desc{}; source->GetDesc(&desc);
        if(desc.ByteWidth>4096) continue;
        desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ; desc.BindFlags=desc.MiscFlags=desc.StructureByteStride=0;
        if(FAILED(device->CreateBuffer(&desc,nullptr,&copy))) continue;
        context->CopyResource(copy.Get(),source.Get());
        D3D11_MAPPED_SUBRESOURCE map{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&map))) continue;
        auto out=TraceFile(Output()/(prefix+(stage?"-ps-cb-":"-vs-cb-")+std::to_string(slot)+".bin"),std::ios::binary);
        out.write(static_cast<const char*>(map.pData),desc.ByteWidth);
        context->Unmap(copy.Get(),0);
    }
    for(unsigned slot=0;slot<8;++slot) {
        ComPtr<ID3D11ShaderResourceView> view; context->PSGetShaderResources(slot,1,&view);
        if(!view) continue;
        ComPtr<ID3D11Resource> resource; view->GetResource(&resource);
        D3D11_SHADER_RESOURCE_VIEW_DESC srv{}; view->GetDesc(&srv);
        meta << "srv=" << slot << " resource=" << resource.Get() << " dimension=" << srv.ViewDimension << " format=" << srv.Format;
        ComPtr<ID3D11Texture2D> texture;
        if(FAILED(resource.As(&texture))) { meta << '\n'; continue; }
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        meta << " width=" << desc.Width << " height=" << desc.Height << " format=" << desc.Format << " array=" << desc.ArraySize << " mips=" << desc.MipLevels << " samples=" << desc.SampleDesc.Count;
        // Only the reflection/depth inputs; no normal/ripple maps. Keep complex
        // views in metadata without pretending a cube or MSAA surface is 2D.
        if((int(slot)!=reflection && slot!=0) || desc.ArraySize!=1 || desc.MipLevels!=1 || desc.SampleDesc.Count!=1 ||
            uint64_t(desc.Width)*desc.Height>2048*2048) { meta << '\n'; continue; }
        desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=desc.MiscFlags=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> copy;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&copy))) { meta << " copy_failed\n"; continue; }
        context->CopyResource(copy.Get(),texture.Get());
        D3D11_MAPPED_SUBRESOURCE map{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&map))) { meta << " map_failed\n"; continue; }
        meta << " row_pitch=" << map.RowPitch << '\n';
        const uint64_t bytes=uint64_t(map.RowPitch)*desc.Height;
        if(bytes<=16*1024*1024 && savedBytes+bytes<=64*1024*1024) {
            auto out=TraceFile(Output()/(prefix+"-srv-"+std::to_string(slot)+".bin"),std::ios::binary);
            out.write(static_cast<const char*>(map.pData),size_t(map.RowPitch)*desc.Height);
            savedBytes+=bytes;
        }
        context->Unmap(copy.Get(),0);
    }
}
}
