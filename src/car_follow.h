#pragma once
#include "car_tracker.h"
#include <array>
#include <dxgi.h>

namespace vr {
// A chase view behind a tracked car, in the engine's camera record layout (up, left,
// forward, position rows; projection fields kept from `native`). False when unusable.
bool CarChaseRecord(const TrackedCar& car,const float* native,std::array<float,28>& out,float behind=6.5f,float above=2.2f);
// Replays (desktop and VR): F5/F6 follow the previous/next car on screen with our own chase
// camera; past the last car the replay's own cameras return. Each Present.
void CarFollowPresent(IDXGISwapChain* swapchain);
// From the camera export hook: our chase view instead of the replay camera being shown.
float* CarFollowExport(void* owner,const float* native);
// The renderer's camera is the followed car's chase view (so VR shows it like a replay chase view).
bool MatchingFollowCamera(const float* a,const float* b);
}
