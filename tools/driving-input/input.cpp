// Launcher-only, non-exclusive capture. The game continues to read its own inputs.
#define DIRECTINPUT_VERSION 0x0800
#include <windows.h>
#include <dinput.h>
#include <xinput.h>
#include <algorithm>
#include <array>
#include <memory>
#include <string>
#include <vector>

struct Sample {
    unsigned connected{}, axes{};
    float values[8]{};
    unsigned char buttons[128]{};
    unsigned pov[4]{};
};
struct Device {
    IDirectInputDevice8W* input{};
    std::wstring id, name;
    unsigned axes{};
    int xbox{-1};
    ~Device() { if(input) { input->Unacquire(); input->Release(); } }
};
struct Capture {
    IDirectInput8W* input{};
    HWND window{};
    std::vector<std::unique_ptr<Device>> devices;
    ~Capture() { devices.clear(); if(input) input->Release(); }
};
BOOL CALLBACK Axis(const DIDEVICEOBJECTINSTANCEW* object, void* user) {
    auto& device=*static_cast<Device*>(user);
    if((object->dwType & DIDFT_AXIS)==0 || object->dwOfs>DIJOFS_SLIDER(1)) return DIENUM_CONTINUE;
    DIPROPRANGE range{};
    range.diph={sizeof(range),sizeof(range.diph),object->dwType,DIPH_BYID};
    range.lMin=0; range.lMax=65535;
    if(SUCCEEDED(device.input->SetProperty(DIPROP_RANGE,&range.diph))) device.axes|=1u<<(object->dwOfs/4);
    return DIENUM_CONTINUE;
}
BOOL CALLBACK Enumerate(const DIDEVICEINSTANCEW* info, void* user) {
    auto& capture=*static_cast<Capture*>(user);
    auto device=std::make_unique<Device>();
    if(FAILED(capture.input->CreateDevice(info->guidInstance,&device->input,nullptr))) return DIENUM_CONTINUE;
    DIPROPGUIDANDPATH path{};
    path.diph={sizeof(path),sizeof(path.diph),0,DIPH_DEVICE};
    if(SUCCEEDED(device->input->GetProperty(DIPROP_GUIDANDPATH,&path.diph)) &&
       (wcsstr(path.wszPath,L"IG_") || wcsstr(path.wszPath,L"ig_"))) return DIENUM_CONTINUE;
    if(FAILED(device->input->SetDataFormat(&c_dfDIJoystick2)) ||
       FAILED(device->input->SetCooperativeLevel(capture.window,DISCL_NONEXCLUSIVE|DISCL_BACKGROUND))) return DIENUM_CONTINUE;
    wchar_t guid[40]{};
    StringFromGUID2(info->guidInstance,guid,40);
    device->id=guid; device->name=info->tszProductName;
    device->input->EnumObjects(Axis,device.get(),DIDFT_AXIS);
    device->input->Acquire();
    capture.devices.push_back(std::move(device));
    return DIENUM_CONTINUE;
}
extern "C" __declspec(dllexport) void* __cdecl CaptureOpen(HWND window) {
    try {
        auto capture=std::make_unique<Capture>(); capture->window=window;
        if(FAILED(DirectInput8Create(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8W,
                                    reinterpret_cast<void**>(&capture->input),nullptr))) return nullptr;
        capture->input->EnumDevices(DI8DEVCLASS_GAMECTRL,Enumerate,capture.get(),DIEDFL_ATTACHEDONLY);
        for(int slot=0;slot<4;++slot) {
            XINPUT_STATE state{};
            if(XInputGetState(slot,&state)!=ERROR_SUCCESS) continue;
            auto device=std::make_unique<Device>(); device->xbox=slot; device->axes=0x3f;
            device->id=L"xinput:"+std::to_wstring(slot); device->name=L"win_xinput";
            capture->devices.push_back(std::move(device));
        }
        return capture.release();
    } catch(...) { return nullptr; }
}
extern "C" __declspec(dllexport) void __cdecl CaptureClose(void* capture) { delete static_cast<Capture*>(capture); }
extern "C" __declspec(dllexport) unsigned __cdecl CaptureCount(void* capture) {
    return capture ? static_cast<unsigned>(static_cast<Capture*>(capture)->devices.size()) : 0;
}
extern "C" __declspec(dllexport) int __cdecl CaptureName(void* capture,unsigned index,wchar_t* id,unsigned idCount,wchar_t* name,unsigned nameCount) {
    if(!capture || index>=CaptureCount(capture) || !id || !name) return 0;
    const auto& device=*static_cast<Capture*>(capture)->devices[index];
    return wcscpy_s(id,idCount,device.id.c_str())==0 && wcscpy_s(name,nameCount,device.name.c_str())==0;
}
extern "C" __declspec(dllexport) int __cdecl CaptureRead(void* capture,unsigned index,Sample* result) {
    if(!result) return 0;
    *result={}; std::fill(std::begin(result->pov),std::end(result->pov),0xffffffffu);
    if(!capture || index>=CaptureCount(capture)) return 0;
    auto& device=*static_cast<Capture*>(capture)->devices[index];
    result->axes=device.axes;
    if(device.xbox>=0) {
        XINPUT_STATE state{};
        if(XInputGetState(device.xbox,&state)!=ERROR_SUCCESS) return 0;
        auto& pad=state.Gamepad;
        result->values[0]=std::clamp(pad.sThumbLX/32767.f,-1.f,1.f);
        result->values[1]=std::clamp(pad.sThumbLY/32767.f,-1.f,1.f);
        result->values[2]=std::clamp(pad.sThumbRX/32767.f,-1.f,1.f);
        result->values[3]=std::clamp(pad.sThumbRY/32767.f,-1.f,1.f);
        result->values[4]=pad.bLeftTrigger/255.f;
        result->values[5]=pad.bRightTrigger/255.f;
        for(unsigned i=0;i<16;++i) result->buttons[i]=(pad.wButtons&(1u<<i))?1:0;
    } else {
        auto status=device.input->Poll();
        if(FAILED(status)) { device.input->Acquire(); return 0; }
        DIJOYSTATE2 state{};
        if(FAILED(device.input->GetDeviceState(sizeof(state),&state))) return 0;
        const LONG axes[]={state.lX,state.lY,state.lZ,state.lRx,state.lRy,state.lRz,state.rglSlider[0],state.rglSlider[1]};
        for(unsigned i=0;i<8;++i) result->values[i]=std::clamp(axes[i]/32767.5f-1.f,-1.f,1.f);
        for(unsigned i=0;i<128;++i) result->buttons[i]=(state.rgbButtons[i]&0x80)?1:0;
        std::copy(std::begin(state.rgdwPOV),std::end(state.rgdwPOV),result->pov);
    }
    result->connected=1; return 1;
}
