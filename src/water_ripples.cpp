#include "water_ripples.h"
#include "common.h"
#include "gfwl_compat.h"
#include <atomic>
#include <cstddef>
#include <cstdint>

namespace vr {
namespace {
struct RippleBuffers {
    unsigned char simulation[0xf8];
    void* textures[3];
    int index;
    unsigned char active,initializing;
};
static_assert(offsetof(RippleBuffers,textures)==0xf8);
static_assert(offsetof(RippleBuffers,index)==0x104);
static_assert(offsetof(RippleBuffers,initializing)==0x109);
using UpdateFn=void (__thiscall*)(RippleBuffers*);
using DirtyFn=void (__thiscall*)(void*,bool,bool);
UpdateFn realUpdate{};
DirtyFn markDirty{};
std::atomic<unsigned> initializationLogs{};

void __fastcall UpdateRipples(RippleBuffers* buffers,void*) {
    const bool initializing=buffers->initializing!=0;
    realUpdate(buffers);
    if(!initializing || buffers->initializing || buffers->index<0 || buffers->index>=3)return;
    // Native RVA 0x6b7b50 fills all three CPU textures on initialization, but
    // marks only (index+2)%3 dirty. The blended shader also samples (index+1)%3.
    // Complete the two omitted notifications using the native texture method.
    // Do not advance the simulation, replace its contents, or repeat its upload.
    void* history[2]{};
    unsigned before[2]{},after[2]{};
    for(unsigned i=0;i<2;++i) {
        auto texture=buffers->textures[(buffers->index+i)%3];
        if(!texture)continue;
        history[i]=texture;
        auto flags=reinterpret_cast<unsigned*>(static_cast<unsigned char*>(texture)+0x34);
        before[i]=*flags;
        markDirty(texture,true,false);
        after[i]=*flags;
    }
    if(LoggingEnabled() && initializationLogs.fetch_add(1)<32)
        Log("water ripple initialization: simulation=%p index=%d history=%p,%p dirty_before=%08x,%08x dirty_after=%08x,%08x",
            buffers,buffers->index,history[0],history[1],before[0],before[1],after[0],after[1]);
}

uint64_t CodeHash(const unsigned char* bytes,size_t size) {
    uint64_t hash=0xcbf29ce484222325ull;
    for(size_t i=0;i<size;++i)hash=(hash^bytes[i])*0x100000001b3ull;
    return hash;
}
bool RippleCodeMatches(const unsigned char* base) {
    // Complete bodies contain no relocated absolute addresses. Together with
    // SupportedHost, this rejects other builds and competing in-memory patches.
    return CodeHash(base+0x6b7b50,0x106)==0x3696e0fdd3b3ce37ull &&
           CodeHash(base+0xb54280,0x27)==0xa8d258e15fb0aa64ull;
}
}

bool EnableWaterRippleInitialization() {
    if(realUpdate)return true;
    if(!SupportedHost())return false;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    if(!RippleCodeMatches(base)) {
        Log("water ripple initialization hook rejected instruction guard");
        return false;
    }
    auto entry=base+0x6b7b50;
    markDirty=reinterpret_cast<DirtyFn>(base+0xb54280);
    auto status=MH_CreateHook(entry,reinterpret_cast<void*>(UpdateRipples),reinterpret_cast<void**>(&realUpdate));
    if(status==MH_OK) {
        status=EnableRecordedHook(entry);
        if(status!=MH_OK)MH_RemoveHook(entry);
    }
    if(status!=MH_OK){realUpdate=nullptr;markDirty=nullptr;}
    Log("water ripple initialization hook RVA=0x6b7b50 status=%s; headset validation pending",MH_StatusToString(status));
    return status==MH_OK;
}
}
