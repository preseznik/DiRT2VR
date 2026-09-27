#include "common.h"
#include "trace.h"
#include <intrin.h>
#include "scene_resolution.h"

static bool SessionActive() {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_ACTIVE",value,8)==1 && value[0]==L'1';
}

static void SetVrSceneResolution(const DXGI_SWAP_CHAIN_DESC* desc, void* caller) {
    wchar_t headset[8]{}, dimensions[64]{};
    unsigned width{},height{};
    if(!desc || !SessionActive() ||
       GetEnvironmentVariableW(L"DIRT2VR_HEADSET",headset,8)!=1 || headset[0]!=L'1' ||
       !GetEnvironmentVariableW(L"DIRT2VR_SCENE_SIZE",dimensions,64) ||
       swscanf_s(dimensions,L"%ux%u",&width,&height)!=2 || !vr::SupportedHost()) return;
    const auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if(reinterpret_cast<uintptr_t>(caller)!=base+0xd29b55) {
        vr::Log("VR scene size rejected: unexpected renderer call site"); return;
    }
    auto renderer=reinterpret_cast<vr::SceneRenderer*>(reinterpret_cast<uintptr_t>(desc)-0xcc);
    MEMORY_BASIC_INFORMATION memory{};
    if(!VirtualQuery(renderer,&memory,sizeof(memory)) || memory.State!=MEM_COMMIT ||
       (memory.Protect & (PAGE_GUARD|PAGE_NOACCESS)) ||
       !(memory.Protect & (PAGE_READWRITE|PAGE_EXECUTE_READWRITE|PAGE_WRITECOPY|PAGE_EXECUTE_WRITECOPY)) ||
       reinterpret_cast<uintptr_t>(renderer)+sizeof(*renderer)>
           reinterpret_cast<uintptr_t>(memory.BaseAddress)+memory.RegionSize) return;
    const unsigned oldWidth=desc->BufferDesc.Width,oldHeight=desc->BufferDesc.Height;
    const bool applied=vr::SetSceneResolution(*renderer,width,height);
    vr::Log("VR scene size engine=%ux%u requested=%ux%u applied=%d",oldWidth,oldHeight,width,height,applied);
}

static FARPROC RealProc(const char* name) {
    static HMODULE real = [] {
        wchar_t system[MAX_PATH]{};
        GetSystemDirectoryW(system, MAX_PATH);
        return LoadLibraryExW((std::filesystem::path(system)/L"d3d11.dll").c_str(),
                              nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    }();
    FARPROC proc = real ? GetProcAddress(real, name) : nullptr;
    if (!proc) __fastfail(FAST_FAIL_FATAL_APP_EXIT);
    return proc;
}

extern "C" HRESULT WINAPI ProxyCreateDevice(IDXGIAdapter* adapter, D3D_DRIVER_TYPE type,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk,
    ID3D11Device** device, D3D_FEATURE_LEVEL* level, ID3D11DeviceContext** context) {
    const auto fn = reinterpret_cast<decltype(&D3D11CreateDevice)>(RealProc("D3D11CreateDevice"));
    HRESULT hr = fn(adapter, type, software, flags, levels, count, sdk, device, level, context);
    if (SUCCEEDED(hr) && device && *device && SessionActive() && vr::SupportedHost())
        vr::AttachTrace(*device, context ? *context : nullptr, nullptr);
    return hr;
}

extern "C" HRESULT WINAPI ProxyCreateDeviceAndSwapChain(IDXGIAdapter* adapter, D3D_DRIVER_TYPE type,
    HMODULE software, UINT flags, const D3D_FEATURE_LEVEL* levels, UINT count, UINT sdk,
    const DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** swapchain, ID3D11Device** device,
    D3D_FEATURE_LEVEL* level, ID3D11DeviceContext** context) {
    const auto fn = reinterpret_cast<decltype(&D3D11CreateDeviceAndSwapChain)>(RealProc("D3D11CreateDeviceAndSwapChain"));
    SetVrSceneResolution(desc,_ReturnAddress());
    HRESULT hr = fn(adapter, type, software, flags, levels, count, sdk, desc, swapchain, device, level, context);
    if (SUCCEEDED(hr) && device && *device && SessionActive() && vr::SupportedHost())
        vr::AttachTrace(*device, context ? *context : nullptr, swapchain ? *swapchain : nullptr);
    return hr;
}
#include "forwarders.inc"
