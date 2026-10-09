#include "shadow_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
#include <algorithm>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace vr {
bool ShadowSequenceFrame(uint64_t frame) {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_SHADOW_SEQUENCE",value,8)==1 && value[0]==L'1'; }();
    return enabled && LoggingEnabled() && frame>=3000 && frame<=3002;
}
void TraceShadowCamera(uint64_t frame,unsigned eye,const float* projection,const float* view) {
    if(!ShadowSequenceFrame(frame))return;
    auto out=TraceFile(Output()/"shadow-camera.csv",std::ios::app);
    out<<frame<<','<<eye;
    for(unsigned i=0;i<16;++i)out<<','<<projection[i];
    for(unsigned i=0;i<16;++i)out<<','<<view[i];
    out<<'\n';
}
void TraceShadowRays(uint64_t frame,unsigned eye,uint64_t shader,const float* rays) {
    if(!ShadowSequenceFrame(frame))return;
    auto out=TraceFile(Output()/"shadow-rays.csv",std::ios::app);
    out<<frame<<','<<eye<<','<<std::hex<<shader<<std::dec;
    for(unsigned i=0;i<4;++i)out<<','<<rays[i];
    out<<'\n';
}
void TraceShadowInputs(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool requested) {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_SHADOW_PROBE",value,8)==1 && value[0]==L'1'; }();
    // Diagnostic only: three reflected shaders with the same 208-byte shadow
    // mask globals, screen depth at t0 and light-space depth maps at t1/t2.
    if((!requested && (!enabled || frame!=3000)) || !LoggingEnabled() || eye>2 ||
       (shader!=0x0024789eedc9b7bcull && shader!=0xc3d93558eae69b72ull && shader!=0xccdb9d07dcf3406eull) ||
       context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return;
    static std::array<unsigned,3> counts{};
    static uint64_t lastFrame=~uint64_t{};if(lastFrame!=frame){lastFrame=frame;counts={};}
    if(counts[eye]>=4) return;
    const auto prefix="shadow-"+std::to_string(frame)+"-eye-"+std::to_string(eye)+"-draw-"+std::to_string(counts[eye]++);
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

namespace {
// Explicit-request diagnostics only. Never retain game textures between frames.
void CaptureLightingTexture(ID3D11DeviceContext* context,ID3D11Resource* resource,
    DXGI_FORMAT format,UINT mip,const std::string& prefix,const char* label,std::ofstream& meta) {
    static uint64_t bytes{};
    constexpr uint64_t budget=512ull*1024*1024, maximum=64ull*1024*1024;
    ComPtr<ID3D11Texture2D> texture;
    if(!resource || FAILED(resource->QueryInterface(IID_PPV_ARGS(&texture))))return;
    D3D11_TEXTURE2D_DESC d{};texture->GetDesc(&d);
    meta<<label<<" resource="<<resource<<" width="<<d.Width<<" height="<<d.Height
        <<" format="<<format<<" samples="<<d.SampleDesc.Count<<" mip="<<mip<<'\n';
    UINT pixelBytes{};
    switch(format) {
    case DXGI_FORMAT_R16G16B16A16_FLOAT:pixelBytes=8;break;
    case DXGI_FORMAT_R8G8B8A8_UNORM:case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
    case DXGI_FORMAT_R32_FLOAT:case DXGI_FORMAT_R16G16_FLOAT:pixelBytes=4;break;
    case DXGI_FORMAT_R16_FLOAT:pixelBytes=2;break;
    case DXGI_FORMAT_R8_UNORM:pixelBytes=1;break;
    default:meta<<label<<"_skipped=unsupported_format\n";return;
    }
    if(d.ArraySize!=1 || mip>=d.MipLevels || mip>=32 || bytes>=budget)return;
    d.Width=(std::max)(1u,d.Width>>mip);d.Height=(std::max)(1u,d.Height>>mip);
    const auto minimumBytes=uint64_t(d.Width)*d.Height*pixelBytes;
    if(d.Width>4096 || d.Height>4096 || minimumBytes>maximum || minimumBytes>budget-bytes)return;
    const bool multisampled=d.SampleDesc.Count>1;
    if(multisampled && mip)return;
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    d.MipLevels=1;d.Format=format;d.BindFlags=d.MiscFlags=0;d.SampleDesc={1,0};
    d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> copy;
    if(FAILED(device->CreateTexture2D(&d,nullptr,&copy)))return;
    if(multisampled) {
        UINT support{};
        if(FAILED(device->CheckFormatSupport(format,&support)) || !(support&D3D11_FORMAT_SUPPORT_MULTISAMPLE_RESOLVE))return;
        d.Usage=D3D11_USAGE_DEFAULT;d.CPUAccessFlags=0;
        ComPtr<ID3D11Texture2D> resolved;
        if(FAILED(device->CreateTexture2D(&d,nullptr,&resolved)))return;
        context->ResolveSubresource(resolved.Get(),0,texture.Get(),0,format);
        context->CopyResource(copy.Get(),resolved.Get());
    }else context->CopySubresourceRegion(copy.Get(),0,0,0,0,texture.Get(),mip,nullptr);
    D3D11_MAPPED_SUBRESOURCE map{};
    if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&map)))return;
    const auto size=uint64_t(map.RowPitch)*d.Height;
    meta<<label<<"_capture width="<<d.Width<<" height="<<d.Height<<" pitch="<<map.RowPitch<<'\n';
    if(size<=maximum && size<=budget-bytes) {
        auto out=TraceFile(Output()/(prefix+"-"+label+".bin"),std::ios::binary);
        out.write(static_cast<const char*>(map.pData),size_t(size));bytes+=size;
    }else meta<<label<<"_skipped=capture_size_limit\n";
    context->Unmap(copy.Get(),0);
}
}

void TraceLightingStage(ID3D11DeviceContext* context,uint64_t shader,uint64_t frame,unsigned eye,bool after) {
    unsigned stage{};
    switch(shader) {
    case 0xeec41c1c282ed8dcull:stage=0;break; // Geometric light shafts.
    case 0xbd3977e4425b341dull:stage=1;break; // Bloom threshold.
    case 0xa9c8102b2e626519ull:stage=2;break; // Bloom composite.
    case 0x013c7c367b82e337ull:stage=3;break; // Exposure adaptation.
    case 0x0024789eedc9b7bcull:case 0xc3d93558eae69b72ull:case 0xccdb9d07dcf3406eull:stage=5;break;
    case 0x17c17f6f0d93e079ull:stage=4;break; // Final colour / bloom composite.
    default:return;
    }
    if(!LoggingEnabled() || eye>2 || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return;
    static uint64_t last=~uint64_t{};static bool seen[3][6][2]{};
    if(last!=frame){last=frame;memset(seen,0,sizeof(seen));}
    if(seen[eye][stage][after])return;seen[eye][stage][after]=true;
    const auto prefix="lighting-"+std::to_string(frame)+"-eye-"+std::to_string(eye)+"-stage-"+std::to_string(stage)+(after?"-after":"");
    auto meta=TraceFile(Output()/(prefix+".txt"));meta<<"shader="<<std::hex<<shader<<std::dec<<'\n';
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(!after)for(unsigned vs=0;vs<2;++vs)for(unsigned slot:{0u,1u,3u}) {
        ComPtr<ID3D11Buffer> source,copy;
        if(vs)context->VSGetConstantBuffers(slot,1,&source);else context->PSGetConstantBuffers(slot,1,&source);
        if(!source)continue;
        D3D11_BUFFER_DESC d{};source->GetDesc(&d);if(d.ByteWidth>4096)continue;
        d.Usage=D3D11_USAGE_STAGING;d.BindFlags=d.MiscFlags=d.StructureByteStride=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        if(FAILED(device->CreateBuffer(&d,nullptr,&copy)))continue;
        context->CopyResource(copy.Get(),source.Get());D3D11_MAPPED_SUBRESOURCE m{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&m)))continue;
        auto out=TraceFile(Output()/(prefix+(vs?"-vs-":"-ps-")+std::to_string(slot)+".bin"),std::ios::binary);
        out.write(static_cast<const char*>(m.pData),d.ByteWidth);context->Unmap(copy.Get(),0);
    }
    ComPtr<ID3D11RenderTargetView> target;context->OMGetRenderTargets(1,&target,nullptr);
    if(target) {
        ComPtr<ID3D11Resource> resource;target->GetResource(&resource);
        D3D11_RENDER_TARGET_VIEW_DESC view{};target->GetDesc(&view);
        if(view.ViewDimension==D3D11_RTV_DIMENSION_TEXTURE2D || view.ViewDimension==D3D11_RTV_DIMENSION_TEXTURE2DMS)
            CaptureLightingTexture(context,resource.Get(),view.Format,view.ViewDimension==D3D11_RTV_DIMENSION_TEXTURE2D?view.Texture2D.MipSlice:0,prefix,"target",meta);
    }
    if(!after)for(unsigned slot=0;slot<(stage==4?2u:1u);++slot) {
        ComPtr<ID3D11ShaderResourceView> source;context->PSGetShaderResources(slot,1,&source);
        if(source) {
            ComPtr<ID3D11Resource> resource;source->GetResource(&resource);
            D3D11_SHADER_RESOURCE_VIEW_DESC view{};source->GetDesc(&view);
            if(view.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2D || view.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2DMS)
                CaptureLightingTexture(context,resource.Get(),view.Format,view.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2D?view.Texture2D.MostDetailedMip:0,prefix,slot?"source1":"source",meta);
        }
    }
}

}
