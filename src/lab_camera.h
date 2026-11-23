#pragma once

namespace vr {
// Lab only (diagnostic logging with DIRT2VR_CAPTURE_REQUESTS=1): while camera.request exists in the
// session's log folder, the displayed view is drawn from the pose it holds, so scripted captures
// (capture.request) can look at a track from anywhere without driving or keyboard input. One line:
//   x y z yaw pitch [fov]           metres (game world), degrees; yaw 0 looks along +Z, 90 along +X;
//                                   pitch positive looks up; fov vertical, default the game's own
//   x y z look tx ty tz [fov]       look from (x,y,z) at (tx,ty,tz)
// Deleting the file gives the view back. Read every 250 ms from Present.
void LabCameraPoll();
bool LabCameraActive();
// From the camera export hook: the requested view instead of the displayed camera's.
float* LabCameraExport(void* owner,const float* native);
// The renderer's camera is the requested view.
bool MatchingLabCamera(const float* a,const float* b);
}
