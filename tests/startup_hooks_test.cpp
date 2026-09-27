#include "startup_hooks.h"
#include <stdexcept>
#include <string>
#include <cstdio>

namespace { std::string calls; bool overrides{}, failCompatibility{}, failDriving{}, failCockpit{}, recorded{}; }
namespace vr {
void Log(const char*,...) {}
bool EnableGfwlCompatibility() { calls+='G'; recorded=!failCompatibility; return recorded; }
bool EnableDrivingControls() {
    calls+='D';
    // Driving overrides used to hide the bug by initializing compatibility early.
    if(overrides && !recorded) EnableGfwlCompatibility();
    return !failDriving;
}
bool EnableCockpitStart() {
    calls+='C';
    if(!recorded) throw std::runtime_error("cockpit edit omitted from checksum recording");
    return !failCockpit;
}
}
void Check(bool value) { if(!value) throw std::runtime_error("startup ordering/guard failure"); }
void Reset() { calls.clear(); recorded=false; }
int main() {
    for(bool bindings:{false,true}) {
        overrides=bindings; Reset(); Check(vr::EnableStartupHooks(true,true)); Check(calls=="GDC");
    }
    overrides=false; Reset(); Check(vr::EnableStartupHooks(false,false)); Check(calls=="D" && !recorded);
    Reset(); Check(vr::EnableStartupHooks(true,false)); Check(calls=="GD");
    Reset(); Check(vr::EnableStartupHooks(false,true)); Check(calls=="GDC");
    failCompatibility=true; Reset(); Check(!vr::EnableStartupHooks(true,true)); Check(calls=="G"); failCompatibility=false;
    failDriving=true; Reset(); Check(!vr::EnableStartupHooks(true,true)); Check(calls=="GD"); failDriving=false;
    failCockpit=true; Reset(); Check(!vr::EnableStartupHooks(true,true)); Check(calls=="GDC");
    puts("VR hook recording precedes cockpit edits with/without driving overrides; desktop and failure guards pass.");
}
