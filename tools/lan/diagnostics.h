#pragma once
#include <stdint.h>
#include <stddef.h>
void DiRT2VRLanLogPacket(const char* function, uintptr_t socket, const void* data, size_t size, const void* caller);
void DiRT2VRLanLogClose(uintptr_t socket, const void* caller, const void* returnSlot = nullptr);
void DiRT2VRLanLogStart();
void DiRT2VRLanLogStop();
bool DiRT2VRLanLogEnabled();
void DiRT2VRLanLog(uint32_t level, uint32_t error, const char* function, const char* format, ...);
