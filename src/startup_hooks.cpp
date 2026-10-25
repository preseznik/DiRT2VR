#include "startup_hooks.h"
#include "event_diagnostics.h"
#include "draw_distance.h"
#include "gfwl_compat.h"
#include "driving_controls.h"
#include "transmission.h"
#include "f04_audio.h"
#include "cockpit_start.h"
#include "scene_camera.h"
#include "flashback.h"
#include "steering_animation.h"
#include "chase_camera.h"
#include "replay_camera.h"
#include "test_message.h"
#include "background_play.h"
#include "photo_mode.h"
namespace vr { bool EnableSeatAdjustment(); }
#include "common.h"

namespace vr {
bool EnableStartupHooks(bool headset, bool cockpit, bool direct) {
    const bool steering=SteeringAnimationRequested();
    const bool chase=ChaseCameraRequested();
    const bool replayProbe=ReplayCamerasEnabled();
    const bool photo=PhotoModeRequested();
    // Direct desktop sessions also edit game code, even without driving overrides.
    if((headset || cockpit || direct || steering || chase || replayProbe || photo) && !EnableGfwlCompatibility()) {
        Log("GFWL compatibility: initialization failed; stopping launch"); return false;
    }
    if(!EnableFlashback()) { Log("flashback: incompatible process"); return false; }
    if(!EnableBackgroundPlay()) { Log("background play: incompatible process"); return false; }
    if(!EnableDrivingControls()) { Log("driving controls: incompatible process"); return false; }
    if(!EnableF04Audio()) { Log("f04 audio: incompatible process"); return false; }
    if(!EnableTransmission()) { Log("transmission: incompatible process"); return false; }
    const bool playerCameraReady=!(cockpit || steering || chase || replayProbe || photo) || EnableCockpitStart(cockpit);
    if(cockpit && !playerCameraReady) { Log("VR starting camera: incompatible process"); return false; }
    if(headset && !EnableDrawDistance()) { Log("VR draw distance: incompatible process"); return false; }
    const bool observerReady=!(headset || cockpit || steering || chase || replayProbe || photo) || EnableSceneCameraObserver(steering || chase || replayProbe || photo);
    if(!observerReady) Log("camera observer: unavailable; original steering visuals retained");
    if(steering && (!playerCameraReady || !observerReady || !EnableSteeringAnimation(headset))) Log("steering animation: unavailable; original visuals retained");
    const bool chaseReady=!chase || (playerCameraReady && observerReady && EnableChaseCamera());
    if(!chaseReady)Log("chase camera: unavailable; native view retained");
    if(photo && (!chaseReady || !playerCameraReady || !observerReady || !EnablePhotoMode())) {Log("photo mode: incompatible process");return false;}
    if(headset && !EnableSeatAdjustment()) Log("seat adjustment: unavailable; existing VR remains active");
    if(headset && TestMessagesEnabled())Log("headset reply input ready=%d",EnableTestMessageInput());
    EnableEventDiagnostics();
    return true;
}
}
