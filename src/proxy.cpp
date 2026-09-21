#include "common.h"
#include "trace.h"
#include <intrin.h>

static bool SessionActive() {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_ACTIVE",value,8)==1 && value[0]==L'1';
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
    HRESULT hr = fn(adapter, type, software, flags, levels, count, sdk, desc, swapchain, device, level, context);
    if (SUCCEEDED(hr) && device && *device && SessionActive() && vr::SupportedHost())
        vr::AttachTrace(*device, context ? *context : nullptr, swapchain ? *swapchain : nullptr);
    return hr;
}
#include "forwarders.inc"
