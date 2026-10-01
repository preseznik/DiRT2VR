#include "scene_camera.h"
#include "common.h"
#include "camera_math.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <array>
#include <mutex>
#include <cstring>
#include <algorithm>
namespace vr {
namespace {
using ActiveFn=void* (__thiscall*)(void*,bool);
ActiveFn activeCamera{};
std::mutex mutex;
struct Sample { char name[48]{}; void* object{}; void* manager{}; ULONGLONG tick{}; };
std::array<Sample,16> samples{};unsigned nextSample{};
bool Read(const void* at,void* out,size_t bytes) {
    SIZE_T count{};return at && ReadProcessMemory(GetCurrentProcess(),at,out,bytes,&count) && count==bytes;
}
void* __fastcall ActiveCamera(void* manager,void*,bool force) {
    void* result=activeCamera(manager,force);Sample next;
    if(!Read(result,&next.object,sizeof(next.object)) || !next.object ||
       !Read(static_cast<char*>(next.object)+0x80,next.name,sizeof(next.name)) || !memchr(next.name,0,sizeof(next.name)))return result;
    next.manager=manager;next.tick=GetTickCount64();
    std::lock_guard lock(mutex);
    auto slot=std::find_if(samples.begin(),samples.end(),[&](const Sample& s){return s.manager==manager;});
    if(slot==samples.end())slot=samples.begin()+(nextSample++%samples.size());
    if(strcmp(slot->name,next.name))Log("extended owner manager=%p name=%s",manager,next.name);
    *slot=next;return result;
}
}
bool ExtendedViewsEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_EXTENDED_VIEWS",value,8)==1 && value[0]==L'1';}();
    return enabled;
}
bool EnableSceneCameraObserver() {
    if(!ExtendedViewsEnabled())return true;
    if(activeCamera)return true;
    if(!SupportedHost())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x57,0x8b,0x79,0x04,0x85,0xff,0x75,0x04};
    if(memcmp(base+0x725080,guard,sizeof(guard)))return false;
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=MH_CreateHook(base+0x725080,reinterpret_cast<void*>(ActiveCamera),reinterpret_cast<void**>(&activeCamera));
        if(status==MH_OK)status=EnableRecordedHook(base+0x725080);
    }
    Log("extended views observer hook=%s",MH_StatusToString(status));return status==MH_OK;
}
SceneCamera IdentifySceneCamera(const float* a,const float* b,uint64_t frame) {
    if(!a || !b)return SceneCamera::Unknown;
    if(ExtendedViewsEnabled() && !LiveDrivingCameraState())return SceneCamera::Unknown;
    if(CockpitCameraCandidate(a,b))return SceneCamera::Cockpit; // Preserve cockpit-only behavior when opted out.
    if(!ExtendedViewsEnabled())return SceneCamera::Unknown;
    std::array<Sample,16> observed;{std::lock_guard lock(mutex);observed=samples;}
    const auto now=GetTickCount64();SceneCamera identified=SceneCamera::Unknown;const char* name="unknown";bool matched=false;
    for(const auto& candidate:observed) {
        if(!candidate.tick || now-candidate.tick>=500)continue;
        char currentName[48]{};std::array<float,28> record{};
        // The owner stores the same basis/position/projection fields four floats
        // earlier than the renderer record. Read after camera update, not at lookup.
        if(!Read(static_cast<char*>(candidate.object)+0x80,currentName,sizeof(currentName)) ||
           (!memchr(currentName,0,sizeof(currentName)) || strcmp(currentName,candidate.name)) ||
           !Read(static_cast<char*>(candidate.object)+0x10,record.data()+4,96))continue;
        if(MatchingSceneCamera(a,record.data()) && MatchingSceneCamera(b,record.data())) {
            const auto kind=NamedSceneCamera(candidate.name);
            if(matched && kind!=identified)return SceneCamera::Unknown;
            matched=true;identified=kind;name=candidate.name;
        }
    }
    if(frame%120==0)Log("extended view frame=%llu name=%s kind=%u near=%.5f/%.5f",frame,name,unsigned(identified),a[21],b[21]);
    return identified;
}
}
