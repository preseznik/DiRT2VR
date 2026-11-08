// Compile-only. Observes existing native work; never submits or waits itself.
#include <atomic>
#include "cloth-observer.h"
#include "cloth-code-guards.h"
namespace frame_observer {
extern volatile LONG enabled,physicsSerial;
extern late_frame::Window window;
extern void(__thiscall*collectionOriginal)(void*,const double*);
}
namespace cloth_observer_live {
namespace contract=cloth_observation;
using Run=void(__thiscall*)(void*);
using Sync=U32(__thiscall*)(void*);
using Collection=void(__thiscall*)(void*,const double*);
Run originalMaster{};Sync originalSync{};void* aggregateTrampoline{};void* predicateTrampoline{};
std::atomic<U32> enabled{},faulted{},started{},completed{},active{},aggregateCalls{},aggregateReturns{},aggregateResult{},aggregateBad{},aggregateMask{},unbound{};
std::atomic<U32> appActive{},appThread{},syncCalls{},syncReturns{},syncResult{},syncBad{};
std::atomic<U32> predicateCalls{},predicateMask{},absentMask{},requiredMask{};
thread_local U32 masterSequence{};
contract::Owners pinned{};contract::Fault fault{};contract::Token current{};contract::Result last{};
bool installAttempted{};U32 installationThread{},nonce{},consumedMaster{};
bool Word(U32 address,U32&value){if(!contract::Ptr(address))return false;__try{value=*reinterpret_cast<const U32*>(address);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool Bytes(U32 p,const unsigned char*bytes,unsigned size){for(unsigned i=0;i<size;++i){U32 v{};if(!Word((p+i)&~3u,v)||((v>>(((p+i)&3u)*8))&255u)!=bytes[i])return false;}return true;}
bool Jump(U32 p,U32 destination){U32 op{},off{};return Word(p,op)&&(op&255u)==0xE9&&Word(p+1,off)&&p+5+off==destination;}
bool OwnerUnchanged(contract::Fault*out=nullptr){contract::Owners sample{};contract::Inspector<decltype(&Word)> i{Word,base};const bool captured=i.Capture(pinned.game,pinned.render,pinned.collection,sample);const bool same=captured&&contract::Same(sample,pinned);if(out&&!same)*out=captured?contract::Fault{contract::Gate::Changed,pinned.cloth,0,0}:i.fault;return same;}
}
#include "physics-observer-live.inc"
namespace cloth_observer_live {
contract::Receipt MasterReceipt(){contract::Receipt r{};r.completed=completed.load(std::memory_order_acquire);r.started=started.load(std::memory_order_acquire);r.active=active.load(std::memory_order_acquire);r.calls=aggregateCalls.load();r.returns=aggregateReturns.load();r.result=aggregateResult.load();r.bad=aggregateBad.load();r.mask=aggregateMask.load();r.predicateCalls=predicateCalls.load();r.predicateMask=predicateMask.load();r.absentMask=absentMask.load();r.requiredMask=requiredMask.load();return r;}
void __fastcall Master(void*self,void*){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress());
 const bool related=enabled.load(std::memory_order_acquire)&&reinterpret_cast<U32>(self)==pinned.master;
 bool tracked=false;U32 sequence{};
 if(related){
  if(masterSequence||active.exchange(1,std::memory_order_acq_rel)){faulted.store(1);aggregateBad.store(1);}
  else{
   aggregateCalls.store(0);aggregateReturns.store(0);aggregateResult.store(0);aggregateBad.store(0);aggregateMask.store(0);
   predicateCalls.store(0);predicateMask.store(0);absentMask.store(0);requiredMask.store(0);
   sequence=started.fetch_add(1,std::memory_order_acq_rel)+1;masterSequence=sequence;tracked=true;
   if(!sequence||(caller!=At(0x57E5B7)&&caller!=At(0x6CD7C5))||!OwnerUnchanged()){aggregateBad.store(1);faulted.store(1);}
  }
 }
 bool returned=false;
 __try {originalMaster(self);returned=true;} // Exactly once, including unrelated/nested calls.
 __finally {if(tracked){if(!returned){aggregateBad.store(1);faulted.store(1);}masterSequence=0;completed.store(returned?sequence:0,std::memory_order_release);active.store(0,std::memory_order_release);}}
}
void __stdcall AggregateReturned(const U32*saved,U32 nativeEsp){
 // PUSHAD layout: EDI,ESI,EBP,oldESP,EBX,EDX,ECX,EAX. The original
 // CALL107EA2C has already returned. Observe EAX without changing it.
 if(!enabled.load(std::memory_order_acquire))return;
 __try {
  if(saved[0]!=pinned.cloth)return;
  U32 selector{},array{};
  if(!Word(saved[2]+0x1C,selector)||!Word(nativeEsp+0x60,array)){faulted.store(1);return;}
  if(!selector){physics_observer_live::Aggregate(saved,nativeEsp);return;}
  if(!masterSequence){unbound.fetch_add(1);if(started.load())faulted.store(1);return;}
  aggregateCalls.fetch_add(1);
  const U32 bit=selector<3?1u<<selector:0;
  const bool bad=!bit||array!=pinned.array||masterSequence!=started.load()||!active.load()||!(requiredMask.load()&bit)||(absentMask.load()&bit)||!OwnerUnchanged();
  if(bad||(aggregateMask.fetch_or(bit)&bit))aggregateBad.store(1);
  if(saved[7]){aggregateResult.store(saved[7]);aggregateBad.store(1);}
  aggregateReturns.fetch_add(1,std::memory_order_release);
 } __except(EXCEPTION_EXECUTE_HANDLER){aggregateBad.store(1);faulted.store(1);}
}
#include "cloth-tail-thunk.inc"
void __stdcall PredicateObserved(const U32*saved,U32 nativeEsp){
 (void)nativeEsp;
 if(!enabled.load(std::memory_order_acquire))return;
 __try {
  if(saved[0]!=pinned.cloth)return;
  const U32 selector=saved[4]; // Original EBX remains selector at this point.
  if(!selector){physics_observer_live::Predicate(saved);return;}
  if(!masterSequence){unbound.fetch_add(1);if(started.load())faulted.store(1);return;}
  U32 caller{},deltaBits{},stackSelector{};
  const U32 bit=selector<3?1u<<selector:0,found=saved[5]&255u;
  predicateCalls.fetch_add(1);
  const bool valid=Word(saved[2]+4,caller)&&Word(saved[2]+8,deltaBits)&&Word(saved[2]+0x1C,stackSelector)&&
   stackSelector==selector&&caller==At(selector==1?0xCD5588:0xCD5616)&&
   masterSequence==started.load()&&active.load()&&OwnerUnchanged()&&
   contract::PredicateValid(pinned,selector,found,saved[6],saved[7],deltaBits);
  if(!valid||!bit||(predicateMask.fetch_or(bit)&bit)){aggregateBad.store(1);return;}
  if(found)requiredMask.fetch_or(bit);else absentMask.fetch_or(bit);
 }__except(EXCEPTION_EXECUTE_HANDLER){aggregateBad.store(1);faulted.store(1);}
}
#include "cloth-predicate-thunk.inc"
U32 __fastcall SyncReturned(void*self,void*){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress());
 const bool related=enabled.load(std::memory_order_acquire)&&reinterpret_cast<U32>(self)==pinned.renderer;
 const bool tracked=related&&appActive.load(std::memory_order_acquire);
 if(tracked){if(syncCalls.fetch_add(1)!=0||caller!=At(0xCD4E95)||GetCurrentThreadId()!=appThread.load()||!OwnerUnchanged())syncBad.store(1);}
 const U32 result=originalSync(self); // Sole original native wait, full result preserved.
 if(tracked){syncResult.store(result);syncReturns.fetch_add(1,std::memory_order_release);}
 return result;
}
bool HookIntact(){
 const U32 m=reinterpret_cast<U32>(originalMaster),s=reinterpret_cast<U32>(originalSync),a=reinterpret_cast<U32>(aggregateTrampoline),p=reinterpret_cast<U32>(predicateTrampoline);
 return enabled.load(std::memory_order_acquire)&&
  Jump(At(0xCD54F0),reinterpret_cast<U32>(&Master))&&Bytes(m,cloth_code::Code0,6)&&Jump(m+6,At(0xCD54F6))&&
  Jump(At(0xAEFE40),reinterpret_cast<U32>(&SyncReturned))&&Bytes(s,cloth_code::Code1,6)&&Jump(s+6,At(0xAEFE46))&&
  Jump(At(0x107EA31),reinterpret_cast<U32>(&AggregateTail))&&Bytes(a,cloth_code::Code2,5)&&Jump(a+5,At(0x107EA36))&&
  Jump(At(0x107DE74),reinterpret_cast<U32>(&PredicateTail))&&contract::PredicateTrampoline(Word,p,At(0x107EA93),At(0x107DE7C));
}
bool Install(){
 if(!cloth_code::Verify(Word,base,false))return false;
 contract::Inspector<decltype(&Word)> i{Word,base};U32 loader{},render{},collection{};
 if(!Word(At(0x1482D8C),loader)||!Word(loader+16,render)||!Word(loader+20,collection)||!i.Capture(loadOwner,render,collection,pinned)){fault=i.fault;return false;}
 installationThread=GetCurrentThreadId();
 if(MH_CreateHook(reinterpret_cast<void*>(At(0xCD54F0)),reinterpret_cast<void*>(&Master),reinterpret_cast<void**>(&originalMaster))!=MH_OK)return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0xAEFE40)),reinterpret_cast<void*>(&SyncReturned),reinterpret_cast<void**>(&originalSync))!=MH_OK)return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x107EA31)),reinterpret_cast<void*>(&AggregateTail),&aggregateTrampoline)!=MH_OK)return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x107DE74)),reinterpret_cast<void*>(&PredicateTail),&predicateTrampoline)!=MH_OK)return false;
 for(const U32 va:{0xCD54F0u,0xAEFE40u,0x107EA31u,0x107DE74u})if(MH_EnableHook(reinterpret_cast<void*>(At(va)))!=MH_OK)return false;
 if(!physics_observer_live::Install()){fault=physics_observer_live::fault;return false;}
 enabled.store(1,std::memory_order_release);return HookIntact();
}
void AfterNativeStartup(){
 if(installAttempted||activation_live_v1::failed||activation_live_v1::attempt.stage!=native_activation::Stage::Complete)return;
 if(GetCurrentThreadId()!=static_cast<DWORD>(holdThread)||initial_load_v2::ledger.phase!=initial_load_v2::Phase::FinalizationBlocked||finalization_v2::state.phase!=finalization_v2::Phase::RegisteredBlocked||loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved)return;
 installAttempted=true;
 if(!Install()){faulted.store(1);char line[240]{};sprintf_s(line,"CLOTH observer install rejected gate=%u address=%08x actual=%08x expected=%08x construction_permitted=0 all_workers_retired=0\r\n",unsigned(fault.gate),fault.address,fault.actual,fault.expected);Log(line);}
 else Log("CLOTH original-return observers installed; awaiting a full observed frame; construction_permitted=0 all_workers_retired=0\r\n");
}
bool Frame(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource,U32&serial){
 if(&w!=&frame_observer::window||&serialSource!=&frame_observer::physicsSerial||!InterlockedCompareExchange(&frame_observer::enabled,0,0))return false;
 serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));
 return query_retirement::Frame(w,o,self,caller,At(0x69471F),GetCurrentThreadId(),serial)&&o.game==pinned.game&&o.render==pinned.render&&o.collection==pinned.collection;
}
contract::Token Before(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource){
 contract::Token t{};U32 serial{};
 last={};
 if(!enabled.load()||faulted.load()){last.fault=fault;return t;}
 if(!Frame(w,o,self,caller,serialSource,serial)){last.gate=contract::Gate::Frame;return t;}
 if(appActive.load()){faulted.store(1);last.gate=contract::Gate::Nested;return t;}
 if(!HookIntact()||!cloth_code::Verify(Word,base,true)){faulted.store(1);last.gate=contract::Gate::Code;return t;}
 if(!OwnerUnchanged(&last.fault)){faulted.store(1);last.gate=last.fault.gate;return t;}
 const U32 sequence=started.load(std::memory_order_acquire);
 if(!sequence||sequence<=consumedMaster)return t; // Installation during work cannot manufacture its receipt.
 t={w.epoch+1,serial,GetCurrentThreadId(),sequence,++nonce,true};current=t;
 syncCalls.store(0);syncReturns.store(0);syncResult.store(0xffffffffu);syncBad.store(0);
 appThread.store(t.thread);appActive.store(1,std::memory_order_release);return t;
}
contract::Result After(const contract::Token&t,const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource,bool originalReturned){
 contract::Result r{};r.original_returned=originalReturned;
 if(!t.valid){r=last;r.original_returned=originalReturned;return r;}
 const bool wasActive=appActive.exchange(0,std::memory_order_acq_rel)!=0;
 r.epoch=t.epoch;r.serial=t.serial;r.master=t.master;r.unbound=unbound.load();
 const auto m=MasterReceipt();r.aggregateCalls=m.calls;r.aggregateReturns=m.returns;r.aggregateResult=m.result;r.aggregateMask=m.mask;
 r.predicateCalls=m.predicateCalls;r.predicateMask=m.predicateMask;r.absentMask=m.absentMask;r.requiredMask=m.requiredMask;r.kindCount=pinned.kindCount;for(U32 i=0;i<pinned.kindCount;++i)r.kinds[i]=pinned.kinds[i];
 r.syncCalls=syncCalls.load();r.syncReturns=syncReturns.load();r.syncResult=syncResult.load();
 U32 serial{};
 if(!originalReturned||!wasActive||!contract::SameInvocation(t,current)||!Frame(w,o,self,caller,serialSource,serial)||serial!=t.serial||t.epoch!=w.epoch+1){r.gate=contract::Gate::Frame;return last=r;}
 consumedMaster=t.master;current.valid=false;
 if(faulted.load()||!HookIntact()||!cloth_code::Verify(Word,base,true)){r.gate=contract::Gate::Code;return last=r;}
 if(!OwnerUnchanged(&r.fault)){r.gate=r.fault.gate;return last=r;}
 if(!contract::MasterComplete(m,t.master)||r.syncCalls!=1||r.syncReturns!=1||r.syncResult||syncBad.load()){r.gate=contract::Gate::Receipt;return last=r;}
 // Original CD4E30, not this observer, clears its five cycle flags.
 U32 flags{},tail{};
 if(!Word(pinned.coordinator+0x9C,flags)||!Word(pinned.coordinator+0xA0,tail)||flags||(tail&255u)){r.gate=contract::Gate::Changed;return last=r;}
 r.observed=true;r.gate=contract::Gate::Observed;return last=r;
}
// Integration boundary: call BEFORE Window::Enter, then forward the original
// collection exactly once here. Inspect the returned receipt only while this
// exact outer App hook is still on its original stack. No lease is returned.
contract::Result InvokeOriginal(Collection nativeCollection,void*self,const double*delta,U32 caller,const late_frame::Window&w,const late_frame::Owners&o,volatile LONG&serialSource){
 contract::Token token{};contract::Result r{};
 last={};
 if(nativeCollection==frame_observer::collectionOriginal)token=Before(w,o,reinterpret_cast<U32>(self),caller,serialSource);
 bool returned=false;
 __try {nativeCollection(self,delta);returned=true;}
 __finally {r=After(token,w,o,reinterpret_cast<U32>(self),caller,serialSource,returned);}
 return r;
}
}
