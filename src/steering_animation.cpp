#include "steering_animation.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <intrin.h>
#include <atomic>
#include <cmath>
#include <cstring>

namespace vr {
namespace {
using LookupFn=void* (__thiscall*)(void*,unsigned);
using SteeringFn=void (__thiscall*)(void*,float);
using FilterFn=void (__thiscall*)(void*,float,float);
LookupFn lookup{};
SteeringFn steering{};
FilterFn filter{};
void* visualTail{};
unsigned char* gameBase{};
std::atomic<void*> selectedCamera{};
std::atomic<ULONGLONG> cockpitTick{};
std::atomic<bool> enabled{};
struct Actor { void* object{}; unsigned index{}; };
thread_local Actor actor;
thread_local bool normalFilter{};
struct Override { bool active{}; float input{}; };
thread_local Override visualOverride;

template<class T> bool Read(const void* object,unsigned offset,T& value) {
    SIZE_T bytes{};
    return object && ReadProcessMemory(GetCurrentProcess(),static_cast<const char*>(object)+offset,&value,sizeof(value),&bytes) && bytes==sizeof(value);
}
bool NormalSetter(unsigned caller,bool filtered) {
    return caller==0x7b2720 || (caller==0x79cbac && filtered);
}
const char* Eligibility(void* self,float input,unsigned caller) {
    if(!enabled)return "disabled";
    if(!NormalSetter(caller,normalFilter))return "different-call-path";
    if(!std::isfinite(input) || input < -1.f || input > 1.f)return "input-out-of-range";
    const auto tick=cockpitTick.load();
    const auto camera=selectedCamera.load();
    if(!camera)return "no-player-camera";
    if(!tick || GetTickCount64()-tick>=250)return "outside-visible-cockpit";
    unsigned index{};void* actualCamera{};void* interior{};
    // The actor comes from the lookup immediately preceding this driver's update.
    // Match both its index and camera owner; never infer the player from car type.
    if(!Read(self,0x24,index) || index!=actor.index)return "driver-index-mismatch";
    if(!Read(self,0x20,interior) || !interior)return "no-interior";
    if(!Read(actor.object,0x8384,actualCamera) || actualCamera!=camera)return "camera-owner-mismatch";
    return nullptr;
}
bool Eligible(void* self,float input,unsigned caller) { return Eligibility(self,input,caller)==nullptr; }
void DiagnoseSteering(void* self,float value,unsigned caller,const char* reason) {
    if(!LoggingEnabled())return;
    static thread_local ULONGLONG previous{};
    static thread_local unsigned samples{};
    const auto now=GetTickCount64();
    if(samples>=180 || (samples && now-previous<2000))return;
    ++samples; previous=now;
    unsigned index{};void* owner{};
    Read(self,0x24,index);Read(actor.object,0x8384,owner);
    Log("steering animation: eligibility=%s caller=%x input=%f driver=%u actor=%u owner=%p selected=%p filtered=%d",
        reason?reason:"active",caller,value,index,actor.index,owner,selectedCamera.load(),normalFilter?1:0);
}
void* __fastcall Lookup(void* self,void*,unsigned index) {
    auto result=lookup(self,index);
    if(reinterpret_cast<unsigned char*>(_ReturnAddress())==gameBase+0x7b25d5)actor={result,index};
    return result;
}
void __fastcall Filter(void* self,void*,float value,float dt) {
    const auto old=normalFilter;
    normalFilter=reinterpret_cast<unsigned char*>(_ReturnAddress())==gameBase+0x7b273a;
    filter(self,value,dt);
    normalFilter=old;
}
void __fastcall Steering(void* self,void*,float value) {
    const auto old=visualOverride;
    const auto caller=unsigned(reinterpret_cast<unsigned char*>(_ReturnAddress())-gameBase);
    const auto reason=Eligibility(self,value,caller);
    DiagnoseSteering(self,value,caller,reason);
    visualOverride={reason==nullptr,value};
    // Run the original state update and feedback calculation unchanged. The
    // separate tail hook substitutes only the subsequent animation input.
    steering(self,value);
    visualOverride=old;
}
void __cdecl RestoreAnimationInput(float* value) {
    if(!visualOverride.active)return;
    if(LoggingEnabled()) {
        static thread_local unsigned samples=0;
        if(++samples==1 || (samples<=21600 && samples%120==0))
            Log("steering animation: sample=%u filtered=%f original_visual=%f",samples,visualOverride.input,*value);
    }
    *value=visualOverride.input;
}
// x86-specific boundary after the original feedback write at 0x77599a, before
// any hand weights or wheel clip time are calculated. Preserve flags, general
// registers, x87 state and all SSE registers across the C++ helper.
__declspec(naked) void VisualTail() {
    __asm {
        pushfd
        pushad
        mov ebx,esp
        sub esp,528
        and esp,0xfffffff0
        fxsave [esp]
        lea eax,[ebx+56] // entry ESP + 0x14: original argument's stack slot
        push eax
        call RestoreAnimationInput
        add esp,4
        fxrstor [esp]
        mov esp,ebx
        popad
        popfd
        movss xmm2,dword ptr [esp+20]
        jmp dword ptr [visualTail]
    }
}
struct Hook { unsigned rva; const unsigned char* guard; size_t size; void* replacement; void** original; };
}
void SteeringSelectCamera(void* manager) {
    if(selectedCamera.exchange(manager)!=manager)cockpitTick=0;
}
void SteeringCockpitView(bool active) {
    if(enabled)cockpitTick=active ? GetTickCount64() : 0;
}
bool EnableSteeringAnimation() {
    wchar_t flag[8]{};
    if(GetEnvironmentVariableW(L"DIRT2VR_STEERING_ANIMATION",flag,8)!=1 || flag[0]!=L'1')return true;
    if(enabled)return true;
    if(!SupportedHost() || !EnableGfwlCompatibility())return false;
    gameBase=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char lookupGuard[]={0x8b,0x41,0x54,0x8b,0x4c,0x24,0x04,0x8d,0x04,0x88};
    const unsigned char setGuard[]={0x83,0xec,0x0c,0x56,0x8b,0xf1,0x8b,0x46,0x20};
    const unsigned char filterGuard[]={0x80,0xb9,0x30,0x0d,0,0,0,0x0f,0x57,0xe4};
    unsigned char tailGuard[]={0xf3,0x0f,0x10,0x05,0,0,0,0};
    const auto constant=reinterpret_cast<uintptr_t>(gameBase)+0xeedcf8;
    memcpy(tailGuard+4,&constant,4);
    Hook hooks[]={
        {0x9ca030,lookupGuard,sizeof(lookupGuard),reinterpret_cast<void*>(Lookup),reinterpret_cast<void**>(&lookup)},
        {0x7758e0,setGuard,sizeof(setGuard),reinterpret_cast<void*>(Steering),reinterpret_cast<void**>(&steering)},
        {0x79cab0,filterGuard,sizeof(filterGuard),reinterpret_cast<void*>(Filter),reinterpret_cast<void**>(&filter)},
        {0x7759a0,tailGuard,sizeof(tailGuard),reinterpret_cast<void*>(VisualTail),&visualTail}};
    for(const auto& h:hooks)if(memcmp(gameBase+h.rva,h.guard,h.size)) {
        Log("steering animation: unsupported code at %x; original animation retained",h.rva);return false;
    }
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return false;
    unsigned created=0;
    for(const auto& h:hooks) {
        status=MH_CreateHook(gameBase+h.rva,h.replacement,h.original);
        if(status!=MH_OK)break;
        ++created;
    }
    if(status==MH_OK)for(const auto& h:hooks) {
        status=EnableRecordedHook(gameBase+h.rva);
        if(status!=MH_OK)break;
    }
    if(status!=MH_OK) {
        for(unsigned i=0;i<created;++i) {MH_DisableHook(gameBase+hooks[i].rva);MH_RemoveHook(gameBase+hooks[i].rva);*hooks[i].original=nullptr;}
        Log("steering animation: installation failed (%s); original animation retained",MH_StatusToString(status));return false;
    }
    enabled=true;
    Log("steering animation: original range; visual-only random correction removal enabled");
    return true;
}
}
