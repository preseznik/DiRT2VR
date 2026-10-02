#include "startup_hooks.h"
#include "draw_distance.h"
#include "gfwl_compat.h"
#include "driving_controls.h"
#include "cockpit_start.h"
#include "scene_camera.h"
#include "flashback.h"
#include "steering_animation.h"
namespace vr { bool EnableSeatAdjustment(); }
#include "common.h"

namespace vr {
bool EnableStartupHooks(bool headset, bool cockpit, bool direct) {
    // Direct desktop sessions also edit game code, even without driving overrides.
    if((headset || cockpit || direct) && !EnableGfwlCompatibility()) {
        Log("GFWL compatibility: initialization failed; stopping launch"); return false;
    }
    if(!EnableFlashback()) { Log("flashback: incompatible process"); return false; }
    if(!EnableDrivingControls()) { Log("driving controls: incompatible process"); return false; }
    if(cockpit && !EnableCockpitStart()) { Log("VR starting camera: incompatible process"); return false; }
    if(headset && !EnableDrawDistance()) { Log("VR draw distance: incompatible process"); return false; }
    if(headset && !EnableSteeringAnimation()) Log("steering animation: unavailable; original visuals retained");
    if(headset && !EnableSeatAdjustment()) Log("seat adjustment: unavailable; existing VR remains active");
    if((headset || cockpit) && !EnableSceneCameraObserver()) Log("extended views: observer unavailable; using existing views");
    return true;
}
}
