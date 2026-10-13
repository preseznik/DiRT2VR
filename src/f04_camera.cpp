#include "f04_camera.h"
#include "f04_camera_filter.h"
#include "f04_look_transition.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <Xinput.h>
#include <intrin.h>
#include <array>
#include <cstring>
#include <mutex>

namespace vr {
namespace {
using SceneFn = void (__thiscall*)(void*,void*,void*,void*,void*,void*);
using InputFn = DWORD (WINAPI*)(DWORD,XINPUT_STATE*);
SceneFn originalScene{};
InputFn originalInput{};
F04CameraFilter filter;
F04LookTransition look;
std::mutex lookMutex;
LARGE_INTEGER lastTick{},frequency{};
unsigned filteredFrames{};

bool Flag(const wchar_t* name) {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(name,value,8)==1 && value[0]==L'1';
}
bool Requested() {
    wchar_t car[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_CUSTOM_CAR",car,8)==3 && !wcscmp(car,L"f04") &&
        Flag(L"DIRT2VR_DESKTOP_PRACTICE") && Flag(L"DIRT2VR_DIRECT_PRACTICE") &&
        !Flag(L"DIRT2VR_HEADSET");
}
DWORD WINAPI ObserveInput(DWORD slot,XINPUT_STATE* state) {
    const auto result=originalInput(slot,state);
    if(result==ERROR_SUCCESS && state) {
        std::lock_guard lock(lookMutex);
        look.Observe(state->Gamepad.sThumbRX,state->Gamepad.sThumbRY,GetTickCount64());
    }
    return result; // Observe only; preserve every native input byte and error.
}
bool ReadCamera(const void* at,std::array<float,28>& out) {
    SIZE_T bytes{};
    return at && ReadProcessMemory(GetCurrentProcess(),at,out.data(),sizeof(out),&bytes) && bytes==sizeof(out);
}
void __fastcall Scene(void* self,void*,void* a,void* b,void* c,void* d,void* e) {
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-
        reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    alignas(16) std::array<float,28> rawA{},rawB{},renderA{},renderB{};
    bool filtered=false;
    if(caller==0x2889c0 && ReadCamera(a,rawA) && ReadCamera(b,rawB)) {
        // The converted Head view uses a 4 cm near plane and matching native
        // camera bases. Chase, reflections and other views retain native records.
        bool head=std::abs(rawA[21]-.04f)<.00001f && std::abs(rawB[21]-.04f)<.00001f;
        for(unsigned i:{4u,5u,6u,8u,9u,10u,12u,13u,14u})
            head &= std::abs(rawA[i]-rawB[i])<.0001f;
        LARGE_INTEGER stamp{};QueryPerformanceCounter(&stamp);
        const float dt=lastTick.QuadPart?float(double(stamp.QuadPart-lastTick.QuadPart)/frequency.QuadPart):1.f/120.f;
        lastTick=stamp;
        bool changingLook=false;
        { std::lock_guard lock(lookMutex);changingLook=look.Active(GetTickCount64()); }
        if(changingLook)filter.Reset();
        if(head)filtered=filter.Apply(rawA,renderA,dt);else filter.Reset();
        if(filtered) {
            renderB=rawB;
            for(unsigned i:{4u,5u,6u,8u,9u,10u,12u,13u,14u})renderB[i]=renderA[i];
            ++filteredFrames;
        }
    }
    // Only render copies: physical motion, native yaw, eye position and both
    // engine-owned records remain untouched, including after the scene returns.
    originalScene(self,filtered?renderA.data():a,filtered?renderB.data():b,c,d,e);
    if(filtered && filteredFrames%120==0)
        Log("f04 desktop camera frames=%u look_sync=1 engine_records_unchanged=%d eye_position_exact=%d",
            filteredFrames,memcmp(a,rawA.data(),sizeof(rawA))==0 && memcmp(b,rawB.data(),sizeof(rawB))==0,
            memcmp(rawA.data()+16,renderA.data()+16,48)==0 && memcmp(rawB.data()+16,renderB.data()+16,48)==0);
}
}
bool EnableF04Camera() {
    if(!Requested())return true;
    if(originalScene)return true;
    if(!SupportedHost() || !QueryPerformanceFrequency(&frequency))return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x81,0xec,0xf8,0,0,0,0x56,0x8b,0xf1};
    auto target=base+0x33ad10;
    if(memcmp(target,guard,sizeof(guard)))return false;
    auto library=LoadLibraryExW(L"xinput1_3.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto input=library?reinterpret_cast<void*>(GetProcAddress(library,"XInputGetState")):nullptr;
    if(!input)return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return false;
    status=MH_CreateHook(input,reinterpret_cast<void*>(ObserveInput),reinterpret_cast<void**>(&originalInput));
    if(status==MH_OK)status=EnableRecordedHook(input);
    if(status!=MH_OK) { Log("f04 camera input observer failed: %s",MH_StatusToString(status));return false; }
    status=MH_CreateHook(target,reinterpret_cast<void*>(Scene),reinterpret_cast<void**>(&originalScene));
    if(status==MH_OK)status=EnableRecordedHook(target);
    Log("f04 desktop camera hook=%s filter=180ms cap=6deg look_settle=500ms input=observe-only",MH_StatusToString(status));
    return status==MH_OK;
}
}
