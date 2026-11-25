#include "car_livery.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstdint>
#include <cstring>

namespace vr {
namespace {
// The demo grid builder gives every entrant the livery "<car>_00" (RVA 0x33f690).
// Only its player call (RVA 0x3583f6) is wrapped: AI cars keep the game's livery,
// and career, menus and LAN never reach this builder.
using Assign = void (__thiscall*)(void*, void*, const char*, unsigned, unsigned*, unsigned, void*, unsigned, unsigned);
using SetLivery = void (__thiscall*)(void*, unsigned, unsigned);
using GetLivery = unsigned (__thiscall*)(void*, unsigned);
using GetTable = void* (__thiscall*)(void*, const char*);
using GetRow = const void* (__thiscall*)(void*, unsigned);
using GetInteger = int (__thiscall*)(void*, int*, const void*, const char*);
Assign assign{};
SetLivery setLivery{};
GetLivery getLivery{};
GetTable getTable{};
GetRow getRow{};
GetInteger getInteger{};
const unsigned* anyKey{};
int selectedLivery{};

void Release(unsigned char* entrant) {
    if (InterlockedDecrement(reinterpret_cast<volatile LONG*>(entrant+4))==0) {
        using Destroy = void (__thiscall*)(void*);
        reinterpret_cast<Destroy>((*reinterpret_cast<void***>(entrant))[1])(entrant);
    }
}

void __fastcall AssignPlayerLivery(void* builder, void*, void* entrant, const char* name, unsigned fallback, unsigned* counter,
                                   unsigned player, void* database, unsigned track, unsigned car) {
    // The original releases this by-value entrant reference; keep one of our own until done.
    auto bytes = static_cast<unsigned char*>(entrant);
    if (bytes) InterlockedIncrement(reinterpret_cast<volatile LONG*>(bytes+4));
    assign(builder, entrant, name, fallback, counter, player, database, track, car);
    if (!bytes) { Log("direct car livery: no player entrant; using game livery"); return; }
    const unsigned key = *anyKey;
    const unsigned current = getLivery(entrant, key);
    auto table = database && player==1 && car==0 ? getTable(database, "vehicle_livery") : nullptr;
    const void* chosen = nullptr;
    bool known = false;
    int currentId = -1, currentVehicle = -1, chosenVehicle = -1;
    if (table) {
        const auto count = *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(table)+0x30);
        // EGO keys are table-owned record pointers, not numeric IDs. Match the real "id"
        // column, and trust the game's own choice only when it is a row of this table.
        if (count<=4096) for (unsigned index=0; index<count; ++index) {
            const auto row = getRow(table, index);
            if (!row) break;
            int id = -1;
            if (getInteger(table, &id, row, "id")!=0) continue;
            if (id==selectedLivery) chosen = row;
            if (reinterpret_cast<unsigned>(row)==current) { known = true; currentId = id; }
        }
    }
    // Same vehicle record only: the paint changes, never the model, class or handling.
    if (chosen && known && getInteger(table, &chosenVehicle, chosen, "vehicle_id")==0 &&
        getInteger(table, &currentVehicle, reinterpret_cast<const void*>(current), "vehicle_id")==0 && chosenVehicle==currentVehicle) {
        setLivery(entrant, reinterpret_cast<unsigned>(chosen), key);
        const bool applied = getLivery(entrant, key)==reinterpret_cast<unsigned>(chosen);
        Log("direct car livery: player livery=%d vehicle=%d (game livery %d) %s (memory only)", selectedLivery, chosenVehicle, currentId, applied ? "applied" : "failed");
    } else {
        Log("direct car livery: livery %d unavailable for the player's vehicle (game livery %d, vehicles %d/%d); using game livery",
            selectedLivery, currentId, chosenVehicle, currentVehicle);
    }
    Release(bytes);
}
}
bool EnableCarLivery() {
    wchar_t value[8]{};
    const auto length = GetEnvironmentVariableW(L"DIRT2VR_CAR_LIVERY", value, 8);
    if (!length) return true;
    wchar_t direct[8]{};
    if (length>5 || !SupportedHost() || GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE", direct, 8)!=1 || direct[0]!=L'1') return false;
    int id = 0;
    for (unsigned i=0; i<length; ++i) {
        if (value[i]<L'0' || value[i]>L'9') return false;
        id = id*10 + (value[i]-L'0');
    }
    if (id<1) return false;
    auto base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char call[]{0x8b,0xce,0xe8,0x95,0x72,0xfe,0xff};
    constexpr unsigned char assignStart[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x04,0x01,0x00,0x00};
    constexpr unsigned char setter[]{0x8b,0x41,0x48,0x83,0xec,0x0c,0x53,0x32,0xdb};
    constexpr unsigned char getterStart[]{0x53,0x8b,0x1d};
    constexpr unsigned char getterBody[]{0x55,0x56,0x8b,0x71,0x48,0x33,0xd2,0x85,0xf6,0x57,0x8b,0xc3};
    constexpr unsigned char tableStart[]{0x51,0x56,0x57,0x8b,0xf1};
    constexpr unsigned char rowStart[]{0x8b,0x54,0x24,0x04,0x33,0xc0,0x3b,0x51,0x30};
    constexpr unsigned char integerStart[]{0x51,0x8b,0x44,0x24,0x10,0x8b,0x54,0x24,0x0c};
    std::uintptr_t sentinelAddress{};
    std::memcpy(&sentinelAddress, base+0x3357c3, sizeof(sentinelAddress));
    if (std::memcmp(base+0x3583f4, call, sizeof(call)) ||
        std::memcmp(base+0x33f690, assignStart, sizeof(assignStart)) ||
        std::memcmp(base+0x335820, setter, sizeof(setter)) ||
        std::memcmp(base+0x3357c0, getterStart, sizeof(getterStart)) ||
        std::memcmp(base+0x3357c7, getterBody, sizeof(getterBody)) ||
        std::memcmp(base+0x72fca0, tableStart, sizeof(tableStart)) ||
        std::memcmp(base+0x6ecc40, rowStart, sizeof(rowStart)) ||
        std::memcmp(base+0x719260, integerStart, sizeof(integerStart)) ||
        sentinelAddress!=reinterpret_cast<std::uintptr_t>(base+0xf4fb70)) return false;
    selectedLivery = id;
    anyKey = reinterpret_cast<const unsigned*>(base+0xf4fb70);
    assign = reinterpret_cast<Assign>(base+0x33f690);
    setLivery = reinterpret_cast<SetLivery>(base+0x335820);
    getLivery = reinterpret_cast<GetLivery>(base+0x3357c0);
    getTable = reinterpret_cast<GetTable>(base+0x72fca0);
    getRow = reinterpret_cast<GetRow>(base+0x6ecc40);
    getInteger = reinterpret_cast<GetInteger>(base+0x719260);
    auto site = base+0x3583f6;
    const auto displacement = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&AssignPlayerLivery)-reinterpret_cast<std::uintptr_t>(site+5));
    unsigned char patch[5]{0xe8};
    std::memcpy(patch+1, &displacement, 4);
    DWORD previous{}, ignored{};
    if (!VirtualProtect(site, sizeof(patch), PAGE_EXECUTE_READWRITE, &previous)) return false;
    for (unsigned i=0; i<sizeof(patch); ++i) { RecordCodeByte(site+i, call[2+i], patch[i]); site[i] = patch[i]; }
    const bool restored = VirtualProtect(site, sizeof(patch), previous, &ignored)!=FALSE;
    const bool flushed = FlushInstructionCache(GetCurrentProcess(), site, sizeof(patch))!=FALSE;
    Log("direct car livery: player livery=%d %s (memory only)", selectedLivery, restored && flushed ? "enabled" : "failed");
    return restored && flushed;
}
}
