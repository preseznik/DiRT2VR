#include "intro.h"
#include <cstdint>
#include <cstring>
static_assert(sizeof(std::uintptr_t)==4,"DiRT 2 intro integration requires x86");

bool InstallLanIntroSkip(HMODULE host) {
    auto base=reinterpret_cast<std::uint8_t*>(host);
    if (!base) return false;
    // StateVideoPostStart occurs only once in the supported states.bin: first_race_video.
    // Register the engine's own zero-delay StateWayPoint factory for that state type.
    auto factory=base+0x284115;
    auto decision=base+0x225e73;
    const std::uint8_t decisionBytes[]{0x75,0x14,0x68};
    const auto originalFactory=reinterpret_cast<std::uintptr_t>(base+0x276050);
    const auto waypointFactory=reinterpret_cast<std::uintptr_t>(base+0x0db440);
    std::uintptr_t currentFactory{};
    std::memcpy(&currentFactory,factory+1,sizeof(currentFactory));
    // Verify both sites before writing either one. The caller also checks the executable hash.
    if (factory[0]!=0xbe || currentFactory!=originalFactory ||
        std::memcmp(decision,decisionBytes,sizeof(decisionBytes))) return false;
    DWORD factoryProtect{},decisionProtect{};
    if (!VirtualProtect(factory,5,PAGE_EXECUTE_READWRITE,&factoryProtect)) return false;
    if (!VirtualProtect(decision,1,PAGE_EXECUTE_READWRITE,&decisionProtect)) {
        DWORD ignored{}; VirtualProtect(factory,5,factoryProtect,&ignored); return false;
    }
    std::memcpy(factory+1,&waypointFactory,sizeof(waypointFactory));
    *decision=0xeb; // Select intro_shown, retaining the engine's normal transition machinery.
    DWORD ignored{};
    const bool restoredFactory=VirtualProtect(factory,5,factoryProtect,&ignored)!=FALSE;
    const bool restoredDecision=VirtualProtect(decision,1,decisionProtect,&ignored)!=FALSE;
    const bool flushed=FlushInstructionCache(GetCurrentProcess(),nullptr,0)!=FALSE;
    return restoredFactory && restoredDecision && flushed;
}
