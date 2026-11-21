#include "live_neural.h"
#include "photo_neural.h"
#include "photo_mode.h"
#include "common.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <algorithm>
#include <vector>

namespace vr {
namespace {
uint64_t frames{},applied{};
}

bool LiveNeuralRequested() {
    static const bool requested=[] {
        wchar_t value[8]{};
        const bool wanted=GetEnvironmentVariableW(L"DIRT2VR_NEURAL_LIVE",value,8)==1 && value[0]==L'1';
        if(wanted)Log("live neural rendering: requested configured=%d",PhotoNeuralConfigured());
        return wanted && PhotoNeuralConfigured();
    }();
    return requested;
}

// Processing earlier in the frame (before the HUD, or after the game's final pass)
// fed the result back into later passes and washed the picture out in the lab
// (artifacts/photo-lab/evidence/live-05, live-06), so the whole frame is processed here.
void LiveNeuralPresent(IDXGISwapChain* swapchain) {
    if(LoggingEnabled()) {
        // Frame pacing for comparisons with and without live neural rendering.
        static std::vector<double> intervals;static LARGE_INTEGER last{},frequency{};
        LARGE_INTEGER now{};QueryPerformanceCounter(&now);
        if(!frequency.QuadPart)QueryPerformanceFrequency(&frequency);
        if(last.QuadPart)intervals.push_back(1000.0*double(now.QuadPart-last.QuadPart)/double(frequency.QuadPart));
        last=now;
        if(intervals.size()==600) {
            auto sorted=intervals;std::sort(sorted.begin(),sorted.end());
            Log("desktop frame pacing: live_neural=%d p50_ms=%.2f p95_ms=%.2f p99_ms=%.2f max_ms=%.2f",
                LiveNeuralRequested(),sorted[300],sorted[570],sorted[594],sorted[599]);
            intervals.clear();
        }
    }
    if(!LiveNeuralRequested() || !swapchain || PhotoModeActive())return;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> frame;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&frame))))return;
    if(PhotoNeuralProcessLive(frame.Get()))++applied;
    if(++frames%600==0)Log("live neural rendering: frames=%llu applied=%llu",frames,applied);
}
}
