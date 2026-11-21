#pragma once
#include <windows.h>
#include <cstdint>
#include <cstddef>

namespace vr::bridge {
constexpr uint32_t Magic=0x42325244; // DR2B, deliberately not a DLSS compatibility API.
constexpr uint32_t Version=3; // 2: Neural Rendering settings block; 3: resolution and foveation.
enum class Status : LONG { Starting, Ready, Failed, Stopped };
// DLSS 5 Neural Rendering look, read by the helper before each frame.
struct NeuralSettings {
    int32_t style{1};      // NGX DLSSNR.Style: 1 Natural, 0 Standard, 2 Cinematic (changing it recreates the feature).
    int32_t passes{1};     // 1..3; later passes take the previous pass's output.
    float intensity{1};    // 0..2, first pass.
    float tone{1};         // Local tone, 0..2, first pass (later passes use 0).
    float structure{1};    // Local detail, 0..2, first pass (later passes cap it at 1).
    float mix{1};          // 0..1 blend from the original frame to the processed one.
    int32_t resolution{100}; // 50..100: processing resolution in percent (of the edges when foveated).
    int32_t foveation{0};    // 0 off, or 30..80: centre width/height in percent, processed at full resolution.
};

// Fixed-width, pointer-free ABI shared by the Win32 proxy and x64 helper.
// Auto-reset request/completion events transfer ownership of this record.
struct alignas(8) Shared {
    uint32_t magic{Magic}, version{Version}, bytes{sizeof(Shared)}, parent{};
    LUID adapter{};
    uint32_t width{}, height{}, format{}, reserved{};
    uint64_t inputHandle{}, outputHandle{}, requested{}, completed{};
    volatile LONG status{static_cast<LONG>(Status::Starting)};
    LONG error{};
    NeuralSettings neural{};
};
static_assert(sizeof(Shared)==112 && offsetof(Shared,requested)==56 && offsetof(Shared,neural)==80);
// The same rules the helper applies, shared so both sides agree.
inline NeuralSettings Clamped(NeuralSettings s) {
    const auto clamp=[](float v,float lo,float hi){return v!=v?lo:v<lo?lo:v>hi?hi:v;};
    s.style=s.style==0||s.style==2?s.style:1;
    s.passes=s.passes<1?1:s.passes>3?3:s.passes;
    s.intensity=clamp(s.intensity,0,2);s.tone=clamp(s.tone,0,2);s.structure=clamp(s.structure,0,2);s.mix=clamp(s.mix,0,1);
    s.resolution=s.resolution<50?50:s.resolution>100?100:s.resolution;
    s.foveation=s.foveation<=0?0:s.foveation<30?30:s.foveation>80?80:s.foveation;
    return s;
}
inline bool Valid(const Shared& s) {
    return s.magic==Magic && s.version==Version && s.bytes==sizeof(Shared) &&
        s.width && s.height && s.width<=8192 && s.height<=8192 &&
        s.inputHandle && s.outputHandle;
}
}
