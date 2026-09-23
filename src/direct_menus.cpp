#include "gfwl_compat.h"
#include "direct_menus.h"
#include "common.h"
#include <MinHook.h>
#include <cstring>
#include <cwchar>

namespace vr {
namespace {
using ShutdownFn=void (__thiscall*)(void*,void*);
ShutdownFn originalShutdown{};
HANDLE returnEvent{};
DWORD returnOwner{};
void __fastcall Shutdown(void* state,void*,void* argument) {
    // State IDs occupy the inline 32-byte field immediately after the vtable.
    // Alt+F4 and the game's ordinary exit_game state must not cause a relaunch.
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
    if(!SupportedHost() || std::memcmp(base+0x22ca10,guard,sizeof(guard)) ||
       *reinterpret_cast<void**>(base+0xf18f94)!=base+0x22ca10) return false;
    returnEvent=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);
    if(!returnEvent) return false;
    wchar_t owner[16]{}; wchar_t* end{};
    if(GetEnvironmentVariableW(L"DIRT2VR_RETURN_PID",owner,16)<16) {
        auto value=wcstoul(owner,&end,10);
        if(end!=owner && !*end) returnOwner=value;
    }
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=MH_CreateHook(base+0x22ca10,reinterpret_cast<void*>(Shutdown),reinterpret_cast<void**>(&originalShutdown));
        if(status==MH_OK) status=EnableRecordedHook(base+0x22ca10);
    }
    if(status!=MH_OK) { CloseHandle(returnEvent); returnEvent=nullptr; }
    Log("direct session: return-to-menus hook=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
