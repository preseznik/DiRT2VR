#include <windows.h>
#include "diagnostics.h"
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <string>

namespace {
SRWLOCK lock = SRWLOCK_INIT;
std::atomic<bool> enabled{false};
HANDLE file = INVALID_HANDLE_VALUE;
std::wstring currentPath, previousPath;
DWORD written = 0;
#ifndef DIRT2VR_LAN_LOG_LIMIT
constexpr DWORD limit = 4 * 1024 * 1024;
#else
constexpr DWORD limit = DIRT2VR_LAN_LOG_LIMIT;
#endif
bool NetworkFunction(const char* function) {
    const char* prefixes[] = {"XSocket", "XWSA", "XNet", "XLiveInitialize", "XLiveUninitialize",
        "XllnThread", "Thread", "ParseNetworkData", "SendPacket", "XllnNetEntity", "SocketMain",
        "BindTitleSocket", "ImplicitlyBindTitleSocket", "ShutdownTitleSocket", "DiRT2VRLanLog"};
    for (auto prefix : prefixes) if (strncmp(function, prefix, strlen(prefix)) == 0) return true;
    return false;
}
void Close() {
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    file = INVALID_HANDLE_VALUE;
}
}

bool DiRT2VRLanLogEnabled() { return enabled.load(std::memory_order_relaxed); }
void DiRT2VRLanLogPacket(const char* function, uintptr_t socket, const void* data, size_t size, const void* caller) {
    if (!DiRT2VRLanLogEnabled()) return;
    const DWORD savedError = GetLastError();
    uint32_t hash = 2166136261u;
    const auto* bytes = static_cast<const unsigned char*>(data);
    for (size_t i = 0; i < size; ++i) hash = (hash ^ bytes[i]) * 16777619u;
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    DiRT2VRLanLog(4, 0, function, "title boundary socket=%zx bytes=%zu checksum=%08x caller_rva=%zx", socket, size, hash, reinterpret_cast<uintptr_t>(caller) - base);
    SetLastError(savedError);
}
void DiRT2VRLanLogClose(uintptr_t socket, const void* caller) {
    if (!DiRT2VRLanLogEnabled()) return;
    const DWORD savedError = GetLastError();
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    DiRT2VRLanLog(4, 0, "XSocketClose", "close caller socket=%zx caller_rva=%zx", socket, reinterpret_cast<uintptr_t>(caller) - base);
    void* frames[12]{};
    const auto count = CaptureStackBackTrace(0, 12, frames, nullptr);
    for (USHORT i = 0; i < count; ++i) {
        HMODULE module = nullptr;
        if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, reinterpret_cast<LPCWSTR>(frames[i]), &module) && reinterpret_cast<uintptr_t>(module) == base)
            DiRT2VRLanLog(4, 0, "XSocketClose", "close stack socket=%zx frame=%u game_rva=%zx", socket, i, reinterpret_cast<uintptr_t>(frames[i]) - base);
    }
    SetLastError(savedError);
}
void DiRT2VRLanLogStart() {
    const DWORD savedError = GetLastError();
    AcquireSRWLockExclusive(&lock);
    if (!enabled.load()) {
        wchar_t setting[4]{}, folder[32768]{};
        if (GetEnvironmentVariableW(L"DIRT2VR_LOGGING", setting, 4) == 1 && setting[0] == L'1') {
            const DWORD length = GetEnvironmentVariableW(L"DIRT2VR_OUTPUT", folder, 32768);
            try {
                if (length && length < 32768 && std::filesystem::path(folder).is_absolute()) {
                    // The launcher owns/creates this session directory. Never create one when opted out.
                    currentPath = (std::filesystem::path(folder) / L"lan-network.log").wstring();
                    previousPath = (std::filesystem::path(folder) / L"lan-network.previous.log").wstring();
                    file = CreateFileW(currentPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
                    written = 0;
                    enabled.store(file != INVALID_HANDLE_VALUE);
                }
            } catch (...) { Close(); } // Diagnostics must not prevent a game session.
        }
    }
    ReleaseSRWLockExclusive(&lock);
    DiRT2VRLanLog(4, 0, "DiRT2VRLanLogStart", "Network trace enabled; two files capped at %lu bytes each; no packet payloads.", limit);
    SetLastError(savedError);
}
void DiRT2VRLanLogStop() {
    const DWORD savedError = GetLastError();
    DiRT2VRLanLog(4, 0, "DiRT2VRLanLogStop", "Network trace stopped.");
    AcquireSRWLockExclusive(&lock);
    enabled.store(false);
    Close();
    ReleaseSRWLockExclusive(&lock);
    SetLastError(savedError);
}
void DiRT2VRLanLog(uint32_t level, uint32_t error, const char* function, const char* format, ...) {
    if (!enabled.load(std::memory_order_relaxed) || !(level & 0x3e) || !NetworkFunction(function)) return;
    const DWORD savedError = GetLastError(); // Includes Winsock's thread-local error; logging must preserve it.
    char message[2048]{};
    va_list args;
    va_start(args, format);
    _vsnprintf_s(message, sizeof(message), _TRUNCATE, format, args);
    va_end(args);
    FILETIME utc{}; GetSystemTimeAsFileTime(&utc);
    ULARGE_INTEGER time{}; time.LowPart = utc.dwLowDateTime; time.HighPart = utc.dwHighDateTime;
    char line[2304]{};
    _snprintf_s(line, sizeof(line), _TRUNCATE, "utc_ms=%llu tick_ms=%llu pid=%lu tid=%lu level=%x error=%u %s: %s\r\n",
        time.QuadPart / 10000 - 11644473600000ULL, GetTickCount64(), GetCurrentProcessId(), GetCurrentThreadId(), level, error, function, message);
    const DWORD bytes = static_cast<DWORD>(strlen(line));
    AcquireSRWLockExclusive(&lock);
    if (file != INVALID_HANDLE_VALUE) {
        if (written + bytes > limit) {
            Close();
            // Rotate only our two fixed filenames within this one session's log directory.
            if (MoveFileExW(currentPath.c_str(), previousPath.c_str(), MOVEFILE_REPLACE_EXISTING))
                file = CreateFileW(currentPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            written = 0;
        }
        if (file != INVALID_HANDLE_VALUE) {
            DWORD actual = 0;
            if (!WriteFile(file, line, bytes, &actual, nullptr) || actual != bytes) Close();
            else written += actual;
        }
        if (file == INVALID_HANDLE_VALUE) enabled.store(false);
    }
    ReleaseSRWLockExclusive(&lock);
    SetLastError(savedError);
}
