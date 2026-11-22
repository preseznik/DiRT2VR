#include "live_neural.h"
#include "photo_neural.h"
#include "photo_mode.h"
#include "hud_capture.h"
#include "overlay_blit.h"
#include "photo_overlay.h"
#include "seat_adjustment.h"
#include <xinput.h>
#include <atomic>
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

// DLSS 5 driving panel: settings while driving, without leaving the race.
bool liveEnabled=true;          // Off from the panel shows the game's own picture.
std::atomic<bool> panelOpen{},escapeLatched{};
bool toggleHeld{},escapeHeld{};
int panelSelected{};
ULONGLONG lastApplied{},enabledSince{};
struct Repeat {
    bool held{};ULONGLONG next{};
    bool Step(bool down,ULONGLONG now) {
        if(!down){held=false;return false;}
        if(!held){held=true;next=now+350;return true;}
        if(now>=next){next=now+70;return true;}
        return false;
    }
} upKey,downKey,leftKey,rightKey;
unsigned PanelKey() {
    static const unsigned key=[] {
        wchar_t value[16]{};
        const unsigned chosen=GetEnvironmentVariableW(L"DIRT2VR_NEURAL_PANEL_KEY",value,16)?unsigned(_wtoi(value)):VK_F8;
        return chosen>0 && chosen<256?chosen:unsigned(VK_F8);
    }();
    return key;
}
std::wstring PanelKeyName() {
    wchar_t name[32]{};
    return GetKeyNameTextW(LONG(MapVirtualKeyW(PanelKey(),MAPVK_VK_TO_VSC)<<16),name,32)>0?std::wstring(name):std::wstring(L"F8");
}
// This module's own reads are not filtered (the hooks only filter the game).
bool Down(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
WORD PadButtons() {
    using Fn=DWORD(WINAPI*)(DWORD,XINPUT_STATE*);
    static const Fn get=[] {
        const auto module=LoadLibraryExW(L"xinput1_4.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
        return module?reinterpret_cast<Fn>(GetProcAddress(module,"XInputGetState")):nullptr;
    }();
    WORD buttons{};
    if(get)for(DWORD i=0;i<4;++i){XINPUT_STATE state{};if(get(i,&state)==ERROR_SUCCESS)buttons|=state.Gamepad.wButtons;}
    return buttons;
}
bool Focused() {
    DWORD process{};GetWindowThreadProcessId(GetForegroundWindow(),&process);
    return process==GetCurrentProcessId();
}
// Keys and the D-pad for the panel; returns what to draw. Caller holds the mutex.
PhotoPanelState PanelFrame() {
    const auto now=GetTickCount64();
    const bool focused=Focused();
    const bool toggle=focused && Down(int(PanelKey()));
    const bool escape=focused && Down(VK_ESCAPE);
    if(toggle && !toggleHeld) {
        panelOpen=!panelOpen;
        upKey={};downKey={};leftKey={};rightKey={};
        Log("live neural rendering: panel open=%d",panelOpen.load());
    }
    if(panelOpen && escape && !escapeHeld){panelOpen=false;escapeLatched=true;}
    if(!escape)escapeLatched=false;
    toggleHeld=toggle;escapeHeld=escape;
    PhotoPanelState state;
    if(!panelOpen)return state;
    const wchar_t* status=!liveEnabled?L"Off":now-lastApplied<1500?L"On":L"Starting\u2026";
    struct Entry { bool toggle; NeuralOption option; };
    std::vector<Entry> entries{{true,{}}};
    state.rows.push_back({L"",L"Neural rendering",status,L""});
    for(auto& [option,row]:NeuralOptionRows(liveEnabled)){entries.push_back({false,option});state.rows.push_back(std::move(row));}
    const int count=int(state.rows.size());
    panelSelected=std::clamp(panelSelected,0,count-1);
    const WORD pad=focused?PadButtons():0;
    if(upKey.Step(focused && (Down(VK_UP) || (pad&XINPUT_GAMEPAD_DPAD_UP)),now))panelSelected=(panelSelected+count-1)%count;
    if(downKey.Step(focused && (Down(VK_DOWN) || (pad&XINPUT_GAMEPAD_DPAD_DOWN)),now))panelSelected=(panelSelected+1)%count;
    const int direction=rightKey.Step(focused && (Down(VK_RIGHT) || (pad&XINPUT_GAMEPAD_DPAD_RIGHT)),now)?1:
        leftKey.Step(focused && (Down(VK_LEFT) || (pad&XINPUT_GAMEPAD_DPAD_LEFT)),now)?-1:0;
    if(direction) {
        const auto& entry=entries[panelSelected];
        if(entry.toggle) {
            liveEnabled=!liveEnabled;enabledSince=now;
            Log("live neural rendering: enabled=%d",liveEnabled);
        } else if(liveEnabled) NeuralOptionAdjust(entry.option,direction);
        // Rebuild the rows with the new values.
        state.rows.clear();
        state.rows.push_back({L"",L"Neural rendering",!liveEnabled?L"Off":now-lastApplied<1500?L"On":L"Starting\u2026",L""});
        for(auto& [option,row]:NeuralOptionRows(liveEnabled))state.rows.push_back(std::move(row));
    }
    state.rows[0].section=L"WHILE DRIVING";
    state.panel=true;state.selected=panelSelected;state.title=L"DLSS 5";
    state.hints=L"\u2191\u2193 / D-pad  Select     \u2190\u2192  Change     "+PanelKeyName()+L" / Esc  Close";
    return state;
}
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
    if(presenting || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    const bool hide=PhotoModeHidesHud();
    if(!hide && !LiveNeuralRequested())return false;
    ComPtr<ID3D11VertexShader> shader;context->VSGetShader(&shader,nullptr,nullptr);
    if(!shader)return false;
    std::lock_guard lock(mutex);
    // Photo Mode in a replay: skip the replay HUD entirely (it is not part of the picture).
    if(hide)return hudShaders.contains(shader.Get());
    // Only HUD drawn straight onto the frame is redirected (HudCapture checks the target).
    return capturing && hudShaders.contains(shader.Get()) && hud.Draw(context,draw,true);
}
bool LiveNeuralBlocksKey(unsigned key) {
    if(!LiveNeuralRequested())return false;
    if(key==PanelKey())return true;
    if(key==VK_ESCAPE && escapeLatched)return true; // The Escape that closed the panel.
    return panelOpen && (key==VK_UP || key==VK_DOWN || key==VK_LEFT || key==VK_RIGHT || key==VK_ESCAPE);
}
void LiveNeuralFilterPad(unsigned short& buttons) {
    if(LiveNeuralRequested() && panelOpen)
        buttons&=static_cast<unsigned short>(~(XINPUT_GAMEPAD_DPAD_UP|XINPUT_GAMEPAD_DPAD_DOWN|XINPUT_GAMEPAD_DPAD_LEFT|XINPUT_GAMEPAD_DPAD_RIGHT));
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
    static const bool input=[]{const bool ready=EnableSeatInput();Log("live neural rendering: panel input hooks=%d key=%u",ready,PanelKey());return ready;}();
    (void)input;
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
        if(liveEnabled && PhotoNeuralProcessLive(frame.Get())){++applied;lastApplied=GetTickCount64();}
        if(overlay && Composite(frame.Get(),overlay))++hudFrames;
        // The driving panel goes on top of everything, after the HUD.
        PhotoPanelDraw(swapchain,PanelFrame());
    } else {
        panelOpen=false;
        if(overlay) Composite(frame.Get(),overlay);
    }
    ++frames;
    if(frames%600==0)Log("live neural rendering: frames=%llu applied=%llu hud_composited=%llu",frames,applied,hudFrames);
    // Capture the next frame's HUD (HudCapture clears its texture here).
    capturing=hud.Begin(frame.Get(),frames,0,false);
}
}
