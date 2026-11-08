#pragma once
// OFFLINE ABI CONTRACT ONLY. No process access, hooks, allocations or calls.
// Preferred VAs for original SHA256 49b1e00e...54fe48; relocate with ImageBase-400000.
// See README.md and byte-guards.json before implementing any native invocation.
#include <cstddef>
#include <cstdint>

namespace dirt2_initial_config_readback {
using Address32 = std::uint32_t;
constexpr Address32 PreferredBase=0x400000;
constexpr Address32 ManagerTable=0x132b1cc, Kind1Table=0x132ac70;
constexpr Address32 ConfigTable=0x132a450, EntryTable=0x132a398;
constexpr Address32 ProviderTable=0x1336248, PlayerMapTable=0x1335ac8;
constexpr Address32 IdentityTable=0x1334b8c, VehicleTable=0x1336410;
constexpr Address32 IdentityType=0x149077c, VehicleType=0x1490784;
constexpr Address32 RosterType=0x1490868, PlayerMapType=0x1490760;
constexpr Address32 InvalidRowGlobal=0x134fb70; // dereference this global
constexpr Address32 IntKey=0x147de90, IntKeyFlag=0x147de98;
constexpr Address32 ShortKey=0x147fc68, ShortKeyFlag=0x147fc70;
constexpr unsigned LocalRole=4, RemoteRole=2;

#pragma pack(push,1)
struct Ref32 { Address32 pointer; }; // owning only when returned by an owning getter
struct ProtectedInt32 { std::uint32_t bits; };
struct IdentityPrefix {
    Address32 vtable;
    std::uint8_t nativeId; // 87CA10 (virtual+18), NOT a driver/database ID
};
struct PlayerRecordPrefix {
    std::uint8_t opaque00[0x0c];
    Address32 root;      // borrowed; must equal one enumerated provider root
    Address32 identity;  // borrowed 149077C component
};
struct PlayerMapPrefix {
    Address32 vtable;
    std::uint8_t opaque04[0x54];
    Address32 provider;
    std::uint8_t opaque5c[4];
    Address32 records;
    std::uint32_t opaque64;
    std::int32_t count;
};
struct RosterPrefix {
    Address32 vtable;
    std::uint8_t opaque04[0x58];
    Address32 ids; // DWORD slots, each canonical byte ID; not pointers
    std::uint32_t capacity;
    std::int32_t count;
};
struct Kind1Prefix {
    Address32 vtable;
    std::int32_t references;
    Address32 allocator;
    std::uint8_t opaque0c[0x2c];
    Address32 database;
    std::int32_t selectedTrackId; // plain, decoded by 75F460
};
struct ConfigPrefix {
    Address32 vtable;
    std::int32_t references;
    Address32 allocator;
    std::uint8_t opaque0c[0x14];
    Address32 entriesBegin;
    Address32 entriesEnd;
    std::uint8_t opaque28[0x10];
    std::uint32_t initialField; // descriptor[0], initialized1 by75F460
    Address32 database;         // +3C, NOT service's database offset+38
    std::int32_t configKind;    // descriptor[8], native73AE50 output
    std::uint8_t opaque44[0x34];
    Address32 trackModelRow;    // borrowed database payload pointer
    Address32 disciplineRow;
};
struct LiveryRecord {
    Address32 keyRow;
    Address32 liveryRow;
    Address32 gradeRow; // may be populated by735A50 before7359C0
};
struct EntryPrefix {
    Address32 vtable;
    std::int32_t references;
    Address32 allocator;
    Address32 database;
    Address32 driverRow;
    Address32 opaque14;
    std::int32_t driverId; // NOT the native ID
    std::int32_t role;
    std::uint8_t opaque20[0x20];
    Address32 liveryRecords;
    std::uint32_t liveryCapacity;
    std::uint32_t liveryCount;
    std::uint8_t opaque4c[4];
    LiveryRecord inlineLiveries[8];
    std::uint8_t opaqueB0[0x10];
    std::uint8_t nativeId;
    std::uint8_t opaqueC1[0x0f];
};
#pragma pack(pop)
static_assert(sizeof(Ref32)==4 && sizeof(ProtectedInt32)==4);
static_assert(offsetof(PlayerRecordPrefix,root)==0x0c && offsetof(PlayerRecordPrefix,identity)==0x10);
static_assert(offsetof(PlayerMapPrefix,provider)==0x58 && offsetof(PlayerMapPrefix,records)==0x60 && offsetof(PlayerMapPrefix,count)==0x68);
static_assert(offsetof(RosterPrefix,ids)==0x5c && offsetof(RosterPrefix,count)==0x64);
static_assert(offsetof(Kind1Prefix,database)==0x38 && offsetof(Kind1Prefix,selectedTrackId)==0x3c);
static_assert(offsetof(ConfigPrefix,entriesBegin)==0x20 && offsetof(ConfigPrefix,entriesEnd)==0x24);
static_assert(offsetof(ConfigPrefix,database)==0x3c && offsetof(ConfigPrefix,configKind)==0x40 && offsetof(ConfigPrefix,trackModelRow)==0x78);
static_assert(sizeof(LiveryRecord)==0x0c && sizeof(EntryPrefix)==0xd0);
static_assert(offsetof(EntryPrefix,role)==0x1c && offsetof(EntryPrefix,liveryRecords)==0x40 && offsetof(EntryPrefix,nativeId)==0xc0);

// These pure helpers intentionally do not initialize game keys. Caller must
// first require keyFlag&1, capture the key, and revalidate it after readback.
constexpr std::int32_t DecodeInt(std::uint32_t bits,std::uint32_t key) {
    return static_cast<std::int32_t>(bits^key);
}
constexpr std::int32_t DecodeTrack(std::uint16_t bits,std::uint16_t key) {
    // Mirrors MOVSX of BOTH operands before XOR at75F5B3/75F5B7.
    return static_cast<std::int32_t>(static_cast<std::int16_t>(bits))^
           static_cast<std::int32_t>(static_cast<std::int16_t>(key));
}

#if defined(_MSC_VER) && defined(_M_IX86)
// All ECX=this. OUT pointers refer to caller-owned stack wrappers, never a T**
// with uninitialized contents. No C++ class-return ABI is inferred.
using AcquireKind = Ref32*(__thiscall*)(void* manager,Ref32* out,unsigned kind); //734A70 RET8, +1ref
using AcquireConfig = Ref32*(__thiscall*)(void* service,Ref32* out); //733D30 RET4, +1ref
using AcquireContainer = Ref32*(__thiscall*)(void* service,Ref32* out); //733B70 RET4, +1ref
using EntryCount = int(__thiscall*)(void* config); //773000 RET0, no ownership
using AcquireEntry = Ref32*(__thiscall*)(void* config,Ref32* out,unsigned index); //B28310 RET8, +1ref
using ReleaseZero = void(__thiscall*)(void* object); //v+4 only AFTER InterlockedDecrement(+4)==0
using ProviderCount = int(__thiscall*)(void* provider); //819B10 RET0
using ProviderRoot = void*(__thiscall*)(void* provider,int index); //819B20 RET4, borrowed; caller rejects negative
using RecordById = void*(__thiscall*)(void* playerMap,unsigned nativeId); //822C80 RET4, borrowed; low byte only
using RecordFromFacade = void*(__thiscall*)(void* facade,unsigned nativeId); //823220 tailcall822C80
using IdentityId = unsigned char(__thiscall*)(void* identity); //87CA10 RET0
using LookupComponent = void*(__thiscall*)(void* root,bool recursive); //534A90 identity /534C90 vehicle, RET4, borrowed
using LookupRegistry = void*(__thiscall*)(void* map,Address32* out,const Address32* key); //454D00 RET8
using SelectedLivery = ProtectedInt32*(__thiscall*)(void* vehicle,ProtectedInt32* out); //82B060 RET4; no ref
using FindTable = void*(__thiscall*)(void* database,const char* tableName); //B2FCA0 RET4, borrowed
using ReadInteger = int(__thiscall*)(void* table,int* out,Address32 payload,const char* field); //B19260 RET0C; status0 success
using TrackRow = void*(__thiscall*)(void* service); //72D770 RET0; borrowed, balances own temp config ref
using BuildInitial = void(__thiscall*)(void* kind1,void* eventOwner,void* network); //75F460 RET8; DESTRUCTIVE transaction, not getter
#endif
// No automatic Release helper: caller must validate object/table/allocator,
// then set its OWN wrapper null, InterlockedDecrement object+4 exactly once,
// and call validated virtual+4(object) only when decrement returns0.
// Never pass a delete flag to virtual+4. Never release borrowed roots/rows.
}
