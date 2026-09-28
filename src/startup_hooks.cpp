#include "startup_hooks.h"
#include "gfwl_compat.h"
#include "driving_controls.h"
#include "cockpit_start.h"
#include "flashback.h"
namespace vr { bool EnableSeatAdjustment(); }
#include "common.h"

namespace vr {
bool EnableStartupHooks(bool headset, bool cockpit) {
    // Recording must precede every VR code edit, even without driving overrides.
    if((headset || cockpit) && !EnableGfwlCompatibility()) {
        Log("GFWL compatibility: initialization failed; stopping VR launch"); return false;
    }
    if(!EnableFlashback()) { Log("flashback: incompatible process"); return false; }
    if(!EnableDrivingControls()) { Log("driving controls: incompatible process"); return false; }
    if(cockpit && !EnableCockpitStart()) { Log("VR starting camera: incompatible process"); return false; }
    if(headset && !EnableSeatAdjustment()) Log("seat adjustment: unavailable; existing VR remains active");
    return true;
}
}
