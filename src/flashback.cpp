#include "common.h"
#include "replay_sample_clock.h"
#include "scene_camera.h"
#include "steering_animation.h"
#include "chase_camera.h"
#include "replay_camera.h"
#include "replay_storage.h"
#include <intrin.h>
#include "gfwl_compat.h"
#include <MinHook.h>
#include <atomic>
#include <cstring>
#include <mutex>

namespace vr {
namespace {
using Tick = void (__thiscall*)(void*, const double*);
Tick update{}, serialize{};
std::atomic<unsigned> recorded{}, skipped{}, fullSuppressed{};
bool sample60{};
bool disableFullRecording{};
std::atomic<ULONGLONG> liveRecordingTick{};
void* recordingCaller{};
thread_local ReplaySampleClock sampleClock;
thread_local void* samplingOwner{};
thread_local void* activeRecorder{};
std::mutex playbackMutex;
ReplayPlayback playback;
template<class T> T Field(const void* object, unsigned offset) {
    T value{};
    std::memcpy(&value, static_cast<const unsigned char*>(object)+offset, sizeof(value));
    return value;
}
// The native writer fans each serialized snapshot out to the full-race file
// and the short rewind ring. Only the ring may drop samples: skipping the
// serializer itself also drops full-replay physics history and causes judder.
unsigned char* RecorderOutput(void* recorder, bool fullOutput = false) {
    __try {
        const auto base=reinterpret_cast<unsigned>(GetModuleHandleW(nullptr));
        auto mux=static_cast<unsigned char*>(recorder)+0xa08;
        if(Field<unsigned>(mux,0)!=base+0xf61568)return nullptr;
        const auto count=Field<unsigned>(mux,8);
        auto entries=Field<unsigned char*>(mux,12);
        if(!entries || count<1 || count>2)return nullptr;
        auto file=Field<unsigned char*>(recorder,0x1a4);
        unsigned char* ring=nullptr;unsigned char* full=nullptr;
        for(unsigned i=0;i<count;++i) {
            auto entry=entries+i*12;auto stream=Field<void*>(entry,0);
            if(entry[4]>1)return nullptr;
            if(stream==static_cast<unsigned char*>(recorder)+0x998 && !ring)ring=entry+4;
            else if(file && stream==file+0x90 && !full)full=entry+4;
            else return nullptr;
        }
        return fullOutput?full:ring;
    } __except(EXCEPTION_EXECUTE_HANDLER) {return nullptr;}
}
void __fastcall Serialize(void* recorder, void*, const double* dt) {
    auto full=disableFullRecording && recorder==activeRecorder ? RecorderOutput(recorder,true) : nullptr;
    const unsigned char fullEnabled=full?*full:0;
    if(full){*full=0;if(fullEnabled)++fullSuppressed;}
    __try {
        // Only the normal recording-state call is eligible. Preserve other serialization
        // paths and leave the simulation clock, event recording and playback untouched.
        if(sample60 && recorder==activeRecorder && _ReturnAddress()==recordingCaller &&
           !sampleClock.Record(Field<double>(recorder,0x1b8))) {
            if(auto enabled=RecorderOutput(recorder);enabled && *enabled) {
                *enabled=0;
                __try {serialize(recorder,dt);}
                __finally {*enabled=1;}
                ++skipped;++recorded;return;
            }
            // Unknown/changed layouts retain native recording rather than risk
            // dropping the full replay or changing an unrelated stream.
        }
        serialize(recorder, dt);
        ++recorded;
    } __finally {if(full)*full=fullEnabled;}
}
void __fastcall Update(void* root, void*, const double* dt) {
    if(samplingOwner!=root || Field<int>(root,0xa60)!=0) sampleClock.Reset();
    samplingOwner=root;
    auto previousRecorder=activeRecorder;
    activeRecorder=static_cast<unsigned char*>(root)+0x10;
    if(ReplayCamerasEnabled())BeginReplayCameraFrame();
    ReplayStorageUpdate(update, root, dt);
    if(ReplayCamerasEnabled()) {
        std::lock_guard lock(playbackMutex);
        playback={Field<int>(root,0xa60),playback.frame+1,GetTickCount64(),
            Field<double>(static_cast<unsigned char*>(root)+0x10,0x1b8),dt?*dt:0.0};
    }
    liveRecordingTick.store(Field<int>(root,0xa60)==0 ? GetTickCount64() : 0);
    activeRecorder=previousRecorder;
    if(!LoggingEnabled()) return;
    // Observe only after the original update has completed.
    static ULONGLONG last{};
    static unsigned lastRecorded{}, lastSkipped{}, ticks{};
    static int lastState=-1;
    static void* lastRoot{};
    const auto now=GetTickCount64();
    const int state=Field<int>(root,0xa60);
    ++ticks;
    if(root==lastRoot && state==lastState && now-last<1000) return;
    const auto count=recorded.load(), dropped=skipped.load();
    const auto inner=static_cast<unsigned char*>(root)+0x10;
    const auto ring=inner+0x998;
    Log("flashback: wall_ms=%llu root=%p state=%d previous=%d dt=%.6f elapsed_ms=%llu updates=%u records=%u skipped=%u time=%.6f end=%.6f range_a=%.6f range_b=%.6f ring_capacity=%u ring_write=%u ring_read=%u ring_frame=%u ring_time_a=%.6f ring_time_b=%.6f ring_time_c=%.6f",
        now,root,state,lastState,dt?*dt:0.0,last?now-last:0,ticks,count-lastRecorded,dropped-lastSkipped,
        Field<double>(inner,0x1b8),Field<double>(inner,0x1c0),
        Field<double>(inner,0xa28),Field<double>(inner,0xa30),
        Field<unsigned>(ring,0xc),Field<unsigned>(ring,0x10),Field<unsigned>(ring,0x14),Field<unsigned>(ring,0x18),
        Field<double>(ring,0x30),Field<double>(ring,0x38),Field<double>(ring,0x40));
    if(disableFullRecording)Log("replay recording: off; full-stream writes suppressed=%u",fullSuppressed.load());
    last=now; lastRecorded=count; lastSkipped=dropped; ticks=0; lastState=state; lastRoot=root;
}
}
ReplayPlayback ObservedReplayPlayback() {
    std::lock_guard lock(playbackMutex);return playback;
}
bool LiveDrivingCameraState() {
    const auto tick=liveRecordingTick.load();
    return tick && GetTickCount64()-tick<500;
}
bool EnableFlashback() {
    wchar_t enabled[8]{};
    sample60=GetEnvironmentVariableW(L"DIRT2VR_FLASHBACK60",enabled,8)==1 && enabled[0]==L'1';
    disableFullRecording=GetEnvironmentVariableW(L"DIRT2VR_RECORD_FULL_REPLAY",enabled,8)==1 && enabled[0]==L'0';
    const bool interceptSerialization=sample60 || disableFullRecording;
    if(!interceptSerialization && !ExtendedViewsEnabled() && !SteeringAnimationRequested() && !ChaseCameraRequested() && !ReplayCamerasEnabled() && !ReplayStorageRequested()) return true;
    if(update && (!interceptSerialization || serialize)) return true;
    if(!SupportedHost()) return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char updateGuard[]={0x83,0xec,0x10,0x56,0x8b,0xf1,0x80,0xbe,0xc8,0x0a,0,0,0};
    const unsigned char serializeGuard[]={0x83,0xec,0x08,0x55,0x56,0x8b,0xf1,0x8b,0x86,0x08,0x0a,0,0};
    auto u=base+0x93ab50, s=base+0x92e850;
    if(std::memcmp(u,updateGuard,sizeof(updateGuard)) || std::memcmp(s,serializeGuard,sizeof(serializeGuard))) return false;
    // The sampling gate must never apply to the alternate recording/resume path.
    const unsigned char recordingCall[]={0xe8,0x7d,0xf0,0xff,0xff};
    if(std::memcmp(base+0x92f7ce,recordingCall,sizeof(recordingCall))) return false;
    recordingCaller=base+0x92f7d3;
    if(!EnableGfwlCompatibility()) return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    status=MH_CreateHook(u,reinterpret_cast<void*>(Update),reinterpret_cast<void**>(&update));
    if(status==MH_OK && interceptSerialization) status=MH_CreateHook(s,reinterpret_cast<void*>(Serialize),reinterpret_cast<void**>(&serialize));
    if(status==MH_OK) status=EnableRecordedHook(u);
    if(status==MH_OK && interceptSerialization) status=EnableRecordedHook(s);
    Log("flashback: hook=%s; sample60=%d; full_recording=%d; sampling applies to rewind ring only",MH_StatusToString(status),sample60,!disableFullRecording);
    return status==MH_OK;
}
}
