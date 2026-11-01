#include "paddock_zoom.h"
#include "common.h"
#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>

namespace vr {
namespace {
IDirectInput8W* input{};
IDirectInputDevice8W* mouse{};
HWND mouseWindow{};
PaddockZoom zoom;
ULONGLONG last{};
bool reported{};
// Wheel movement since the previous frame while the game window is in front.
float Wheel() {
    const auto window=GetForegroundWindow();DWORD pid{};GetWindowThreadProcessId(window,&pid);
    if(pid!=GetCurrentProcessId())return 0;
    if(mouseWindow!=window) {
        if(mouse) {mouse->Unacquire();mouse->Release();mouse=nullptr;}
        mouseWindow=window;
        if(!input)DirectInput8Create(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8W,reinterpret_cast<void**>(&input),nullptr);
        if(input && SUCCEEDED(input->CreateDevice(GUID_SysMouse,&mouse,nullptr)) &&
           (FAILED(mouse->SetDataFormat(&c_dfDIMouse2)) || FAILED(mouse->SetCooperativeLevel(window,DISCL_FOREGROUND|DISCL_NONEXCLUSIVE)))) {
            mouse->Release();mouse=nullptr;
        }
    }
    if(!mouse)return 0;
    DIMOUSESTATE2 state{};
    if(FAILED(mouse->GetDeviceState(sizeof(state),&state))) {mouse->Acquire();return 0;}
    return float(state.lZ);
}
}
bool PaddockZoomEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_PADDOCK_ZOOM",value,8)==1 && value[0]==L'1';}();
    return enabled;
}
float PaddockZoomDistance(bool paddock,const float* camera) {
    const auto now=GetTickCount64();
    if(!paddock || !camera || !PaddockZoomEnabled()) {zoom.Reset();last=0;return 0;}
    // Leaving the paddock, pausing or a long hitch starts again from the game's view.
    if(!last || now-last>500)zoom.Reset();
    const float dt=last?float(now-last)/1000.f:0;last=now;
    zoom.Observe(camera);
    const float wheel=Wheel();
    if(dt>0)zoom.Step(std::min(dt,.25f),wheel);
    if(!reported && zoom.target>0) {reported=true;Log("paddock zoom used distance=%.2f m",zoom.target);}
    return zoom.distance;
}
}
