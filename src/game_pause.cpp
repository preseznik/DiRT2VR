#include "game_pause.h"
#include "common.h"
#include "gfwl_compat.h"
#include "photo_mode.h"
#include <MinHook.h>
#include <atomic>
#include <cstring>
#include <array>
#include <mutex>

namespace vr {
namespace {
using PauseFn=int (__thiscall*)(void*,const void*);
PauseFn original{};
std::atomic<bool> paused{};
bool ready{};
std::mutex presentationMutex;
void* pauseOwner{};
std::array<unsigned char,8> pauseMessage{};
int __fastcall Pause(void* self,void*,const void* message) {
    // Shared by the VR renderer and desktop camera input. Observe the native
    // RenderPauseWorld event so gamepad pause is handled too.
    const bool next=static_cast<const unsigned char*>(message)[4]!=0;
    const int result=original(self,message);
    {
        std::lock_guard lock(presentationMutex);
        pauseOwner=self;std::memcpy(pauseMessage.data(),message,pauseMessage.size());
    }
    if(paused.exchange(next)!=next)Log("pause world=%d",next);
    PhotoModePauseChanged(next);
    return result;
}
}
bool GamePaused(){return paused.load();}
bool PhotoWorldVisible(bool visible) {
    std::lock_guard lock(presentationMutex);
    if(!original || !pauseOwner || !paused.load())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    void* current{};SIZE_T bytes{};
    if(!ReadProcessMemory(GetCurrentProcess(),base+0x11151f8,&current,sizeof(current),&bytes) ||
       bytes!=sizeof(current) || current!=pauseOwner)return false;
    auto message=pauseMessage;message[4]=visible?0:1;
    original(pauseOwner,message.data()); // Do not change the observed native pause lease.
    Log("photo presentation: world_visible=%d native_pause=%d",visible,paused.load());
    return true;
}
bool EnablePauseObserver() {
    if(ready)return true;
    if(!SupportedHost())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char guard[]={0x8b,0x44,0x24,0x04,0x8a,0x50,0x04,0x56,0x8b,0xf1,
        0x8a,0x8e,0x96,0,0,0,0x88,0x96,0x96,0,0,0};
    if(memcmp(base+0x16ec90,guard,sizeof(guard)))return false;
    auto status=MH_Initialize();if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return false;
    status=MH_CreateHook(base+0x16ec90,reinterpret_cast<void*>(Pause),reinterpret_cast<void**>(&original));
    if(status==MH_OK) {
        status=EnableRecordedHook(base+0x16ec90);
        if(status!=MH_OK){MH_RemoveHook(base+0x16ec90);original=nullptr;}
    }
    ready=status==MH_OK;
    Log("pause world hook RVA=0x16ec90 status=%s",MH_StatusToString(status));
    return ready;
}
}
