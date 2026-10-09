#include "cockpit_start.h"
#include "common.h"
#include "seat_adjustment.h"
#include "steering_animation.h"
#include "chase_camera.h"
#include "replay_camera.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <intrin.h>
#include <cstring>

namespace vr {
namespace {
using FindViewFn=int (__thiscall*)(void*,const char*);
FindViewFn findView{};
bool forceCockpitStart{};
unsigned char* gameBase{};
// Saved current_camera restoration, deferred/start-event override and demo start.
// Camera cycling, look-back, replay and frontend lookups are deliberately excluded.
constexpr unsigned startupCalls[]={0x233ce4,0x3217b0,0x352772,0x245360};
int __fastcall FindView(void* manager,void*,const char* name) {
    const auto caller=reinterpret_cast<unsigned char*>(_ReturnAddress());
    if(ReplayCameraProbe()) {
        static unsigned count{};
        char observed[48]{};SIZE_T bytes{};
        if(count<256 && name && ReadProcessMemory(GetCurrentProcess(),name,observed,sizeof(observed)-1,&bytes)) {
            Log("replay camera lookup manager=%p name=%s caller=%x",manager,observed,unsigned(caller-gameBase));
            ++count;
        }
    }
    for(const auto call:startupCalls) {
        if(caller!=gameBase+call+5) continue;
        SeatSelectCamera(manager);
        SteeringSelectCamera(manager);
        ChaseSelectCamera(manager);
        if(!forceCockpitStart && !ChaseCameraDiagnostic()) return findView(manager,name);
        const char* startup="head-cam";
        char diagnostic[48]{};
        if(LoggingEnabled() && GetEnvironmentVariableA("DIRT2VR_CAMERA_PROBE",diagnostic,sizeof(diagnostic)))
            for(auto candidate:{"bonnet","bumper","chase_close","chase_far"})if(!strcmp(candidate,diagnostic))startup=candidate;
        const int cockpit=findView(manager,startup);
        Log("VR starting camera: requested=%s cockpit=%d caller=%x",name?name:"(none)",cockpit,call);
        if(cockpit>=0) return cockpit;
        break; // Unsupported car: preserve the game's fallback rather than an invalid index.
    }
    return findView(manager,name);
}
}
bool EnableCockpitStart(bool forceCockpit) {
    forceCockpitStart=forceCockpit;
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
    Log("player camera: hook=%s force_cockpit=%d",MH_StatusToString(status),forceCockpitStart);
    return status==MH_OK;
}
}
