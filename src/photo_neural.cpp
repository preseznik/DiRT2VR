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
// Paused scenes tolerate slow frames; still processing is not paced to the display.
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
            Log("photo neural rendering: configured=%d runtime=%ls helper=%ls",p.valid,p.runtime.c_str(),p.helper.c_str());
        return p;
    }();
    return paths;
}
}

bool PhotoNeuralConfigured(){return Configured().valid;}

PhotoNeuralState PhotoNeuralApply(IDXGISwapChain* swapchain,bool wanted) {
    if(!wanted || !swapchain)return PhotoNeuralState::Off;
    const auto& paths=Configured();
    if(!paths.valid)return PhotoNeuralState::Failed;
    std::lock_guard lock(mutex);
    if(!helper) {
        std::wstring arguments=L"\""+paths.runtime+L"\"";
        if(LoggingEnabled())arguments+=L" \""+(Output()/L"neural-helper.log").wstring()+L"\"";
        helper=std::make_unique<DesktopBridge>(paths.helper,[](const char* message){Log("photo neural rendering: %s",message);},
            WaitMilliseconds,L"--neural",arguments);
    }
    if(helper->Disabled())return PhotoNeuralState::Failed;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> frame;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&frame))))return PhotoNeuralState::Failed;
    if(helper->Transfer(frame.Get()))return PhotoNeuralState::On;
    if(helper->Disabled())return PhotoNeuralState::Failed;
    return helper->Completed()?PhotoNeuralState::On:PhotoNeuralState::Starting;
}
}
