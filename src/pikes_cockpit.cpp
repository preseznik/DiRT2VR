// Supported desktop Pikes layouts: increase world depth precision without changing
// the separate short-range cockpit projection or any car camera files.
#include "common.h"
#include "pikes_scope.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <intrin.h>
#include <cmath>
#include <cstring>
#include <set>
#include <mutex>
#include <tuple>

namespace vr {
namespace {
using Setup = void(__thiscall*)(void*, void*, void*, float, bool);
Setup setup{};
uintptr_t host{};
bool diagnostics{};

constexpr bool CorrectWorldRange(uintptr_t offset, float cameraNear, float nearPlane, float farPlane) {
    return offset == 0x5e0 && cameraNear > .07499f && cameraNear < .07501f &&
        nearPlane > 0 && nearPlane < .3f && farPlane > 100;
}
static_assert(CorrectWorldRange(0x5e0, .075f, .075f, 6000));
static_assert(!CorrectWorldRange(0x650, .075f, .075f, 10000)); // Cockpit depth.
static_assert(!CorrectWorldRange(0x650, .075f, .05f, 5)); // Interior.
static_assert(!CorrectWorldRange(0x6c0, .5f, .5f, 500)); // Mirror.
static_assert(!CorrectWorldRange(0x5e0, .2f, .2f, 6000)); // Chase/intro.
static_assert(!CorrectWorldRange(0x5e0, .075f, .3f, 6000));

void __fastcall WorldSetup(void* self, void*, void* context, void* camera, float farPlane, bool upload) {
    if (!self || !camera) {
        setup(self, context, camera, farPlane, upload);
        return;
    }
    auto renderer = static_cast<unsigned char*>(self);
    auto view = static_cast<unsigned char*>(camera);
    const auto offset = reinterpret_cast<uintptr_t>(camera) - reinterpret_cast<uintptr_t>(self);
    float original{}, cameraNear{};
    std::memcpy(&original, renderer + 0x40, sizeof(float));
    std::memcpy(&cameraNear, view + 0x54, sizeof(float));
    const bool world = CorrectWorldRange(offset, cameraNear, original, farPlane) && std::isfinite(farPlane);
    // This argument is FAR distance. The near distance comes from renderer+40.
    // Only +5e0 is eligible. +650 also supplies cockpit depth even when its
    // far range is 10000, so filtering only by the far distance is unsafe.
    if (world) {
        const float nearPlane = .3f;
        std::memcpy(renderer + 0x40, &nearPlane, sizeof(float));
    }
    setup(self, context, camera, farPlane, upload);
    if (world) std::memcpy(renderer + 0x40, &original, sizeof(float));

    if (diagnostics) {
        static std::mutex mutex;
        static std::set<std::tuple<unsigned, uintptr_t, bool, bool>> seen;
        std::lock_guard lock(mutex);
        const auto caller = static_cast<unsigned>(reinterpret_cast<uintptr_t>(_ReturnAddress()) - host);
        const auto key = std::make_tuple(caller, offset, cameraNear < .1f, farPlane > 100);
        if (seen.size() < 64 && seen.insert(key).second)
            Log("pikes-cockpit caller=%x camera-offset=%x camera-near=%.6f input-near=%.6f far=%.3f world-corrected=%d",
                caller, static_cast<unsigned>(offset), cameraNear, original, farPlane, world);
    }
}
}
void EnablePikesCockpit() {
    if (setup || !PikesScope() || !SupportedHost()) return;
    wchar_t value[8]{};
    if (GetEnvironmentVariableW(L"DIRT2VR_HEADSET", value, 8) == 1 && value[0] == L'1') return;
    if (GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_PRACTICE", value, 8) != 1 || value[0] != L'1') return;
    diagnostics = LoggingEnabled() && GetEnvironmentVariableW(L"DIRT2VR_CAPTURE_REQUESTS", value, 8) == 1 && value[0] == L'1';
    host = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    auto entry = reinterpret_cast<void*>(host + 0x330b70);
    const unsigned char guard[] = {0x55, 0x8b, 0xec, 0x83, 0xe4, 0xf0, 0x81, 0xec, 0xe4, 0, 0, 0};
    if (std::memcmp(entry, guard, sizeof(guard))) {
        Log("pikes-cockpit incompatible camera setup; unchanged");
        return;
    }
    auto status = MH_CreateHook(entry, reinterpret_cast<void*>(WorldSetup), reinterpret_cast<void**>(&setup));
    if (status == MH_OK) status = EnableRecordedHook(entry);
    Log("pikes-cockpit hook=%s world-near=0.3 interior/camera-files unchanged", MH_StatusToString(status));
}
}
