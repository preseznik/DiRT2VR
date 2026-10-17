#include "car_grade.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstdint>
#include <cstring>

namespace vr {
namespace {
using SetLivery = void (__thiscall*)(void*, unsigned, unsigned);
using SetGrade = void (__thiscall*)(void*, const unsigned*, unsigned);
using ReadGrade = unsigned (__thiscall*)(void*, unsigned);
using GetTable = void* (__thiscall*)(void*, const char*);
using GetRow = const void* (__thiscall*)(void*, unsigned);
using GetInteger = int (__thiscall*)(void*, int*, const void*, const char*);
SetLivery setLivery{};
SetGrade setGrade{};
ReadGrade readGrade{};
GetTable getTable{};
GetRow getRow{};
GetInteger getInteger{};
unsigned selectedGrade{};
void __fastcall DemoLivery(void* entrant, void*, unsigned livery, unsigned key) {
    setLivery(entrant, livery, key);
    auto database = *reinterpret_cast<void**>(static_cast<unsigned char*>(entrant)+0x0c);
    auto table = getTable(database,"vehicle_grade");
    unsigned nativeGrade{};
    if(table) {
        const auto count = *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(table)+0x30);
        if(count<=64) for(unsigned index=0;index<count;++index) {
            const auto row = getRow(table,index);
            int id=-1;
            if(row && getInteger(table,&id,row,"id")==0 && id==static_cast<int>(selectedGrade)) {
                // EGO keys are table-owned record pointers, not numeric IDs.
                nativeGrade = reinterpret_cast<unsigned>(row);
                break;
            }
        }
    }
    if(!nativeGrade) { Log("direct car grade: native record lookup failed"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    // Set the native event data, not merely the handling filename. Replay and
    // player/AI vehicle creation subsequently read this same grade.
    setGrade(entrant, &nativeGrade, key);
    const auto actual = readGrade(entrant, key);
    Log("direct car grade: livery=%u key=%u requested=%u actual=%u (whole grid)", livery, key, selectedGrade, actual==nativeGrade ? selectedGrade : 0);
    if(actual != nativeGrade) ExitProcess(ERROR_BAD_EXE_FORMAT);
}
}
bool EnableCarGrade() {
    wchar_t value[8]{};
    const auto length = GetEnvironmentVariableW(L"DIRT2VR_CAR_GRADE", value, 8);
    if(!length) return true;
    wchar_t direct[8]{};
    if(length != 1 || value[0] < L'1' || value[0] > L'3' || !SupportedHost() ||
       GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE", direct, 8) != 1 || direct[0] != L'1') return false;
    auto base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char call[]{0xe8,0xc8,0x5e,0xff,0xff};
    constexpr unsigned char setter[]{0x8b,0x41,0x48,0x83,0xec,0x0c,0x53,0x32,0xdb};
    constexpr unsigned char reader[]{0x53,0x8b,0x1d};
    constexpr unsigned char tableStart[]{0x51,0x56,0x57,0x8b,0xf1};
    constexpr unsigned char rowStart[]{0x8b,0x54,0x24,0x04,0x33,0xc0,0x3b,0x51,0x30};
    constexpr unsigned char integerStart[]{0x51,0x8b,0x44,0x24,0x10,0x8b,0x54,0x24,0x0c};
    std::uintptr_t sentinelAddress{};
    std::memcpy(&sentinelAddress,base+0x335963,sizeof(sentinelAddress));
    if(std::memcmp(base+0x33f953,call,sizeof(call)) ||
       std::memcmp(base+0x335820,setter,sizeof(setter)) ||
       std::memcmp(base+0x3358c0,setter,sizeof(setter)) ||
       std::memcmp(base+0x335960,reader,sizeof(reader)) ||
       std::memcmp(base+0x72fca0,tableStart,sizeof(tableStart)) ||
       std::memcmp(base+0x6ecc40,rowStart,sizeof(rowStart)) ||
       std::memcmp(base+0x719260,integerStart,sizeof(integerStart)) ||
       sentinelAddress != reinterpret_cast<std::uintptr_t>(base+0xf4fb70)) return false;
    selectedGrade = value[0] - L'0';
    setLivery = reinterpret_cast<SetLivery>(base+0x335820);
    setGrade = reinterpret_cast<SetGrade>(base+0x3358c0);
    readGrade = reinterpret_cast<ReadGrade>(base+0x335960);
    getTable = reinterpret_cast<GetTable>(base+0x72fca0);
    getRow = reinterpret_cast<GetRow>(base+0x6ecc40);
    getInteger = reinterpret_cast<GetInteger>(base+0x719260);
    const auto displacement = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&DemoLivery)-reinterpret_cast<std::uintptr_t>(base+0x33f958));
    unsigned char patch[5]{0xe8}; std::memcpy(patch+1,&displacement,4);
    DWORD previous{},ignored{};
    if(!VirtualProtect(base+0x33f953,sizeof(patch),PAGE_EXECUTE_READWRITE,&previous)) return false;
    for(unsigned i=0;i<sizeof(patch);++i) { RecordCodeByte(base+0x33f953+i,call[i],patch[i]); base[0x33f953+i]=patch[i]; }
    const bool restored = VirtualProtect(base+0x33f953,sizeof(patch),previous,&ignored)!=FALSE;
    const bool flushed = FlushInstructionCache(GetCurrentProcess(),base+0x33f953,sizeof(patch))!=FALSE;
    Log("direct car grade: native grade=%u %s (memory only)",selectedGrade,restored && flushed ? "enabled" : "failed");
    return restored && flushed;
}
}
