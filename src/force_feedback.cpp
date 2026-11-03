#include "force_feedback.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <atomic>
#include <cstdint>
#include <cstring>

namespace vr {
namespace {
// DiRT 2 keeps force feedback on only for wheels it knows (its older preset
// wheels, not e.g. a Logitech G920), and the choice made in Options does not
// last (ProJict 2ec8701d). Two places switch the option: when the active
// controller changes, a defaults action (RVA 0x16cee0, run by 0x252b70 from
// 0x256827) sets force_feedback_on_off from the device, off for an unknown
// wheel; and the flow's StateIngameEnableFFB state (run method 0x233b20)
// resumes the player's FFB. Keep the option on after both, as the in-race
// Options switch would.
// The option only reaches the player's FFB processor through the player's
// apply method (RVA 0x34fe80), which sets the processor's enabled flag at race
// start, on unpause and from Options. Direct practice starts driving without
// the enable state, so a processor applied while the option was off stayed
// off. When live driving starts, keep the option on and enable the processor
// as apply would, resuming it too if it is paused while the player is not.
// The player comes from the flow context of the last state entered (every
// state's enter, RVA 0x62d60, keeps it at +0x28).
constexpr unsigned stateRva=0x233b20,enterRva=0x62d60,playersRva=0x107f3d8,defaultRva=0x16cee0;
constexpr unsigned storeIndexRva=0x107dd6c,nameRva=0xeffbd4,typeRva=0x107d160,lookupRva=0x84010;
constexpr unsigned pauseRva=0x6c9dd0,refreshRva=0x6c9d90,enableRva=0x6c9da0,applyRva=0x34fe80,playerPauseRva=0x352fe0;
constexpr unsigned playerTableRva=0xf29a48,playerLoadRva=0x363e20;
constexpr unsigned controllerOffset=0x222c,processorOffset=0x150,enabledOffset=0xa4,pausedOffset=0xa5,deviceOffset=0x34,playerPausedOffset=0x2070;
using RunFn=void (__thiscall*)(void*,void*);
using EnterFn=void (__thiscall*)(void*,void*);
using DefaultFn=int (__thiscall*)(void*);
using LookupFn=void* (__thiscall*)(void*,const void*,const char*);
using SetFn=void (__thiscall*)(void*,int);
using GetFn=int (__thiscall*)(void*);
using FlagFn=void (__thiscall*)(void*,bool);
using RefreshFn=void (__thiscall*)(void*);
RunFn original{};
EnterFn originalEnter{};
DefaultFn originalDefault{};
unsigned char* base{};
// The flow's service context, the same for every state of the session.
std::atomic<unsigned char*> flowContext{};
template<class T> T Read(const void* address) { T value{}; std::memcpy(&value,address,sizeof(value)); return value; }
// States and actions reach the option store the same way (e.g. RVA 0x233c4c,
// 0x16cee0): a context of service slots, indexed by the store's global slot.
void KeepOn(const unsigned char* context,const char* where) {
    __try {
        const auto store=context ? Read<void*>(context+Read<unsigned>(base+storeIndexRva)*4) : nullptr;
        const auto option=store ? reinterpret_cast<LookupFn>(base+lookupRva)(store,base+typeRva,reinterpret_cast<const char*>(base+nameRva)) : nullptr;
        if(!option) { Log("force feedback: %s option not found; game choice kept",where); return; }
        const auto table=Read<void**>(option);
        const int before=reinterpret_cast<GetFn>(table[5])(option);
        if(!before) reinterpret_cast<SetFn>(table[6])(option,1);
        Log("force feedback: %s option=%d, kept on=%d",where,before,reinterpret_cast<GetFn>(table[5])(option));
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        Log("force feedback: %s option update failed; game choice kept",where);
    }
}
// The local player in a flow context, if it is one (the enable state's path).
unsigned char* Player(const unsigned char* context) {
    __try {
        const auto players=context ? Read<unsigned char*>(context+Read<unsigned>(base+playersRva)*4) : nullptr;
        return players && Read<std::uintptr_t>(players)==reinterpret_cast<std::uintptr_t>(base+playerTableRva) ? players : nullptr;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}
void __fastcall Run(void* state,void*,void* argument) {
    // +0x3c is the state's enable_ffb flag; leave disable_force_feedback alone.
    if(Read<unsigned char>(static_cast<unsigned char*>(state)+0x3c)) KeepOn(Read<unsigned char*>(static_cast<unsigned char*>(state)+0x28),"race start");
    original(state,argument);
}
void __fastcall Enter(void* state,void*,void* argument) {
    originalEnter(state,argument);
    const auto context=Read<unsigned char*>(static_cast<unsigned char*>(state)+0x28);
    if(Player(context)) flowContext.store(context);
}
int __fastcall Default(void* action,void*) {
    const int result=originalDefault(action);
    KeepOn(Read<unsigned char*>(static_cast<unsigned char*>(action)+8),"controller default");
    return result;
}
bool Absolute(const unsigned char* operand,unsigned rva) { return Read<std::uintptr_t>(operand)==reinterpret_cast<std::uintptr_t>(base+rva); }
bool Calls(const unsigned char* call,unsigned rva) { return call[0]==0xe8 && call+5+Read<std::int32_t>(call+1)==base+rva; }
// Bytes and relocated operands of the state's run method: its option store and
// lookup, and the player controller → FFB processor path with the pause and
// refresh calls it makes.
bool Matches(const unsigned char* code) {
    constexpr unsigned char head[]{0x8b,0x44,0x24,0x04,0x57,0x50,0x8b,0xf9,0xe8};
    constexpr unsigned char context[]{0x8b,0x4f,0x28,0x8b,0x15};
    constexpr unsigned char controller[]{0x8b,0xb0,0x2c,0x22,0x00,0x00};
    constexpr unsigned char processor[]{0x8b,0x8e,0x50,0x01,0x00,0x00};
    constexpr unsigned char lookup[]{0x83,0xec,0x24,0x8b,0x44,0x24,0x2c,0x53};
    constexpr unsigned char pause[]{0x53,0x8a,0x5c,0x24,0x08,0x56,0x8b,0xf1,0x38,0x9e,0xa5,0x00,0x00,0x00};
    return !std::memcmp(code,head,sizeof head) && code+13+Read<std::int32_t>(code+9)==base+enterRva &&
        !std::memcmp(code+13,context,sizeof context) && Absolute(code+18,playersRva) &&
        !std::memcmp(code+0x1e,controller,sizeof controller) && !std::memcmp(code+0x2c,processor,sizeof processor) &&
        Calls(code+0x36,pauseRva) && Calls(code+0x41,refreshRva) && !std::memcmp(base+pauseRva,pause,sizeof pause) &&
        !std::memcmp(base+lookupRva,lookup,sizeof lookup) &&
        !std::strcmp(reinterpret_cast<const char*>(base+nameRva),"force_feedback_on_off");
}
// The defaults action reads the option through the same store and lookup.
bool MatchesDefault(const unsigned char* code) {
    constexpr unsigned char head[]{0x55,0x56,0x8b,0xf1,0x8b,0x46,0x08,0x8b,0x0d};
    constexpr unsigned char scale[]{0x8b,0x0c,0x88,0x57,0x68};
    return !std::memcmp(code,head,sizeof head) && Absolute(code+9,storeIndexRva) && !std::memcmp(code+13,scale,sizeof scale) &&
        Absolute(code+18,nameRva) && code[22]==0x68 && Absolute(code+23,typeRva) && code[27]==0x33 && code[28]==0xed && code[29]==0xe8 &&
        code+34+Read<std::int32_t>(code+30)==base+lookupRva;
}
// The state enter keeping its flags and timer next to the context; the player
// class (its vtable's load method registers the pause handler that reads the
// player's paused flag); and the apply method passing the option to the
// processor's enable setter.
bool MatchesPlayer() {
    constexpr unsigned char enter[]{0x8b,0x41,0x2c,0x0f,0x57,0xc0,0x89,0x41,0x30,0x8b,0x41,0x24};
    constexpr unsigned char pausedFlag[]{0x53,0x56,0x8b,0xf1,0x8a,0x8e,0x70,0x20,0x00,0x00};
    constexpr unsigned char enable[]{0x8a,0x44,0x24,0x04,0x56,0x8b,0xf1,0x38,0x86,0xa4,0x00,0x00,0x00};
    constexpr unsigned char controller[]{0x8b,0x96,0x2c,0x22,0x00,0x00};
    constexpr unsigned char processor[]{0x8b,0x8a,0x50,0x01,0x00,0x00};
    const auto load=base+playerLoadRva,apply=base+applyRva;
    return !std::memcmp(base+enterRva,enter,sizeof enter) && Absolute(base+playerTableRva+4,playerLoadRva) &&
        load[0xb6b]==0x68 && Absolute(load+0xb6c,playerPauseRva) && !std::memcmp(base+playerPauseRva+10,pausedFlag,sizeof pausedFlag) &&
        !std::memcmp(base+enableRva,enable,sizeof enable) && apply[0x19]==0x68 && Absolute(apply+0x1a,nameRva) &&
        !std::memcmp(apply+0x4d,controller,sizeof controller) && !std::memcmp(apply+0x56,processor,sizeof processor) && Calls(apply+0x5c,enableRva);
}
MH_STATUS Hook(unsigned rva,void* replacement,void** trampoline) {
    auto status=MH_CreateHook(base+rva,replacement,trampoline);
    if(status==MH_OK) status=EnableRecordedHook(base+rva);
    return status;
}
}
void ForceFeedbackLiveDriving() {
    if(!originalEnter) return;
    const auto context=flowContext.load();
    const auto players=Player(context);
    if(!players) { Log("force feedback: live driving without the player; not checked"); return; }
    KeepOn(context,"live driving");
    __try {
        const auto controller=Read<unsigned char*>(players+controllerOffset);
        const auto processor=controller ? Read<unsigned char*>(controller+processorOffset) : nullptr;
        if(!processor) { Log("force feedback: live driving without a player FFB processor"); return; }
        const bool enabled=Read<unsigned char>(processor+enabledOffset)!=0,paused=Read<unsigned char>(processor+pausedOffset)!=0;
        const bool playerPaused=Read<unsigned char>(players+playerPausedOffset)!=0;
        if(!enabled) reinterpret_cast<FlagFn>(base+enableRva)(processor,true);
        if(paused && !playerPaused) {
            reinterpret_cast<FlagFn>(base+pauseRva)(processor,false);
            reinterpret_cast<RefreshFn>(base+refreshRva)(processor);
        }
        Log("force feedback: live driving device=%d enabled=%d->%d paused=%d->%d player_paused=%d",Read<void*>(processor+deviceOffset)!=nullptr,
            enabled,Read<unsigned char>(processor+enabledOffset)!=0,paused,Read<unsigned char>(processor+pausedOffset)!=0,playerPaused);
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        Log("force feedback: live driving check failed");
    }
}
bool ForceFeedbackRequested() {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_FORCE_FEEDBACK",value,8)==1 && value[0]==L'1';
}
bool EnableForceFeedback() {
    if(original || !ForceFeedbackRequested()) return true;
    if(!SupportedHost() || !EnableGfwlCompatibility()) return false;
    base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    if(!Matches(base+stateRva) || !MatchesDefault(base+defaultRva) || !MatchesPlayer()) { Log("force feedback: unexpected game code; game choice kept"); return false; }
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=Hook(defaultRva,reinterpret_cast<void*>(Default),reinterpret_cast<void**>(&originalDefault));
        if(status==MH_OK) status=Hook(stateRva,reinterpret_cast<void*>(Run),reinterpret_cast<void**>(&original));
        if(status==MH_OK) status=Hook(enterRva,reinterpret_cast<void*>(Enter),reinterpret_cast<void**>(&originalEnter));
    }
    Log("force feedback: keep-on hooks=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
