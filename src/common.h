#pragma once
#include <windows.h>
#include <string>
#include <filesystem>
#include <fstream>
namespace vr {
bool LoggingEnabled();
std::ofstream TraceFile(const std::filesystem::path& path, std::ios::openmode mode=std::ios::out);
std::filesystem::path Output();
void Log(const char* format, ...);
bool SupportedHost();
}
