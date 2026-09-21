#pragma once
#include <windows.h>

namespace vr {
enum Hotkey : unsigned { ToggleScreen=1, Recenter=2 };
// One process-owned game window. Unrelated messages stay on its original chain.
bool AttachHotkeys(HWND window);
// Modifier mask: Ctrl=1, Alt=2, Shift=4. Call before attaching the window.
bool ConfigureHotkeys(unsigned toggleKey,unsigned toggleModifiers,unsigned recenterKey,unsigned recenterModifiers);
unsigned ConsumeHotkeys();
}
