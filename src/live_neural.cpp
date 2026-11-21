#include "live_neural.h"
#include "photo_neural.h"
#include "photo_mode.h"
#include "hud_capture.h"
#include "overlay_blit.h"
#include "common.h"
#include <d3d11_1.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <mutex>
#include <unordered_set>
#include <vector>

namespace vr {
namespace {
using Microsoft::WRL::ComPtr;
std::mutex mutex;
std::unordered_set<void*> hudShaders;
HudCapture hud;
bool capturing{};
// Set while Present does its own work: the compositor's draws pass through the
// same draw hooks, which must not take the lock again (that aborted the game).
thread_local bool presenting{};
uint64_t frames{},applied{},hudFrames{};

// EGO UI image, ramp, distance-field font and lit dial shaders (as in the VR HUD
// capture), plus the scaled UI image and RV badge variants used in menus.
bool HudShader(uint64_t hash) {
    switch(hash) {
    case 0xbef999561247f2d5ull: case 0x9d4dd795ddf7c656ull: case 0x47b5203eae84ab0full:
    case 0xedfda3c33aa2b9fcull: case 0xce4f6cb0ec7d5697ull:
    case 0x2444a05c829f7ffbull: case 0x692f35feedc24131ull: return true;
    default: return false;
    }
}

// Puts the captured (premultiplied) HUD back over the processed frame.
OverlayBlit hudBlit;
bool Composite(ID3D11Texture2D* frame,ID3D11Texture2D* overlay) {
    ComPtr<ID3D11Device> device;frame->GetDevice(&device);
    ComPtr<ID3D11RenderTargetView> target;
    if(FAILED(device->CreateRenderTargetView(frame,nullptr,&target)))return false;
    D3D11_TEXTURE2D_DESC desc{};frame->GetDesc(&desc);
    return hudBlit.Draw(target.Get(),overlay,0,0,float(desc.Width),float(desc.Height));
}
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
void LiveNeuralVertexShader(void* shader,uint64_t hash) {
    if(!shader || !HudShader(hash))return;
    std::lock_guard lock(mutex);hudShaders.insert(shader);
}
bool LiveNeuralHudDraw(ID3D11DeviceContext* context,const std::function<void()>& draw) {
    if(presenting || !LiveNeuralRequested() || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    ComPtr<ID3D11VertexShader> shader;context->VSGetShader(&shader,nullptr,nullptr);
    if(!shader)return false;
    std::lock_guard lock(mutex);
    // Only HUD drawn straight onto the frame is redirected (HudCapture checks the target).
    return capturing && hudShaders.contains(shader.Get()) && hud.Draw(context,draw,true);
}
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
    if(!LiveNeuralRequested() || !swapchain)return;
    ComPtr<ID3D11Texture2D> frame;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&frame))))return;
    std::lock_guard lock(mutex);
    presenting=true;
    struct Done { ~Done(){presenting=false;} } done;
    ID3D11Texture2D* overlay{};
    if(capturing) {
        hud.End(true);capturing=false;
        overlay=hud.Current(frames);
    }
    // Photo Mode has its own path; its HUD is hidden anyway.
    if(!PhotoModeActive()) {
        if(PhotoNeuralProcessLive(frame.Get()))++applied;
        if(overlay && Composite(frame.Get(),overlay))++hudFrames;
    } else if(overlay) Composite(frame.Get(),overlay);
    ++frames;
    if(frames%600==0)Log("live neural rendering: frames=%llu applied=%llu hud_composited=%llu",frames,applied,hudFrames);
    // Capture the next frame's HUD (HudCapture clears its texture here).
    capturing=hud.Begin(frame.Get(),frames,0,false);
}
}
