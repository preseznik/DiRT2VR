#pragma once
#include "camera_math.h"
#include "xr_frames.h"
#include "seat_state.h"
#include <string>

struct IDXGISwapChain;
namespace vr {
bool EnableSeatAdjustment();
bool DesktopSeatRequested();
void SeatDesktopFrame(IDXGISwapChain* swapchain);
void SeatSelectCamera(void* manager);
void SeatInactive();
void SeatPrepare(const std::array<XrView,2>& views);
XrPosef SeatEyePose(XrPosef relative);
XrPosef SeatHudPose(XrPosef pose,const XrPosef& reference);
XrFrames::Overlay* SeatOverlay();
bool SeatWindowKey(UINT message,WPARAM key);
bool SeatBlocksKey(unsigned virtualKey);
bool SeatInputReady();
bool EnableSeatInput();
void SeatFilterController(const GUID& device,bool keyboard,unsigned offset,unsigned type,void* value,unsigned bytes);
void SeatFilterXbox(unsigned slot,WORD& buttons);
}
