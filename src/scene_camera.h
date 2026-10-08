#pragma once
#include <cstdint>
#include <cmath>
#include <string_view>
#include <utility>
namespace vr {
enum class SceneCamera { Unknown, Cockpit, External, Replay, Frontend };
inline SceneCamera NamedSceneCamera(std::string_view name) {
    if(name=="head-cam")return SceneCamera::Cockpit;
    for(auto n:{"bonnet","bonnet_reverse","bumper","bumper_reverse","chase_close","chase_close_reverse","chase_far","chase_far_reverse","head-cam_reverse","drift_cam_rear"})
        if(name==n)return SceneCamera::External;
    if(name.starts_with("replay_"))return SceneCamera::Replay;
    return SceneCamera::Unknown;
}
inline bool MatchingSceneCamera(const float* a,const float* b) {
    if(!a || !b)return false;
    for(const auto c:{a,b}) {
        if(!std::isfinite(c[20]) || c[20]<=0 || c[20]>=3.14f ||
           !std::isfinite(c[21]) || c[21]<=0 || c[21]>=10 ||
           !std::isfinite(c[22]) || c[22]<=c[21] || c[22]>100000)return false;
        for(unsigned start:{4u,8u,12u}) {
            const float length=c[start]*c[start]+c[start+1]*c[start+1]+c[start+2]*c[start+2];
            if(!std::isfinite(length) || std::abs(length-1.f)>.02f)return false;
        }
        for(auto pair:{std::pair{4u,8u},std::pair{4u,12u},std::pair{8u,12u}}) {
            float dot=0;for(unsigned i=0;i<3;++i)dot+=c[pair.first+i]*c[pair.second+i];
            if(std::abs(dot)>.02f)return false;
        }
    }
    // Ignore padding and far distance: the VR visibility hook deliberately expands it.
    for(unsigned i:{4u,5u,6u,8u,9u,10u,12u,13u,14u,16u,17u,18u,20u,21u})
        if(!std::isfinite(a[i]) || !std::isfinite(b[i]) || std::abs(a[i]-b[i])>.005f)return false;
    return true;
}
inline bool StereoCameraAllowed(SceneCamera camera,bool extended,bool paused,bool ready) {
    return !paused && ready && (camera==SceneCamera::Cockpit || (extended && camera==SceneCamera::External));
}
bool LiveDrivingCameraState();
bool ExtendedViewsEnabled();
bool EnableSceneCameraObserver(bool required=false);
SceneCamera ObservedSceneCamera(void* manager);
void* ObservedCameraObject(void* manager);
SceneCamera IdentifySceneCamera(const float* a,const float* b,uint64_t frame);
}
