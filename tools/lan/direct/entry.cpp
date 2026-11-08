#include <windows.h>
#include <cstdio>
#include <cstdint>

extern void DirectLabPrepareLan();
extern "C" DWORD WINAPI StartBootstrap(void*);
extern "C" const unsigned char direct_peer_windows[3][130] = {
#include "direct-peer-bytes.inc"
};

static FILE* OpenResult() {
    wchar_t path[32768]{};
    const auto length = GetEnvironmentVariableW(L"DIRT2VR_OUTPUT", path, _countof(path));
    if (!length || length >= _countof(path) || wcscat_s(path, L"\\startup.txt")) return nullptr;
    FILE* file{};
    return _wfopen_s(&file, path, L"a") == 0 ? file : nullptr;
}

namespace trace {
void ReportMismatch(const void* address, const void* expected, size_t length) {
    FILE* file = OpenResult();
    if (!file) return;
    MEMORY_BASIC_INFORMATION info{};
    VirtualQuery(address, &info, sizeof(info));
    const auto base = reinterpret_cast<uintptr_t>(info.AllocationBase);
    const char* module = info.AllocationBase == GetModuleHandleW(L"xlive.dll") ? "xlive.dll" : "game";
    fprintf(file, "Mismatch at %s+%08x, length %zu, expected/actual:", module,
            static_cast<unsigned>(reinterpret_cast<uintptr_t>(address) - base), length);
    for (size_t i = 0; i < length && i < 24; ++i)
        fprintf(file, " %02x/%02x", reinterpret_cast<const unsigned char*>(expected)[i],
                                    reinterpret_cast<const unsigned char*>(address)[i]);
    fprintf(file, "\n");
    fclose(file);
}
}

void DiRT2VRLanInitialize() {
    // This is XLLN's game-entry callback, after import resolution and outside DllMain.
    DirectLabPrepareLan();
    DWORD result = StartBootstrap(nullptr);
    if (auto* file = OpenResult()) {
        fprintf(file, "Direct bootstrap returned %lu\n", result);
        fclose(file);
    }
    if (result != ERROR_SUCCESS) {
        ExitProcess(ERROR_INVALID_DATA);
    }
}
