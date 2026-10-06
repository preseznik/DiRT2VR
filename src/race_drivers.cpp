#include "race_drivers.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstdint>
#include <cstring>

namespace vr {
namespace {
using Initialize = void (__thiscall*)(void*, void*, const char*, void*, const void*);
using GetTable = void* (__thiscall*)(void*, const char*);
using GetRow = const void* (__thiscall*)(void*, unsigned);
using GetInteger = int (__thiscall*)(void*, int*, const void*, const char*);
Initialize original{};
GetTable getTable{};
GetRow getRow{};
GetInteger getInteger{};

int DriverFor(const char* name) {
    // The seven demo AI slots. Use actual stock driver identities, not dev_1.0.
    constexpr int drivers[]{839,840,842,843,844,845,847};
    if (!name || std::strncmp(name,"car_ai",6) || name[6]<'1' || name[6]>'7' || name[7]) return -1;
    return drivers[name[6]-'1'];
}

void __fastcall InitializeOpponent(void* owner, void*, void* entrant, const char* name, void* database, const void* fallback) {
    const int driver=DriverFor(name);
    auto table=driver>=0 ? getTable(database,"driver") : nullptr;
    int identity=-1;
    const void* row=nullptr;
    if (table) {
        const auto count=*reinterpret_cast<const unsigned*>(static_cast<const unsigned char*>(table)+0x30);
        // The engine wants a table-owned ITM record pointer, not its numeric primary key.
        // Read the real "id" column; table ordering is not an identity contract.
        if (count<=4096) for (unsigned index=0;index<count;++index) {
            const auto candidate=getRow(table,index);
            if (!candidate) break;
            identity=-1;
            if (getInteger(table,&identity,candidate,"id")==0 && identity==driver) { row=candidate; break; }
        }
    }
    if (!row) {
        Log("direct race: driver lookup unavailable for %s; using game default",name ? name : "(null)");
        original(owner,entrant,name,database,fallback);
        return;
    }
    // Same identity fields and ownership transfer as the original demo initializer.
    // Its dev_1.0 lookup otherwise selects (or rewrites) the same driver for every slot.
    auto bytes=static_cast<unsigned char*>(entrant);
    *reinterpret_cast<void**>(bytes+0x0c)=database;
    *reinterpret_cast<const void**>(bytes+0x10)=row;
    *reinterpret_cast<int*>(bytes+0x18)=identity;
    Log("direct race: %s driver=%d",name,driver);
    if (InterlockedDecrement(reinterpret_cast<volatile LONG*>(bytes+4))==0) {
        using Destroy=void (__thiscall*)(void*);
        reinterpret_cast<Destroy>((*reinterpret_cast<void***>(entrant))[1])(entrant);
    }
}
}

bool InstallRaceDrivers(unsigned char* base) {
    // Only the AI call inside the demo grid builder, never the player or career/LAN grid.
    constexpr unsigned char call[]{0xe8,0xaa,0x73,0xfe,0xff};
    constexpr unsigned char tableStart[]{0x51,0x56,0x57,0x8b,0xf1};
    constexpr unsigned char integerStart[]{0x51,0x8b,0x44,0x24,0x10,0x8b,0x54,0x24,0x0c};
    constexpr unsigned char rowStart[]{0x8b,0x54,0x24,0x04,0x33,0xc0,0x3b,0x51,0x30};
    if (std::memcmp(base+0x3585d1,call,sizeof(call)) ||
        std::memcmp(base+0x72fca0,tableStart,sizeof(tableStart)) ||
        std::memcmp(base+0x719260,integerStart,sizeof(integerStart)) ||
        std::memcmp(base+0x6ecc40,rowStart,sizeof(rowStart))) return false;
    original=reinterpret_cast<Initialize>(base+0x33f980);
    getTable=reinterpret_cast<GetTable>(base+0x72fca0);
    getRow=reinterpret_cast<GetRow>(base+0x6ecc40);
    getInteger=reinterpret_cast<GetInteger>(base+0x719260);
    const auto displacement=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&InitializeOpponent)-reinterpret_cast<std::uintptr_t>(base+0x3585d6));
    unsigned char patch[5]{0xe8};
    std::memcpy(patch+1,&displacement,4);
    DWORD protection{},ignored{};
    if (!VirtualProtect(base+0x3585d1,sizeof(patch),PAGE_EXECUTE_READWRITE,&protection)) return false;
    for (unsigned i=0;i<sizeof(patch);++i) {
        RecordCodeByte(base+0x3585d1+i,call[i],patch[i]);
        base[0x3585d1+i]=patch[i];
    }
    const bool restored=VirtualProtect(base+0x3585d1,sizeof(patch),protection,&ignored)!=FALSE;
    const bool flushed=FlushInstructionCache(GetCurrentProcess(),base+0x3585d1,sizeof(patch))!=FALSE;
    Log("direct race: distinct stock opponent identities %s (memory only)",restored && flushed ? "enabled" : "failed");
    return restored && flushed;
}
bool EnableRaceDrivers() {
    return SupportedHost() && InstallRaceDrivers(reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr)));
}
}
