// Neural Rendering mode of the x64 helper: D3D11 frame -> D3D12 DLSS 5 Neural
// Rendering -> D3D11 frame, one still at a time (history reset every frame),
// optionally at reduced resolution and with a full-resolution centre (foveated).
//
// No NVIDIA SDK code is used. The driver's NGX core (_nvngx.dll) and a
// player-supplied nvngx_dlssnr.dll are loaded at run time; the minimal
// interface declarations below follow the MIT-licensed ComfyUI-DLSS5-NR bridge
// (see THIRD_PARTY_NOTICES.md). NVIDIA, DLSS and NGX are NVIDIA trademarks.
#include "neural.h"
#include <d3d11_4.h>
#include <d3d12.h>
#include <d3dcompiler.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace {
namespace ngx {
using Result=int;
constexpr Result Success=1;
constexpr int NeuralRendering=18;
constexpr unsigned long long Application=0x0876232Cull;
constexpr char Project[]="c78798d5-4105-4aca-8115-c60a8145c46b";
struct Handle { unsigned int Id; };
// Same virtual order as NGX's parameter object.
struct Parameter {
    virtual void Set(const char*,unsigned long long)=0;
    virtual void Set(const char*,float)=0;
    virtual void Set(const char*,double)=0;
    virtual void Set(const char*,unsigned int)=0;
    virtual void Set(const char*,int)=0;
    virtual void Set(const char*,ID3D11Resource*)=0;
    virtual void Set(const char*,ID3D12Resource*)=0;
    virtual void Set(const char*,void*)=0;
    virtual Result Get(const char*,unsigned long long*) const=0;
    virtual Result Get(const char*,float*) const=0;
    virtual Result Get(const char*,double*) const=0;
    virtual Result Get(const char*,unsigned int*) const=0;
    virtual Result Get(const char*,int*) const=0;
    virtual Result Get(const char*,ID3D11Resource**) const=0;
    virtual Result Get(const char*,ID3D12Resource**) const=0;
    virtual Result Get(const char*,void**) const=0;
    virtual void Reset()=0;
};
struct PathList { const wchar_t* const* path; unsigned int length; };
using LogCallback=void(__cdecl*)(const char*,int,int);
struct Logging { int level; LogCallback callback; void* user; bool exclusive; };
struct CommonInfo { PathList paths; void* internal; Logging logging; };
using CoreInit=Result(__cdecl*)(unsigned long long,const wchar_t*,ID3D12Device*,int,const void*);
using CoreInitProject=Result(__cdecl*)(const char*,int,const char*,const wchar_t*,ID3D12Device*,int,const void*);
using Allocate=Result(__cdecl*)(Parameter**);
using CoreShutdown=Result(__cdecl*)();
// Caller shim exports: the runtime checks the module that calls it.
using ShimInit=Result(__cdecl*)(void*,unsigned long long,const wchar_t*,ID3D12Device*,int,const void*);
using ShimCreate=Result(__cdecl*)(void*,ID3D12GraphicsCommandList*,int,Parameter*,Handle**);
using ShimEvaluate=Result(__cdecl*)(void*,ID3D12GraphicsCommandList*,const Handle*,const Parameter*,void*);
using ShimRelease=Result(__cdecl*)(void*,Handle*);
}

void Check(HRESULT hr,const char* stage) { if(FAILED(hr)) throw std::runtime_error(stage); }
template<class T> T Export(HMODULE module,const char* name) {
    auto fn=GetProcAddress(module,name);
    if(!fn) throw std::runtime_error(std::string("missing export ")+name);
    return reinterpret_cast<T>(fn);
}
bool IsFile(const std::filesystem::path& path) {
    const auto a=GetFileAttributesW(path.c_str());
    return a!=INVALID_FILE_ATTRIBUTES && !(a&FILE_ATTRIBUTE_DIRECTORY);
}
// The driver's NGX core: an override in the runtime folder, NVIDIA's registry
// entry, the normal search, then the newest NVIDIA DriverStore package.
HMODULE LoadCore(const std::filesystem::path& runtime) {
    if(IsFile(runtime/L"_nvngx.dll"))
        if(auto m=LoadLibraryW((runtime/L"_nvngx.dll").c_str()))return m;
    wchar_t folder[MAX_PATH]{};DWORD bytes=sizeof(folder);
    if(RegGetValueW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore",L"FullPath",RRF_RT_REG_SZ,nullptr,folder,&bytes)==ERROR_SUCCESS) {
        const auto core=std::filesystem::path(folder)/L"_nvngx.dll";
        if(IsFile(core))if(auto m=LoadLibraryW(core.c_str()))return m;
    }
    if(auto m=LoadLibraryW(L"_nvngx.dll"))return m;
    wchar_t windows[MAX_PATH]{};
    const auto length=GetWindowsDirectoryW(windows,MAX_PATH);
    if(!length || length>=MAX_PATH)return nullptr;
    const auto store=std::filesystem::path(windows)/L"System32"/L"DriverStore"/L"FileRepository";
    struct Candidate { std::filesystem::path path; ULONGLONG stamp; };
    std::vector<Candidate> candidates;
    WIN32_FIND_DATAW found{};
    const auto search=FindFirstFileW((store/L"nv*.inf_*").c_str(),&found);
    if(search!=INVALID_HANDLE_VALUE) {
        do {
            if(!(found.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY))continue;
            const auto core=store/found.cFileName/L"_nvngx.dll";
            WIN32_FILE_ATTRIBUTE_DATA data{};
            if(GetFileAttributesExW(core.c_str(),GetFileExInfoStandard,&data) && !(data.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY))
                candidates.push_back({core,(ULONGLONG(data.ftLastWriteTime.dwHighDateTime)<<32)|data.ftLastWriteTime.dwLowDateTime});
        } while(FindNextFileW(search,&found));
        FindClose(search);
    }
    std::sort(candidates.begin(),candidates.end(),[](const Candidate& a,const Candidate& b){return a.stamp>b.stamp;});
    for(const auto& c:candidates)if(auto m=LoadLibraryW(c.path.c_str()))return m;
    return nullptr;
}

struct Handle { HANDLE value{}; ~Handle(){if(value)CloseHandle(value);} };

// D3D11 (shared with the game) <-> D3D12 (Neural Rendering) on one adapter.
// The frame is processed in one or two stages, each its own NGX feature: the
// whole picture (optionally at reduced resolution) and, when foveated, the
// centre at full resolution. Each stage's change (result minus its input) is
// added to the full-resolution frame, so a smaller stage softens the neural
// look rather than the picture itself.
class Neural {
public:
    Neural(const vr::bridge::Shared& shared,const std::filesystem::path& runtime,const std::filesystem::path& shim,FILE* log):
        width_(shared.width),height_(shared.height),format_(DXGI_FORMAT(shared.format)),log_(log) {
        Devices(shared.adapter);
        Modules(runtime,shim);
        Resources();
        Initialize(runtime);
        Configure(vr::bridge::Clamped(shared.neural)); // The settings at start, so the first frame needs no rebuild.
    }
    ~Neural() {
        ReleaseStages();
        if(coreShutdown_)coreShutdown_();
        if(event_)CloseHandle(event_);
        for(auto m:{shimModule_,runtimeModule_,coreModule_})if(m)FreeLibrary(m);
    }
    ID3D11Device* Device11() const {return device11_.Get();}
    void Process(ID3D11Texture2D* input,ID3D11Texture2D* output,vr::bridge::NeuralSettings settings) {
        settings=vr::bridge::Clamped(settings);
        Configure(settings);
        if(settings.passes!=lastPasses_ || settings.mix!=lastMix_) {
            char line[96]{};std::snprintf(line,sizeof(line),"neural passes=%d mix=%.2f style=%d",settings.passes,settings.mix,settings.style);Note(line);
            lastPasses_=settings.passes;lastMix_=settings.mix;
        }
        ++sequence_;
        LARGE_INTEGER start{};QueryPerformanceCounter(&start);
        context11_->CopyResource(input8_.Get(),input);
        for(auto& stage:stages_) {
            // The stage's input: its part of the frame, resampled to its size.
            Crop(stage,stage.sourceTarget.Get());Crop(stage,stage.colorTarget.Get());Crop(stage,stage.outputTarget.Get());
        }
        Check(context4_->Signal(inputFence11_.Get(),sequence_),"signal input");context11_->Flush();
        Check(queue_->Wait(inputFence12_.Get(),sequence_),"wait input");
        Check(allocator_->Reset(),"allocator reset");Check(list_->Reset(allocator_.Get(),nullptr),"list reset");
        for(auto& stage:stages_) {
            Barrier(stage.color12.Get(),D3D12_RESOURCE_STATE_COMMON,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
            Barrier(stage.output12.Get(),D3D12_RESOURCE_STATE_COMMON,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Bind(stage);
            params_->Set("DLSSNR.Reset",1); // Still frames: no temporal history.
            for(int pass=0;pass<settings.passes;++pass) {
                if(pass) {
                    // Chain: this pass starts from the previous pass's result.
                    Barrier(stage.output12.Get(),D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COPY_SOURCE);
                    Barrier(stage.color12.Get(),D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_COPY_DEST);
                    list_->CopyResource(stage.color12.Get(),stage.output12.Get());
                    Barrier(stage.color12.Get(),D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
                    Barrier(stage.output12.Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
                }
                // Later passes refine without applying the look twice (community multi-pass practice).
                params_->Set("DLSSNR.Intensity",pass?1.0f:settings.intensity);
                params_->Set("DLSSNR.LocalToneStrength",pass?0.0f:settings.tone);
                params_->Set("DLSSNR.LocalStructureStrength",pass?(settings.structure<1?settings.structure:1.0f):settings.structure);
                if(shimEvaluate_(evaluate_,list_.Get(),stage.feature,params_,nullptr)!=ngx::Success)throw std::runtime_error("neural evaluate");
            }
            Barrier(stage.color12.Get(),D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_COMMON);
            Barrier(stage.output12.Get(),D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COMMON);
        }
        Check(list_->Close(),"list close");
        ID3D12CommandList* lists[]{list_.Get()};queue_->ExecuteCommandLists(1,lists);
        Check(queue_->Signal(outputFence12_.Get(),sequence_),"signal output");
        Check(context4_->Wait(outputFence11_.Get(),sequence_),"wait output");
        Compose(settings.mix);
        context11_->CopyResource(output,output8_.Get());
        Check(context4_->Signal(completion_.Get(),sequence_),"signal completion");context11_->Flush();
        Check(completion_->SetEventOnCompletion(sequence_,event_),"completion event");
        if(WaitForSingleObject(event_,4000)!=WAIT_OBJECT_0)throw std::runtime_error("neural GPU timeout");
        // Frame cost (CPU wait for the whole round trip): the first frames after a change, then every 600.
        LARGE_INTEGER end{},frequency{};QueryPerformanceCounter(&end);QueryPerformanceFrequency(&frequency);
        const double ms=double(end.QuadPart-start.QuadPart)*1000/double(frequency.QuadPart);
        timeSum_+=ms;++timeFrames_;
        if(timeFrames_<=3 || timeFrames_%600==0) {
            char line[96]{};std::snprintf(line,sizeof(line),"neural frame ms=%.2f average=%.2f frames=%u",ms,timeSum_/timeFrames_,timeFrames_);Note(line);
        }
    }
private:
    struct Stage {
        UINT width{},height{};
        float rect[4]{0,0,1,1}; // Source area of the frame (uv).
        ComPtr<ID3D11Texture2D> source11,color11,output11;ComPtr<ID3D12Resource> color12,output12;
        ComPtr<ID3D11ShaderResourceView> sourceView,outputView;
        ComPtr<ID3D11RenderTargetView> sourceTarget,colorTarget,outputTarget;
        ngx::Handle* feature{};
    };
    void Note(const char* text) {if(log_){std::fprintf(log_,"%s\n",text);std::fflush(log_);}}
    void Devices(LUID adapterId) {
        ComPtr<IDXGIFactory4> factory;Check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"dxgi factory");
        Check(factory->EnumAdapterByLuid(adapterId,IID_PPV_ARGS(&adapter_)),"game adapter");
        DXGI_ADAPTER_DESC1 desc{};Check(adapter_->GetDesc1(&desc),"adapter description");
        if(desc.VendorId!=0x10de)throw std::runtime_error("not an NVIDIA GPU");
        Check(D3D12CreateDevice(adapter_.Get(),D3D_FEATURE_LEVEL_12_0,IID_PPV_ARGS(&device12_)),"d3d12 device");
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        Check(device12_->CreateCommandQueue(&q,IID_PPV_ARGS(&queue_)),"queue");
        Check(device12_->CreateCommandAllocator(q.Type,IID_PPV_ARGS(&allocator_)),"allocator");
        Check(device12_->CreateCommandList(0,q.Type,allocator_.Get(),nullptr,IID_PPV_ARGS(&list_)),"command list");
        Check(list_->Close(),"initial close");
        const D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_0};
        Check(D3D11CreateDevice(adapter_.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,levels,1,D3D11_SDK_VERSION,&device11_,nullptr,&context11_),"d3d11 device");
        Check(device11_.As(&device5_),"d3d11 device5");Check(context11_.As(&context4_),"d3d11 context4");
    }
    void Modules(const std::filesystem::path& runtime,const std::filesystem::path& shim) {
        coreModule_=LoadCore(runtime);
        if(!coreModule_)throw std::runtime_error("NVIDIA NGX core (_nvngx.dll) not found; update the NVIDIA driver");
        const auto nr=runtime/L"nvngx_dlssnr.dll";
        if(!IsFile(nr))throw std::runtime_error("nvngx_dlssnr.dll not found in the runtime folder");
        runtimeModule_=LoadLibraryExW(nr.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        shimModule_=LoadLibraryExW(shim.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if(!runtimeModule_ || !shimModule_)throw std::runtime_error("runtime or caller shim could not be loaded");
        coreInit_=Export<ngx::CoreInit>(coreModule_,"NVSDK_NGX_D3D12_Init_Ext");
        coreInitProject_=reinterpret_cast<ngx::CoreInitProject>(GetProcAddress(coreModule_,"NVSDK_NGX_D3D12_Init_ProjectID"));
        allocate_=Export<ngx::Allocate>(coreModule_,"NVSDK_NGX_D3D12_AllocateParameters");
        coreShutdown_=Export<ngx::CoreShutdown>(coreModule_,"NVSDK_NGX_D3D12_Shutdown");
        init_=Export<void*>(runtimeModule_,"NVSDK_NGX_D3D12_Init_Ext");
        create_=Export<void*>(runtimeModule_,"NVSDK_NGX_D3D12_CreateFeature");
        evaluate_=Export<void*>(runtimeModule_,"NVSDK_NGX_D3D12_EvaluateFeature");
        release_=Export<void*>(runtimeModule_,"NVSDK_NGX_D3D12_ReleaseFeature");
        shimInit_=Export<ngx::ShimInit>(shimModule_,"DLSSNR_CallInit");
        shimCreate_=Export<ngx::ShimCreate>(shimModule_,"DLSSNR_CallCreate");
        shimEvaluate_=Export<ngx::ShimEvaluate>(shimModule_,"DLSSNR_CallEvaluate");
        shimRelease_=Export<ngx::ShimRelease>(shimModule_,"DLSSNR_CallRelease");
    }
    static D3D11_TEXTURE2D_DESC Describe(UINT width,UINT height,DXGI_FORMAT format,UINT bind,UINT misc) {
        D3D11_TEXTURE2D_DESC d{};d.Width=width;d.Height=height;d.MipLevels=d.ArraySize=1;d.Format=format;
        d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=bind;d.MiscFlags=misc;return d;
    }
    void Resources() {
        auto d=Describe(width_,height_,format_,D3D11_BIND_SHADER_RESOURCE,0);Check(device11_->CreateTexture2D(&d,nullptr,&input8_),"input8");
        d=Describe(width_,height_,format_,D3D11_BIND_RENDER_TARGET,0);Check(device11_->CreateTexture2D(&d,nullptr,&output8_),"output8");
        Check(device11_->CreateShaderResourceView(input8_.Get(),nullptr,&inputView_),"input view");
        Check(device11_->CreateRenderTargetView(output8_.Get(),nullptr,&output8Target_),"output8 target");
        const char* shader=R"(
            Texture2D<float4> image:register(t0);
            Texture2D<float4> fullOut:register(t1);
            Texture2D<float4> fullSource:register(t2);
            Texture2D<float4> centreOut:register(t3);
            Texture2D<float4> centreSource:register(t4);
            SamplerState linearClamp:register(s0);
            cbuffer Constants:register(b0) { float4 rect; float4 options; } // options: mix, centre on, feather
            struct V { float4 position:SV_Position; float2 uv:TEXCOORD0; };
            V VS(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1); return v; }
            float4 CropPS(V v):SV_Target { return float4(image.SampleLevel(linearClamp,lerp(rect.xy,rect.zw,v.uv),0).rgb,1); }
            float4 ComposePS(V v):SV_Target {
                float3 o=image.Load(int3(int2(v.position.xy),0)).rgb;
                float3 d=saturate(fullOut.SampleLevel(linearClamp,v.uv,0).rgb)-fullSource.SampleLevel(linearClamp,v.uv,0).rgb;
                if(options.y>0.5) {
                    float2 c=(v.uv-rect.xy)/(rect.zw-rect.xy);
                    if(all(c>0) && all(c<1)) {
                        float edge=min(min(c.x,1-c.x),min(c.y,1-c.y));
                        float3 centre=saturate(centreOut.SampleLevel(linearClamp,c,0).rgb)-centreSource.SampleLevel(linearClamp,c,0).rgb;
                        d=lerp(d,centre,smoothstep(0,options.z,edge));
                    }
                }
                return float4(lerp(o,saturate(o+d),options.x),1);
            })";
        ComPtr<ID3DBlob> vertex,crop,compose;
        Check(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"VS","vs_5_0",0,0,&vertex,nullptr),"compile vs");
        Check(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"CropPS","ps_5_0",0,0,&crop,nullptr),"compile crop");
        Check(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"ComposePS","ps_5_0",0,0,&compose,nullptr),"compile compose");
        Check(device11_->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&vs_),"vs");
        Check(device11_->CreatePixelShader(crop->GetBufferPointer(),crop->GetBufferSize(),nullptr,&cropPs_),"crop ps");
        Check(device11_->CreatePixelShader(compose->GetBufferPointer(),compose->GetBufferSize(),nullptr,&composePs_),"compose ps");
        D3D11_BUFFER_DESC constants{};constants.ByteWidth=32;constants.Usage=D3D11_USAGE_DEFAULT;constants.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        Check(device11_->CreateBuffer(&constants,nullptr,&constants_),"constants");
        D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampler.MaxLOD=D3D11_FLOAT32_MAX;
        Check(device11_->CreateSamplerState(&sampler,&sampler_),"sampler");
        ShareFence(inputFence12_,inputFence11_);ShareFence(outputFence12_,outputFence11_);
        Check(device5_->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&completion_)),"completion fence");
        event_=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!event_)throw std::runtime_error("completion event");
    }
    void Initialize(const std::filesystem::path& runtime) {
        const std::wstring folder=runtime.wstring();
        const wchar_t* paths[]{folder.c_str()};
        ngx::CommonInfo common{};common.paths={paths,1};
        bool core=false;
        for(int version=0x13;version<=0x20 && !core && coreInitProject_;++version)
            core=coreInitProject_(ngx::Project,0,"1.0",folder.c_str(),device12_.Get(),version,nullptr)==ngx::Success;
        for(int version=0x13;version<=0x20 && !core;++version)
            core=coreInit_(ngx::Application,folder.c_str(),device12_.Get(),version,&common)==ngx::Success;
        if(!core)throw std::runtime_error("NGX core initialization failed");
        if(shimInit_(init_,ngx::Application,folder.c_str(),device12_.Get(),0x15,&common)!=ngx::Success)
            throw std::runtime_error("Neural Rendering runtime initialization failed (unsupported GPU or runtime?)");
        if(allocate_(&params_)!=ngx::Success || !params_)throw std::runtime_error("NGX parameters");
    }
    // Rebuilds the stages when the style, resolution or foveation changes (the GPU is idle here).
    void Configure(const vr::bridge::NeuralSettings& settings) {
        // A full-resolution frame needs no separate centre.
        const int foveation=settings.resolution<100?settings.foveation:0;
        if(!stages_.empty() && settings.style==style_ && settings.resolution==resolution_ && foveation==foveation_)return;
        ReleaseStages();
        const auto scaled=[](UINT size,int percent){return std::max<UINT>(64,UINT((uint64_t(size)*unsigned(percent)+50)/100));};
        stages_.reserve(2);
        Stage full;full.width=scaled(width_,settings.resolution);full.height=scaled(height_,settings.resolution);
        stages_.push_back(std::move(full));
        if(foveation) {
            Stage centre;centre.width=scaled(width_,foveation);centre.height=scaled(height_,foveation);
            const float x=.5f*(1-float(centre.width)/float(width_)),y=.5f*(1-float(centre.height)/float(height_));
            centre.rect[0]=x;centre.rect[1]=y;centre.rect[2]=1-x;centre.rect[3]=1-y;
            stages_.push_back(std::move(centre));
        }
        for(auto& stage:stages_){StageResources(stage);Create(stage,settings.style);}
        style_=settings.style;resolution_=settings.resolution;foveation_=foveation;timeSum_=0;timeFrames_=0;
        char line[200]{};std::snprintf(line,sizeof(line),"neural ready %ux%u format=%u style=%d resolution=%d%% stage=%ux%u foveation=%d%%%s",
            width_,height_,unsigned(format_),style_,resolution_,stages_[0].width,stages_[0].height,foveation_,foveation_?"":" (off)");
        Note(line);
    }
    void ReleaseStages() {
        for(auto& stage:stages_)if(stage.feature && release_)shimRelease_(release_,stage.feature);
        stages_.clear();
    }
    void StageResources(Stage& stage) {
        auto d=Describe(stage.width,stage.height,DXGI_FORMAT_R16G16B16A16_FLOAT,D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET,0);
        Check(device11_->CreateTexture2D(&d,nullptr,&stage.source11),"stage source");
        d=Describe(stage.width,stage.height,DXGI_FORMAT_R16G16B16A16_FLOAT,D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET|D3D11_BIND_UNORDERED_ACCESS,
            D3D11_RESOURCE_MISC_SHARED_NTHANDLE|D3D11_RESOURCE_MISC_SHARED);
        Check(device11_->CreateTexture2D(&d,nullptr,&stage.color11),"shared colour");
        Check(device11_->CreateTexture2D(&d,nullptr,&stage.output11),"shared output");
        Share(stage.color11.Get(),stage.color12);Share(stage.output11.Get(),stage.output12);
        Check(device11_->CreateShaderResourceView(stage.source11.Get(),nullptr,&stage.sourceView),"stage source view");
        Check(device11_->CreateShaderResourceView(stage.output11.Get(),nullptr,&stage.outputView),"output view");
        Check(device11_->CreateRenderTargetView(stage.source11.Get(),nullptr,&stage.sourceTarget),"stage source target");
        Check(device11_->CreateRenderTargetView(stage.color11.Get(),nullptr,&stage.colorTarget),"colour target");
        Check(device11_->CreateRenderTargetView(stage.output11.Get(),nullptr,&stage.outputTarget),"output target");
    }
    // Points the shared NGX parameters at one stage's textures.
    void Bind(const Stage& stage) {
        const auto w=stage.width,h=stage.height;
        params_->Set("DLSSNR.Width",w);params_->Set("DLSSNR.Height",h);
        params_->Set("DLSSNR.Color",stage.color12.Get());params_->Set("DLSSNR.Output",stage.output12.Get());params_->Set("DLSSNR.Backbuffer",stage.output12.Get());
        params_->Set("DLSSNR.ColorSubrectWidth",w);params_->Set("DLSSNR.ColorSubrectHeight",h);
        params_->Set("DLSSNR.OutputSubrectWidth",w);params_->Set("DLSSNR.OutputSubrectHeight",h);
    }
    void Create(Stage& stage,int style) {
        Bind(stage);params_->Set("DLSSNR.Enabled",1);
        params_->Set("DLSSNR.Reset",1);params_->Set("DLSSNR.Style",style);params_->Set("DLSSNR.Hint.Render.Preset",3);
        params_->Set("DLSSNR.Intensity",1.0f);params_->Set("DLSSNR.LocalToneStrength",1.0f);params_->Set("DLSSNR.LocalStructureStrength",1.0f);
        params_->Set("DLSSNR.SkinStructureStrength",-1.0f);params_->Set("DLSSNR.UseAutoMask",0);params_->Set("DLSSNR.UICorrection",0);
        params_->Set("DLSSNR.ScalingRatio",1.0f);params_->Set("DLSSNR.Upscaling",0);
        params_->Set("DLSSNR.MVec",static_cast<ID3D12Resource*>(nullptr));
        Check(allocator_->Reset(),"allocator reset");Check(list_->Reset(allocator_.Get(),nullptr),"list reset");
        if(shimCreate_(create_,list_.Get(),ngx::NeuralRendering,params_,&stage.feature)!=ngx::Success || !stage.feature)
            throw std::runtime_error("Neural Rendering feature could not be created");
        Check(list_->Close(),"create close");
        ID3D12CommandList* lists[]{list_.Get()};queue_->ExecuteCommandLists(1,lists);
        ComPtr<ID3D12Fence> fence;Check(device12_->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)),"create fence");
        Handle wait{CreateEventW(nullptr,FALSE,FALSE,nullptr)};
        Check(queue_->Signal(fence.Get(),1),"create signal");Check(fence->SetEventOnCompletion(1,wait.value),"create wait");
        if(WaitForSingleObject(wait.value,10000)!=WAIT_OBJECT_0)throw std::runtime_error("feature creation timeout");
    }
    void Share(ID3D11Texture2D* texture,ComPtr<ID3D12Resource>& shared) {
        ComPtr<IDXGIResource1> resource;Check(texture->QueryInterface(IID_PPV_ARGS(&resource)),"dxgi resource");
        HANDLE handle{};Check(resource->CreateSharedHandle(nullptr,DXGI_SHARED_RESOURCE_READ|DXGI_SHARED_RESOURCE_WRITE,nullptr,&handle),"texture handle");
        const auto hr=device12_->OpenSharedHandle(handle,IID_PPV_ARGS(&shared));CloseHandle(handle);Check(hr,"open texture");
    }
    void ShareFence(ComPtr<ID3D12Fence>& fence,ComPtr<ID3D11Fence>& opened) {
        Check(device12_->CreateFence(0,D3D12_FENCE_FLAG_SHARED,IID_PPV_ARGS(&fence)),"shared fence");
        HANDLE handle{};Check(device12_->CreateSharedHandle(fence.Get(),nullptr,GENERIC_ALL,nullptr,&handle),"fence handle");
        const auto hr=device5_->OpenSharedFence(handle,IID_PPV_ARGS(&opened));CloseHandle(handle);Check(hr,"open fence");
    }
    void Draw(ID3D11PixelShader* shader,const float (&constants)[8],ID3D11ShaderResourceView* const* views,UINT count,ID3D11RenderTargetView* target,UINT width,UINT height) {
        context11_->UpdateSubresource(constants_.Get(),0,nullptr,constants,0,0);
        D3D11_VIEWPORT viewport{0,0,float(width),float(height),0,1};context11_->RSSetViewports(1,&viewport);
        context11_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context11_->VSSetShader(vs_.Get(),nullptr,0);context11_->PSSetShader(shader,nullptr,0);
        context11_->PSSetShaderResources(0,count,views);
        auto* buffer=constants_.Get();context11_->PSSetConstantBuffers(0,1,&buffer);
        auto* sampler=sampler_.Get();context11_->PSSetSamplers(0,1,&sampler);
        context11_->OMSetRenderTargets(1,&target,nullptr);
        context11_->Draw(3,0);context11_->ClearState();
    }
    void Crop(const Stage& stage,ID3D11RenderTargetView* target) {
        const float constants[8]{stage.rect[0],stage.rect[1],stage.rect[2],stage.rect[3],0,0,0,0};
        ID3D11ShaderResourceView* views[]{inputView_.Get()};
        Draw(cropPs_.Get(),constants,views,1,target,stage.width,stage.height);
    }
    // Original frame plus each stage's change, then blended with the original by mix.
    void Compose(float mix) {
        const auto& full=stages_[0];
        const Stage* centre=stages_.size()>1?&stages_[1]:nullptr;
        float constants[8]{0,0,1,1,mix,centre?1.f:0.f,.12f,0};
        if(centre)std::memcpy(constants,centre->rect,sizeof(centre->rect));
        ID3D11ShaderResourceView* views[]{inputView_.Get(),full.outputView.Get(),full.sourceView.Get(),
            centre?centre->outputView.Get():full.outputView.Get(),centre?centre->sourceView.Get():full.sourceView.Get()};
        Draw(composePs_.Get(),constants,views,5,output8Target_.Get(),width_,height_);
    }
    void Barrier(ID3D12Resource* resource,D3D12_RESOURCE_STATES before,D3D12_RESOURCE_STATES after) {
        D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition={resource,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,before,after};list_->ResourceBarrier(1,&b);
    }
    UINT width_,height_;DXGI_FORMAT format_;FILE* log_;
    ComPtr<IDXGIAdapter1> adapter_;
    ComPtr<ID3D12Device> device12_;ComPtr<ID3D12CommandQueue> queue_;ComPtr<ID3D12CommandAllocator> allocator_;ComPtr<ID3D12GraphicsCommandList> list_;
    ComPtr<ID3D11Device> device11_;ComPtr<ID3D11DeviceContext> context11_;ComPtr<ID3D11Device5> device5_;ComPtr<ID3D11DeviceContext4> context4_;
    ComPtr<ID3D11Texture2D> input8_,output8_;
    ComPtr<ID3D11ShaderResourceView> inputView_;ComPtr<ID3D11RenderTargetView> output8Target_;
    ComPtr<ID3D11VertexShader> vs_;ComPtr<ID3D11PixelShader> cropPs_,composePs_;ComPtr<ID3D11Buffer> constants_;ComPtr<ID3D11SamplerState> sampler_;
    std::vector<Stage> stages_;
    int style_{-1},resolution_{},foveation_{},lastPasses_{};float lastMix_{-1};double timeSum_{};unsigned timeFrames_{};
    ComPtr<ID3D12Fence> inputFence12_,outputFence12_;ComPtr<ID3D11Fence> inputFence11_,outputFence11_,completion_;
    HANDLE event_{};UINT64 sequence_{};
    HMODULE coreModule_{},runtimeModule_{},shimModule_{};
    ngx::CoreInit coreInit_{};ngx::CoreInitProject coreInitProject_{};ngx::Allocate allocate_{};ngx::CoreShutdown coreShutdown_{};
    void *init_{},*create_{},*evaluate_{},*release_{};
    ngx::ShimInit shimInit_{};ngx::ShimCreate shimCreate_{};ngx::ShimEvaluate shimEvaluate_{};ngx::ShimRelease shimRelease_{};
    ngx::Parameter* params_{};
};
}

int RunNeural(vr::bridge::Shared& shared,HANDLE request,HANDLE done,HANDLE stop,const std::filesystem::path& runtime,FILE* log) {
    using namespace vr::bridge;
    auto fail=[&](const char* why,HRESULT hr) {
        if(log){std::fprintf(log,"neural failed: %s\n",why);std::fflush(log);}
        shared.error=hr;InterlockedExchange(&shared.status,static_cast<LONG>(Status::Failed));SetEvent(done);return 1;
    };
    if(!Valid(shared) || (shared.format!=DXGI_FORMAT_R8G8B8A8_UNORM && shared.format!=DXGI_FORMAT_B8G8R8A8_UNORM) ||
       shared.width>8192 || shared.height>8192)return fail("unsupported frame",E_INVALIDARG);
    Handle parent{OpenProcess(SYNCHRONIZE,FALSE,shared.parent)};
    if(!parent.value)return fail("parent process",HRESULT_FROM_WIN32(GetLastError()));
    wchar_t self[MAX_PATH]{};GetModuleFileNameW(nullptr,self,MAX_PATH);
    const auto shim=std::filesystem::path(self).parent_path()/L"nvngx.dll_dirt2vr.dll";
    try {
        Neural neural(shared,runtime,shim,log);
        ComPtr<ID3D11Texture2D> input,output;ComPtr<IDXGIKeyedMutex> inputLock,outputLock;
        Check(neural.Device11()->OpenSharedResource(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(shared.inputHandle)),IID_PPV_ARGS(&input)),"open input");
        Check(neural.Device11()->OpenSharedResource(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(shared.outputHandle)),IID_PPV_ARGS(&output)),"open output");
        Check(input.As(&inputLock),"input lock");Check(output.As(&outputLock),"output lock");
        InterlockedExchange(&shared.status,static_cast<LONG>(Status::Ready));
        uint64_t previous{};
        HANDLE waits[]{stop,parent.value,request};
        while(WaitForMultipleObjects(3,waits,FALSE,INFINITE)==WAIT_OBJECT_0+2) {
            const auto sequence=shared.requested;
            if(sequence<=previous)return fail("non-increasing request",E_INVALIDARG);
            if(inputLock->AcquireSync(1,100)!=S_OK)return fail("input ownership",DXGI_ERROR_WAIT_TIMEOUT);
            if(outputLock->AcquireSync(0,100)!=S_OK){inputLock->ReleaseSync(0);return fail("output ownership",DXGI_ERROR_WAIT_TIMEOUT);}
            neural.Process(input.Get(),output.Get(),shared.neural);
            if(outputLock->ReleaseSync(1)!=S_OK || inputLock->ReleaseSync(0)!=S_OK)return fail("ownership release",E_FAIL);
            previous=sequence;shared.completed=sequence;SetEvent(done);
        }
    }
    catch(const std::exception& error) {return fail(error.what(),E_FAIL);}
    InterlockedExchange(&shared.status,static_cast<LONG>(Status::Stopped));
    return 0;
}
