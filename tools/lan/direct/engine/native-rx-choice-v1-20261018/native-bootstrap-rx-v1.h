#pragma once
#include "rx-request-v1.h"
#include "../direct-bootstrap/direct-laps.h"
#include <cstddef>
#include <cstdint>
#include <limits>
#include <type_traits>

// Offline engine ABI/translation only. No frontend state, menu object, native
// function resolution, process memory read/write, or executable entry is exposed.
// All VAs are preferred-image VAs for the SHA pinned in capture.py/findings.md.
namespace dirt2_engine_bootstrap {
using I32 = std::int32_t;
using U32 = std::uint32_t;
constexpr I32 Unknown = (std::numeric_limits<I32>::min)();

struct SessionDescriptor {             // 64FDB0 stable stack +38
    I32 type;                         // LAN=0, unranked/ranked=1
    I32 value04;                      // native frontend passes 1
    std::uint8_t ranked;              // LAN=0
    std::uint8_t event90IsZero;       // 5F99A0; meaning not established
    std::uint8_t padding[2];
    I32 reserved[3];                  // native frontend initializes to 0
};
struct EventAdvertisement {           // 64FDB0 stable stack +68
    I32 team;                         // event+6C
    I32 roundCount;                   // event+68
    I32 vehicleClass;                 // event+74
    I32 event78;
    I32 event8C;
    I32 event98;
    I32 race;                         // event+64; native -1 maps to 11
};
struct RoundAdvertisement {           // 64FDB0 stable stack +50
    I32 trackModel;                   // current round protected short+C
    I32 zero04;
    I32 event70;
    I32 collision;                    // current round+1C, NOT laps
    I32 event84;
    I32 event88;
};
struct HostDescriptors {
    SessionDescriptor session{};
    EventAdvertisement event{};
    RoundAdvertisement round{};
};
static_assert(sizeof(SessionDescriptor)==0x18);
static_assert(offsetof(SessionDescriptor,ranked)==8);
static_assert(offsetof(SessionDescriptor,event90IsZero)==9);
static_assert(offsetof(SessionDescriptor,reserved)==0xC);
static_assert(sizeof(EventAdvertisement)==0x1C);
static_assert(offsetof(EventAdvertisement,race)==0x18);
static_assert(sizeof(RoundAdvertisement)==0x18);
static_assert(offsetof(RoundAdvertisement,collision)==0xC);
static_assert(std::is_standard_layout_v<HostDescriptors>);
static_assert(std::is_trivially_copyable_v<HostDescriptors>);

constexpr U32 CreateVA=0x864A90;       // thiscall container; ret14h; result AL
constexpr U32 JoinVA=0x8510D0;         // thiscall container; ret18h; result AL
// 8511AC calls 8456E0; 8511B1 tests AL. Both branches reach 8511C6 without
// changing EAX (success only MOVs +7E1/+7E4); 636F8D consumes the returned AL.
constexpr U32 ContainerTableVA=0x13364EC;
constexpr U32 ContainerTypeGetterVA=0x8B46F0;
constexpr U32 ContainerDescriptorVA=0x1490684;
constexpr U32 ContainerHolderOffset=0x2450;
constexpr U32 ContainerHelperOffset=0x259C;
constexpr U32 HolderStateOffset=8;
constexpr U32 HolderSessionOffset=0x18;
constexpr U32 HolderTransportOffset=0x24;
#if defined(_MSC_VER) && defined(_M_IX86)
static_assert(sizeof(void*)==4,"The supported executable ABI is x86 only");
using CreateAbi = std::uint8_t(__thiscall*)(void*,const SessionDescriptor*,
    const EventAdvertisement*,const RoundAdvertisement*,const char*,const char*);
using JoinAbi = std::uint8_t(__thiscall*)(void*,const void*,I32,I32,const char*,I32,const char*);
#endif

// Caller prerequisites remain unresolved, so no function can arm or invoke the
// above ABI. A valid translation does NOT initialize the authoritative event,
// apply laps/location/per-player cars, bind a peer, or prove engine readiness.
constexpr bool ExecutionEnabled=false;
enum class ExecutionBlocker {
    NativeServiceOwnershipAndEventInitializationUnproven
};
constexpr ExecutionBlocker Blocker=ExecutionBlocker::NativeServiceOwnershipAndEventInitializationUnproven;
enum class NativeInitiation { Rejected, AcceptedPending };
constexpr NativeInitiation InterpretNativeAL(std::uint8_t al) {
    return al ? NativeInitiation::AcceptedPending : NativeInitiation::Rejected;
}

struct Manifest {
    // Native protected event enums. RX DB race_type row2/class row17 map to
    // native enums1/1; database row IDs must never be passed in these fields.
    I32 nativeRaceEnum=1,nativeClassEnum=1,rounds=1,location=4,trackModel=143,laps=3;
    bool joinCurrentRace=false;
};
// Decoded values from one exact native event/current-round identity. Unknown
// fields cannot be invented from launcher defaults. This is an offline input;
// there is intentionally no claim that receiving it proves runtime ownership.
struct NativeEventSnapshot {
    I32 race=Unknown,vehicleClass=Unknown,rounds=Unknown,currentRound=Unknown;
    I32 location=Unknown,trackModel=Unknown,laps=Unknown,collision=Unknown;
    I32 team=Unknown,event70=Unknown,event78=Unknown,event84=Unknown;
    I32 event88=Unknown,event8C=Unknown,event90=Unknown,event98=Unknown;
};
enum class PrepareResult {
    PreparedExecutionBlocked,UnsupportedManifest,CurrentRaceJoinUnproven,
    MissingNativeValues,EventMismatch,InvalidCurrentRound,InvalidCollision,NativeLanModeRejected
};

inline PrepareResult PrepareLanHost(const Manifest& m,const NativeEventSnapshot& n,HostDescriptors& out,
                                    const rx_choice_v1::Request* selection=nullptr) {
    out={};
    if(m.joinCurrentRace)return PrepareResult::CurrentRaceJoinUnproven;
    const bool matches=selection ? rx_choice_v1::Valid(*selection) && selection->race==m.nativeRaceEnum &&
        selection->vehicleClass==m.nativeClassEnum && selection->selected.model==m.trackModel && selection->selected.location==m.location :
        m.nativeRaceEnum==1 && m.nativeClassEnum==1 && rx_choice_v1::Supported(m.trackModel,m.location);
    if(!matches || m.rounds!=1 || !direct_laps::Valid(m.laps))
        return PrepareResult::UnsupportedManifest;
    const I32 required[]{n.race,n.vehicleClass,n.rounds,n.currentRound,n.location,n.trackModel,
        n.laps,n.collision,n.team,n.event70,n.event78,n.event84,n.event88,n.event8C,n.event90,n.event98};
    for(I32 value:required)if(value==Unknown)return PrepareResult::MissingNativeValues;
    if(n.currentRound!=0)return PrepareResult::InvalidCurrentRound;
    if(n.race!=m.nativeRaceEnum||n.vehicleClass!=m.nativeClassEnum||n.rounds!=m.rounds||n.location!=m.location||
       n.trackModel!=m.trackModel||n.laps!=m.laps)return PrepareResult::EventMismatch;
    if(n.collision<0||n.collision>1)return PrepareResult::InvalidCollision;
    // 864A90 converts event90IsZero to mode flags1 (true) or4 (false).
    // 845705..84571E rejects LAN type0 with flags1. Do not produce a packet
    // which that known native prerequisite rejects. The field's UI meaning is
    // still unknown, so it must come from native initialization, not a default.
    if(n.event90==0)return PrepareResult::NativeLanModeRejected;
    out.session={0,1,0,static_cast<std::uint8_t>(n.event90==0),{0,0},{0,0,0}};
    out.event={n.team,n.rounds,n.vehicleClass,n.event78,n.event8C,n.event98,n.race};
    out.round={n.trackModel,0,n.event70,n.collision,n.event84,n.event88};
    return PrepareResult::PreparedExecutionBlocked;
}

// Join uses a native enumeration RECORD pointer, not an advertised integer ID.
// C94F00 requires a valid zero-based index into the current container's live
// enumeration model. Pointer lifetime, endpoint translation and completion are
// unresolved: no join packet builder or forged record is provided here.
}
