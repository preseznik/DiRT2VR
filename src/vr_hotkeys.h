#pragma once
#include <windows.h>

namespace vr {
enum Hotkey : unsigned { ToggleScreen=1, Recenter=2 };
// One process-owned game window. Unrelated messages stay on its original chain.
bool AttachHotkeys(HWND window);
unsigned ConsumeHotkeys();
}
