#pragma once
#include <windows.h>

// OpenXR names the graphics adapter a session must use. SteamVR's virtual test
// headset (its null driver) names none: an all-zero LUID. Accept any adapter
// only then; every runtime that names one still needs that exact adapter.
inline bool AnyAdapter(const LUID& required) { return required.LowPart==0 && required.HighPart==0; }
inline bool RequiredAdapter(const LUID& required,const LUID& adapter) {
    return AnyAdapter(required) || (required.LowPart==adapter.LowPart && required.HighPart==adapter.HighPart);
}
