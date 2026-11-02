#include "filecheck.h"
#include "diagnostics.h"
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <set>
#include <string>
static_assert(sizeof(std::uintptr_t)==4,"DiRT 2 file check integration requires x86");

namespace {
// Each record holds a file name, the expected CRC32 from the game's list and the
// CRC32 of the local file. A failed comparison makes the host close the session
// at race start. The comparison has a single call site, in the validator.
constexpr std::uintptr_t compareRva=0x899a30,callRva=0x899ba4;
struct Record {
    std::uint32_t expected,actual,header[2];
    const char* name;
    std::uint32_t tail[2];
    std::uint8_t known,computed,optional,unused;
};
static_assert(sizeof(Record)==0x20,"file check record layout");
using CompareFn=bool(__thiscall*)(void*,std::uint32_t*);
CompareFn compare{};
std::mutex mutex;
std::set<std::string> reported;

bool CopyName(const char* source,char (&text)[260]) {
    __try { return strncpy_s(text,source,_TRUNCATE)>=0; }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void Report(void* collection,bool passed) {
    std::lock_guard lock(mutex);
    const auto fields=static_cast<Record* const*>(static_cast<void*>(static_cast<char*>(collection)+0x28));
    unsigned count=0;
    for(auto record=fields[0];record!=fields[1];++record,++count) {
        char name[260]{};
        if(!record->name || !CopyName(record->name,name)) strcpy_s(name,"<unreadable>");
        char key[400]{};
        sprintf_s(key,"%p|%s|%08x|%08x|%u%u%u",collection,name,record->expected,record->actual,record->known,record->computed,record->optional);
        if(!reported.insert(key).second) continue;
        DiRT2VRLanLog(4,0,"DiRT2VRLanLogFileCheck","file check collection=%p name=%s expected=%08x actual=%08x known=%u computed=%u optional=%u",
            collection,name,record->expected,record->actual,record->known,record->computed,record->optional);
    }
    char key[64]{};
    sprintf_s(key,"%p|result|%u|%d",collection,count,passed);
    if(reported.insert(key).second)
        DiRT2VRLanLog(4,0,"DiRT2VRLanLogFileCheck","file check collection=%p records=%u passed=%d",collection,count,passed);
}
// The per-record rule of the game's comparison: a computed CRC must equal the
// expected one; an uncomputed record passes when no CRC is expected or it is not
// required. The game additionally ANDs an obfuscated code-integrity flag
// (1481fa8, read only here) that fails as soon as any hook patches game code.
bool RecordsPass(void* collection) {
    const auto fields=static_cast<Record* const*>(static_cast<void*>(static_cast<char*>(collection)+0x28));
    for(auto record=fields[0];record!=fields[1];++record) {
        const bool matched=record->known && record->computed && record->expected && record->actual && record->actual==record->expected;
        const bool absent=!record->computed && record->known && (!record->expected || !record->optional);
        if(!matched && !absent) return false;
    }
    return true;
}
bool __fastcall Compare(void* collection,void*,std::uint32_t* total) {
    bool passed=compare(collection,total);
    // DiRT2VR's own hooks trip the integrity flag, so a LAN race start judges the
    // game files only: any file that differs still fails (ProJict 8ac0f636).
    if(!passed && RecordsPass(collection)) {
        passed=true;
        DiRT2VRLanLog(4,0,"DiRT2VRLanLogFileCheck","file check collection=%p: every file matches; code-integrity flag ignored",collection);
    }
    if(DiRT2VRLanLogEnabled()) Report(collection,passed);
    return passed;
}
}

bool InstallLanFileCheck(HMODULE host) {
    auto base=reinterpret_cast<std::uint8_t*>(host);
    if(!base) return false;
    const std::uint8_t prologue[]{0x83,0xec,0x08,0x8b,0x41,0x28,0x56,0x8b,0x71,0x2c,0x3b,0xc6};
    auto call=base+callRva;
    std::int32_t relative{};
    std::memcpy(&relative,call+1,sizeof(relative));
    // Verify the call and its target before redirecting. The caller also checks the executable hash.
    if(call[0]!=0xe8 || call+5+relative!=base+compareRva || std::memcmp(base+compareRva,prologue,sizeof(prologue))) return false;
    compare=reinterpret_cast<CompareFn>(base+compareRva);
    const auto replacement=static_cast<std::int32_t>(reinterpret_cast<std::uintptr_t>(&Compare)-reinterpret_cast<std::uintptr_t>(call+5));
    DWORD protect{};
    if(!VirtualProtect(call+1,sizeof(replacement),PAGE_EXECUTE_READWRITE,&protect)) return false;
    std::memcpy(call+1,&replacement,sizeof(replacement));
    DWORD ignored{};
    const bool restored=VirtualProtect(call+1,sizeof(replacement),protect,&ignored)!=FALSE;
    return restored && FlushInstructionCache(GetCurrentProcess(),call,5)!=FALSE;
}
