#include "water_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
#include <fstream>
#include <string>
#include <algorithm>
#include <atomic>
#include <mutex>
#include <vector>
#include <string_view>
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

// Object-owned metadata avoids a pointer cache mistaking a recycled shader for
// water. It has no effect on shader execution and dies with the shader object.
constexpr GUID waterShaderInfoId{0x3ba1fbd7,0x9786,0x4fa6,{0xa6,0x6b,0xe1,0x73,0x4a,0x52,0x22,0x17}};
struct WaterShaderInfo { uint64_t hash{},identity{},frame{},tick{}; double milliseconds{}; };
std::atomic<uint64_t> nextWaterShader{1};
struct SurfaceKey {
    uintptr_t context{},vertex{},index{},layout{};
    unsigned eye{},stride{},offset{},indexOffset{},format{},topology{},count{},instances{},start{},firstInstance{};
    int base{};
    std::string_view kind;
    bool operator==(const SurfaceKey&) const=default;
};
struct SurfaceInputs {
    uint64_t shader{},identity{};
    std::array<uintptr_t,8> views{};
    bool operator==(const SurfaceInputs&) const=default;
};
struct SurfaceState {
    SurfaceKey key;
    std::array<SurfaceInputs,8> variants{};
    unsigned variantCount{},current{},changes{},reportedChanges{};
    uint64_t lastFrame{},lastRecord{},observations{};
};
struct ResourceInfo {
    uintptr_t resource{};
    D3D11_SHADER_RESOURCE_VIEW_DESC view{};
    D3D11_TEXTURE2D_DESC texture{};
};
struct SurfaceEvent {
    SurfaceKey key;
    WaterShaderInfo shader;
    SurfaceInputs inputs;
    std::array<ResourceInfo,8> resources{};
    uint64_t frame{},previousFrame{},observations{};
    unsigned geometry{},variant{},changes{};
    const char* reason{};
    bool newInputs{};
};
constexpr size_t maxSurfaces=128,maxVariants=8,maxQueuedEvents=128,maxSurfaceEvents=8192;
constexpr uint64_t maxSurfaceFrames=36000;
std::atomic<bool> surfaceTraceActive{};
std::mutex surfaceTraceMutex;
std::vector<SurfaceState> surfaces;
std::vector<SurfaceEvent> surfaceEvents;
std::ofstream surfaceCsv,inputCsv,surfaceCostCsv;
uint64_t eventCount{},surfaceQueries{},surfaceTicks{};
uint64_t surfaceFirstFrame=~uint64_t{};
LARGE_INTEGER surfaceFrequency{};
const char* surfaceLimit{};
thread_local ID3D11DeviceContext* boundContext{};
thread_local WaterShaderInfo boundWater{};

void SurfaceLimit(const char* reason) {
    surfaceLimit=reason;
    surfaceTraceActive=false;
}
struct SurfaceTimer {
    LARGE_INTEGER start{};
    SurfaceTimer() { QueryPerformanceCounter(&start); }
    ~SurfaceTimer() { LARGE_INTEGER end{};QueryPerformanceCounter(&end);surfaceTicks+=end.QuadPart-start.QuadPart; }
};
}

bool InitializeWaterSurfaceTrace() {
    wchar_t value[8]{};
    if(!LoggingEnabled() || GetEnvironmentVariableW(L"DIRT2VR_WATER_REFLECTIONS",value,8)!=1 || value[0]!=L'1') return false;
    std::lock_guard lock(surfaceTraceMutex);
    if(surfaceCsv.is_open()) return surfaceTraceActive;
    surfaceCsv=TraceFile(Output()/"water-surfaces.csv");
    inputCsv=TraceFile(Output()/"water-surface-inputs.csv");
    surfaceCostCsv=TraceFile(Output()/"water-surface-cost.csv");
    if(!surfaceCsv || !inputCsv || !surfaceCostCsv) { Log("water surface trace unavailable: cannot open diagnostic files");return false; }
    QueryPerformanceFrequency(&surfaceFrequency);
    surfaces.reserve(maxSurfaces);surfaceEvents.reserve(maxQueuedEvents);
    surfaceCsv << "frame,eye,event,geometry,variant,shader,shader_id,shader_created_frame,shader_created_tick,shader_create_ms,previous_frame,observations,binding_changes,kind,count,instances,start,base,first_instance,context,vertex,index,layout,stride,vertex_offset,index_offset,index_format,topology,reflection_slot\n";
    inputCsv << "geometry,variant,slot,view,resource,view_dimension,view_format,width,height,texture_format,mips,array_size,samples,first_mip,view_mips\n";
    surfaceCostCsv << "frame,water_draw_queries,observe_cpu_ms,write_cpu_ms,event_total\n";
    surfaceCsv.flush();inputCsv.flush();surfaceCostCsv.flush();
    surfaceTraceActive=true;
    Log("water surface trace enabled: known shaders only; metadata, not texture contents; max_surfaces=%zu variants_per_surface=%zu events=%zu frames=%llu; timings include diagnostic cost",maxSurfaces,maxVariants,maxSurfaceEvents,maxSurfaceFrames);
    return true;
}
bool TraceWaterShaderCreationEnabled(uint64_t shader) { return surfaceTraceActive && ReflectionSlot(shader)!=-1; }
void ObserveWaterShaderCreation(ID3D11PixelShader* shader,uint64_t hash,uint64_t frame,double milliseconds) {
    if(!shader || !TraceWaterShaderCreationEnabled(hash)) return;
    const WaterShaderInfo info{hash,nextWaterShader++,frame,GetTickCount64(),milliseconds};
    if(FAILED(shader->SetPrivateData(waterShaderInfoId,sizeof(info),&info)))
        Log("water shader metadata unavailable hash=%llx frame=%llu",hash,frame);
}
void ObserveWaterShaderBinding(ID3D11DeviceContext* context,ID3D11PixelShader* shader) {
    if(!surfaceTraceActive || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return;
    boundContext=context;boundWater={};
    UINT bytes=sizeof(boundWater);
    if(shader && FAILED(shader->GetPrivateData(waterShaderInfoId,&bytes,&boundWater))) boundWater={};
}
void TraceWaterSurfaceDraw(ID3D11DeviceContext* context,uint64_t frame,unsigned eye,const char* kind,
    unsigned count,unsigned instances,unsigned start,int base,unsigned firstInstance) {
    if(!surfaceTraceActive || !boundWater.hash || context!=boundContext || eye<1 || eye>2) return;
    std::lock_guard lock(surfaceTraceMutex);
    if(!surfaceTraceActive) return;
    SurfaceTimer timer;
    ++surfaceQueries;
    if(surfaceFirstFrame==~uint64_t{}) surfaceFirstFrame=frame;
    // State blocks/command lists can bypass PSSetShader. Never mislabel a draw
    // when the tracked binding no longer matches the actual immediate context.
    ComPtr<ID3D11PixelShader> pixel;context->PSGetShader(&pixel,nullptr,nullptr);
    ObserveWaterShaderBinding(context,pixel.Get());if(!boundWater.hash)return;
    SurfaceKey key;
    key.context=reinterpret_cast<uintptr_t>(context);key.eye=eye;key.kind=kind;
    key.count=count;key.instances=instances;key.start=start;key.base=base;key.firstInstance=firstInstance;
    ComPtr<ID3D11Buffer> vertex,index;ComPtr<ID3D11InputLayout> layout;
    DXGI_FORMAT format{};D3D11_PRIMITIVE_TOPOLOGY topology{};
    context->IAGetVertexBuffers(0,1,&vertex,&key.stride,&key.offset);
    context->IAGetIndexBuffer(&index,&format,&key.indexOffset);context->IAGetInputLayout(&layout);context->IAGetPrimitiveTopology(&topology);
    key.vertex=reinterpret_cast<uintptr_t>(vertex.Get());key.index=reinterpret_cast<uintptr_t>(index.Get());
    key.layout=reinterpret_cast<uintptr_t>(layout.Get());key.format=format;key.topology=topology;
    auto state=std::find_if(surfaces.begin(),surfaces.end(),[&](const auto& s){return s.key==key;});
    const bool first=state==surfaces.end();
    if(first) {
        if(surfaces.size()==maxSurfaces) { SurfaceLimit("geometry capacity");return; }
        surfaces.push_back({key});state=surfaces.end()-1;
    }
    SurfaceInputs inputs;inputs.shader=boundWater.hash;inputs.identity=boundWater.identity;
    std::array<ComPtr<ID3D11ShaderResourceView>,8> views;
    std::array<ID3D11ShaderResourceView*,8> raw{};
    context->PSGetShaderResources(0,static_cast<UINT>(raw.size()),raw.data());
    for(size_t i=0;i<raw.size();++i) { views[i].Attach(raw[i]);inputs.views[i]=reinterpret_cast<uintptr_t>(raw[i]); }
    const auto variantEnd=state->variants.begin()+state->variantCount;
    auto found=std::find(state->variants.begin(),variantEnd,inputs);
    const bool newInputs=found==variantEnd;
    const unsigned variant=static_cast<unsigned>(found-state->variants.begin());
    if(newInputs) {
        if(state->variantCount==maxVariants) { SurfaceLimit("input variant capacity");return; }
        state->variants[state->variantCount++]=inputs;
    }
    if(!first && state->current!=variant) ++state->changes;
    const auto previous=state->lastFrame;
    const bool returned=!first && frame>previous && frame-previous>90;
    state->lastFrame=frame;state->current=variant;++state->observations;
    // Ping-pong textures are common. Record each new combination immediately,
    // then summarize repeated swaps instead of exhausting the log on lap one.
    if(!first && !newInputs && !returned && !(frame>=state->lastRecord+120 && state->changes!=state->reportedChanges)) return;
    if(eventCount==maxSurfaceEvents || surfaceEvents.size()==maxQueuedEvents) { SurfaceLimit("event capacity");return; }
    SurfaceEvent event;
    event.key=key;event.shader=boundWater;event.inputs=inputs;event.frame=frame;event.previousFrame=previous;
    event.geometry=static_cast<unsigned>(state-surfaces.begin());event.variant=variant;event.changes=state->changes;
    event.observations=state->observations;event.newInputs=newInputs;
    event.reason=first?"first-use":newInputs?"new-inputs":returned?"return":"repeated-swaps";
    if(newInputs) for(size_t slot=0;slot<views.size();++slot) if(views[slot]) {
        auto& info=event.resources[slot];views[slot]->GetDesc(&info.view);
        ComPtr<ID3D11Resource> resource;views[slot]->GetResource(&resource);
        info.resource=reinterpret_cast<uintptr_t>(resource.Get());
        ComPtr<ID3D11Texture2D> texture;if(SUCCEEDED(resource.As(&texture))) texture->GetDesc(&info.texture);
    }
    surfaceEvents.push_back(std::move(event));++eventCount;
    state->lastRecord=frame;state->reportedChanges=state->changes;
}
void FlushWaterSurfaceTrace(uint64_t frame) {
    if(!LoggingEnabled()) return;
    std::lock_guard lock(surfaceTraceMutex);
    if(!surfaceTraceActive && !surfaceLimit) return;
    if(surfaceTraceActive && surfaceFirstFrame!=~uint64_t{} && frame>=surfaceFirstFrame && frame-surfaceFirstFrame>=maxSurfaceFrames)
        SurfaceLimit("frame window");
    LARGE_INTEGER start{},end{};QueryPerformanceCounter(&start);
    for(const auto& e:surfaceEvents) {
        const auto& k=e.key;
        surfaceCsv << e.frame << ',' << k.eye << ',' << e.reason << ',' << e.geometry << ',' << e.variant << ',' << std::hex << e.shader.hash << std::dec
            << ',' << e.shader.identity << ',' << e.shader.frame << ',' << e.shader.tick << ',' << e.shader.milliseconds << ',' << e.previousFrame << ',' << e.observations << ',' << e.changes
            << ',' << k.kind << ',' << k.count << ',' << k.instances << ',' << k.start << ',' << k.base << ',' << k.firstInstance
            << ',' << k.context << ',' << k.vertex << ',' << k.index << ',' << k.layout << ',' << k.stride << ',' << k.offset << ',' << k.indexOffset << ',' << k.format << ',' << k.topology << ',' << ReflectionSlot(e.shader.hash) << '\n';
        if(e.newInputs) for(size_t slot=0;slot<e.resources.size();++slot) {
            const auto& r=e.resources[slot];const auto& t=r.texture;
            inputCsv << e.geometry << ',' << e.variant << ',' << slot << ',' << e.inputs.views[slot] << ',' << r.resource << ',' << r.view.ViewDimension << ',' << r.view.Format
                << ',' << t.Width << ',' << t.Height << ',' << t.Format << ',' << t.MipLevels << ',' << t.ArraySize << ',' << t.SampleDesc.Count
                << ',' << (r.view.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2D?r.view.Texture2D.MostDetailedMip:0)
                << ',' << (r.view.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2D?r.view.Texture2D.MipLevels:0) << '\n';
        }
    }
    surfaceEvents.clear();
    if(frame%120==0 || surfaceLimit) { surfaceCsv.flush();inputCsv.flush();surfaceCostCsv.flush(); }
    QueryPerformanceCounter(&end);
    if(surfaceQueries) surfaceCostCsv << frame << ',' << surfaceQueries << ',' << 1000.0*surfaceTicks/surfaceFrequency.QuadPart
        << ',' << 1000.0*(end.QuadPart-start.QuadPart)/surfaceFrequency.QuadPart << ',' << eventCount << '\n';
    surfaceQueries=surfaceTicks=0;
    if(surfaceLimit) { Log("water surface trace limit reached frame=%llu reason=%s; later surface observations unavailable",frame,surfaceLimit);surfaceLimit=nullptr;surfaceCostCsv.flush(); }
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
