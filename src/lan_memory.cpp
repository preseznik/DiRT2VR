#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <algorithm>
#include <atomic>
#include <cstring>
#include <mutex>
#include <vector>

// VR LAN (ProJict 8ac0f636): the race start checks game files, and a VR
// session's edited cameras.xml / effects.xml end it right after the countdown.
// LAN therefore keeps those files original and applies the same changes in
// memory: no motion blur (it smears between eyes) and no head camera shake.
namespace vr {
bool EnableLanMemory();
namespace {
constexpr unsigned blurCtorRva=0x4edac0, blurVtableRva=0xf399d0, blurLength=0x14;
constexpr unsigned shakeBindRva=0x66ad20, headBindRva=0x6faf30;
using CtorFn=void* (__thiscall*)(void*);
using BindFn=void (__thiscall*)(void*,void*);
CtorFn blurCtor{};
BindFn shakeBind{},headBind{};
unsigned char* base{};
std::mutex mutex;
std::vector<unsigned char*> blurGroups;
std::atomic<bool> watching{};
bool HasVtable(const unsigned char* object,unsigned rva) {
    // Effects reload with the track; only write to objects that are still groups.
    unsigned vtable{}; SIZE_T read{};
    return ReadProcessMemory(GetCurrentProcess(),object,&vtable,sizeof(vtable),&read) && vtable==unsigned(uintptr_t(base)+rva);
}
DWORD WINAPI HoldBlur(void*) {
    // effects.xml values are parsed into the group after construction.
    for(;;) {
        {
            std::lock_guard lock(mutex);
            const float zero=0;
            for(auto* group:blurGroups) if(HasVtable(group,blurVtableRva) && memcmp(group+blurLength,&zero,sizeof(zero)))
                memcpy(group+blurLength,&zero,sizeof(zero));
        }
        Sleep(100);
    }
}
void* __fastcall BlurCtor(void* self,void*) {
    auto result=blurCtor(self);
    std::lock_guard lock(mutex);
    auto* group=static_cast<unsigned char*>(self);
    if(std::find(blurGroups.begin(),blurGroups.end(),group)==blurGroups.end()) blurGroups.push_back(group);
    if(!watching.exchange(true)) if(auto thread=CreateThread(nullptr,0,HoldBlur,nullptr,0,nullptr)) CloseHandle(thread);
    return result;
}
void __fastcall ShakeBind(void* self,void*,void* visitor) {
    shakeBind(self,visitor);
    // AccelerationBasedShake: 15 scalars at +0x40 and delayEnabled at +0x7c.
    auto* shake=static_cast<unsigned char*>(self);
    memset(shake+0x40,0,15*sizeof(float)); shake[0x7c]=0;
}
void __fastcall HeadBind(void* self,void*,void* visitor) {
    headBind(self,visitor);
    // The values the VR cameras.xml patch sets: no body/head offset or look.
    auto* head=static_cast<unsigned char*>(self);
    const float zero=0;
    for(unsigned field:{0x25cu,0x274u,0x1a2cu}) memcpy(head+field,&zero,sizeof(zero));
    head[0x28c]=0; // headBuffeting
}
bool Hook(unsigned rva,const unsigned char* guard,size_t bytes,void* replacement,void** original) {
    if(memcmp(base+rva,guard,bytes)) { Log("LAN memory: unexpected code at RVA %x",rva); return false; }
    auto result=MH_CreateHook(base+rva,replacement,original);
    if(result==MH_OK) result=EnableRecordedHook(base+rva);
    Log("LAN memory: hook RVA %x=%s",rva,MH_StatusToString(result));
    return result==MH_OK;
}
}
bool EnableLanMemory() {
    char value[4]{};
    if(GetEnvironmentVariableA("DIRT2VR_LAN_MEMORY",value,sizeof(value))!=1 || value[0]!='1') return true;
    if(!SupportedHost()) return false;
    base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char blur[]={0x53,0x56,0x8b,0xf1,0x33,0xc9,0x8d,0x46,0x18,0x89,0x4e,0x04,0x89,0x4e,0x08,0x89,0x4e,0x0c};
    // Stops before the push of an absolute address, which differs when the game is relocated.
    const unsigned char shake[]={0x56,0x8b,0x74,0x24,0x08,0x8b,0x06,0x8b,0x50,0x18,0x57,0x8b,0xf9,0x68};
    const unsigned char head[]={0x56,0x8b,0x74,0x24,0x08,0x8b,0x06,0x8b,0x50,0x10,0x57,0x8b,0xf9,0x8d,0x4f,0x7b};
    return Hook(blurCtorRva,blur,sizeof(blur),reinterpret_cast<void*>(BlurCtor),reinterpret_cast<void**>(&blurCtor)) &&
        Hook(shakeBindRva,shake,sizeof(shake),reinterpret_cast<void*>(ShakeBind),reinterpret_cast<void**>(&shakeBind)) &&
        Hook(headBindRva,head,sizeof(head),reinterpret_cast<void*>(HeadBind),reinterpret_cast<void**>(&headBind));
}
}
