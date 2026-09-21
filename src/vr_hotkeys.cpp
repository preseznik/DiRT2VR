#include "vr_hotkeys.h"
#include <atomic>
#include <cwchar>

namespace vr {
namespace {
HWND gameWindow{};
WNDPROC previous{};
std::atomic<unsigned> pending{};
struct Binding { unsigned key,modifiers; bool held{}; };
Binding bindings[2]={{VK_F9,0},{VK_F10,0}};
bool configured{};
unsigned Modifiers() {
    return (GetKeyState(VK_CONTROL)<0 ? 1u:0u) | (GetKeyState(VK_MENU)<0 ? 2u:0u) | (GetKeyState(VK_SHIFT)<0 ? 4u:0u);
}
unsigned ControllerActions() {
    // ABI shared with the VB session manager: magic, version, toggle, recenter.
    struct Commands { DWORD magic,version; volatile DWORD toggle,recenter; };
    static const Commands* commands=[]() -> const Commands* {
        wchar_t name[128]{};
        if(!GetEnvironmentVariableW(L"DIRT2VR_INPUT_CHANNEL",name,128)) return nullptr;
        HANDLE mapping=OpenFileMappingW(FILE_MAP_READ,FALSE,name);
        if(!mapping) return nullptr;
        auto view=static_cast<const Commands*>(MapViewOfFile(mapping,FILE_MAP_READ,0,0,sizeof(Commands)));
        CloseHandle(mapping);
        if(view && (view->magic!=0x32565244 || view->version!=1)) { UnmapViewOfFile(view); return nullptr; }
        return view; // Process-owned, no loader-lock teardown.
    }();
    if(!commands) return 0;
    static DWORD toggle{},recenter{};
    DWORD nextToggle=commands->toggle,nextRecenter=commands->recenter;
    unsigned result=((nextToggle-toggle)&1u ? ToggleScreen:0) | (nextRecenter!=recenter ? Recenter:0);
    toggle=nextToggle; recenter=nextRecenter;
    DWORD foreground{}; GetWindowThreadProcessId(GetForegroundWindow(),&foreground);
    return foreground==GetCurrentProcessId() ? result:0;
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM key,LPARAM flags) {
    const bool down=message==WM_KEYDOWN || message==WM_SYSKEYDOWN;
    const bool up=message==WM_KEYUP || message==WM_SYSKEYUP;
    for(unsigned i=0;i<2;++i) {
        auto& binding=bindings[i];
        if((down || up) && key==binding.key && (binding.held || Modifiers()==binding.modifiers)) {
            if(down && !(flags&(1L<<30)) && !binding.held) {
                binding.held=true;
                if(i==0) pending.fetch_xor(ToggleScreen); else pending.fetch_or(Recenter);
            }
            if(up) binding.held=false;
            // Consume both edges, including F10 and release after modifiers lift.
            return 0;
        }
    }
    if(message==WM_KILLFOCUS) { for(auto& binding:bindings) binding.held=false; pending=0; }
    const auto result=CallWindowProcW(previous,window,message,key,flags);
    if(message==WM_NCDESTROY) { gameWindow=nullptr; previous=nullptr; pending=0; }
    return result;
}
}
bool ConfigureHotkeys(unsigned toggleKey,unsigned toggleModifiers,unsigned recenterKey,unsigned recenterModifiers) {
    if(gameWindow || !toggleKey || toggleKey>255 || !recenterKey || recenterKey>255 ||
       toggleModifiers>7 || recenterModifiers>7 || (toggleKey==recenterKey && toggleModifiers==recenterModifiers)) return false;
    bindings[0]={toggleKey,toggleModifiers}; bindings[1]={recenterKey,recenterModifiers}; configured=true; return true;
}
bool AttachHotkeys(HWND window) {
    if(gameWindow) return gameWindow==window;
    DWORD process{};
    if(!window || !GetWindowThreadProcessId(window,&process) || process!=GetCurrentProcessId()) return false;
    if(!configured) {
        wchar_t text[64]{}; unsigned tk=VK_F9,tm=0,rk=VK_F10,rm=0;
        if(GetEnvironmentVariableW(L"DIRT2VR_KEYS",text,64)) {
            if(swscanf_s(text,L"%u:%u,%u:%u",&tk,&tm,&rk,&rm)!=4 || !ConfigureHotkeys(tk,tm,rk,rm)) return false;
        }
    }
    previous=reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window,GWLP_WNDPROC));
    if(!previous) return false;
    gameWindow=window;
    SetLastError(0);
    const auto old=SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(WindowProc));
    if(!old && GetLastError()) { gameWindow=nullptr; previous=nullptr; return false; }
    if(old) previous=reinterpret_cast<WNDPROC>(old);
    return true;
}
unsigned ConsumeHotkeys() {
    const auto keyboard=pending.exchange(0),controller=ControllerActions();
    return ((keyboard^controller)&ToggleScreen) | ((keyboard|controller)&Recenter);
}
}
