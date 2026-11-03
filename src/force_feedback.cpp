#include "force_feedback.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <cstdint>
#include <cstring>

namespace vr {
namespace {
// DiRT 2 keeps force feedback on only for wheels it knows (its older preset
// wheels, not e.g. a Logitech G920), and the choice made in Options does not
// last (ProJict 2ec8701d). Two places switch it: when the active controller
// changes, a defaults action (RVA 0x16cee0, run by 0x252b70 from 0x256827) sets
// force_feedback_on_off from the device, off for an unknown wheel; and at each
// race start the flow's StateIngameEnableFFB state (run method 0x233b20)
// resumes the player's FFB. Keep the option on after the first and before the
// second, as the in-race Options switch would.
constexpr unsigned stateRva=0x233b20,baseRva=0x62d60,playersRva=0x107f3d8,defaultRva=0x16cee0;
constexpr unsigned storeIndexRva=0x107dd6c,nameRva=0xeffbd4,typeRva=0x107d160,lookupRva=0x84010;
using RunFn=void (__thiscall*)(void*,void*);
using DefaultFn=int (__thiscall*)(void*);
using LookupFn=void* (__thiscall*)(void*,const void*,const char*);
using SetFn=void (__thiscall*)(void*,int);
using GetFn=int (__thiscall*)(void*);
RunFn original{};
DefaultFn originalDefault{};
unsigned char* base{};
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
void __fastcall Run(void* state,void*,void* argument) {
    // +0x3c is the state's enable_ffb flag; leave disable_force_feedback alone.
    if(Read<unsigned char>(static_cast<unsigned char*>(state)+0x3c)) KeepOn(Read<unsigned char*>(static_cast<unsigned char*>(state)+0x28),"race start");
    original(state,argument);
}
int __fastcall Default(void* action,void*) {
    const int result=originalDefault(action);
    KeepOn(Read<unsigned char*>(static_cast<unsigned char*>(action)+8),"controller default");
    return result;
}
bool Absolute(const unsigned char* operand,unsigned rva) { return Read<std::uintptr_t>(operand)==reinterpret_cast<std::uintptr_t>(base+rva); }
// Bytes and relocated absolute operands of the state's run method and the lookup it shares with other states.
bool Matches(const unsigned char* code) {
    constexpr unsigned char head[]{0x8b,0x44,0x24,0x04,0x57,0x50,0x8b,0xf9,0xe8};
    constexpr unsigned char context[]{0x8b,0x4f,0x28,0x8b,0x15};
    constexpr unsigned char lookup[]{0x83,0xec,0x24,0x8b,0x44,0x24,0x2c,0x53};
    return !std::memcmp(code,head,sizeof head) && code+13+Read<std::int32_t>(code+9)==base+baseRva &&
        !std::memcmp(code+13,context,sizeof context) && Absolute(code+18,playersRva) &&
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
MH_STATUS Hook(unsigned rva,void* replacement,void** trampoline) {
    auto status=MH_CreateHook(base+rva,replacement,trampoline);
    if(status==MH_OK) status=EnableRecordedHook(base+rva);
    return status;
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
    if(!Matches(base+stateRva) || !MatchesDefault(base+defaultRva)) { Log("force feedback: unexpected game code; game choice kept"); return false; }
    auto status=MH_Initialize();
    if(status==MH_OK || status==MH_ERROR_ALREADY_INITIALIZED) {
        status=Hook(defaultRva,reinterpret_cast<void*>(Default),reinterpret_cast<void**>(&originalDefault));
        if(status==MH_OK) status=Hook(stateRva,reinterpret_cast<void*>(Run),reinterpret_cast<void**>(&original));
    }
    Log("force feedback: keep-on hooks=%s",MH_StatusToString(status));
    return status==MH_OK;
}
}
