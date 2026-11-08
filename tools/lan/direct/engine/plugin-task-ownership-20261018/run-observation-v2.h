#pragma once
#include <windows.h>
#include <cstdint>
namespace finalization_v2 {
struct Job {uint32_t table{},render{},game{};};
static_assert(sizeof(Job)==12);
using NativeRun=void(__thiscall*)(void*);
struct RunObservation {
    Job* ownedJob{};
    uint32_t expectedTable{},expectedRender{},expectedGame{};
    volatile LONG armed{},entered{},returned{},runFault{},unrelated{},worker{};
};
inline LONG Load(volatile LONG& v){return InterlockedCompareExchange(&v,0,0);}
inline bool JobUnchanged(const RunObservation& o) {
    return o.ownedJob && o.ownedJob->table==o.expectedTable && o.ownedJob->render==o.expectedRender && o.ownedJob->game==o.expectedGame;
}
// No catch: original abnormal termination propagates to its original caller.
inline void ObserveOriginal(RunObservation& o,void* self,NativeRun nativeRun) {
    const bool armedNow=Load(o.armed)!=0;
    const bool owned=self==o.ownedJob && armedNow;
    if(owned) {
        InterlockedExchange(&o.worker,static_cast<LONG>(GetCurrentThreadId()));
        if(InterlockedIncrement(&o.entered)!=1 || !JobUnchanged(o))InterlockedExchange(&o.runFault,1);
    } else if(armedNow)InterlockedIncrement(&o.unrelated);
    bool returnedNormally=false;
    __try {nativeRun(self);returnedNormally=true;}
    __finally {
        if(owned) {
            if(!returnedNormally || !JobUnchanged(o))InterlockedExchange(&o.runFault,1);
            else InterlockedIncrement(&o.returned);
        }
    }
}
}
