#pragma once
#include <cstddef>
#include <cstdint>

// Original x86 DiRT 2 only. Static contracts, not an executable adapter.
// See findings.md and manifest.json. Every address is a preferred VA.
namespace native_event_contract {
inline constexpr bool ExecutionEnabled = false;
inline constexpr std::uint32_t PreferredBase = 0x00400000;
inline constexpr std::uint32_t EventVtable = 0x013366ec;
inline constexpr std::uint32_t RoundVtable = 0x01333840;
inline constexpr std::uint32_t EventType = 0x014906b0;
inline constexpr std::uint32_t EventMappingType = 0x014907d4;
inline constexpr std::uint32_t LocalPlayerType = 0x01490784;
inline constexpr std::uint32_t SelectedLiveryType = 0x01490730;
inline constexpr std::uint32_t NetworkEventOffset = 0x2244;
inline constexpr std::uint32_t FirstRoundOffset = 0xa4;
inline constexpr std::uint32_t RoundStride = 0x24;

struct DatabaseView { std::uint32_t opaque[2]; };
struct VehicleRecord {
    std::uint32_t context;
    DatabaseView vehicle, model, vehicleClass;
};
struct LiveryRecord { std::uint32_t context; DatabaseView livery; };
struct SessionContext { std::uint32_t context, containerIndex; };
// Stock stack object uses table12F0B30 and calls503E20(context,1).
struct LocalPlayerContext {
    std::uint32_t vtable, context, containerIndex;
};
static_assert(sizeof(DatabaseView) == 8);
static_assert(sizeof(VehicleRecord) == 0x1c);
static_assert(offsetof(VehicleRecord, vehicleClass) == 0x14);
static_assert(sizeof(LiveryRecord) == 0xc);
static_assert(sizeof(SessionContext) == 8);
static_assert(sizeof(LocalPlayerContext) == 0xc);

inline constexpr std::uint32_t CanEditEvent = 0x0084b130;
inline constexpr std::uint32_t ResetRoundDefaults = 0x0084a8c0;
inline constexpr std::uint32_t SetRoundCount = 0x00854640;
inline constexpr std::uint32_t SetEventRace = 0x00854680;
inline constexpr std::uint32_t SetTeams = 0x008546c0;
inline constexpr std::uint32_t SetVehicleClass = 0x00854700;
inline constexpr std::uint32_t SetRoundRace = 0x005f94c0;
inline constexpr std::uint32_t SetTrackModel = 0x005f94e0;
inline constexpr std::uint32_t SetLocation = 0x005f9500;
inline constexpr std::uint32_t SetPhysicalLaps = 0x005f9520;
inline constexpr std::uint32_t SetCollision = 0x005f9540;
inline constexpr std::uint32_t SetTimedValue = 0x005f9560;
inline constexpr std::uint32_t SetStaggered = 0x005f2870;
inline constexpr std::uint32_t NotifyEventChanged = 0x0083f710;
inline constexpr std::uint32_t GetSession = 0x00823130;
inline constexpr std::uint32_t StockHostPredicate = 0x00822420;
inline constexpr std::uint32_t ResolveSessionEvent = 0x005360c0;
inline constexpr std::uint32_t CopyEvent = 0x00854ae0;
inline constexpr std::uint32_t InitializeSessionContext = 0x00562520;
inline constexpr std::uint32_t StockCopyBlocked = 0x005791b0;
inline constexpr std::uint32_t ResolveVehicle = 0x0050df80;
inline constexpr std::uint32_t ResolveProfileLivery = 0x0050e4b0;
inline constexpr std::uint32_t ResolveLivery = 0x0050e850;
inline constexpr std::uint32_t InitializeLocalPlayerContext = 0x00503e20;
inline constexpr std::uint32_t SetLocalPlayerLivery = 0x0051bc00;

#if defined(_MSC_VER) && defined(_M_IX86)
static_assert(sizeof(void*) == 4);
using CanEditEventFn = bool (__thiscall*)(void* event);
using ResetRoundDefaultsFn = void (__thiscall*)(void* event, bool reset, int race, int value4);
using SetEventIntFn = void (__thiscall*)(void* event, int value, bool notify);
using SetRoundIntFn = void (__thiscall*)(void* round, int value);
using SetStaggeredFn = void (__thiscall*)(void* round, bool enabled);
using NotifyEventChangedFn = void (__thiscall*)(void* event);
using GetSessionFn = void* (__thiscall*)(void* network, int containerIndex);
// this = network+1160, not network. Only AL is meaningful.
using StockHostPredicateFn = bool (__thiscall*)(void* networkSessions, int containerIndex);
using ResolveSessionEventFn = void* (__thiscall*)(void* sessionComponents);
using InitializeSessionContextFn = bool (__thiscall*)(SessionContext*, void* context, int containerIndex);
using StockCopyBlockedFn = bool (__thiscall*)(SessionContext*);
// Native serialize/deserialize+notification. It does not gate host ownership itself.
using CopyEventFn = void (__thiscall*)(void* destination, const void* source);
using ResolveVehicleFn = bool (__thiscall*)(VehicleRecord*, void* context, int vehicleId);
using ResolveProfileLiveryFn = int (__thiscall*)(VehicleRecord*);
using ResolveLiveryFn = bool (__thiscall*)(LiveryRecord*, void* context, int liveryId);
using InitializeLocalPlayerContextFn = bool (__thiscall*)(LocalPlayerContext*, void* context, int containerIndex);
// Livery ID, never vehicle row ID; missing session/components silently no-op.
using SetLocalPlayerLiveryFn = void (__thiscall*)(LocalPlayerContext*, int liveryId);
#endif
}
