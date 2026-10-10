#include "gfwl_compat.h"
#include "direct_menus.h"
#include "replay_storage.h"
#include "common.h"
#include <MinHook.h>
#include <cstring>
#include <cwchar>
#include <cmath>

namespace vr {
#ifdef DIRT2VR_REPLAY_ARCHIVE_LAB
bool ReplayArchiveLab(unsigned char* inner);
bool EnableReplayArchiveLab();
#endif
namespace {
using ShutdownFn=void (__thiscall*)(void*,void*);
ShutdownFn originalShutdown{};
HANDLE returnEvent{};
HANDLE profileFailureEvent{};
DWORD returnOwner{};
using ReplayCheckFn=void* (__thiscall*)(void*,void*);
ReplayCheckFn originalReplayCheck{};
template<class T> T ReplayField(const void* object,unsigned offset) {
    T value{};
    std::memcpy(&value,static_cast<const unsigned char*>(object)+offset,sizeof(value));
    return value;
}
bool CompleteRecording(void* state) {
    // Native full replay has its own disk writer. Its sticky error includes cache
    // exhaustion; the game's ordinary availability check only checks the file.
    __try {
        auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
        const auto registry=ReplayField<void**>(state,0x28);
        const auto index=*reinterpret_cast<unsigned*>(base+0x107f3d8);
        if(!registry || index>4096) return false;
        const auto game=registry[index];
        const auto root=ReplayField<unsigned char*>(game,0x229c);
        if(!root) return false;
        const auto inner=root+0x10;
        const auto writer=ReplayField<unsigned char*>(inner,0x1a4);
        const auto start=ReplayField<double>(inner,0x260);
        const auto end=ReplayField<double>(inner,0x280);
        const auto duration=ReplayField<double>(inner,0x1c0);
        const auto bytes=ReplayField<unsigned>(inner,0x288);
        const auto capacity=ReplayField<unsigned>(inner+0x20,0xc);
        const bool complete=writer && !ReplayField<unsigned char>(writer,0x80) &&
            ReplayField<unsigned char>(inner+0x20,0x120) &&
            std::isfinite(start) && start>=0 && start<0.1 &&
            std::isfinite(end) && end>start && std::isfinite(duration) &&
            std::abs(end-duration)<0.1 && bytes>0 && bytes<capacity &&
            ReplayField<void*>(inner,0x274)!=nullptr;
        Log("full replay: complete=%d start=%.6f end=%.6f duration=%.6f bytes=%u capacity=%u writer_error=%d",
            complete,start,end,duration,bytes,capacity,writer?ReplayField<unsigned char>(writer,0x80):-1);
#ifdef DIRT2VR_REPLAY_ARCHIVE_LAB
        if(complete && !ReplayArchiveLab(inner)) return false;
#endif
        if(complete && !ReplayStorageComplete(inner)) return false;
        return complete;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        Log("full replay: recording metadata unavailable");
        return false;
    }
}
bool RearmReplay(void* state) {
    // Esc puts the native replay input controller into mode 0 (exit). Changing
    // only the replay clock to Play makes StateReplay immediately exit again.
    __try {
        auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
        constexpr unsigned char guard[]{0x8b,0x44,0x24,0x04,0x3b,0x41,0x28,0x74};
        if(std::memcmp(base+0x3a8470,guard,sizeof guard)) return false;
        const auto registry=ReplayField<void**>(state,0x28);
        const auto index=*reinterpret_cast<unsigned*>(base+0x107f3d8);
        if(!registry || index>4096) return false;
        const auto game=registry[index];
        if(!game) return false;
        const auto root=ReplayField<unsigned char*>(game,0x229c);
        const auto controls=ReplayField<void*>(game,0x2300);
        if(!root || !controls) return false;
        const auto playbackState=ReplayField<int>(root,0xa60);
        if(playbackState!=1 && playbackState!=2) return false;
        const auto time=ReplayField<double>(root+0x10,0x1b8);
        const auto mode=ReplayField<int>(controls,0x28);
        // Native full-replay entry uses controller mode 6 (742f27), whereas
        // mode 1 belongs to ordinary play and would lose replay shortcuts.
        reinterpret_cast<void(__thiscall*)(void*,int)>(base+0x3a8470)(controls,6);
        const bool ready=ReplayField<int>(controls,0x28)==6;
        Log("full replay: resume controls=%d->%d time=%.6f preserved=%d",mode,
            ReplayField<int>(controls,0x28),time,time==ReplayField<double>(root+0x10,0x1b8));
        return ready;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        Log("full replay: resume controls unavailable");return false;
    }
}
void* __fastcall ReplayCheck(void* state,void*,void* result) {
    if(std::memcmp(static_cast<const char*>(state)+4,"d2vr_record_begin",sizeof("d2vr_record_begin"))==0)
        ReplayStorageBegin();
    auto value=originalReplayCheck(state,result);
    if(std::memcmp(static_cast<const char*>(state)+4,"d2vr_replay_rearm",sizeof("d2vr_replay_rearm"))==0) {
        using Assign=void (__thiscall*)(void*,const char*);
        auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
        reinterpret_cast<Assign>(base+0x6d4830)(result,RearmReplay(state)?"enabled":"disabled");
    }
    if(std::memcmp(static_cast<const char*>(state)+4,"d2vr_full_replay_check",sizeof("d2vr_full_replay_check"))==0 &&
       !CompleteRecording(state)) {
        using Assign=void (__thiscall*)(void*,const char*);
        auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
        reinterpret_cast<Assign>(base+0x6d4830)(result,"disabled");
    }
    return value;
}
void __fastcall Shutdown(void* state,void*,void* argument) {
    // State IDs occupy the inline 32-byte field immediately after the vtable.
    // Alt+F4 and the game's ordinary exit_game state must not cause a relaunch.
    if(profileFailureEvent && std::memcmp(static_cast<const char*>(state)+4,"d2vr_profile_failed",sizeof("d2vr_profile_failed"))==0) {
        const bool sent=SetEvent(profileFailureEvent)!=FALSE;
        Log("direct session: profile loading/sign-in failed; normal shutdown requested=%d",sent);
        CloseHandle(profileFailureEvent); profileFailureEvent=nullptr;
    }
    if(returnEvent && std::memcmp(static_cast<const char*>(state)+4,"d2vr_return",12)==0) {
        if(returnOwner) AllowSetForegroundWindow(returnOwner);
        const bool sent=SetEvent(returnEvent)!=FALSE;
        Log("direct session: return to menus requested=%d; normal game shutdown",sent);
        CloseHandle(returnEvent); returnEvent=nullptr;
    }
    originalShutdown(state,argument);
}
}
bool EnableDirectReturn() {
    wchar_t name[128]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_RETURN_CHANNEL",name,128);
    if(!length) return true; // Existing diagnostic launches do not use this feature.
    if(originalShutdown) return true;
    constexpr wchar_t prefix[]=L"Local\\DiRT2VR.Return.";
    if(length!=wcslen(prefix)+32 || wcsncmp(name,prefix,wcslen(prefix))!=0) return false;
    for(const wchar_t* p=name+wcslen(prefix);*p;++p)
        if(!(*p>=L'0'&&*p<=L'9')&&!(*p>=L'a'&&*p<=L'f')) return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x8b,0x41,0x24,0x83,0xec,0x0c,0xa8,0x02};
    const unsigned char replayGuard[]={0x51,0x56,0x8d,0x44,0x24,0x04,0x50,0x8b,0xf1};
    if(!SupportedHost() || std::memcmp(base+0x22ca10,guard,sizeof(guard)) ||
       std::memcmp(base+0x23ec70,replayGuard,sizeof(replayGuard)) ||
       *reinterpret_cast<void**>(base+0xf18f94)!=base+0x22ca10) return false;
    returnEvent=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);
    if(!returnEvent) return false;
    wchar_t failureName[160]{};
    swprintf_s(failureName,L"%s.ProfileFailure",name);
    profileFailureEvent=OpenEventW(EVENT_MODIFY_STATE,FALSE,failureName);
    wchar_t owner[16]{}; wchar_t* end{};
    if(GetEnvironmentVariableW(L"DIRT2VR_RETURN_PID",owner,16)<16) {
        auto value=wcstoul(owner,&end,10);
        if(end!=owner && !*end) returnOwner=value;
    }
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=MH_CreateHook(base+0x22ca10,reinterpret_cast<void*>(Shutdown),reinterpret_cast<void**>(&originalShutdown));
        if(status==MH_OK) status=MH_CreateHook(base+0x23ec70,reinterpret_cast<void*>(ReplayCheck),reinterpret_cast<void**>(&originalReplayCheck));
        if(status==MH_OK) status=EnableRecordedHook(base+0x22ca10);
        if(status==MH_OK) status=EnableRecordedHook(base+0x23ec70);
        if(status==MH_OK && !EnableReplayStorage()) status=MH_ERROR_UNSUPPORTED_FUNCTION;
#ifdef DIRT2VR_REPLAY_ARCHIVE_LAB
        if(status==MH_OK && !EnableReplayArchiveLab()) status=MH_ERROR_UNSUPPORTED_FUNCTION;
#endif
    }
    if(status!=MH_OK) {
        CloseHandle(returnEvent); returnEvent=nullptr;
        if(profileFailureEvent) { CloseHandle(profileFailureEvent); profileFailureEvent=nullptr; }
    }
    Log("direct session: return-to-menus hook=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
