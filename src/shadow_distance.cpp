#include "shadow_distance.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
void* prepareOriginal{};
unsigned requested{},reported{};
void __cdecl PrepareRange(void* self) {
    auto* object=static_cast<unsigned char*>(self);
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    // Revalidate this live argument on every preparation, including reloads.
    // No renderer pointers survive the native call.
    __try {
        if(!object || *reinterpret_cast<void**>(object)!=base+0xf20540 ||
           *reinterpret_cast<void**>(object+0x6d4)!=object)return;
        const auto preset=*reinterpret_cast<unsigned*>(object+0x2c);
        // Native world/exterior and cockpit presets only. Keep the separate
        // trailer/menu and reflection presets at their original distances.
        if(preset>1)return;
        auto* settings=object+0xc78+preset*0x94;
        // Preset byte zero is an unrelated optional feature, not the global
        // shadow switch. Changing only this radius never enables shadows.
        const auto nearRange=*reinterpret_cast<float*>(settings+0x58);
        auto& farRange=*reinterpret_cast<float*>(settings+0x74);
        const auto before=farRange;
        if(ExtendOuterShadowRange(nearRange,farRange,requested) && reported++<12)
            Log("desktop shadows: preset=%u near=%.3f outer=%.3f -> %.3f",preset,nearRange,before,farRange);
    } __except(EXCEPTION_EXECUTE_HANDLER) { /* Retain native behavior. */ }
}
// Before native projection/visibility construction: caster bounds, matrices
// and mask constants must all use the same range. Preserve incoming CPU/FPU
// state, including nonstandard native callers.
void __declspec(naked) Prepare() {
    __asm {
        pushfd
        pushad
        mov ebx,esp
        sub esp,528
        and esp,0fffffff0h
        fxsave [esp]
        push ecx
        call PrepareRange
        add esp,4
        fxrstor [esp]
        mov esp,ebx
        popad
        popfd
        jmp dword ptr [prepareOriginal]
    }
}
}
void EnableDesktopShadowDistance() {
    if(prepareOriginal)return;
    wchar_t value[16]{};
    const auto size=GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_SHADOW_DISTANCE",value,16);
    if(!size)return;
    wchar_t headset[8]{};
    if(GetEnvironmentVariableW(L"DIRT2VR_HEADSET",headset,8)==1 && headset[0]==L'1')return;
    unsigned distance{};
    bool valid=size<16;
    if(valid)for(const auto* p=value;*p;++p) {
        if(*p<L'0' || *p>L'9' || distance>640) {valid=false;break;}
        distance=distance*10+unsigned(*p-L'0');
    }
    if(!valid || !ValidDesktopShadowDistance(distance)) {
        Log("desktop shadows: invalid distance; native shadows retained");return;
    }
    if(!distance)return;
    if(!SupportedHost()) {Log("desktop shadows: unsupported executable; native shadows retained");return;}
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char prefix[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x54,0x0b,0,0,0x53,0x56,0x57,0x8b,0xf9,0x8b,0x47,0x2c,0x69,0xc0,0x94,0,0,0};
    if(memcmp(base+0x28cb60,prefix,sizeof(prefix))) {
        Log("desktop shadows: preparation guard rejected; native shadows retained");return;
    }
    if(!EnableGfwlCompatibility()) {Log("desktop shadows: compatibility unavailable; native shadows retained");return;}
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return;
    requested=distance;
    status=MH_CreateHook(base+0x28cb60,reinterpret_cast<void*>(Prepare),&prepareOriginal);
    if(status==MH_OK)status=EnableRecordedHook(base+0x28cb60);
    Log("desktop shadows: requested=%um preparation hook=%s",requested,MH_StatusToString(status));
    if(status!=MH_OK)prepareOriginal=nullptr;
}
}
