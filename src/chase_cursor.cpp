#include "chase_cursor.h"
#include "game_pause.h"
#include <atomic>

namespace vr {
namespace {
SRWLOCK windowLock=SRWLOCK_INIT;
HWND attached{};
WNDPROC previous{};
std::atomic<ULONGLONG> activeTick{};
// These are only accessed by the window thread.
HCURSOR savedCursor{};
bool hidden{};
bool windowInteraction{};
UINT_PTR timer{};
UINT WakeMessage() {
    // First used by the running game, never from a DLL global initializer.
    static const UINT value=RegisterWindowMessageW(L"DiRT2VR.ChaseCursor.Refresh.1");
    return value;
}

void Restore() {
    if(!hidden)return;
    SetCursor(savedCursor);
    savedCursor=nullptr;hidden=false;
}
bool Inside(HWND window) {
    POINT point{};RECT client{};
    if(GetForegroundWindow()!=window || IsIconic(window) || !GetCursorPos(&point) ||
       WindowFromPoint(point)!=window || !GetClientRect(window,&client) || !ScreenToClient(window,&point))return false;
    return PtInRect(&client,point)!=FALSE;
}
bool Refresh(HWND window) {
    const auto tick=activeTick.load();
    if(!tick || GetTickCount64()-tick>250 || windowInteraction || GamePaused() || !Inside(window)) {Restore();return false;}
    // Do not touch ShowCursor's counter or constrain the pointer. WM_SETCURSOR
    // handles movement; the timer also handles a stationary pointer and pause.
    const auto cursor=GetCursor();
    if(!hidden || cursor)savedCursor=cursor;
    hidden=true;SetCursor(nullptr);return true;
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM key,LPARAM flags) {
    AcquireSRWLockShared(&windowLock);
    const auto original=previous;
    ReleaseSRWLockShared(&windowLock);
    if(message==WakeMessage()) {
        if(!timer)timer=SetTimer(window,reinterpret_cast<UINT_PTR>(&WindowProc),50,nullptr);
        if(timer)Refresh(window); // Fail visible if the recovery timer cannot start.
        return 0;
    }
    if(message==WM_TIMER && timer && key==timer) {Refresh(window);return 0;}
    if(message==WM_SETCURSOR && timer) {
        if(LOWORD(flags)==HTCLIENT && Refresh(window))return TRUE;
        Restore();
    }
    if(message==WM_KILLFOCUS || (message==WM_ACTIVATEAPP && !key) || message==WM_ENTERMENULOOP || message==WM_ENTERSIZEMOVE) {
        activeTick=0;Restore();
    }
    if(message==WM_ENTERMENULOOP || message==WM_ENTERSIZEMOVE)windowInteraction=true;
    if(message==WM_EXITMENULOOP || message==WM_EXITSIZEMOVE)windowInteraction=false;
    if(message==WM_NCDESTROY) {
        activeTick=0;Restore();
        if(timer)KillTimer(window,timer);
        timer=0;windowInteraction=false;
        AcquireSRWLockExclusive(&windowLock);
        attached=nullptr;previous=nullptr;
        ReleaseSRWLockExclusive(&windowLock);
    }
    return original?CallWindowProcW(original,window,message,key,flags):DefWindowProcW(window,message,key,flags);
}
}
void UpdateChaseCursor(HWND window,bool active) {
    const auto old=activeTick.exchange(active?GetTickCount64():0);
    const auto wake=WakeMessage();
    if(!wake)return;
    bool installed=false;
    AcquireSRWLockExclusive(&windowLock);
    if(!attached && active && window) {
        DWORD pid{};GetWindowThreadProcessId(window,&pid);
        // Never attach to another application or to an owned dialog/overlay.
        if(pid==GetCurrentProcessId() && !GetWindow(window,GW_OWNER)) {
            previous=reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window,GWLP_WNDPROC));
            if(previous) {
                SetLastError(0);
                const auto proc=SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(&WindowProc));
                if(proc || !GetLastError()) {
                    if(proc)previous=reinterpret_cast<WNDPROC>(proc);
                    attached=window;installed=true;
                } else previous=nullptr;
            }
        }
    }
    const auto target=attached;
    ReleaseSRWLockExclusive(&windowLock);
    if(target && (installed || (old!=0)!=active))PostMessageW(target,wake,0,0);
}
}
