#pragma once
#include <stdint.h>
void DiRT2VRLanLogStart();
void DiRT2VRLanLogStop();
bool DiRT2VRLanLogEnabled();
void DiRT2VRLanLog(uint32_t level, uint32_t error, const char* function, const char* format, ...);
