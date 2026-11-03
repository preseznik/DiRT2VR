#include "shadow_distance.h"
#include "shadow_snap.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
void* prepareOriginal{};
void* alignContinue{};
unsigned requested{},reported{},rejected{};
bool aligned{};
const char* mode="desktop";
bool ShadowObject(const unsigned char* object) {
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    return object && *reinterpret_cast<void* const*>(object)==base+0xf20540 &&
           *reinterpret_cast<const unsigned char* const*>(object+0x6d4)==object;
}
void __cdecl PrepareRange(void* self) {
    auto* object=static_cast<unsigned char*>(self);
    // Revalidate this live argument on every preparation, including reloads.
    // No renderer pointers survive the native call.
    __try {
        if(!ShadowObject(object))return;
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
            Log("%s shadows: preset=%u near=%.3f outer=%.3f -> %.3f",mode,preset,nearRange,before,farRange);
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
// In the native cascade fit (0x2ab47b) the light camera has just been built
// and its view and view-projection copied into the cascade record; the
// record's inverse view (+0xe0) and position copy (+0x140) follow. Casters
// and the shadow mask read the record later, so aligning here keeps them all
// consistent. Cascades 1 (near) and 2 (outer) are the two sampled maps.
void __cdecl AlignCascade(unsigned char* record,unsigned char* camera,const unsigned char* locals) {
    __try {
        const auto cascade=*reinterpret_cast<const unsigned*>(locals+0x44);
        if(cascade<1 || cascade>2)return;
        const auto* object=record-0x150-cascade*0x160;
        if(!ShadowObject(object) || *reinterpret_cast<const unsigned*>(object+0x2c)>1)return;
        auto* view=reinterpret_cast<float*>(record+0x10);
        auto* projection=reinterpret_cast<float*>(record+0x50);
        ShadowSnap snap{};
        // Only when the record holds this camera's own matrices (no native view override).
        if(memcmp(view,camera+0x160,64) || memcmp(projection,camera+0x1a0,64) || !ShadowSnapShift(view,projection,snap)) {
            if(!rejected++)Log("%s shadows: cascade %u alignment rejected; native fit retained",mode,cascade);
            return;
        }
        ShiftShadowView(view,snap);ShiftShadowView(reinterpret_cast<float*>(camera+0x160),snap);
        ShiftShadowProjection(projection,snap);ShiftShadowProjection(reinterpret_cast<float*>(camera+0x1a0),snap);
        ShiftShadowPosition(reinterpret_cast<float*>(camera+0xc0),snap);
        ShiftShadowPosition(reinterpret_cast<float*>(record),snap);
        if(!aligned){aligned=true;Log("%s shadows: cascade texel alignment active",mode);}
    } __except(EXCEPTION_EXECUTE_HANDLER) { /* Retain native behavior. */ }
}
// Mid-function: ESI is the cascade record, EDI its light camera and ESP the
// fit's locals (cascade index at +44h). Preserve everything.
void __declspec(naked) AlignInFit() {
    __asm {
        pushfd
        pushad
        lea eax,[esp+36]
        mov ebx,esp
        sub esp,528
        and esp,0fffffff0h
        fxsave [esp]
        push eax
        push edi
        push esi
        call AlignCascade
        add esp,12
        fxrstor [esp]
        mov esp,ebx
        popad
        popfd
        jmp dword ptr [alignContinue]
    }
}
}
void EnableDesktopShadowDistance() {
    if(prepareOriginal || alignContinue)return;
    wchar_t flag[8]{};
    const bool headset=GetEnvironmentVariableW(L"DIRT2VR_HEADSET",flag,8)==1 && flag[0]==L'1';
    mode=headset ? "VR" : "desktop";
    if(headset) {
        // The opt-in never enables VR shadows or accepts an inherited desktop override.
        if(GetEnvironmentVariableW(L"DIRT2VR_SHADOWS",flag,8)!=1 || flag[0]!=L'1')return;
    }
    wchar_t value[16]{};
    const auto size=GetEnvironmentVariableW(headset ? L"DIRT2VR_VR_SHADOW_DISTANCE" : L"DIRT2VR_DESKTOP_SHADOW_DISTANCE",value,16);
    // VR shadows are always aligned: every head movement would show crawling edges.
    if(!size && !headset)return;
    unsigned distance{};
    bool valid=size<16;
    if(valid)for(const auto* p=value;*p;++p) {
        if(*p<L'0' || *p>L'9' || distance>640) {valid=false;break;}
        distance=distance*10+unsigned(*p-L'0');
    }
    if(!valid || !ValidDesktopShadowDistance(distance)) {
        Log("%s shadows: invalid distance; native shadows retained",mode);return;
    }
    if(!distance && !headset)return;
    if(!SupportedHost()) {Log("%s shadows: unsupported executable; native shadows retained",mode);return;}
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char prefix[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x54,0x0b,0,0,0x53,0x56,0x57,0x8b,0xf9,0x8b,0x47,0x2c,0x69,0xc0,0x94,0,0,0};
    // Cascade fit: copy the light camera's view-projection (EDI+1A0h) and view
    // into the record (ESI), then its inverse view; loop index at ESP+44h.
    const unsigned char copy[]={0x0f,0x28,0x87,0xa0,0x01,0,0,0x8b,0x44,0x24,0x20,0x8b,0x54,0x24,0x1c,0x0f,0x29,0x46,0x50};
    const unsigned char fit[]={0x0f,0x28,0x84,0x24,0xd0,0x02,0,0,0x0f,0x29,0x86,0x90,0,0,0};
    const unsigned char next[]={0x8b,0x44,0x24,0x44,0x0f,0x28,0x06,0x83,0xc3,0x50,0x83,0xc0,0x01,0x83,0xf8,0x03,0x0f,0x29,0x86,0x40,0x01,0,0};
    if(memcmp(base+0x28cb60,prefix,sizeof(prefix)) || memcmp(base+0x2ab3e1,copy,sizeof(copy)) ||
       memcmp(base+0x2ab47b,fit,sizeof(fit)) || memcmp(base+0x2ab51e,next,sizeof(next))) {
        Log("%s shadows: preparation guard rejected; native shadows retained",mode);return;
    }
    if(!EnableGfwlCompatibility()) {Log("%s shadows: compatibility unavailable; native shadows retained",mode);return;}
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return;
    requested=distance;
    if(distance) {
        status=MH_CreateHook(base+0x28cb60,reinterpret_cast<void*>(Prepare),&prepareOriginal);
        if(status==MH_OK)status=EnableRecordedHook(base+0x28cb60);
        if(status!=MH_OK)prepareOriginal=nullptr;
    }
    auto align=MH_CreateHook(base+0x2ab47b,reinterpret_cast<void*>(AlignInFit),&alignContinue);
    if(align==MH_OK)align=EnableRecordedHook(base+0x2ab47b);
    if(align!=MH_OK)alignContinue=nullptr;
    Log("%s shadows: requested=%um preparation hook=%s alignment hook=%s",mode,requested,distance ? MH_StatusToString(status) : "not needed",MH_StatusToString(align));
}
}
