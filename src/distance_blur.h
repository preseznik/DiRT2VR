#pragma once
struct IDXGISwapChain;

namespace vr {
// Distance blur while driving (desktop, opt-in: DIRT2VR_DISTANCE_BLUR=1 slight, 2 medium, 3 strong). Far scenery
// and the sky soften with distance (the Photo Mode depth-of-field pass in its far-only mode, on the scene depth
// copied from the shadow-mask pass); the road, cars and the near landscape stay sharp. The HUD is captured
// apart and drawn back on top (as for live neural rendering), so it is never blurred.
bool DistanceBlurRequested();
// Each desktop Present, before the HUD goes back on top; not while Photo Mode is active (it has its own blur).
void DistanceBlurApply(IDXGISwapChain* swapchain);
}
