#include "cockpit_start.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <intrin.h>
#include <cstring>

namespace vr {
namespace {
using FindViewFn=int (__thiscall*)(void*,const char*);
FindViewFn findView{};
unsigned char* gameBase{};
// Saved current_camera restoration, deferred/start-event override and demo start.
// Camera cycling, look-back, replay and frontend lookups are deliberately excluded.
constexpr unsigned startupCalls[]={0x233ce4,0x3217b0,0x352772,0x245360};
int __fastcall FindView(void* manager,void*,const char* name) {
    const auto caller=reinterpret_cast<unsigned char*>(_ReturnAddress());
    for(const auto call:startupCalls) {
        if(caller!=gameBase+call+5) continue;
        const int cockpit=findView(manager,"head-cam");
        Log("VR starting camera: requested=%s cockpit=%d caller=%x",name?name:"(none)",cockpit,call);
        if(cockpit>=0) return cockpit;
        break; // Unsupported car: preserve the game's fallback rather than an invalid index.
    }
    return findView(manager,name);
}
}
bool EnableCockpitStart() {
    if(findView) return true;
    if(!SupportedHost()) return false;
    gameBase=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned target=0x724f90;
    const unsigned char guard[]={0x83,0x7c,0x24,0x04,0x00,0x53,0x55,0x56,0x57,0x8b,0xe9};
    if(std::memcmp(gameBase+target,guard,sizeof(guard))) return false;
    for(const auto call:startupCalls) {
        int displacement{};
        std::memcpy(&displacement,gameBase+call+1,sizeof(displacement));
        if(gameBase[call]!=0xe8 || gameBase+call+5+displacement!=gameBase+target) return false;
    }
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=MH_CreateHook(gameBase+target,reinterpret_cast<void*>(FindView),reinterpret_cast<void**>(&findView));
        if(status==MH_OK) status=EnableRecordedHook(gameBase+target);
    }
    Log("VR starting camera: hook=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
