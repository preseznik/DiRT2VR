#pragma once
#include <cstddef>
#include <cstdint>

// Static ABI notes only. No resolver, process access, native invocation or
// executable adapter. Preferred VAs require relocation from image base00400000.
namespace dirt2_native_join {
using I32 = std::int32_t;
using U32 = std::uint32_t;
constexpr bool ExecutionEnabled = false;
constexpr bool ImmediateSearchCancelVerified = false;
constexpr bool CurrentRaceAdmissionVerified = false;
constexpr char OriginalSha256[] =
    "49b1e00ea1d4bd02e633ceed63390b5cae07601333f673c4efd8d2f0eb54fe48";

struct SearchDescriptor {
    I32 type = 0;                    // Native LAN type.
    I32 value04 = 1;                 // Original caller value; unnamed semantics.
    std::uint8_t ranked = 0;
    std::uint8_t zero09 = 0;         // Search caller5D1E71, not host-mode builder.
    std::uint8_t padding[2]{};
    I32 reserved[3]{};
};
struct SearchFilter {
    I32 team = -1;                   // Property7.
    I32 roundCount = -1;             // Property8.
    I32 vehicleClass = -1;           // Property9, native enum, not DB row.
    I32 event78 = -1;                // Property10; unnamed semantics.
    I32 event8C = -1;                // Property11; unnamed semantics.
    I32 event98 = -1;                // Property12; unnamed semantics.
    I32 race = -1;                   // Property15, native enum, not DB row.
};
static_assert(sizeof(SearchDescriptor) == 0x18);
static_assert(offsetof(SearchDescriptor, ranked) == 8);
static_assert(offsetof(SearchDescriptor, zero09) == 9);
static_assert(offsetof(SearchDescriptor, reserved) == 0xC);
static_assert(sizeof(SearchFilter) == 0x1C);
static_assert(offsetof(SearchFilter, race) == 0x18);

constexpr U32 CanStartVA = 0x818E40;  // this=container; EAX0/1; RET0.
constexpr U32 StartVA = 0x864AE0;     // this=container; AL accepted; RET8.
constexpr U32 CountVA = 0xC8FD30;     // this=searcher; EAX count; RET0.
constexpr U32 RecordVA = 0xC94F00;    // this=searcher; EAX borrowed; RET4.
constexpr U32 JoinVA = 0x8510D0;      // this=container; AL accepted; RET18.
constexpr U32 ContainerTableVA = 0x13364EC;
constexpr U32 SearcherTableVA = 0x135D120;
constexpr U32 SearchTickVA = 0xC9E140;
constexpr U32 SearchPostprocessorTableVA = 0x1334FD4;
constexpr U32 SearchHelperTableVA = 0x13340DC;
constexpr U32 RecordTableVA = 0x13357C0;
constexpr U32 LanTransportTableVA = 0x135CFC0;
constexpr U32 LanQueryTableVA = 0x135CC64;
constexpr U32 HolderTableVA = 0x133549C;
constexpr U32 SessionTableVA = 0x1335844;
constexpr U32 SessionDataTableVA = 0x1336248;

constexpr U32 ContainerProperties = 0x1E20;
constexpr U32 ContainerHolder = 0x2450;
constexpr U32 ContainerSearcher = 0x2460;
constexpr U32 ContainerProvider = 0x2578;
constexpr U32 ContainerSearchHelper = 0x259C;
constexpr U32 ContainerSearchPostprocessor = 0x25A0;
constexpr U32 SearcherAllocator = 4;
constexpr U32 SearcherService = 8;
constexpr U32 SearcherState = 0xC;
constexpr U32 SearcherError = 0x10;
constexpr U32 SearcherType = 0x1C;
constexpr U32 SearcherTransport = 0x2C;
constexpr U32 SearcherProperties = 0x30;
constexpr U32 SearcherModel = 0x34;
constexpr U32 SearcherAuxActive = 0x54;
constexpr U32 SearcherPostprocessor = 0xD0;
constexpr U32 ModelProperties = 0;    // Not a vtable.
constexpr U32 ModelAllocator = 4;
constexpr U32 ModelService = 8;
constexpr U32 ModelType = 0xC;
constexpr U32 ModelActiveArray = 0x70;
constexpr U32 ModelActiveCapacity = 0x74;
constexpr U32 ModelActiveCount = 0x78;
constexpr U32 ModelInlineActiveArray = 0x80;
constexpr I32 NativePoolCapacity = 20;
constexpr float NativeSearchSeconds = 2.0f;
constexpr U32 RecordBytes = 0x6E0;
constexpr U32 RecordType = 0x10;
constexpr U32 RecordCapacityPublic = 0x14;
constexpr U32 RecordCapacityPrivate = 0x18;
constexpr U32 RecordCountPublic = 0x1C;
constexpr U32 RecordCountPrivate = 0x20;
constexpr U32 RecordMetadata = 0x30;
constexpr U32 RecordNativeAddress = 0x668; // Opaque48bytes; not plain sockaddr.
constexpr U32 RecordSessionKeyId = 0x698; // Eight bytes, preserve native value.
constexpr U32 RecordSessionKey = 0x6A0;   // Sixteen bytes; do not log secret bytes.
constexpr U32 RecordName = 0x6B0;         // Native16-byte name buffer.
constexpr U32 HolderOwnedRecord = 0x684;  // Pointer, not inline record.
constexpr U32 HolderTransportKeyId = 0x6D8;

#if defined(_MSC_VER) && defined(_M_IX86)
static_assert(sizeof(void*) == 4);
using CanStartAbi = U32(__thiscall*)(void* container);
using StartAbi = std::uint8_t(__thiscall*)(void* container,
    const SearchDescriptor*, const SearchFilter*);
using CountAbi = I32(__thiscall*)(void* searcher);
using RecordAbi = const void*(__thiscall*)(void* searcher, I32 index);
using JoinAbi = std::uint8_t(__thiscall*)(void* container, const void* nativeRecord,
    I32 arg2, I32 arg3, const char* text1, I32 arg5, const char* text2);
// Original caller passes(record,1,1,text1,1,empty). No guessed semantic names.
#endif

// Poll/read only after one accepted search. Completion has replaced the model
// and freed its old pool. Re-read all ownership identities before enumeration.
// Completion requires state0/error0/transport state0/auxActive0, a nonnull
// coherent model and a known accepted attempt. Idle-before-search is not success.
// A borrowed record is valid only while that completed model is unchanged.
// 8510D0 copies it to *holder+684 before returning; AL means initiation only.
// Joined candidate: holder state2, transport state2, session ready byte+6C,
// session role+24==1, data+1EC nonnull, cancel+800==0, exact owned identities.
// Keep runtime client completion and current-race admission independently gated.
}
