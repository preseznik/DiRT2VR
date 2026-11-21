#pragma once
#include <windows.h>

namespace vr {
// Called by the chase update; all cursor changes run on the window's thread.
void UpdateChaseCursor(HWND window,bool active);
void UpdatePhotoCursor(HWND window,bool active);
// Wheel delta (WHEEL_DELTA per notch) received by the game window during Photo Mode since the last call.
int TakePhotoWheel();
}
