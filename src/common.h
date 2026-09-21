#pragma once
#include <windows.h>
#include <string>
#include <filesystem>
namespace vr {
std::filesystem::path Output();
void Log(const char* format, ...);
bool SupportedHost();
}
