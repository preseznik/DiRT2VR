#include "lap_results.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstring>
#include <cwchar>
#include <cstdint>
#include <intrin.h>

namespace vr {
namespace {
struct Result {
    uint32_t magic,version,size;
    volatile LONG sequence;
    uint32_t processId,completedLaps;
    int64_t microseconds;
};
static_assert(sizeof(Result)==32);
using FinishFn=bool(__thiscall*)(void*,int64_t,bool);
FinishFn originalFinish{};
Result* result{};
HANDLE mapping{};
void* finishCaller{};
unsigned char* gameBase{};

bool __fastcall Finished(void* participant,void*,int64_t fractionTime,bool allowFinish) {
    const bool fromFinishGate=_ReturnAddress()==finishCaller;
    const bool accepted=originalFinish(participant,fractionTime,allowFinish);
    if(!accepted || !fromFinishGate || !result) return accepted;
    void* table[32]{};void** vtable{};SIZE_T read{};
    if(!ReadProcessMemory(GetCurrentProcess(),participant,&vtable,sizeof(vtable),&read) ||
       !ReadProcessMemory(GetCurrentProcess(),vtable,table,sizeof(table),&read) ||
       table[5]!=gameBase+0x8f3d70 || table[31]!=gameBase+0x8ee7e0) return accepted;
    using LapFn=int(__thiscall*)(void*);
    const int laps=reinterpret_cast<LapFn>(table[5])(participant);
    int64_t time{};
    // The game publishes this same microsecond duration to its finish observer.
    // Its initial finish crossing starts lap zero and has no completed duration.
    if(laps!=1 || !ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(participant)+0x70,&time,sizeof(time),&read) ||
       time<1000000 || time>7200000000LL) return accepted;
    InterlockedIncrement(&result->sequence);
    result->processId=GetCurrentProcessId();result->completedLaps=1;result->microseconds=time;
    MemoryBarrier();InterlockedIncrement(&result->sequence);
    Log("lap result: completed=%d engine-microseconds=%lld",laps,time);
    return accepted;
}
}
void EnableLapResults() {
    static bool attempted{};
    if(attempted) return;
    attempted=true;
    wchar_t name[128]{};
    const DWORD length=GetEnvironmentVariableW(L"DIRT2VR_LAP_CHANNEL",name,_countof(name));
    if(!length) return;
    constexpr wchar_t prefix[]=L"Local\\DiRT2VR.Lap.";
    if(length!=wcslen(prefix)+32 || wcsncmp(name,prefix,wcslen(prefix))) return;
    for(const wchar_t* p=name+wcslen(prefix);*p;++p)
        if(!(*p>=L'0'&&*p<=L'9')&&!(*p>=L'a'&&*p<=L'f')) return;
    gameBase=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]{0x83,0xec,0x08,0x53,0x56,0x32,0xdb,0xf6};
    const unsigned char call[]{0xe8,0x02,0xdd,0xff,0xff};
    if(!SupportedHost() || std::memcmp(gameBase+0x8f3f40,guard,sizeof(guard)) ||
       std::memcmp(gameBase+0x8f6239,call,sizeof(call))) return;
    mapping=OpenFileMappingW(FILE_MAP_WRITE,FALSE,name);
    if(!mapping) return;
    result=static_cast<Result*>(MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,sizeof(Result)));
    if(result && result->magic==0x32504c44 && result->version==1 && result->size==sizeof(Result)) {
        finishCaller=gameBase+0x8f623e;
        auto status=MH_Initialize();
        if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
            status=MH_CreateHook(gameBase+0x8f3f40,reinterpret_cast<void*>(Finished),reinterpret_cast<void**>(&originalFinish));
            if(status==MH_OK)status=EnableRecordedHook(gameBase+0x8f3f40);
        }
        Log("lap result: finish observer hook=%s",MH_StatusToString(status));
        if(status==MH_OK)return;
    }
    if(result){UnmapViewOfFile(result);result=nullptr;}
    CloseHandle(mapping);mapping=nullptr;
}
}
