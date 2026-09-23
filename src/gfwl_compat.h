#pragma once
#include <MinHook.h>
namespace vr {
// Candidate compatibility for the exact Microsoft 3.5.95.0 DLL. Memory only.
bool EnableGfwlCompatibility();
MH_STATUS EnableRecordedHook(void* target);
void RecordCodeByte(void* target,unsigned char before,unsigned char after);
}
