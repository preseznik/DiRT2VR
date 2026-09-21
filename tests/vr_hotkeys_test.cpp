#include "vr_hotkeys.h"
#include <cstdio>
#include <cstdlib>
#define CHECK(x) do { if(!(x)) { std::fprintf(stderr,"Failed line %d: %s\n",__LINE__,#x); std::exit(1); } } while(0)
unsigned forwardedKeys{},menuCommands{};
LRESULT CALLBACK Original(HWND window,UINT message,WPARAM key,LPARAM flags) {
    if(message==WM_KEYDOWN || message==WM_KEYUP || message==WM_SYSKEYDOWN || message==WM_SYSKEYUP) ++forwardedKeys;
    if(message==WM_SYSCOMMAND && (key&0xfff0)==SC_KEYMENU) { ++menuCommands; return 0; }
    return DefWindowProcW(window,message,key,flags);
}
int main() {
    WNDCLASSW type{}; type.lpfnWndProc=Original; type.hInstance=GetModuleHandleW(nullptr); type.lpszClassName=L"DiRT2VRHotkeyTest";
    CHECK(RegisterClassW(&type));
    HWND window=CreateWindowW(type.lpszClassName,L"Hidden regression test",0,0,0,100,100,nullptr,nullptr,type.hInstance,nullptr);
    CHECK(window);
    // Reproduce Windows' default F10 system-menu path before installing us.
    SendMessageW(window,WM_KEYDOWN,VK_F10,1);
    SendMessageW(window,WM_KEYUP,VK_F10,static_cast<LPARAM>(0xc0000001u));
    CHECK(menuCommands==1);
    forwardedKeys=menuCommands=0;
    CHECK(vr::AttachHotkeys(window));
    for(unsigned iteration=0;iteration<2;++iteration) {
        SendMessageW(window,WM_KEYDOWN,VK_F10,1);
        SendMessageW(window,WM_KEYDOWN,VK_F10,1|(1L<<30)); // Auto-repeat.
        SendMessageW(window,WM_KEYUP,VK_F10,static_cast<LPARAM>(0xc0000001u));
        CHECK(vr::ConsumeHotkeys()==vr::Recenter && vr::ConsumeHotkeys()==0);
        CHECK(forwardedKeys==0 && menuCommands==0);
    }
    SendMessageW(window,WM_SYSKEYDOWN,VK_F9,1);
    SendMessageW(window,WM_SYSKEYUP,VK_F9,static_cast<LPARAM>(0xc0000001u));
    CHECK(vr::ConsumeHotkeys()==vr::ToggleScreen && forwardedKeys==0);
    SendMessageW(window,WM_KEYDOWN,'A',1); SendMessageW(window,WM_KEYUP,'A',static_cast<LPARAM>(0xc0000001u));
    CHECK(forwardedKeys==2 && vr::ConsumeHotkeys()==0);
    CHECK(DestroyWindow(window));
    CHECK(!vr::ConfigureHotkeys(VK_F8,0,VK_F8,0));
    CHECK(!vr::ConfigureHotkeys(256,0,VK_F7,0));
    CHECK(vr::ConfigureHotkeys(VK_F8,0,VK_F7,0));
    window=CreateWindowW(type.lpszClassName,L"Remapped keys",0,0,0,100,100,nullptr,nullptr,type.hInstance,nullptr);
    CHECK(vr::AttachHotkeys(window));
    forwardedKeys=0;
    SendMessageW(window,WM_KEYDOWN,VK_F8,1);
    SendMessageW(window,WM_KEYUP,VK_F8,static_cast<LPARAM>(0xc0000001u));
    CHECK(vr::ConsumeHotkeys()==vr::ToggleScreen && forwardedKeys==0);
    SendMessageW(window,WM_KEYDOWN,VK_F7,1);
    SendMessageW(window,WM_KEYUP,VK_F7,static_cast<LPARAM>(0xc0000001u));
    CHECK(vr::ConsumeHotkeys()==vr::Recenter && forwardedKeys==0);
    SendMessageW(window,WM_KEYDOWN,VK_F9,1);
    SendMessageW(window,WM_KEYUP,VK_F9,static_cast<LPARAM>(0xc0000001u));
    CHECK(vr::ConsumeHotkeys()==0 && forwardedKeys==2);
    CHECK(DestroyWindow(window));
    CHECK(UnregisterClassW(type.lpszClassName,type.hInstance));
    puts("VR shortcuts consumed, repeats suppressed, ordinary keys forwarded; F10 cannot enter system menu.");
}
