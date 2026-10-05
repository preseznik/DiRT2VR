#include "race_difficulty.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <atomic>
#include <cstring>
#include <intrin.h>

namespace vr {
namespace {
int selectedDifficulty{};
std::atomic<unsigned> reads{};
int __fastcall Difficulty(void*, void*) {
    // This is the event difficulty reader used by the game's own AI time-factor
    // and flashback tables. Never write the selected career or its settings.
    const auto count = reads.fetch_add(1, std::memory_order_relaxed);
    if (count < 8) Log("direct race: difficulty=%d read=%u caller=%p", selectedDifficulty, count + 1, _ReturnAddress());
    return selectedDifficulty;
}
}
bool EnableRaceDifficulty() {
    wchar_t value[8]{};
    const auto length = GetEnvironmentVariableW(L"DIRT2VR_RACE_DIFFICULTY", value, 8);
    if (!length) return true;
    if (length != 1 || value[0] < L'0' || value[0] > L'5' || !SupportedHost()) return false;
    auto base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char prologue[] = {0x56,0x8b,0xf1,0x8b,0x46,0x0c,0x57,0x83,0xcf,0xff,0x85,0xc0};
    if (memcmp(base + 0x2db560, prologue, sizeof(prologue))) return false;
    auto result = MH_Initialize();
    if (result != MH_OK && result != MH_ERROR_ALREADY_INITIALIZED) return false;
    selectedDifficulty = value[0] - L'0';
    result = MH_CreateHook(base + 0x2db560, reinterpret_cast<void*>(Difficulty), nullptr);
    if (result == MH_OK) result = EnableRecordedHook(base + 0x2db560);
    Log("direct race: difficulty override=%d hook=%s (memory only)", selectedDifficulty, MH_StatusToString(result));
    return result == MH_OK;
}
}
