#include "vr_hotkeys.h"
#include <atomic>

namespace vr {
namespace {
HWND gameWindow{};
WNDPROC previous{};
std::atomic<unsigned> pending{};
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM key,LPARAM flags) {
    const bool down=message==WM_KEYDOWN || message==WM_SYSKEYDOWN;
    const bool up=message==WM_KEYUP || message==WM_SYSKEYUP;
    if((down || up) && (key==VK_F9 || key==VK_F10)) {
        if(down && !(flags&(1L<<30))) {
            if(key==VK_F9) pending.fetch_xor(ToggleScreen);
            else pending.fetch_or(Recenter);
        }
        // F10 passed to DefWindowProc enters SC_KEYMENU on key-up and can
        // block the game's render loop. Consume BOTH edges of our shortcuts.
        return 0;
    }
    const auto result=CallWindowProcW(previous,window,message,key,flags);
    if(message==WM_NCDESTROY) { gameWindow=nullptr; previous=nullptr; pending=0; }
    return result;
}
}
bool AttachHotkeys(HWND window) {
    if(gameWindow) return gameWindow==window;
    DWORD process{};
    if(!window || !GetWindowThreadProcessId(window,&process) || process!=GetCurrentProcessId()) return false;
    previous=reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window,GWLP_WNDPROC));
    if(!previous) return false;
    gameWindow=window;
    SetLastError(0);
    const auto old=SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(WindowProc));
    if(!old && GetLastError()) { gameWindow=nullptr; previous=nullptr; return false; }
    if(old) previous=reinterpret_cast<WNDPROC>(old);
    return true;
}
unsigned ConsumeHotkeys() { return pending.exchange(0); }
}
