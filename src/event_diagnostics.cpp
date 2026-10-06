#include "event_diagnostics.h"
#include "common.h"
#include "gfwl_compat.h"
#include "diagnostic_catalog.h"
#include <MinHook.h>
#include <cstring>
#include <mutex>

namespace vr {
namespace {
using RouteFn=void(__thiscall*)(void*,const char*,const char*,const char*);
RouteFn originalRoute{};
std::mutex mutex;
unsigned records{};
char lastCar[32]{};
// Game identifiers are short ASCII tokens. Failed/truncated reads are unknown.
bool Token(const char* source,char (&value)[32]) {
    if(!source) return false;
    for(unsigned i=0;i<sizeof(value);++i) {
        SIZE_T count{};
        if(!ReadProcessMemory(GetCurrentProcess(),source+i,value+i,1,&count) || count!=1) return false;
        const auto c=value[i];
        if(!c) return i>0;
        if(!((c>='a' && c<='z') || (c>='0' && c<='9') || c=='_')) return false;
    }
    return false;
}
bool Record() {
    if(records>=512) return false;
    if(++records==512) { Log("event context: limit reached; later observations omitted"); return false; }
    return true;
}
void __fastcall Route(void* self,void*,const char* country,const char* track,const char* route) {
    // RVA 286f60 formats the actual tracks/country/track/route scene path.
    // Log before loading proceeds so a transition crash retains its destination.
    ObserveDiagnosticRoute(country,track,route);
    originalRoute(self,country,track,route);
}
}
void ObserveDiagnosticRoute(const char* country,const char* track,const char* route) noexcept {
    if(!LoggingEnabled()) return;
    try {
        char c[32]{},t[32]{},r[32]{};
        const bool valid=Token(country,c) && Token(track,t) && Token(route,r);
        std::lock_guard lock(mutex);
        lastCar[0]=0; // Never inherit the previous event's player car after a new load.
        if(!Record()) return;
        if(!valid) { Log("event context: route_load identity=unknown player_car=unknown"); return; }
        const char *label="unknown",*discipline="unknown";
        for(const auto& row:diagnosticTracks) if(!strcmp(c,row.country) && !strcmp(t,row.track) && !strcmp(r,row.route)) {
            label=row.label;discipline=row.discipline;break;
        }
        Log("event context: route_load country=%s track=%s route=%s label=\"%s\" catalog_discipline=\"%s\" player_car=unknown",c,t,r,label,discipline);
    } catch(...) {} // Diagnostics must not turn memory pressure into a game failure.
}
void ObserveDiagnosticCar(const char* code) noexcept {
    if(!LoggingEnabled()) return;
    try {
        char value[32]{};
        if(!Token(code,value)) strcpy_s(value,"unknown");
        std::lock_guard lock(mutex);
        if(!strcmp(lastCar,value)) return;
        strcpy_s(lastCar,value);
        if(!Record()) return;
        const char* label="unknown";
        for(const auto& car:diagnosticCars) if(!strcmp(value,car.code)) { label=car.label;break; }
        Log("event context: player_camera car=%s label=\"%s\"",value,label);
    } catch(...) {}
}
void EnableEventDiagnostics() noexcept {
    try {
        if(!LoggingEnabled() || originalRoute) return;
        if(!SupportedHost()) { Log("event context: unsupported host; observer unavailable"); return; }
        auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
        const unsigned char guard[]={0x8b,0x44,0x24,0x0c,0x53,0x8b,0x5c,0x24,0x0c,0x56,0x57,0x8b,0x7c,0x24,0x10};
        if(memcmp(base+0x286f60,guard,sizeof(guard)) || !EnableGfwlCompatibility()) {
            Log("event context: route observer guard failed; unavailable"); return;
        }
        auto status=MH_Initialize();
        if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
            status=MH_CreateHook(base+0x286f60,reinterpret_cast<void*>(Route),reinterpret_cast<void**>(&originalRoute));
            if(status==MH_OK) status=EnableRecordedHook(base+0x286f60);
        }
        Log("event context: route observer=%s; route_load is a loading destination, not proof of completed gameplay; career series name unavailable",MH_StatusToString(status));
    } catch(...) {} // Optional diagnostics cannot veto an otherwise valid launch.
}
}
