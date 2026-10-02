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
// protection, state transitions or return values. State methods ret 4; the
// screen update takes a time step and an argument and ret 8.
using StateFn=uintptr_t (__thiscall*)(void*,void*);
std::array<StateFn,6> original{};
using UpdateFn=uintptr_t (__thiscall*)(void*,float,void*);
UpdateFn originalUpdate{};
constexpr const char* names[]={"protected-context", "profile-dataset-enter", "profile-dataset-poll", "profile-enumeration", "auto-load-enter", "auto-load-poll", "auto-load-update"};
struct SampleBudget {
    unsigned samples{};
    ULONGLONG previous{};
    bool Take(ULONGLONG now,bool changed=false) {
        if(samples>=60 || (!changed && samples && now-previous<5000))return false;
        ++samples; previous=now; return true;
    }
};
std::array<SampleBudget,7> budgets;
std::atomic_flag sampling=ATOMIC_FLAG_INIT;
bool Sample(unsigned stage,bool changed=false) {
    if(sampling.test_and_set())return false;
    const bool take=budgets[stage].Take(GetTickCount64(),changed);
    sampling.clear(); return take;
}
template<class T> bool Read(const void* object,size_t offset,T& value) {
    SIZE_T bytes{};
    return object && ReadProcessMemory(GetCurrentProcess(),static_cast<const char*>(object)+offset,&value,sizeof(value),&bytes) && bytes==sizeof(value);
}
// Only print fixed transition names. Never print a profile name or buffer text.
constexpr const char* outcomes[]={"waiting", "next", "nonefound", "showpopup", "success", "cancelled", "skipped", "x360fail", "pcfail", "no_existing_save", "other-or-unreadable"};
unsigned Outcome(void* argument) {
    const char* text{};
    if(!Read(argument,0,text))return 10;
    if(!text)return 0;
    char value[24]{};
    for(unsigned i=0;i<sizeof(value);++i) {
        if(!Read(text,i,value[i]))return 10;
        if(!value[i]) {
            for(unsigned n=1;n<10;++n)if(!strcmp(value,outcomes[n]))return n;
            return 10;
        }
    }
    return 10;
}
std::array<std::atomic<unsigned>,7> transitions{{~0u,~0u,~0u,~0u,~0u,~0u,~0u}};
void ReportState(unsigned stage,void* self) {
    if(stage==3) {
        void* task{};unsigned status=~0u,count=~0u;
        Read(self,0x2c,task);Read(task,0x7c,status);Read(task,0x78,count);
        Log("direct startup: enumeration status=0x%x count=%u (0x10=pending)",status,count);
    } else if(stage>=4) {
        void* manager{};void* wrapper{};void* task{};
        unsigned status=~0u,type=~0u,operation=~0u,subtype=~0u;
        float timer=-1;
        Read(self,0x128,manager);Read(manager,0x38,wrapper);Read(wrapper,0xc,task);
        Read(task,0x2c,status);Read(self,0x130,type);Read(self,0x134,operation);Read(self,0x138,subtype);Read(self,0x14c,timer);
        Log("direct startup: auto-load status=0x%x datatype=%u operation=%u subtype=%u timer=%.3f",status,type,operation,subtype,timer);
    }
}
template<unsigned Stage> uintptr_t __fastcall Observe(void* self,void*,void* argument) {
    const auto before=GetTickCount64();
    const bool sample=Sample(Stage);
    if(sample) {Log("direct startup: %s begin",names[Stage]);ReportState(Stage,self);}
    const auto result=original[Stage](self,argument);
    if constexpr(Stage==2 || Stage==3 || Stage==5) {
        const auto outcome=Outcome(argument);
        const bool changed=transitions[Stage].exchange(outcome)!=outcome;
        if(sample || (changed && Sample(Stage,true))) {
            Log("direct startup: %s returned outcome=%s elapsed_ms=%llu",names[Stage],outcomes[outcome],GetTickCount64()-before);
            ReportState(Stage,self);
        }
    } else if(sample)Log("direct startup: %s returned elapsed_ms=%llu",names[Stage],GetTickCount64()-before);
    return result;
}
uintptr_t __fastcall ObserveUpdate(void* self,void*,float dt,void* argument) {
    const auto before=GetTickCount64();
    const bool sample=Sample(6);
    if(sample) {Log("direct startup: auto-load-update begin dt=%f",dt);ReportState(6,self);}
    const auto result=originalUpdate(self,dt,argument);
    if(sample) {Log("direct startup: auto-load-update returned elapsed_ms=%llu",GetTickCount64()-before);ReportState(6,self);}
    return result;
}
struct Hook { unsigned rva; std::array<unsigned char,12> guard; void* replacement; void** original; };
}
void EnableDirectStartupTrace() {
    static bool attempted=false;
    if(attempted || !LoggingEnabled())return;
    attempted=true;
    if(!SupportedHost() || !EnableGfwlCompatibility())return;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    Hook hooks[]={
        {0x22f9b0,{0x8b,0x41,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8,0x02,0x89,0x41},reinterpret_cast<void*>(Observe<0>),reinterpret_cast<void**>(&original[0])},
        {0x223930,{0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8},reinterpret_cast<void*>(Observe<1>),reinterpret_cast<void**>(&original[1])},
        {0x223960,{0x51,0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x20,0x57,0x75,0x06},reinterpret_cast<void*>(Observe<2>),reinterpret_cast<void**>(&original[2])},
        {0x0618a0,{0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x20,0x57,0x75,0x06,0x83},reinterpret_cast<void*>(Observe<3>),reinterpret_cast<void**>(&original[3])},
        {0x09a8c0,{0x81,0xec,0x88,0x00,0x00,0x00,0x53,0x55,0x56,0x8b,0xf1,0x8b},reinterpret_cast<void*>(Observe<4>),reinterpret_cast<void**>(&original[4])},
        {0x102130,{0x51,0x56,0x8d,0x44,0x24,0x04,0x50,0x8b,0xf1,0xe8,0xc2,0xf2},reinterpret_cast<void*>(Observe<5>),reinterpret_cast<void**>(&original[5])},
        {0x0a3430,{0xd9,0x44,0x24,0x04,0x83,0xec,0x08,0x55,0x56,0x57,0x8b,0x7c},reinterpret_cast<void*>(ObserveUpdate),reinterpret_cast<void**>(&originalUpdate)}};
    for(const auto& hook:hooks)if(memcmp(base+hook.rva,hook.guard.data(),hook.guard.size())) {
        Log("direct startup: diagnostic unavailable at %x; original loading retained",hook.rva);return;
    }
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED)return;
    unsigned created=0;
    for(const auto& hook:hooks) {
        status=MH_CreateHook(base+hook.rva,hook.replacement,hook.original);
        if(status!=MH_OK)break;
        ++created;
    }
    if(status==MH_OK)for(const auto& hook:hooks) {
        status=EnableRecordedHook(base+hook.rva);
        if(status!=MH_OK)break;
    }
    if(status!=MH_OK) {
        for(unsigned i=0;i<created;++i) {
            MH_DisableHook(base+hooks[i].rva);MH_RemoveHook(base+hooks[i].rva);*hooks[i].original=nullptr;
        }
        Log("direct startup: diagnostic unavailable (%s); original loading retained",MH_StatusToString(status));return;
    }
    Log("direct startup: profile-stage diagnostics v2 enabled (transitions and auto-load; bounded; save contents not logged)");
}
}
