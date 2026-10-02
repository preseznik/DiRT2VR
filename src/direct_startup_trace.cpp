#include "direct_startup_trace.h"
#include "common.h"
#include "gfwl_compat.h"
#include <MinHook.h>
#include <array>
#include <atomic>
#include <cstring>

namespace vr {
namespace {
// Observe the existing profile-loading states. Do not change sign-in, save
// protection, state transitions or return values. These x86 methods all ret 4.
using StateFn=uintptr_t (__thiscall*)(void*,void*);
std::array<StateFn,4> original{};
constexpr const char* names[]={"protected-context", "profile-dataset-enter", "profile-dataset-poll", "profile-enumeration"};
struct SampleBudget {
    unsigned samples{};
    ULONGLONG previous{};
    bool Take(ULONGLONG now) {
        if(samples>=60 || (samples && now-previous<5000))return false;
        ++samples; previous=now; return true;
    }
};
std::array<SampleBudget,4> budgets;
std::atomic_flag sampling=ATOMIC_FLAG_INIT;
bool Sample(unsigned stage) {
    if(sampling.test_and_set())return false;
    const bool take=budgets[stage].Take(GetTickCount64());
    sampling.clear(); return take;
}
template<unsigned Stage> uintptr_t __fastcall Observe(void* self,void*,void* argument) {
    const auto before=GetTickCount64();
    const bool sample=Sample(Stage);
    if(sample)Log("direct startup: %s begin",names[Stage]);
    const auto result=original[Stage](self,argument);
    if(sample)Log("direct startup: %s returned elapsed_ms=%llu",names[Stage],GetTickCount64()-before);
    return result;
}
struct Hook { unsigned rva; std::array<unsigned char,12> guard; void* replacement; };
}
void EnableDirectStartupTrace() {
    static bool attempted=false;
    if(attempted || !LoggingEnabled())return;
    attempted=true;
    if(!SupportedHost() || !EnableGfwlCompatibility())return;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    Hook hooks[]={
        {0x22f9b0,{0x8b,0x41,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8,0x02,0x89,0x41},reinterpret_cast<void*>(Observe<0>)},
        {0x223930,{0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8},reinterpret_cast<void*>(Observe<1>)},
        {0x223960,{0x51,0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x20,0x57,0x75,0x06},reinterpret_cast<void*>(Observe<2>)},
        {0x0618a0,{0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x20,0x57,0x75,0x06,0x83},reinterpret_cast<void*>(Observe<3>)}};
    for(const auto& hook:hooks)if(memcmp(base+hook.rva,hook.guard.data(),hook.guard.size())) {
        Log("direct startup: diagnostic unavailable at %x; original loading retained",hook.rva);return;
    }
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return;
    unsigned created=0;
    for(unsigned i=0;i<original.size();++i) {
        status=MH_CreateHook(base+hooks[i].rva,hooks[i].replacement,reinterpret_cast<void**>(&original[i]));
        if(status!=MH_OK)break;
        ++created;
    }
    if(status==MH_OK)for(const auto& hook:hooks) {
        status=EnableRecordedHook(base+hook.rva);
        if(status!=MH_OK)break;
    }
    if(status!=MH_OK) {
        for(unsigned i=0;i<created;++i) {
            MH_DisableHook(base+hooks[i].rva);MH_RemoveHook(base+hooks[i].rva);original[i]=nullptr;
        }
        Log("direct startup: diagnostic unavailable (%s); original loading retained",MH_StatusToString(status));return;
    }
    Log("direct startup: profile-stage diagnostics enabled (bounded; save contents not logged)");
}
}
