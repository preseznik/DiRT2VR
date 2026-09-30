#include "shadow_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
using Microsoft::WRL::ComPtr;
namespace vr {
void TraceShadowInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye) {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_SHADOW_PROBE",value,8)==1 && value[0]==L'1'; }();
    // Diagnostic only: three reflected shaders with the same 208-byte shadow
    // mask globals, screen depth at t0 and light-space depth maps at t1/t2.
    if(!enabled || !LoggingEnabled() || frame!=3000 || eye<1 || eye>2 ||
       (shader!=0x0024789eedc9b7bcull && shader!=0xc3d93558eae69b72ull && shader!=0xccdb9d07dcf3406eull) ||
       context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return;
    static std::array<unsigned,2> counts{};
    if(counts[eye-1]>=4) return;
    const auto prefix="shadow-"+std::to_string(frame)+"-eye-"+std::to_string(eye)+"-draw-"+std::to_string(counts[eye-1]++);
    auto meta=TraceFile(Output()/(prefix+".txt"));
    meta << "shader=" << std::hex << shader << std::dec << '\n';
    ComPtr<ID3D11Device> device; context->GetDevice(&device);
    {
        ComPtr<ID3D11Buffer> source,copy; context->VSGetConstantBuffers(0,1,&source);
        if(source) {
            D3D11_BUFFER_DESC d{};source->GetDesc(&d);
            if(d.ByteWidth==16) {
                d.Usage=D3D11_USAGE_STAGING;d.BindFlags=d.MiscFlags=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
                if(SUCCEEDED(device->CreateBuffer(&d,nullptr,&copy))) {
                    context->CopyResource(copy.Get(),source.Get());D3D11_MAPPED_SUBRESOURCE m{};
                    if(SUCCEEDED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&m))) {
                        auto out=TraceFile(Output()/(prefix+"-vs-cb-0.bin"),std::ios::binary);
                        out.write(static_cast<const char*>(m.pData),16);context->Unmap(copy.Get(),0);
                    }
                }
            }
        }
    }
    for(unsigned slot:{0u,3u}) {
        ComPtr<ID3D11Buffer> source,copy; context->PSGetConstantBuffers(slot,1,&source);
        if(!source) continue;
        D3D11_BUFFER_DESC desc{}; source->GetDesc(&desc);
        if(desc.ByteWidth>4096) continue;
        desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=desc.MiscFlags=desc.StructureByteStride=0; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        if(FAILED(device->CreateBuffer(&desc,nullptr,&copy))) continue;
        context->CopyResource(copy.Get(),source.Get());
        D3D11_MAPPED_SUBRESOURCE map{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&map))) continue;
        auto out=TraceFile(Output()/(prefix+"-cb-"+std::to_string(slot)+".bin"),std::ios::binary);
        out.write(static_cast<const char*>(map.pData),desc.ByteWidth);context->Unmap(copy.Get(),0);
    }
    for(unsigned slot=0;slot<3;++slot) {
        ComPtr<ID3D11ShaderResourceView> view;context->PSGetShaderResources(slot,1,&view);
        if(!view) continue;
        ComPtr<ID3D11Resource> resource;view->GetResource(&resource);
        ComPtr<ID3D11Texture2D> texture;
        if(FAILED(resource.As(&texture))) continue;
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        meta << "srv=" << slot << " resource=" << resource.Get() << " width=" << desc.Width << " height=" << desc.Height
             << " format=" << desc.Format << " samples=" << desc.SampleDesc.Count << '\n';
        if(desc.ArraySize!=1 || desc.MipLevels!=1 || desc.SampleDesc.Count!=1 || uint64_t(desc.Width)*desc.Height>2048*2048) continue;
        desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=desc.MiscFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> copy;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&copy))) continue;
        context->CopyResource(copy.Get(),texture.Get());
        D3D11_MAPPED_SUBRESOURCE map{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&map))) continue;
        meta << "row_pitch=" << map.RowPitch << '\n';
        if(uint64_t(map.RowPitch)*desc.Height<=16*1024*1024) {
            auto out=TraceFile(Output()/(prefix+"-srv-"+std::to_string(slot)+".bin"),std::ios::binary);
            out.write(static_cast<const char*>(map.pData),size_t(map.RowPitch)*desc.Height);
        }
        context->Unmap(copy.Get(),0);
    }
}
}
