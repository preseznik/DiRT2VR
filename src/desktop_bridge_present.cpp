#include "desktop_bridge.h"
#include "common.h"

namespace vr {
void DesktopBridgePresent(IDXGISwapChain* swapchain,uint64_t frameIndex) {
    static const bool requested=[] {
        wchar_t mode[32]{},headset[8]{};
        if(GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_BRIDGE",mode,32)>=32) return false;
        if(GetEnvironmentVariableW(L"DIRT2VR_HEADSET",headset,8)>=8) return false;
        if(mode[0] && !DesktopBridgeRequested(mode,headset,LoggingEnabled()))
            Log("desktop bridge unavailable: requires logged desktop transport mode; DLSS modes are not implemented");
        return DesktopBridgeRequested(mode,headset,LoggingEnabled());
    }();
    if(!requested) return;
    static DesktopBridge bridge([] {
        wchar_t helper[32768]{};
        const auto length=GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_BRIDGE_HELPER",helper,32768);
        return length && length<32768 ? std::wstring(helper) : std::wstring{};
    }(),[](const char* message) { Log("desktop bridge: %s",message); },[] {
        wchar_t slow[8]{};const auto length=GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_BRIDGE_SLOW_PROBE",slow,8);
        return length==1 && slow[0]==L'1'?32u:8u;
    }());
    if(bridge.Disabled()) return;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> frame;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&frame)))) return;
    LARGE_INTEGER start{},end{},frequency{};
    QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    const bool copied=bridge.Transfer(frame.Get());
    QueryPerformanceCounter(&end);
    static auto csv=TraceFile(Output()/"desktop-bridge.csv");
    static bool header=false;
    if(!header) { csv << "completed,applied,cpu_ms,disabled,dropped,width,height,requested,frame\n"; header=true; }
    D3D11_TEXTURE2D_DESC desc{}; frame->GetDesc(&desc);
    csv << bridge.Completed() << ',' << copied << ',' << 1000.0*(end.QuadPart-start.QuadPart)/frequency.QuadPart
        << ',' << bridge.Disabled() << ',' << bridge.Dropped() << ',' << desc.Width << ',' << desc.Height << ',' << bridge.Requested() << ',' << frameIndex << '\n';
    if(bridge.Completed()%120==0 || bridge.Disabled()) csv.flush();
}
}
