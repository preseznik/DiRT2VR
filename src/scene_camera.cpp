#include "scene_camera.h"
#include "common.h"
#include "camera_math.h"
#include "gfwl_compat.h"
#include "replay_camera.h"
#include <intrin.h>
#include <MinHook.h>
#include <array>
#include <mutex>
#include <cstring>
#include <algorithm>
#include <atomic>
namespace vr {
namespace {
using ActiveFn=void* (__thiscall*)(void*,bool);
ActiveFn activeCamera{};
ActiveFn displayCamera{};
std::atomic<void*> displayed{};
std::atomic<ULONGLONG> displayTick{};
std::mutex mutex;
struct Sample { char name[48]{}; void* object{}; void* manager{}; ULONGLONG tick{}; };
std::array<Sample,16> samples{};unsigned nextSample{};
bool Read(const void* at,void* out,size_t bytes) {
    SIZE_T count{};return at && ReadProcessMemory(GetCurrentProcess(),at,out,bytes,&count) && count==bytes;
}
void* __fastcall DisplayCamera(void* manager,void*,bool force) {
    void* object=displayCamera(manager,force);
    const auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    // The scene renderer checks the chosen view here before applying its view-
    // specific rendering rules. Other callers also query inactive camera owners.
    if(_ReturnAddress()==base+0x2d3163) {
        displayed.store(object);displayTick.store(GetTickCount64());
        thread_local void* previous{};
        if(ReplayCameraProbe() && previous!=object) {
            char name[48]{};
            if(object && Read(static_cast<char*>(object)+0x80,name,sizeof(name)) && memchr(name,0,sizeof(name)))
                Log("replay displayed camera manager=%p object=%p name=%s",manager,object,name);
            previous=object;
        }
        thread_local ULONGLONG poseTick{};
        const auto now=GetTickCount64();
        if(ReplayOrbitProbe() && ObservedReplayPlayback().Available(now) && now-poseTick>=1000) {
            std::array<float,16> pose{};
            if(object && Read(static_cast<char*>(object)+0x10,pose.data(),sizeof(pose)))
                Log("replay display read: manager=%p camera=%p right=%.6f,%.6f,%.6f up=%.6f,%.6f,%.6f forward=%.6f,%.6f,%.6f p=%.6f,%.6f,%.6f",
                    manager,object,pose[0],pose[1],pose[2],pose[4],pose[5],pose[6],pose[8],pose[9],pose[10],pose[12],pose[13],pose[14]);
            poseTick=now;
        }
    }
    return object;
}
void ProbeCameraList(void* manager) {
    // FindView/ActiveCamera use a 0x270-byte entry array at +4/+8 and
    // the selected entry index at +0x128. This probe never selects a camera.
    unsigned begin{},end{},selected{};
    if(!Read(static_cast<char*>(manager)+4,&begin,4) ||
       !Read(static_cast<char*>(manager)+8,&end,4) ||
       !Read(static_cast<char*>(manager)+0x128,&selected,4) ||
       !begin || end<begin || (end-begin)%0x270 || (end-begin)/0x270>64)return;
    struct Seen { void* manager{};unsigned begin{},end{},selected{}; };
    thread_local std::array<Seen,16> seen{};
    auto previous=std::find_if(seen.begin(),seen.end(),[&](const Seen& s){return s.manager==manager;});
    if(previous==seen.end())previous=std::find_if(seen.begin(),seen.end(),[](const Seen& s){return !s.manager;});
    if(previous==seen.end())return; // Bounded diagnostic, even with unexpected owners.
    if(previous->manager==manager && previous->begin==begin && previous->end==end && previous->selected==selected)return;
    Log("replay camera list manager=%p count=%u selected=%u",manager,(end-begin)/0x270,selected);
    if(previous->manager!=manager || previous->begin!=begin || previous->end!=end) {
        for(unsigned i=0;i<(end-begin)/0x270;++i) {
            void* object{};char name[48]{};
            if(Read(reinterpret_cast<void*>(begin+i*0x270),&object,sizeof(object)) && object &&
               Read(static_cast<char*>(object)+0x80,name,sizeof(name)) && memchr(name,0,sizeof(name)))
                Log("replay camera entry manager=%p index=%u object=%p name=%s",manager,i,object,name);
        }
    }
    *previous={manager,begin,end,selected};
}
void* __fastcall ActiveCamera(void* manager,void*,bool force) {
    void* result=activeCamera(manager,force);Sample next;
    if(!Read(result,&next.object,sizeof(next.object)) || !next.object ||
       !Read(static_cast<char*>(next.object)+0x80,next.name,sizeof(next.name)) || !memchr(next.name,0,sizeof(next.name)))next={};
    next.manager=manager;next.tick=GetTickCount64();
    if(ReplayCameraProbe() && next.object) {
        ProbeCameraList(manager);
        thread_local ULONGLONG last{};
        thread_local void* previous{};
        if(previous!=next.object || next.tick-last>=1000) {
            unsigned table{},providerTable{};void* provider{};std::array<float,28> record{};
            Read(next.object,&table,sizeof(table));
            Read(static_cast<char*>(next.object)+0x74,&provider,sizeof(provider));
            if(provider)Read(provider,&providerTable,sizeof(providerTable));
            Read(static_cast<char*>(next.object)+0x10,record.data()+4,96);
            const auto base=reinterpret_cast<unsigned>(GetModuleHandleW(nullptr));
            Log("replay camera sample manager=%p object=%p name=%s table=%x provider=%p provider_table=%x caller=%x live=%d force=%d p=%.4f,%.4f,%.4f fov=%.5f near=%.5f",
                manager,next.object,next.name,table-base,provider,providerTable?providerTable-base:0,
                reinterpret_cast<unsigned>(_ReturnAddress())-base,LiveDrivingCameraState(),force,record[16],record[17],record[18],record[20],record[21]);
            previous=next.object;last=next.tick;
        }
    }
    std::lock_guard lock(mutex);
    auto slot=std::find_if(samples.begin(),samples.end(),[&](const Sample& s){return s.manager==manager;});
    if(slot==samples.end())slot=samples.begin()+(nextSample++%samples.size());
    if(ReplayCameraProbe() && strcmp(slot->name,next.name))Log("extended owner manager=%p name=%s",manager,next.name);
    *slot=next;return result;
}
}
void* ObservedDisplayCamera() {
    return GetTickCount64()-displayTick.load()<250?displayed.load():nullptr;
}
bool ExtendedViewsEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_EXTENDED_VIEWS",value,8)==1 && value[0]==L'1';}();
    return enabled;
}
SceneCamera ObservedSceneCamera(void* manager) {
    std::lock_guard lock(mutex);
    const auto now=GetTickCount64();
    for(const auto& sample:samples)
        if(sample.manager==manager && sample.tick && now-sample.tick<250) return NamedSceneCamera(sample.name);
    return SceneCamera::Unknown;
}
void* ObservedCameraObject(void* manager) {
    std::lock_guard lock(mutex);
    const auto now=GetTickCount64();
    for(const auto& sample:samples)
        if(sample.manager==manager && sample.tick && now-sample.tick<250)return sample.object;
    return nullptr;
}
bool EnableSceneCameraObserver(bool required) {
    if(!required && !ExtendedViewsEnabled() && !ReplayCamerasEnabled())return true;
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
    Log("extended views observer hook=%s",MH_StatusToString(status));
    if(status==MH_OK && ReplayCamerasEnabled()) {
        const unsigned char displayGuard[]={0x8b,0x44,0x24,0x04,0x50,0xe8,0x16,0x56,0xff,0xff,0x8b,0x00,0xc2,0x04,0x00};
        const unsigned char callGuard[]={0xe8,0xfd,0xc8,0x45,0x00};
        if(memcmp(base+0x72fa60,displayGuard,sizeof(displayGuard)) || memcmp(base+0x2d315e,callGuard,sizeof(callGuard)))return false;
        status=MH_CreateHook(base+0x72fa60,reinterpret_cast<void*>(DisplayCamera),reinterpret_cast<void**>(&displayCamera));
        if(status==MH_OK)status=EnableRecordedHook(base+0x72fa60);
        Log("replay displayed camera observer hook=%s",MH_StatusToString(status));
    }
    return status==MH_OK;
}
SceneCamera IdentifySceneCamera(const float* a,const float* b,uint64_t frame) {
    if(!a || !b)return SceneCamera::Unknown;
    if(ReplayCamerasEnabled() && ObservedReplayPlayback().Available(GetTickCount64())) {
        return ReplayStereoEnabled() && MatchingReplayChaseCamera(a,b) ? SceneCamera::ReplayChase : SceneCamera::Unknown;
    }
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
