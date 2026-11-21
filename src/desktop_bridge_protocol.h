#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>

namespace vr::bridge {
constexpr uint32_t Magic=0x42325244; // DR2B, deliberately not a DLSS compatibility API.
constexpr uint32_t Version=1;
enum class Status : LONG { Starting, Ready, Failed, Stopped };

// Fixed-width, pointer-free ABI shared by the Win32 proxy and x64 helper.
// Auto-reset request/completion events transfer ownership of this record.
struct alignas(8) Shared {
    uint32_t magic{Magic}, version{Version}, bytes{sizeof(Shared)}, parent{};
    LUID adapter{};
    uint32_t width{}, height{}, format{}, reserved{};
    uint64_t inputHandle{}, outputHandle{}, requested{}, completed{};
    volatile LONG status{static_cast<LONG>(Status::Starting)};
    LONG error{};
};
static_assert(sizeof(Shared)==80 && offsetof(Shared,requested)==56);
inline bool Valid(const Shared& s) {
    return s.magic==Magic && s.version==Version && s.bytes==sizeof(Shared) &&
        s.width && s.height && s.width<=8192 && s.height<=8192 &&
        s.inputHandle && s.outputHandle;
}
}
