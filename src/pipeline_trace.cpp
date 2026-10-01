#include "pipeline_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
#include <unordered_map>
#include <unordered_set>
#include <sstream>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
uint64_t Hash(const void* bytes,size_t size) {
    uint64_t hash=14695981039346656037ull;auto p=static_cast<const unsigned char*>(bytes);
    for(size_t i=0;i<size;++i){hash^=p[i];hash*=1099511628211ull;}return hash;
}
std::string Hex(uint64_t value) {std::ostringstream s;s<<std::hex<<value;return s.str();}
struct Capture {
    ComPtr<ID3D11Device> device;
    std::unordered_map<UINT,ComPtr<ID3D11Buffer>> staging;
    std::unordered_set<std::string> written;
    std::unordered_map<void*,unsigned> resources;
    uint64_t bytes{};unsigned draws{};
    std::string Blob(const char* type,const void* data,size_t count) {
        std::string name=std::string(type)+"-"+Hex(Hash(data,count));
        if(!written.contains(name)) {
            if(count>16*1024*1024-bytes)return "budget-exceeded";
            auto file=TraceFile(Output()/("pipeline-"+name+".bin"),std::ios::binary);
            file.write(static_cast<const char*>(data),count);if(!file)return "write-failed";
            bytes+=count;written.insert(name);
        }
        return name;
    }
    std::string Buffer(ID3D11DeviceContext* context,ID3D11Buffer* buffer) {
        D3D11_BUFFER_DESC d{};buffer->GetDesc(&d);
        if(d.ByteWidth>4096)return "oversize";
        auto& copy=staging[d.ByteWidth];
        if(!copy) {
            d.Usage=D3D11_USAGE_STAGING;d.BindFlags=d.MiscFlags=d.StructureByteStride=0;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            if(FAILED(device->CreateBuffer(&d,nullptr,&copy)))return "allocation-failed";
        }
        context->CopyResource(copy.Get(),buffer);D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&mapped)))return "map-failed";
        auto id=Blob("cb",mapped.pData,d.ByteWidth);context->Unmap(copy.Get(),0);return id;
    }
    void Resource(std::ostream& out,ID3D11Resource* resource) {
        if(!resource){out<<"null";return;}
        auto [it,inserted]=resources.try_emplace(resource,unsigned(resources.size()+1));
        out<<"{\"id\":"<<it->second;
        ComPtr<ID3D11Texture2D> texture;
        if(SUCCEEDED(resource->QueryInterface(IID_PPV_ARGS(&texture)))) {
            D3D11_TEXTURE2D_DESC d{};texture->GetDesc(&d);
            out<<",\"width\":"<<d.Width<<",\"height\":"<<d.Height<<",\"format\":"<<d.Format
                <<",\"samples\":"<<d.SampleDesc.Count<<",\"mips\":"<<d.MipLevels<<",\"array\":"<<d.ArraySize<<",\"bind\":"<<d.BindFlags;
        }else {D3D11_RESOURCE_DIMENSION type{};resource->GetType(&type);out<<",\"dimension\":"<<type;}
        out<<'}';
    }
};
}
bool PipelineTraceEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_PIPELINE_PROBE",value,8)==1 && value[0]==L'1';}();
    return enabled && LoggingEnabled();
}
void TracePipelineDraw(ID3D11DeviceContext* c,uint64_t frame,unsigned eye,bool reflection,const char* kind,
    UINT count,UINT instances,uint64_t vertex,uint64_t pixel,bool patched) {
    if(!PipelineTraceEnabled() || frame!=3000 || eye>2 || c->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return;
    static Capture capture;
    ComPtr<ID3D11Device> device;c->GetDevice(&device);
    if(capture.device && device.Get()!=capture.device.Get())return;
    capture.device=device;
    if(capture.draws>=4096)return;
    auto out=TraceFile(Output()/"pipeline-draws.jsonl",std::ios::app);
    out<<"{\"draw\":"<<capture.draws++<<",\"eye\":"<<eye<<",\"reflection\":"<<reflection<<",\"patched\":"<<patched
        <<",\"kind\":\""<<kind<<"\",\"count\":"<<count<<",\"instances\":"<<instances
        <<",\"vs\":\""<<Hex(vertex)<<"\",\"ps\":\""<<Hex(pixel)<<"\"";
    D3D11_PRIMITIVE_TOPOLOGY topology{};c->IAGetPrimitiveTopology(&topology);out<<",\"topology\":"<<topology;
    std::array<D3D11_VIEWPORT,16> viewports{};UINT n=16;c->RSGetViewports(&n,viewports.data());
    out<<",\"viewports\":\""<<capture.Blob("viewport",viewports.data(),n*sizeof(D3D11_VIEWPORT))<<'"';
    std::array<D3D11_RECT,16> scissors{};n=16;c->RSGetScissorRects(&n,scissors.data());
    out<<",\"scissors\":\""<<capture.Blob("scissor",scissors.data(),n*sizeof(D3D11_RECT))<<'"';
    ComPtr<ID3D11RasterizerState> raster;c->RSGetState(&raster);
    if(raster){D3D11_RASTERIZER_DESC d{};raster->GetDesc(&d);out<<",\"raster\":\""<<capture.Blob("raster",&d,sizeof(d))<<'"';}
    ComPtr<ID3D11BlendState> blend;float factors[4]{};UINT mask{};c->OMGetBlendState(&blend,factors,&mask);
    if(blend){D3D11_BLEND_DESC d{};blend->GetDesc(&d);out<<",\"blend\":\""<<capture.Blob("blend",&d,sizeof(d))<<'"';}
    out<<",\"blendFactors\":["<<factors[0]<<','<<factors[1]<<','<<factors[2]<<','<<factors[3]<<"],\"sampleMask\":"<<mask;
    ComPtr<ID3D11DepthStencilState> ds;UINT ref{};c->OMGetDepthStencilState(&ds,&ref);
    if(ds){D3D11_DEPTH_STENCIL_DESC d{};ds->GetDesc(&d);out<<",\"depthState\":\""<<capture.Blob("depth",&d,sizeof(d))<<'"';}
    out<<",\"stencilRef\":"<<ref;
    ComPtr<ID3D11GeometryShader> gs;ComPtr<ID3D11HullShader> hs;ComPtr<ID3D11DomainShader> domain;
    c->GSGetShader(&gs,nullptr,nullptr);c->HSGetShader(&hs,nullptr,nullptr);c->DSGetShader(&domain,nullptr,nullptr);
    out<<",\"otherStagesPresent\":["<<bool(gs)<<','<<bool(hs)<<','<<bool(domain)<<']';
    ID3D11RenderTargetView* targets[8]{};ComPtr<ID3D11DepthStencilView> depth;c->OMGetRenderTargets(8,targets,&depth);
    out<<",\"targets\":[";
    for(unsigned i=0;i<8;++i) {if(i)out<<',';if(targets[i]) {
        ComPtr<ID3D11Resource> r;targets[i]->GetResource(&r);D3D11_RENDER_TARGET_VIEW_DESC v{};targets[i]->GetDesc(&v);
        out<<"{\"view\":\""<<capture.Blob("rtv",&v,sizeof(v))<<"\",\"resource\":";capture.Resource(out,r.Get());out<<'}';targets[i]->Release();
    }else out<<"null";}out<<']';
    out<<",\"depth\":";
    if(depth){ComPtr<ID3D11Resource> r;depth->GetResource(&r);D3D11_DEPTH_STENCIL_VIEW_DESC v{};depth->GetDesc(&v);
        out<<"{\"view\":\""<<capture.Blob("dsv",&v,sizeof(v))<<"\",\"resource\":";capture.Resource(out,r.Get());out<<'}';
    }else out<<"null";
    for(unsigned stage=0;stage<2;++stage) {
        out<<(stage?",\"vsCB\":[":",\"psCB\":[");
        ID3D11Buffer* buffers[14]{};if(stage)c->VSGetConstantBuffers(0,14,buffers);else c->PSGetConstantBuffers(0,14,buffers);
        for(unsigned i=0;i<14;++i){if(i)out<<',';if(buffers[i]){out<<'"'<<capture.Buffer(c,buffers[i])<<'"';buffers[i]->Release();}else out<<"null";}out<<']';
        out<<(stage?",\"vsSRV\":[":",\"psSRV\":[");
        ID3D11ShaderResourceView* views[16]{};if(stage)c->VSGetShaderResources(0,16,views);else c->PSGetShaderResources(0,16,views);
        for(unsigned i=0;i<16;++i){if(i)out<<',';if(views[i]){
            ComPtr<ID3D11Resource> r;views[i]->GetResource(&r);D3D11_SHADER_RESOURCE_VIEW_DESC v{};views[i]->GetDesc(&v);
            out<<"{\"view\":\""<<capture.Blob("srv",&v,sizeof(v))<<"\",\"resource\":";capture.Resource(out,r.Get());out<<'}';views[i]->Release();
        }else out<<"null";}out<<']';
        out<<(stage?",\"vsSamplers\":[":",\"psSamplers\":[");
        ID3D11SamplerState* samplers[16]{};if(stage)c->VSGetSamplers(0,16,samplers);else c->PSGetSamplers(0,16,samplers);
        for(unsigned i=0;i<16;++i){if(i)out<<',';if(samplers[i]){D3D11_SAMPLER_DESC d{};samplers[i]->GetDesc(&d);
            out<<'"'<<capture.Blob("sampler",&d,sizeof(d))<<'"';samplers[i]->Release();}else out<<"null";}out<<']';
    }
    out<<"}\n";
}
}
