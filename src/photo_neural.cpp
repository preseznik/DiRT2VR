#include "photo_neural.h"
#include "desktop_bridge.h"
#include "common.h"
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <algorithm>
#include <cmath>
#include <cwchar>

namespace vr {
namespace {
std::mutex mutex;
std::unique_ptr<DesktopBridge> helper;
int EnvironmentNumber(const wchar_t* name,int fallback) {
    wchar_t value[16]{};
    const auto length=GetEnvironmentVariableW(name,value,16);
    return length && length<16 ? _wtoi(value) : fallback;
}
// Launcher defaults; percentages for the float settings.
bridge::NeuralSettings settings=bridge::Clamped({
    EnvironmentNumber(L"DIRT2VR_NEURAL_STYLE",1),EnvironmentNumber(L"DIRT2VR_NEURAL_PASSES",1),
    EnvironmentNumber(L"DIRT2VR_NEURAL_STRENGTH",100)/100.f,EnvironmentNumber(L"DIRT2VR_NEURAL_TONE",100)/100.f,
    EnvironmentNumber(L"DIRT2VR_NEURAL_DETAIL",100)/100.f,EnvironmentNumber(L"DIRT2VR_NEURAL_MIX",100)/100.f,
    EnvironmentNumber(L"DIRT2VR_NEURAL_RESOLUTION",100),EnvironmentNumber(L"DIRT2VR_NEURAL_FOVEATION",0)});
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
    helper->Neural(settings);
    return *helper;
}
}

bool PhotoNeuralConfigured(){return Configured().valid;}
std::vector<std::pair<NeuralOption,PhotoMenuRow>> NeuralOptionRows(bool enabled) {
    const auto s=PhotoNeuralSettings();
    const auto percent=[](float value){wchar_t text[16]{};swprintf_s(text,L"%.0f%%",value*100);return std::wstring(text);};
    const auto whole=[](int value,const wchar_t* suffix){return std::to_wstring(value)+suffix;};
    const bool foveated=s.foveation>0 && s.resolution<100;
    std::vector<std::pair<NeuralOption,PhotoMenuRow>> rows;
    const auto add=[&](NeuralOption option,const wchar_t* label,std::wstring value,bool dim=false) {
        rows.push_back({option,PhotoMenuRow{L"",label,std::move(value),L"",!enabled || dim,false}});
    };
    add(NeuralOption::Style,L"Style",s.style==2?L"Cinematic":s.style==0?L"Standard":L"Natural");
    add(NeuralOption::Strength,L"Strength",percent(s.intensity));
    add(NeuralOption::Tone,L"Local tone",percent(s.tone));
    add(NeuralOption::Detail,L"Local detail",percent(s.structure));
    add(NeuralOption::Passes,L"Passes",std::to_wstring(s.passes));
    add(NeuralOption::Mix,L"Mix with original",percent(s.mix));
    add(NeuralOption::Resolution,foveated?L"Edge resolution":L"Resolution",whole(s.resolution,L"%"));
    // A foveated centre only matters when the rest of the picture is reduced.
    add(NeuralOption::Foveation,L"Foveated centre",s.foveation?whole(s.foveation,L"%"):std::wstring(L"Off"),s.resolution>=100);
    return rows;
}
void NeuralOptionAdjust(NeuralOption option,int direction) {
    auto s=PhotoNeuralSettings();
    const auto step=[&](float& value,float low,float high){value=std::clamp(std::round((value+.1f*direction)*10)/10,low,high);};
    switch(option) {
    case NeuralOption::Style: {
        constexpr int order[]{1,0,2}; // Natural, Standard, Cinematic
        int at=0;for(int i=0;i<3;++i)if(order[i]==s.style)at=i;
        s.style=order[(at+direction+3)%3];break;
    }
    case NeuralOption::Strength:step(s.intensity,0,2);break;
    case NeuralOption::Tone:step(s.tone,0,2);break;
    case NeuralOption::Detail:step(s.structure,0,2);break;
    case NeuralOption::Passes:s.passes=std::clamp(s.passes+direction,1,3);break;
    case NeuralOption::Mix:step(s.mix,0,1);break;
    case NeuralOption::Resolution:s.resolution=std::clamp(s.resolution+10*direction,50,100);break;
    case NeuralOption::Foveation:
        s.foveation=s.foveation==0?(direction>0?30:0):s.foveation==30 && direction<0?0:std::clamp(s.foveation+10*direction,30,80);break;
    }
    PhotoNeuralSetSettings(s);
}
bridge::NeuralSettings PhotoNeuralSettings() {
    std::lock_guard lock(mutex);return settings;
}
void PhotoNeuralSetSettings(const bridge::NeuralSettings& value) {
    std::lock_guard lock(mutex);
    settings=bridge::Clamped(value);
    Log("neural rendering: style=%d passes=%d strength=%.2f tone=%.2f detail=%.2f mix=%.2f resolution=%d foveation=%d",
        settings.style,settings.passes,settings.intensity,settings.tone,settings.structure,settings.mix,settings.resolution,settings.foveation);
}

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
