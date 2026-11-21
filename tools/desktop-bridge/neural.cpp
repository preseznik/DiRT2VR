// Neural Rendering mode of the x64 helper: D3D11 frame -> D3D12 DLSS 5 Neural
// Rendering -> D3D11 frame, one still at a time (history reset every frame).
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
class Neural {
public:
    Neural(const vr::bridge::Shared& shared,const std::filesystem::path& runtime,const std::filesystem::path& shim,FILE* log):
        width_(shared.width),height_(shared.height),format_(DXGI_FORMAT(shared.format)),log_(log) {
        Devices(shared.adapter);
        Modules(runtime,shim);
        Resources();
        Feature(runtime);
    }
    ~Neural() {
        if(feature_ && release_)shimRelease_(release_,feature_);
        if(coreShutdown_)coreShutdown_();
        if(event_)CloseHandle(event_);
        for(auto m:{shimModule_,runtimeModule_,coreModule_})if(m)FreeLibrary(m);
    }
    ID3D11Device* Device11() const {return device11_.Get();}
    void Process(ID3D11Texture2D* input,ID3D11Texture2D* output) {
        ++sequence_;
        context11_->CopyResource(input8_.Get(),input);
        Convert(inputView_.Get(),colorTarget_.Get());
        Convert(inputView_.Get(),outputTarget_.Get());
        Check(context4_->Signal(inputFence11_.Get(),sequence_),"signal input");context11_->Flush();
        Check(queue_->Wait(inputFence12_.Get(),sequence_),"wait input");
        Check(allocator_->Reset(),"allocator reset");Check(list_->Reset(allocator_.Get(),nullptr),"list reset");
        Barrier(color12_.Get(),D3D12_RESOURCE_STATE_COMMON,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Barrier(output12_.Get(),D3D12_RESOURCE_STATE_COMMON,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        params_->Set("DLSSNR.Reset",1); // Still frames: no temporal history.
        if(shimEvaluate_(evaluate_,list_.Get(),feature_,params_,nullptr)!=ngx::Success)throw std::runtime_error("neural evaluate");
        Barrier(color12_.Get(),D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_COMMON);
        Barrier(output12_.Get(),D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_COMMON);
        Check(list_->Close(),"list close");
        ID3D12CommandList* lists[]{list_.Get()};queue_->ExecuteCommandLists(1,lists);
        Check(queue_->Signal(outputFence12_.Get(),sequence_),"signal output");
        Check(context4_->Wait(outputFence11_.Get(),sequence_),"wait output");
        Convert(outputView_.Get(),output8Target_.Get());
        context11_->CopyResource(output,output8_.Get());
        Check(context4_->Signal(completion_.Get(),sequence_),"signal completion");context11_->Flush();
        Check(completion_->SetEventOnCompletion(sequence_,event_),"completion event");
        if(WaitForSingleObject(event_,4000)!=WAIT_OBJECT_0)throw std::runtime_error("neural GPU timeout");
    }
private:
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
    void Resources() {
        auto describe=[&](DXGI_FORMAT format,UINT bind,UINT misc) {
            D3D11_TEXTURE2D_DESC d{};d.Width=width_;d.Height=height_;d.MipLevels=d.ArraySize=1;d.Format=format;
            d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=bind;d.MiscFlags=misc;return d;
        };
        auto d=describe(format_,D3D11_BIND_SHADER_RESOURCE,0);Check(device11_->CreateTexture2D(&d,nullptr,&input8_),"input8");
        d=describe(format_,D3D11_BIND_RENDER_TARGET,0);Check(device11_->CreateTexture2D(&d,nullptr,&output8_),"output8");
        d=describe(DXGI_FORMAT_R16G16B16A16_FLOAT,D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET|D3D11_BIND_UNORDERED_ACCESS,
            D3D11_RESOURCE_MISC_SHARED_NTHANDLE|D3D11_RESOURCE_MISC_SHARED);
        Check(device11_->CreateTexture2D(&d,nullptr,&color11_),"shared colour");
        Check(device11_->CreateTexture2D(&d,nullptr,&output11_),"shared output");
        Share(color11_.Get(),color12_);Share(output11_.Get(),output12_);
        Check(device11_->CreateShaderResourceView(input8_.Get(),nullptr,&inputView_),"input view");
        Check(device11_->CreateShaderResourceView(output11_.Get(),nullptr,&outputView_),"output view");
        Check(device11_->CreateRenderTargetView(color11_.Get(),nullptr,&colorTarget_),"colour target");
        Check(device11_->CreateRenderTargetView(output11_.Get(),nullptr,&outputTarget_),"output target");
        Check(device11_->CreateRenderTargetView(output8_.Get(),nullptr,&output8Target_),"output8 target");
        const char* shader=R"(
            Texture2D<float4> image:register(t0);
            float4 VS(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),0,1); }
            float4 PS(float4 position:SV_Position):SV_Target { return image.Load(int3(int2(position.xy),0)); })";
        ComPtr<ID3DBlob> vertex,pixel;
        Check(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"VS","vs_5_0",0,0,&vertex,nullptr),"compile vs");
        Check(D3DCompile(shader,std::strlen(shader),nullptr,nullptr,nullptr,"PS","ps_5_0",0,0,&pixel,nullptr),"compile ps");
        Check(device11_->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&vs_),"vs");
        Check(device11_->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps_),"ps");
        ShareFence(inputFence12_,inputFence11_);ShareFence(outputFence12_,outputFence11_);
        Check(device5_->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&completion_)),"completion fence");
        event_=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!event_)throw std::runtime_error("completion event");
    }
    void Feature(const std::filesystem::path& runtime) {
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
        const auto w=width_,h=height_;
        params_->Set("DLSSNR.Width",w);params_->Set("DLSSNR.Height",h);params_->Set("DLSSNR.Enabled",1);
        params_->Set("DLSSNR.Reset",1);params_->Set("DLSSNR.Style",1);params_->Set("DLSSNR.Hint.Render.Preset",3);
        params_->Set("DLSSNR.Intensity",1.0f);params_->Set("DLSSNR.LocalToneStrength",1.0f);params_->Set("DLSSNR.LocalStructureStrength",1.0f);
        params_->Set("DLSSNR.SkinStructureStrength",-1.0f);params_->Set("DLSSNR.UseAutoMask",0);params_->Set("DLSSNR.UICorrection",0);
        params_->Set("DLSSNR.ScalingRatio",1.0f);params_->Set("DLSSNR.Upscaling",0);
        params_->Set("DLSSNR.Color",color12_.Get());params_->Set("DLSSNR.Output",output12_.Get());params_->Set("DLSSNR.Backbuffer",output12_.Get());
        params_->Set("DLSSNR.ColorSubrectWidth",w);params_->Set("DLSSNR.ColorSubrectHeight",h);
        params_->Set("DLSSNR.OutputSubrectWidth",w);params_->Set("DLSSNR.OutputSubrectHeight",h);
        params_->Set("DLSSNR.MVec",static_cast<ID3D12Resource*>(nullptr));
        Check(allocator_->Reset(),"allocator reset");Check(list_->Reset(allocator_.Get(),nullptr),"list reset");
        if(shimCreate_(create_,list_.Get(),ngx::NeuralRendering,params_,&feature_)!=ngx::Success || !feature_)
            throw std::runtime_error("Neural Rendering feature could not be created");
        Check(list_->Close(),"create close");
        ID3D12CommandList* lists[]{list_.Get()};queue_->ExecuteCommandLists(1,lists);
        ComPtr<ID3D12Fence> fence;Check(device12_->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)),"create fence");
        Handle wait{CreateEventW(nullptr,FALSE,FALSE,nullptr)};
        Check(queue_->Signal(fence.Get(),1),"create signal");Check(fence->SetEventOnCompletion(1,wait.value),"create wait");
        if(WaitForSingleObject(wait.value,10000)!=WAIT_OBJECT_0)throw std::runtime_error("feature creation timeout");
        char line[160]{};std::snprintf(line,sizeof(line),"neural ready %ux%u format=%u",width_,height_,unsigned(format_));Note(line);
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
    void Convert(ID3D11ShaderResourceView* source,ID3D11RenderTargetView* target) {
        D3D11_VIEWPORT viewport{0,0,float(width_),float(height_),0,1};context11_->RSSetViewports(1,&viewport);
        context11_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context11_->VSSetShader(vs_.Get(),nullptr,0);context11_->PSSetShader(ps_.Get(),nullptr,0);
        context11_->PSSetShaderResources(0,1,&source);context11_->OMSetRenderTargets(1,&target,nullptr);
        context11_->Draw(3,0);context11_->ClearState();
    }
    void Barrier(ID3D12Resource* resource,D3D12_RESOURCE_STATES before,D3D12_RESOURCE_STATES after) {
        D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition={resource,D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,before,after};list_->ResourceBarrier(1,&b);
    }
    UINT width_,height_;DXGI_FORMAT format_;FILE* log_;
    ComPtr<IDXGIAdapter1> adapter_;
    ComPtr<ID3D12Device> device12_;ComPtr<ID3D12CommandQueue> queue_;ComPtr<ID3D12CommandAllocator> allocator_;ComPtr<ID3D12GraphicsCommandList> list_;
    ComPtr<ID3D11Device> device11_;ComPtr<ID3D11DeviceContext> context11_;ComPtr<ID3D11Device5> device5_;ComPtr<ID3D11DeviceContext4> context4_;
    ComPtr<ID3D11Texture2D> input8_,output8_,color11_,output11_;ComPtr<ID3D12Resource> color12_,output12_;
    ComPtr<ID3D11ShaderResourceView> inputView_,outputView_;ComPtr<ID3D11RenderTargetView> colorTarget_,outputTarget_,output8Target_;
    ComPtr<ID3D11VertexShader> vs_;ComPtr<ID3D11PixelShader> ps_;
    ComPtr<ID3D12Fence> inputFence12_,outputFence12_;ComPtr<ID3D11Fence> inputFence11_,outputFence11_,completion_;
    HANDLE event_{};UINT64 sequence_{};
    HMODULE coreModule_{},runtimeModule_{},shimModule_{};
    ngx::CoreInit coreInit_{};ngx::CoreInitProject coreInitProject_{};ngx::Allocate allocate_{};ngx::CoreShutdown coreShutdown_{};
    void *init_{},*create_{},*evaluate_{},*release_{};
    ngx::ShimInit shimInit_{};ngx::ShimCreate shimCreate_{};ngx::ShimEvaluate shimEvaluate_{};ngx::ShimRelease shimRelease_{};
    ngx::Parameter* params_{};ngx::Handle* feature_{};
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
            neural.Process(input.Get(),output.Get());
            if(outputLock->ReleaseSync(1)!=S_OK || inputLock->ReleaseSync(0)!=S_OK)return fail("ownership release",E_FAIL);
            previous=sequence;shared.completed=sequence;SetEvent(done);
        }
    }
    catch(const std::exception& error) {return fail(error.what(),E_FAIL);}
    InterlockedExchange(&shared.status,static_cast<LONG>(Status::Stopped));
    return 0;
}
