#pragma once
#include "common.h"
#include "replay_clock.h"
namespace vr {
// Development-only gates; orbit and stereo each require explicit opt-in.
inline bool ReplayCameraProbe() {
    static const bool enabled=[] {
        wchar_t value[8]{};
        return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_CAMERA_PROBE",value,8)==1 && value[0]==L'1';
    }();
    return enabled && LoggingEnabled();
}
inline bool ReplayOrbitProbe() {
    static const bool enabled=[] {
        wchar_t value[8]{};
        return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_ORBIT_PROBE",value,8)==1 && value[0]==L'1';
    }();
    return enabled && ReplayCameraProbe();
}
ReplayPlayback ObservedReplayPlayback();
void* ObservedDisplayCamera();
void BeginReplayCameraFrame();
void EndReplayCameraFrame();
inline bool ReplayStereoProbe() {
    static const bool enabled=[] {
        wchar_t value[8]{};
        return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_STEREO_PROBE",value,8)==1 && value[0]==L'1';
    }();
    return enabled && ReplayOrbitProbe();
}
bool MatchingReplayChaseCamera(const float* a,const float* b);
void ObserveReplayRenderCamera(void* a,void* b);
inline bool ReplayStereoEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_STEREO",value,8)==1 && value[0]==L'1';}();
    return enabled || ReplayStereoProbe();
}
inline bool ReplayCamerasEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_CAMERAS",value,8)==1 && value[0]==L'1';}();
    return enabled || ReplayStereoEnabled() || ReplayCameraProbe();
}
inline bool ReplayOrbitEnabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_CHASE_FREE_LOOK",value,8)==1 && value[0]==L'1';}();
    return (enabled && ReplayCamerasEnabled()) || ReplayOrbitProbe();
}
}
