#include "photo_neural.h"
#include "desktop_bridge.h"
#include "common.h"
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>

namespace vr {
namespace {
std::mutex mutex;
std::unique_ptr<DesktopBridge> helper;
// Paused scenes tolerate slow frames; live frames wait at most this long for the
// previous result before showing the original frame.
constexpr DWORD WaitMilliseconds=250;

std::wstring Environment(const wchar_t* name) {
    wchar_t value[32768]{};
    const auto length=GetEnvironmentVariableW(name,value,32768);
    return length && length<32768 ? std::wstring(value) : std::wstring{};
}
bool IsFile(const std::filesystem::path& path) {
    const auto a=GetFileAttributesW(path.c_str());
    return a!=INVALID_FILE_ATTRIBUTES && !(a&FILE_ATTRIBUTE_DIRECTORY);
}
struct Paths { std::wstring runtime,helper; bool valid{}; };
const Paths& Configured() {
    static const Paths paths=[] {
        Paths p{Environment(L"DIRT2VR_NEURAL_RUNTIME"),Environment(L"DIRT2VR_NEURAL_HELPER")};
        p.valid=!p.runtime.empty() && !p.helper.empty() &&
            std::filesystem::path(p.runtime).is_absolute() && std::filesystem::path(p.helper).is_absolute() &&
            IsFile(std::filesystem::path(p.runtime)/L"nvngx_dlssnr.dll") && IsFile(p.helper);
        if(!p.runtime.empty() || !p.helper.empty())
            Log("neural rendering: configured=%d runtime=%ls helper=%ls",p.valid,p.runtime.c_str(),p.helper.c_str());
        return p;
    }();
    return paths;
}
// One helper process serves Photo Mode and live driving. Caller holds the mutex.
DesktopBridge& Helper(const Paths& paths) {
    if(!helper) {
        std::wstring arguments=L"\""+paths.runtime+L"\"";
        if(LoggingEnabled())arguments+=L" \""+(Output()/L"neural-helper.log").wstring()+L"\"";
        helper=std::make_unique<DesktopBridge>(paths.helper,[](const char* message){Log("neural rendering: %s",message);},
            WaitMilliseconds,L"--neural",arguments);
    }
    return *helper;
}
}

bool PhotoNeuralConfigured(){return Configured().valid;}

PhotoNeuralState PhotoNeuralApply(IDXGISwapChain* swapchain,bool wanted) {
    if(!wanted || !swapchain)return PhotoNeuralState::Off;
    const auto& paths=Configured();
    if(!paths.valid)return PhotoNeuralState::Failed;
    std::lock_guard lock(mutex);
    auto& bridge=Helper(paths);
    if(bridge.Disabled())return PhotoNeuralState::Failed;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> frame;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&frame))))return PhotoNeuralState::Failed;
    if(bridge.Transfer(frame.Get()))return PhotoNeuralState::On;
    if(bridge.Disabled())return PhotoNeuralState::Failed;
    return bridge.Completed()?PhotoNeuralState::On:PhotoNeuralState::Starting;
}

bool PhotoNeuralProcessLive(ID3D11Texture2D* frame) {
    const auto& paths=Configured();
    if(!paths.valid || !frame)return false;
    std::lock_guard lock(mutex);
    auto& bridge=Helper(paths);
    return !bridge.Disabled() && bridge.TransferPipelined(frame);
}
}
