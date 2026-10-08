#pragma once
#include <windows.h>

namespace vr {
// Called by the chase update; all cursor changes run on the window's thread.
void UpdateChaseCursor(HWND window,bool active);
}
