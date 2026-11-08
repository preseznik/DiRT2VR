#include "world_record_target.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstdint>
#include <cstring>
#include <intrin.h>

namespace vr {
namespace {
// The lap interface's TARGET box (element at +0x498) is native HUD art that only
// personal challenges (game mode 7) add. Direct starts run in mode 5 (skipfe).
using TargetTimeFn=bool(__thiscall*)(void*,int64_t*);
TargetTimeFn originalTargetTime{};
void* originalGate{};
void* addElement{};
void* gateDone{};
void* targetDisplay{};
int64_t worldRecord{};

// GetTargetTime: true and microseconds for the box. Only the box's own display
// receives the record; other callers and modes keep the native answer.
bool __fastcall TargetTime(void* hud,void*,int64_t* time) {
    const auto* career=*reinterpret_cast<unsigned char**>(static_cast<unsigned char*>(hud)+8);
    if(_ReturnAddress()!=targetDisplay || !time || *reinterpret_cast<const int*>(career+0xac0)!=5)
        return originalTargetTime(hud,time);
    *time=worldRecord;
    return true;
}
// Replaces "cmp [career+0xac0],7 / jne done" while the interface rebuilds its
// element list (ECX career, ESI interface). In skipfe, add the box as a time
// objective exactly as the mode-7 path does and skip the challenge lookup.
// The interface setup supplies the element's owner; without one, stay native.
__declspec(naked) void Gate() {
    __asm {
        cmp dword ptr [ecx+0xac0],5
        jne native_gate
        cmp dword ptr [esi+0x4e8],0
        je native_gate
        pushad
        mov dword ptr [esi+0x504],1
        mov eax,dword ptr [esi+0x598]
        mov eax,dword ptr [eax+0x10]
        mov dword ptr [esi+0x508],eax
        lea eax,[esi+0x498]
        push eax
        mov ecx,esi
        call dword ptr [addElement]
        popad
        jmp dword ptr [gateDone]
    native_gate:
        jmp dword ptr [originalGate]
    }
}
}
// The launcher sets the selected class's record for Direct practice only.
void EnableWorldRecordTarget() {
    static bool attempted{};
    if(attempted) return;
    attempted=true;
    wchar_t text[16]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_WORLD_RECORD_MICROSECONDS",text,_countof(text));
    if(!length) return;
    int64_t value{};
    bool valid=length<_countof(text);
    for(const wchar_t* p=text;valid && *p;++p) {
        valid=*p>=L'0' && *p<=L'9' && value<=720000000LL;
        value=value*10+(*p-L'0');
    }
    if(!valid || value<1000000 || value>7200000000LL) {Log("world record target: invalid time; native HUD retained");return;}
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char gate[]{0x83,0xb9,0xc0,0x0a,0,0,0x07,0x0f,0x85,0xed,0,0,0};
    const unsigned char done[]{0x5e,0x59,0xc3};
    const unsigned char add[]{0x8b,0xc1,0x8b,0x90,0x08,0x02,0,0,0x83,0xfa,0x10};
    const unsigned char target[]{0x83,0xec,0x10,0x8b,0x49,0x08,0x32,0xc0,0x83,0xb9,0xc0,0x0a,0,0,0x07};
    const unsigned char display[]{0x8b,0x4e,0x50,0x8b,0x11,0x8b,0x42,0x64,0x8d,0x7e,0x60,0x57,0xff,0xd0};
    if(!SupportedHost() || std::memcmp(base+0x3ba96a,gate,sizeof(gate)) || std::memcmp(base+0x3baa64,done,sizeof(done)) ||
       std::memcmp(base+0x889600,add,sizeof(add)) || std::memcmp(base+0x3c2760,target,sizeof(target)) ||
       std::memcmp(base+0x3afbe1,display,sizeof(display)) ||
       *reinterpret_cast<unsigned char**>(base+0xf30b34)!=base+0x3c2760) {
        Log("world record target: HUD guard rejected; native HUD retained");return;
    }
    if(!EnableGfwlCompatibility()) {Log("world record target: compatibility unavailable; native HUD retained");return;}
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return;
    worldRecord=value;addElement=base+0x889600;gateDone=base+0x3baa64;targetDisplay=base+0x3afbef;
    status=MH_CreateHook(base+0x3c2760,reinterpret_cast<void*>(TargetTime),reinterpret_cast<void**>(&originalTargetTime));
    if(status==MH_OK) {
        status=MH_CreateHook(base+0x3ba96a,reinterpret_cast<void*>(Gate),&originalGate);
        if(status!=MH_OK) MH_RemoveHook(base+0x3c2760);
    }
    if(status==MH_OK) {
        status=EnableRecordedHook(base+0x3c2760);
        if(status==MH_OK) status=EnableRecordedHook(base+0x3ba96a);
        if(status!=MH_OK) {
            for(auto* hook:{base+0x3ba96a,base+0x3c2760}) {MH_DisableHook(hook);MH_RemoveHook(hook);}
        }
    }
    Log("world record target: %lld us, HUD hooks=%s (memory only)",value,MH_StatusToString(status));
}
}
