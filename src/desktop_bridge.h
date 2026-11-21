#pragma once
#include "desktop_bridge_protocol.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <string>

namespace vr {
// Transport only. No upscaling, neural rendering or synthetic engine inputs.
class DesktopBridge {
public:
    using Report=void(*)(const char*);
    // mode: "--transport" or "--neural"; arguments follow the session name on the helper's command line.
    explicit DesktopBridge(std::wstring helper,Report report=nullptr,DWORD waitMilliseconds=8,
                           std::wstring mode=L"--transport",std::wstring arguments={});
    ~DesktopBridge();
    DesktopBridge(const DesktopBridge&)=delete;
    DesktopBridge& operator=(const DesktopBridge&)=delete;
    bool Transfer(ID3D11Texture2D* frame);
    bool Disabled() const { return disabled_; }
    uint64_t Completed() const { return completed_; }
    uint64_t Dropped() const { return dropped_; }
    uint64_t Requested() const { return sequence_; }
    DWORD HelperPid() const { return processId_; }
private:
    bool Start(ID3D11Texture2D*,const D3D11_TEXTURE2D_DESC&);
    bool Fail(const char* message);
    void Stop();
    void Note(const char* message) const;
    // Neural Rendering loads a model at startup and can be slow on its first frame.
    ULONGLONG StartupLimit() const { return mode_==L"--neural" ? 30000 : 5000; }
    ULONGLONG StallLimit() const { return mode_==L"--neural" ? 10000 : 1000; }
    std::wstring helper_,mode_,arguments_;
    Report report_{};
    DWORD waitMilliseconds_{8};
    bool disabled_{};
    bool pending_{},pendingReady_{};
    HANDLE mapping_{},request_{},done_{},stop_{},process_{},job_{};
    DWORD processId_{};
    bridge::Shared* shared_{};
    ULONGLONG started_{},pendingSince_{};
    uint64_t sequence_{},completed_{},dropped_{};
    D3D11_TEXTURE2D_DESC desc_{};
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> input_,output_;
    Microsoft::WRL::ComPtr<IDXGIKeyedMutex> inputLock_,outputLock_;
};
bool DesktopBridgeRequested(const wchar_t* mode,const wchar_t* headset,bool logging);
void DesktopBridgePresent(IDXGISwapChain* swapchain,uint64_t frame);
}
