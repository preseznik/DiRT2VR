#include "paddock_zoom.h"
#include "common.h"
#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>
#include <Xinput.h>

namespace vr {
namespace {
using XboxFn=DWORD (WINAPI*)(DWORD,XINPUT_STATE*);
IDirectInput8W* input{};
IDirectInputDevice8W* mouse{};
HWND mouseWindow{};
XboxFn xbox{};
PaddockZoom zoom;
ULONGLONG last{};
bool reported{};
bool GameInFront() {
    const auto window=GetForegroundWindow();DWORD pid{};GetWindowThreadProcessId(window,&pid);
    return pid==GetCurrentProcessId();
}
// The strongest right-stick push across connected Xbox controllers.
float Stick(float dt) {
    if(!xbox) {
        static bool tried{};
        if(tried)return 0;
        tried=true;
        if(const auto module=LoadLibraryExW(L"xinput1_4.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32))
            xbox=reinterpret_cast<XboxFn>(GetProcAddress(module,"XInputGetState"));
        if(!xbox)return 0;
    }
    float strongest=0;
    for(DWORD slot=0;slot<4;++slot) {
        XINPUT_STATE state{};
        if(xbox(slot,&state)!=ERROR_SUCCESS)continue;
        const float value=PaddockStickWheel(state.Gamepad.sThumbRY,dt);
        if(std::abs(value)>std::abs(strongest))strongest=value;
    }
    return strongest;
}
// Wheel movement since the previous frame while the game window is in front.
float Wheel() {
    const auto window=GetForegroundWindow();
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
    const float step=std::min(dt,.25f);
    const float wheel=GameInFront()?Wheel()+Stick(step):0;
    if(dt>0)zoom.Step(step,wheel);
    if(!reported && zoom.target>0) {reported=true;Log("paddock zoom used distance=%.2f m",zoom.target);}
    return zoom.distance;
}
}
