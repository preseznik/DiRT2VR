#include "background_play.h"
#include "common.h"
#include "gfwl_compat.h"
#include <cstring>

namespace vr {
bool BackgroundPlayRequested() {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_BACKGROUND_PLAY",value,8)==1 && value[0]==L'1';
}
bool EnableBackgroundPlay() {
    static bool ready{};
    if(ready || !BackgroundPlayRequested())return true;
    if(!SupportedHost() || !EnableGfwlCompatibility())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    // Only the application's focus-loss handler calls this routine. Leave its
    // input/audio notifications and the application's real focus state intact.
    // Omit its deferred pause-menu request and replay-only pause transition.
    // Explicit pause/menu/replay controls call other sites and are unchanged.
    constexpr unsigned char pauseMenu[]{0xc6,0x86,0x50,0x2f,0,0,1};
    constexpr unsigned char pauseReplay[]{0x6a,2,0xe8,0xc2,0x98,0x61,0};
    auto menu=base+0x3205ea, replay=base+0x320617;
    if(std::memcmp(menu,pauseMenu,sizeof pauseMenu) || std::memcmp(replay,pauseReplay,sizeof pauseReplay))return false;
    DWORD protection{};
    const auto length=static_cast<SIZE_T>(replay+sizeof pauseReplay-menu);
    if(!VirtualProtect(menu,length,PAGE_EXECUTE_READWRITE,&protection))return false;
    for(unsigned i=0;i<sizeof pauseMenu;++i)RecordCodeByte(menu+i,menu[i],0x90);
    for(unsigned i=0;i<sizeof pauseReplay;++i)RecordCodeByte(replay+i,replay[i],0x90);
    std::memset(menu,0x90,sizeof pauseMenu);
    std::memset(replay,0x90,sizeof pauseReplay);
    DWORD previous{};
    const bool protectedAgain=VirtualProtect(menu,length,protection,&previous)!=FALSE;
    const bool flushed=FlushInstructionCache(GetCurrentProcess(),menu,length)!=FALSE;
    ready=protectedAgain && flushed;
    Log("background play: focus-loss pause suppressed; native focus/input retained; protection_restored=%d",protectedAgain);
    return ready;
}
}
