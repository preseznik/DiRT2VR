#include "physics-observer.h"
#include "physics-code-guards.h"
namespace frame_observer {extern void(__thiscall*physicsOriginal)(void*,const double*);void __fastcall PhysicsReturned(void*,void*,const double*);}
namespace cloth_observer_live {bool Frame(const late_frame::Window&,const late_frame::Owners&,U32,U32,volatile LONG&,U32&);}
namespace physics_observer_live {
using U32=std::uint32_t;using Update=void(__thiscall*)(void*,const double*);
Update originalStage1{};void* handlingTrampoline{};
physics_observation::Owners pinned{};cloth_observation::Fault fault{};
std::atomic<U32> enabled{},failed{},sequence{},stage1Finished{},stage7Finished{},publishedSerial{},ownerThread{},active1{},active7{},handlingCalls{},handlingResult{},predicateCalls{},predicateFound{},aggregateCalls{},aggregateResult{},bad{},unbound{};
thread_local U32 handlingSequence{},stage7Sequence{};
U32 consumedSerial{};
bool Word(U32 p,U32&v){return cloth_observer_live::Word(p,v);}
bool OwnerUnchanged(){physics_observation::Owners s{};physics_observation::Inspector<decltype(&Word)> i{Word,base};const bool ok=i.Capture(cloth_observer_live::pinned,s)&&physics_observation::Same(s,pinned);if(!ok)failed.store(1);return ok;}
physics_observation::Receipt Receipt(){physics_observation::Receipt r{};r.sequence=sequence.load(std::memory_order_acquire);r.stage1Finished=stage1Finished.load();r.stage7Finished=stage7Finished.load(std::memory_order_acquire);r.serial=publishedSerial.load();r.thread=ownerThread.load();r.active1=active1.load();r.active7=active7.load();r.handlingCalls=handlingCalls.load();r.handlingResult=handlingResult.load();r.predicateCalls=predicateCalls.load();r.found=predicateFound.load();r.aggregateCalls=aggregateCalls.load();r.aggregateResult=aggregateResult.load();r.bad=bad.load()|failed.load();return r;}
void __fastcall Stage1(void*self,void*,const double*delta){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress()),thread=GetCurrentThreadId();
 const bool related=enabled.load(std::memory_order_acquire)&&reinterpret_cast<U32>(self)==pinned.stage1;
 bool tracked=false;U32 current{};
 if(related){
  if(handlingSequence||stage7Sequence||active7.load()||active1.exchange(1,std::memory_order_acq_rel)){failed.store(1);bad.store(1);}
  else{
   const U32 previous=sequence.load();
   if(previous&&stage7Finished.load()!=previous)failed.store(1);
   U32 expected=0;ownerThread.compare_exchange_strong(expected,thread);
   current=sequence.fetch_add(1,std::memory_order_acq_rel)+1;handlingSequence=current;tracked=true;
   handlingCalls.store(0);handlingResult.store(0);predicateCalls.store(0);predicateFound.store(0);aggregateCalls.store(0);aggregateResult.store(0);bad.store(0);publishedSerial.store(0);
   if(!current||ownerThread.load()!=thread||caller!=At(0xC2AEE2)||!OwnerUnchanged()||!cloth_observer_live::OwnerUnchanged())failed.store(1);
  }
 }
 bool returned=false;
 __try {originalStage1(self,delta);returned=true;}
 __finally {if(tracked){if(!returned||handlingCalls.load()!=1||handlingResult.load())bad.store(1);handlingSequence=0;stage1Finished.store(returned?current:0,std::memory_order_release);active1.store(0,std::memory_order_release);}}
}
void __stdcall HandlingReturned(const U32*saved,U32 nativeEsp){
 (void)nativeEsp;if(!enabled.load(std::memory_order_acquire))return;
 __try{
  U32 caller{},task{};if(!Word(saved[2]+4,caller)||caller!=At(0x6C4F09))return;
  if(!handlingSequence){unbound.fetch_add(1);if(sequence.load())failed.store(1);return;}
  const bool match=Word(saved[2]+12,task)&&task==pinned.task&&handlingSequence==sequence.load()&&active1.load()&&GetCurrentThreadId()==ownerThread.load()&&OwnerUnchanged();
  if(!match||handlingCalls.fetch_add(1)!=0)bad.store(1);
  handlingResult.store(saved[7]);if(saved[7])bad.store(1);
 }__except(EXCEPTION_EXECUTE_HANDLER){bad.store(1);failed.store(1);}
}
#include "handling-tail-thunk.inc"
physics_observation::StageToken BeforeStage7(void*self,U32 caller){
 physics_observation::StageToken t{};
 if(!enabled.load(std::memory_order_acquire)||reinterpret_cast<U32>(self)!=pinned.stage7)return t;
 const U32 seq=sequence.load(std::memory_order_acquire),thread=GetCurrentThreadId();
 if(!seq){return t;} // Installation midway through a frame cannot create a receipt.
 if(stage7Sequence||handlingSequence||active1.load()||active7.exchange(1,std::memory_order_acq_rel)){failed.store(1);bad.store(1);return t;}
 t={seq,thread,true};stage7Sequence=seq;
 if(stage1Finished.load()!=seq||stage7Finished.load()==seq||thread!=ownerThread.load()||caller!=At(0xC2AEE2)||!OwnerUnchanged()||!cloth_observer_live::OwnerUnchanged())bad.store(1);
 return t;
}
void Predicate(const U32*saved){
 if(!enabled.load(std::memory_order_acquire)||saved[0]!=pinned.cloth)return;
 if(!stage7Sequence){unbound.fetch_add(1);if(sequence.load())failed.store(1);return;}
 U32 caller{},selector{},delta{};
 const U32 found=saved[5]&255u;
 const bool valid=Word(saved[2]+4,caller)&&caller==At(0x6CD555)&&Word(saved[2]+0x1C,selector)&&selector==0&&saved[4]==0&&Word(saved[2]+8,delta)&&physics_observation::PredicateValid(cloth_observer_live::pinned,found,saved[6],saved[7],delta)&&stage7Sequence==sequence.load()&&active7.load()&&GetCurrentThreadId()==ownerThread.load()&&OwnerUnchanged();
 if(!valid||predicateCalls.fetch_add(1)!=0)bad.store(1);predicateFound.store(found);
}
void Aggregate(const U32*saved,U32 nativeEsp){
 if(!enabled.load(std::memory_order_acquire)||saved[0]!=pinned.cloth)return;
 if(!stage7Sequence){unbound.fetch_add(1);if(sequence.load())failed.store(1);return;}
 U32 caller{},selector{},array{};
 const bool valid=Word(saved[2]+4,caller)&&caller==At(0x6CD555)&&Word(saved[2]+0x1C,selector)&&selector==0&&Word(nativeEsp+0x60,array)&&array==pinned.array&&stage7Sequence==sequence.load()&&active7.load()&&predicateCalls.load()==1&&predicateFound.load()==1&&GetCurrentThreadId()==ownerThread.load()&&OwnerUnchanged();
 if(!valid||aggregateCalls.fetch_add(1)!=0)bad.store(1);aggregateResult.store(saved[7]);if(saved[7])bad.store(1);
}
void AfterStage7(const physics_observation::StageToken&t,bool originalReturned,U32 serial){
 if(!t.valid)return;
 const bool valid=t.sequence==sequence.load()&&t.sequence==stage7Sequence&&t.thread==GetCurrentThreadId()&&t.thread==ownerThread.load()&&originalReturned&&serial&&serial>publishedSerial.load()&&OwnerUnchanged()&&cloth_observer_live::OwnerUnchanged();
 if(!valid)bad.store(1);
 stage7Sequence=0;publishedSerial.store(valid?serial:0);active7.store(0,std::memory_order_release);stage7Finished.store(originalReturned?t.sequence:0,std::memory_order_release);
}
bool HookIntact(){
 const U32 s=reinterpret_cast<U32>(originalStage1),h=reinterpret_cast<U32>(handlingTrampoline);U32 op{},address{};
 return enabled.load(std::memory_order_acquire)&&cloth_observer_live::Jump(At(0x6C4E80),reinterpret_cast<U32>(&Stage1))&&cloth_observer_live::Bytes(s,physics_code::Code0,5)&&cloth_observer_live::Jump(s+5,At(0x6C4E85))&&cloth_observer_live::Jump(At(0x10D03CE),reinterpret_cast<U32>(&HandlingTail))&&Word(h,op)&&(op&255u)==0xA1&&Word(h+1,address)&&address==At(0x168B5BC)&&cloth_observer_live::Jump(h+5,At(0x10D03D3));
}
bool FrameHookIntact(){const U32 p=reinterpret_cast<U32>(frame_observer::physicsOriginal);return cloth_observer_live::Jump(At(0x6CD440),reinterpret_cast<U32>(&frame_observer::PhysicsReturned))&&cloth_observer_live::Bytes(p,physics_code::Code2,6)&&cloth_observer_live::Jump(p+6,At(0x6CD446));}
bool Install(){
 if(!FrameHookIntact()||!physics_code::Verify(Word,base,false))return false;
 physics_observation::Inspector<decltype(&Word)> i{Word,base};if(!i.Capture(cloth_observer_live::pinned,pinned)){fault=i.fault;return false;}
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x6C4E80)),reinterpret_cast<void*>(&Stage1),reinterpret_cast<void**>(&originalStage1))!=MH_OK)return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x10D03CE)),reinterpret_cast<void*>(&HandlingTail),&handlingTrampoline)!=MH_OK)return false;
 for(const U32 va:{0x6C4E80u,0x10D03CEu})if(MH_EnableHook(reinterpret_cast<void*>(At(va)))!=MH_OK)return false;
 enabled.store(1,std::memory_order_release);return HookIntact();
}
physics_observation::Result AtApp(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource,const cloth_observation::Result&cloth){
 physics_observation::Result r{};r.receipt=Receipt();r.epoch=w.epoch+1;r.unbound=unbound.load();r.modelCount=pinned.modelCount;r.clothCount=pinned.count;U32 serial{};
 if(!enabled.load())return r;
 if(!cloth.observed||!cloth.original_returned||!cloth_observer_live::Frame(w,o,self,caller,serialSource,serial)||cloth.serial!=serial||cloth.epoch!=r.epoch||o.game!=pinned.game){r.gate=cloth_observation::Gate::Frame;return r;}
 if(failed.load()){r.gate=cloth_observation::Gate::Receipt;return r;}
 if(!HookIntact()||!FrameHookIntact()||!physics_code::Verify(Word,base,true)){r.gate=cloth_observation::Gate::Code;return r;}
 if(!OwnerUnchanged()||!cloth_observer_live::OwnerUnchanged()){r.gate=cloth_observation::Gate::Changed;return r;}
 if(!r.epoch||serial<=consumedSerial||!physics_observation::Complete(r.receipt,serial)||r.receipt.sequence!=sequence.load(std::memory_order_acquire)){r.gate=cloth_observation::Gate::Receipt;return r;}
 consumedSerial=serial;r.observed=true;r.gate=cloth_observation::Gate::Observed;return r;
}
}
