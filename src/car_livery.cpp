#include "car_livery.h"
#include "common.h"
#include "gfwl_compat.h"
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <utility>
#include <vector>

namespace vr {
namespace {
// The demo grid builder gives every entrant the livery "<car>_00" (RVA 0x33f690): the player through the call at
// RVA 0x3583f6, each AI car through the call at RVA 0x358621. The player call applies the player's chosen livery
// (DIRT2VR_CAR_LIVERY). The AI call gives each AI car a livery of its own vehicle from the launcher's list
// (DIRT2VR_AI_LIVERIES: the grid's cars' installed liveries), never the player's: per vehicle in a random order
// from the launcher's seed (DIRT2VR_AI_LIVERY_SEED, kept in saved replays), one after another, so AI cars of one
// model differ and mixed grids get random liveries. Career, menus and LAN never reach this builder.
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
std::vector<int> aiLiveries;
std::uint32_t aiSeed{};
// Per grid build (the player call comes first): the player's livery as set, and how many AI cars of each vehicle
// have had one, so a restart or a replay of the same grid gives every car the same livery again.
int playerLivery = -1, playerVehicle = -1;
std::vector<std::pair<int, unsigned>> aiTurns;

void Release(unsigned char* entrant) {
    if (InterlockedDecrement(reinterpret_cast<volatile LONG*>(entrant+4))==0) {
        using Destroy = void (__thiscall*)(void*);
        reinterpret_cast<Destroy>((*reinterpret_cast<void***>(entrant))[1])(entrant);
    }
}

// The seed's random order of liveries: a hash of seed and row id.
std::uint32_t Rank(int id) {
    std::uint32_t x = aiSeed ^ (static_cast<std::uint32_t>(id)*0x9e3779b9u);
    x ^= x>>16; x *= 0x7feb352du; x ^= x>>15; x *= 0x846ca68bu; x ^= x>>16;
    return x;
}

unsigned RowCount(void* table) {
    return *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(table)+0x30);
}

void __fastcall AssignPlayerLivery(void* builder, void*, void* entrant, const char* name, unsigned fallback, unsigned* counter,
                                   unsigned player, void* database, unsigned track, unsigned car) {
    playerLivery = playerVehicle = -1;
    aiTurns.clear();
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
        const auto count = RowCount(table);
        // EGO keys are table-owned record pointers, not numeric IDs. Match the real "id"
        // column, and trust the game's own choice only when it is a row of this table.
        if (count<=4096) for (unsigned index=0; index<count; ++index) {
            const auto row = getRow(table, index);
            if (!row) break;
            int id = -1;
            if (getInteger(table, &id, row, "id")!=0) continue;
            if (selectedLivery>0 && id==selectedLivery) chosen = row;
            if (reinterpret_cast<unsigned>(row)==current) { known = true; currentId = id; }
        }
    }
    if (known && getInteger(table, &currentVehicle, reinterpret_cast<const void*>(current), "vehicle_id")!=0) known = false;
    if (selectedLivery>0) {
        // Same vehicle record only: the paint changes, never the model, class or handling.
        if (chosen && known && getInteger(table, &chosenVehicle, chosen, "vehicle_id")==0 && chosenVehicle==currentVehicle) {
            setLivery(entrant, reinterpret_cast<unsigned>(chosen), key);
            const bool applied = getLivery(entrant, key)==reinterpret_cast<unsigned>(chosen);
            Log("direct car livery: player livery=%d vehicle=%d (game livery %d) %s (memory only)", selectedLivery, chosenVehicle, currentId, applied ? "applied" : "failed");
            if (applied) currentId = selectedLivery;
        } else {
            Log("direct car livery: livery %d unavailable for the player's vehicle (game livery %d, vehicles %d/%d); using game livery",
                selectedLivery, currentId, chosenVehicle, currentVehicle);
        }
    }
    if (known) { playerLivery = currentId; playerVehicle = currentVehicle; }
    Release(bytes);
}

void __fastcall AssignAiLivery(void* builder, void*, void* entrant, const char* name, unsigned fallback, unsigned* counter,
                               unsigned player, void* database, unsigned track, unsigned car) {
    auto bytes = static_cast<unsigned char*>(entrant);
    if (bytes) InterlockedIncrement(reinterpret_cast<volatile LONG*>(bytes+4));
    assign(builder, entrant, name, fallback, counter, player, database, track, car);
    if (!bytes) { Log("direct car livery: no entrant for AI car %u; using game livery", car); return; }
    const unsigned key = *anyKey;
    const unsigned current = getLivery(entrant, key);
    auto table = database && player==0 ? getTable(database, "vehicle_livery") : nullptr;
    // The listed rows found in the table, in the launcher's order, and the game's own choice.
    struct Listed { const void* row{}; int vehicle{-1}; };
    std::vector<Listed> listed(aiLiveries.size());
    int currentId = -1, currentVehicle = -1;
    if (table) {
        const auto count = RowCount(table);
        if (count<=4096) for (unsigned index=0; index<count; ++index) {
            const auto row = getRow(table, index);
            if (!row) break;
            int id = -1, vehicle = -1;
            if (getInteger(table, &id, row, "id")!=0 || getInteger(table, &vehicle, row, "vehicle_id")!=0) continue;
            if (reinterpret_cast<unsigned>(row)==current) { currentId = id; currentVehicle = vehicle; }
            for (std::size_t i=0; i<aiLiveries.size(); ++i) if (aiLiveries[i]==id) listed[i] = {row, vehicle};
        }
    }
    // This vehicle's listed liveries, without the player's: the paint changes, never the model, class or handling.
    std::vector<std::size_t> own;
    if (currentId>=0) for (std::size_t i=0; i<listed.size(); ++i)
        if (listed[i].row && listed[i].vehicle==currentVehicle && !(currentVehicle==playerVehicle && aiLiveries[i]==playerLivery)) own.push_back(i);
    if (own.empty()) {
        Log("direct car livery: AI car %u keeps the game livery %d (vehicle %d has no other listed livery)", car, currentId, currentVehicle);
        Release(bytes);
        return;
    }
    std::sort(own.begin(), own.end(), [](std::size_t a, std::size_t b) {
        const auto x = Rank(aiLiveries[a]), y = Rank(aiLiveries[b]);
        return x!=y ? x<y : a<b;
    });
    unsigned* turn = nullptr;
    for (auto& entry : aiTurns) if (entry.first==currentVehicle) turn = &entry.second;
    if (!turn) { aiTurns.emplace_back(currentVehicle, 0u); turn = &aiTurns.back().second; }
    const auto pick = own[(*turn)++ % own.size()];
    setLivery(entrant, reinterpret_cast<unsigned>(listed[pick].row), key);
    const bool applied = getLivery(entrant, key)==reinterpret_cast<unsigned>(listed[pick].row);
    Log("direct car livery: AI car %u livery=%d vehicle=%d (game livery %d) %s (memory only)", car, aiLiveries[pick], currentVehicle, currentId, applied ? "applied" : "failed");
    Release(bytes);
}

// "12,34,56": decimal vehicle_livery ids, at most 512.
bool ParseIds(const wchar_t* text, unsigned length, std::vector<int>& ids) {
    int value = 0; unsigned digits = 0;
    for (unsigned i=0; i<=length; ++i) {
        const wchar_t c = i<length ? text[i] : L',';
        if (c==L',') {
            if (!digits || value<1 || ids.size()>=512) return false;
            ids.push_back(value); value = 0; digits = 0;
        } else if (c>=L'0' && c<=L'9' && digits<6) {
            value = value*10 + (c-L'0'); ++digits;
        } else return false;
    }
    return !ids.empty();
}

// Replaces the call at site (after "mov ecx, esi") with one to target.
bool Redirect(unsigned char* site, const unsigned char* original, void* target) {
    const auto displacement = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(target)-reinterpret_cast<std::uintptr_t>(site+5));
    unsigned char patch[5]{0xe8};
    std::memcpy(patch+1, &displacement, 4);
    DWORD previous{}, ignored{};
    if (!VirtualProtect(site, sizeof(patch), PAGE_EXECUTE_READWRITE, &previous)) return false;
    for (unsigned i=0; i<sizeof(patch); ++i) { RecordCodeByte(site+i, original[i], patch[i]); site[i] = patch[i]; }
    const bool restored = VirtualProtect(site, sizeof(patch), previous, &ignored)!=FALSE;
    const bool flushed = FlushInstructionCache(GetCurrentProcess(), site, sizeof(patch))!=FALSE;
    return restored && flushed;
}
}
bool EnableCarLivery() {
    wchar_t value[8]{};
    const auto length = GetEnvironmentVariableW(L"DIRT2VR_CAR_LIVERY", value, 8);
    std::vector<wchar_t> list(4096);
    const auto listLength = GetEnvironmentVariableW(L"DIRT2VR_AI_LIVERIES", list.data(), static_cast<DWORD>(list.size()));
    if (!length && !listLength) return true;
    wchar_t direct[8]{};
    if (length>5 || listLength>=list.size() || !SupportedHost() || GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE", direct, 8)!=1 || direct[0]!=L'1') return false;
    int id = 0;
    for (unsigned i=0; i<length; ++i) {
        if (value[i]<L'0' || value[i]>L'9') return false;
        id = id*10 + (value[i]-L'0');
    }
    if (length && id<1) return false;
    std::vector<int> ai;
    if (listLength && !ParseIds(list.data(), listLength, ai)) return false;
    // A seed comes with the list: 1 to 2147483647.
    wchar_t seedText[16]{};
    const auto seedLength = GetEnvironmentVariableW(L"DIRT2VR_AI_LIVERY_SEED", seedText, 16);
    std::uint32_t seed = 0;
    if (seedLength>10 || (seedLength==0)!=ai.empty()) return false;
    for (unsigned i=0; i<seedLength; ++i) {
        if (seedText[i]<L'0' || seedText[i]>L'9') return false;
        seed = seed*10u + static_cast<std::uint32_t>(seedText[i]-L'0');
        if (seed>0x7fffffffu) return false;
    }
    if (!ai.empty() && seed==0) return false;
    auto base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char call[]{0x8b,0xce,0xe8,0x95,0x72,0xfe,0xff};
    constexpr unsigned char aiCall[]{0x8b,0xce,0xe8,0x6a,0x70,0xfe,0xff};
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
        (!ai.empty() && std::memcmp(base+0x35861f, aiCall, sizeof(aiCall))) ||
        std::memcmp(base+0x33f690, assignStart, sizeof(assignStart)) ||
        std::memcmp(base+0x335820, setter, sizeof(setter)) ||
        std::memcmp(base+0x3357c0, getterStart, sizeof(getterStart)) ||
        std::memcmp(base+0x3357c7, getterBody, sizeof(getterBody)) ||
        std::memcmp(base+0x72fca0, tableStart, sizeof(tableStart)) ||
        std::memcmp(base+0x6ecc40, rowStart, sizeof(rowStart)) ||
        std::memcmp(base+0x719260, integerStart, sizeof(integerStart)) ||
        sentinelAddress!=reinterpret_cast<std::uintptr_t>(base+0xf4fb70)) return false;
    selectedLivery = id;
    aiLiveries = std::move(ai);
    aiSeed = seed;
    anyKey = reinterpret_cast<const unsigned*>(base+0xf4fb70);
    assign = reinterpret_cast<Assign>(base+0x33f690);
    setLivery = reinterpret_cast<SetLivery>(base+0x335820);
    getLivery = reinterpret_cast<GetLivery>(base+0x3357c0);
    getTable = reinterpret_cast<GetTable>(base+0x72fca0);
    getRow = reinterpret_cast<GetRow>(base+0x6ecc40);
    getInteger = reinterpret_cast<GetInteger>(base+0x719260);
    // The player call always: AI cars of the player's vehicle skip the player's livery.
    bool ok = Redirect(base+0x3583f6, call+2, reinterpret_cast<void*>(&AssignPlayerLivery));
    if (!aiLiveries.empty()) ok = ok && Redirect(base+0x358621, aiCall+2, reinterpret_cast<void*>(&AssignAiLivery));
    if (selectedLivery>0) Log("direct car livery: player livery=%d %s (memory only)", selectedLivery, ok ? "enabled" : "failed");
    if (!aiLiveries.empty()) Log("direct car livery: AI cars from %u listed liveries, seed %u %s (memory only)", static_cast<unsigned>(aiLiveries.size()), aiSeed, ok ? "enabled" : "failed");
    return ok;
}
}
