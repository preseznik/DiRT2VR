// Includes the sealed concrete native lock backend, without invoking its
// standalone probe. This candidate has its own fixed combined body only.
#include "../resource-worker-scope-20261018/resource-scope-live-v1.inc"
#include "combined-policy-v1.h"
#include "receipt-lineage-v1.h"
#include "combined-code-guards-v1.h"
namespace combined_live_v1 {
namespace contract=combined_noop_v1;
struct Invocation {
 resource_scope_v1::Entry entry{};resource_scope_v1::Owners owners{};contract::Loader loader{};
 query_retirement_live::Result drain{};query_retirement::Snapshot query{};
 U32 epoch{},nonce{};bool started{},ready{};
};
Invocation*active{};bool attempted{},finished{},preflightReported{};U32 nonce{};contract::Receipt last{};
bool TakePreflightFailure(contract::Receipt&out){return contract::TakePreflightFailure(finished,preflightReported,last,out);}
bool Word(U32 p,U32&v){return resource_scope_live_v1::Word(p,v);}
bool Fail(contract::Receipt&r,contract::Gate g,U32 address=0,U32 actual=0,U32 expected=0){r.gate=g;if(r.fault.gate==contract::Gate::Pending)r.fault={g,address,actual,expected};return false;}
bool ReadLoader(U32 loader,contract::Loader&out){out.pointer=loader;return Word(loader+0x1c,out.phase)&&Word(loader+0x38,out.first)&&Word(loader+0x3c,out.last)&&Word(loader+0x40,out.issued)&&Word(loader+0x44,out.active)&&Word(loader+0x48,out.flags)&&Word(loader+8,out.processorActive);}
bool LoaderReady(Invocation&t,contract::Receipt&r,bool repeat){
 contract::Loader now{};
 const auto&ledger=initial_load_v2::ledger;
 if(!ledger.vehiclesComplete||ledger.vehicles!=t.owners.loader||!ReadLoader(t.owners.loader,now)||!contract::LoaderIdle(now,ledger.vehicleSerial))return Fail(r,contract::Gate::Loader,t.owners.loader+0x1c,now.phase,0);
 if(repeat&&!contract::Same(now,t.loader))return Fail(r,contract::Gate::Changed,t.owners.loader,now.issued,t.loader.issued);
 if(!repeat)t.loader=now;return true;
}
bool Code(){return resource_scope_live_v1::Imports()&&resource_code_v1::Verify(Word,base)&&combined_code_v1::Verify(Word,base)&&
 vehicle_processor_live::HookIntact()&&processor_code::Verify(Word,base,true)&&query_retirement_live::HookIntact()&&query_code::Verify(Word,base,true)&&query_retirement_live::Tables()&&
 cloth_observer_live::HookIntact()&&cloth_code::Verify(Word,base,true)&&physics_observer_live::HookIntact()&&physics_observer_live::FrameHookIntact()&&physics_code::Verify(Word,base,true);}
bool Current(Invocation&t,const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 app,volatile LONG&serialSource,contract::Receipt&r){
 if(active!=&t||!t.started||&w!=&frame_observer::window||&serialSource!=&frame_observer::physicsSerial||t.epoch!=w.epoch+1||
    t.entry.image!=base||t.entry.app!=app||t.entry.self!=self||t.entry.caller!=caller||t.entry.thread!=GetCurrentThreadId()||
    !late_frame::Same(t.entry.frame,o)||static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0))!=t.entry.serial)return Fail(r,contract::Gate::Frame);
 const U32 token=reinterpret_cast<U32>(&t);const auto*tib=reinterpret_cast<const NT_TIB*>(NtCurrentTeb());
 if(t.entry.stackLow!=reinterpret_cast<U32>(tib->StackLimit)||t.entry.stackHigh!=reinterpret_cast<U32>(tib->StackBase)||token<t.entry.stackLow||std::uint64_t(token)+sizeof(t)>t.entry.stackHigh)return Fail(r,contract::Gate::Frame,token);
 resource_scope_v1::Inspector<decltype(&Word)> inspector{Word,t.entry,w};resource_scope_v1::Owners now{};
 if(!inspector.Capture(now)||!resource_scope_v1::Same(now,t.owners))return Fail(r,contract::Gate::Changed,inspector.fault.address,inspector.fault.actual,inspector.fault.expected);
 return true;
}
// Called once before the sole original App invocation. No resource locks or
// closed processor gate are held across the native query drain/collection.
void Before(Invocation&t,const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 app,volatile LONG&serialSource){
 if(attempted||finished||active||!resource_scope_live_v1::StartupReady()||&w!=&frame_observer::window||&serialSource!=&frame_observer::physicsSerial)return;
 const U32 serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));const auto*tib=reinterpret_cast<const NT_TIB*>(NtCurrentTeb());
 t.entry={base,app,self,caller,GetCurrentThreadId(),reinterpret_cast<U32>(tib->StackLimit),reinterpret_cast<U32>(tib->StackBase),serial,o};
 resource_scope_v1::Inspector<decltype(&Word)> inspector{Word,t.entry,w};if(!inspector.Capture(t.owners))return;
 // Do not spend the one-shot query receipt on a partial observer frame.
 if(!physics_observer_live::enabled.load()||physics_observer_live::failed.load()||!physics_observation::Complete(physics_observer_live::Receipt(),serial)||
    !cloth_observer_live::enabled.load()||cloth_observer_live::faulted.load()||!cloth_observation::MasterComplete(cloth_observer_live::MasterReceipt(),cloth_observer_live::started.load()))return;
 const auto arm=vehicle_processor_live::TryArmAtAppBoundary(w,o,self,caller,serialSource);
 if(!arm.armed||arm.failed)return;
 contract::Receipt r{};r.epoch=w.epoch+1;r.serial=serial;r.thread=t.entry.thread;r.nonce=++nonce;
 t.epoch=r.epoch;t.nonce=r.nonce;
 if(!Code()||!LoaderReady(t,r,false)){finished=true;last=r;if(last.gate==contract::Gate::Pending)Fail(last,contract::Gate::Code);return;}
 t.started=true;active=&t;
 if(!Current(t,w,o,self,caller,app,serialSource,r)){finished=true;active=nullptr;last=r;return;}
 t.drain=query_retirement_live::TryRetire(w,o,self,caller,serialSource);
 if(!t.drain.called){active=nullptr;t.started=false;if(t.drain.failed){finished=true;Fail(r,contract::Gate::Query);last=r;}return;}
 attempted=true;r.attempted=r.queryCalled=true;r.queryCalls=t.drain.waitCalls;r.queryReturns=t.drain.waitReturns;r.queryResult=t.drain.waitResult;
 r.queryBefore=query_retirement_live::before.phase;r.queryAfter=query_retirement_live::after.phase;last=r;
 if(!t.drain.retired||t.drain.failed||t.drain.epoch!=t.epoch||t.drain.serial!=serial||t.drain.waitCalls!=1||t.drain.waitReturns!=1||t.drain.waitResult){Fail(last,contract::Gate::Query);return;}
 t.query=query_retirement_live::after;t.ready=true;
}
bool SamePhysics(const physics_observation::Receipt&a,const physics_observation::Receipt&b){return std::memcmp(&a,&b,sizeof(a))==0;}
struct Backend:resource_scope_live_v1::NativeLocks {
 Invocation&t;const late_frame::Owners&o;U32 self{},caller{},app{};const cloth_observation::Result&cloth;const physics_observation::Result&physics;contract::Receipt&r;bool gateHeld{};
 Backend(Invocation&inv,const late_frame::Window&window,const late_frame::Owners&owner,U32 collection,U32 returnAddress,U32 application,volatile LONG&serial,
         const cloth_observation::Result&clothResult,const physics_observation::Result&physicsResult,contract::Receipt&result):
  NativeLocks{inv.entry,window,serial,inv.owners},t(inv),o(owner),self(collection),caller(returnAddress),app(application),cloth(clothResult),physics(physicsResult),r(result){}
 bool Fresh(){
  if(!Current(t,w,o,self,caller,app,serialSource,r)||!resource_scope_live_v1::StartupReady()||!LoaderReady(t,r,true))return false;
  if(vehicle_processor_live::failed||vehicle_processor_live::counter.Faulted()||!vehicle_processor_live::OwnersUnchanged())return Fail(r,contract::Gate::Loader);
  // The App's original callbacks may have opened new query work. Re-read
  // phase0 and the complete real query owner/job/audio graph now, not merely
  // the earlier successful wait receipt.
  query_retirement::Inspector<decltype(&Word)> query{Word,base};query_retirement::Snapshot q{};
  const bool captured=query.Capture(o,q,0);r.queryRecheck=q.phase;++r.queryChecks;
  if(!captured||!query_retirement::Same(q,t.query)||!query_retirement::Same(q,query_retirement_live::after)||query_retirement_live::failed)
   return Fail(r,contract::Gate::Query,query.fault.address,query.fault.actual,query.fault.expected);
  if(!query_retirement_live::attempted||!query_retirement_live::retired||query_retirement_live::expectedEpoch!=t.epoch||
     query_retirement_live::expectedThread!=t.entry.thread||query_retirement_live::expectedGroup!=q.query+0xbb4||
     InterlockedCompareExchange(&query_retirement_live::active,0,0)||InterlockedCompareExchange(&query_retirement_live::receiptCalls,0,0)!=1||
     InterlockedCompareExchange(&query_retirement_live::receiptReturns,0,0)!=1||InterlockedCompareExchange(&query_retirement_live::receiptResult,0,0)||
     InterlockedCompareExchange(&query_retirement_live::receiptMismatch,0,0))return Fail(r,contract::Gate::Query,q.query);
  const contract::QueryProof proof{t.drain.epoch,t.drain.serial,query_retirement_live::expectedThread,q.phase,t.drain.called,t.drain.retired,t.drain.failed,
   {t.drain.waitCalls,t.drain.waitReturns,t.drain.waitResult,false}};
  const auto lineage=contract::SameFrame(t.epoch,t.entry.serial,t.entry.thread,proof,cloth,physics);
  if(lineage!=contract::Gate::Observed)return Fail(r,lineage);
  if(!cloth.observed||!cloth.original_returned||cloth.epoch!=t.epoch||cloth.serial!=t.entry.serial||cloth_observer_live::faulted.load()||cloth_observer_live::appActive.load()||
     cloth_observer_live::consumedMaster!=cloth.master||!cloth_observation::MasterComplete(cloth_observer_live::MasterReceipt(),cloth.master)||
     cloth.syncCalls!=1||cloth.syncReturns!=1||cloth.syncResult||cloth_observer_live::syncCalls.load()!=1||cloth_observer_live::syncReturns.load()!=1||cloth_observer_live::syncResult.load()||cloth_observer_live::syncBad.load()||
     !cloth_observer_live::OwnerUnchanged()||cloth_observer_live::pinned.app!=app)return Fail(r,contract::Gate::Cloth);
  U32 flags{},tail{};if(!Word(cloth_observer_live::pinned.coordinator+0x9c,flags)||!Word(cloth_observer_live::pinned.coordinator+0xa0,tail)||flags||(tail&255u))return Fail(r,contract::Gate::Cloth);
  if(!physics.observed||physics.epoch!=t.epoch||!physics_observation::Complete(physics.receipt,t.entry.serial)||!SamePhysics(physics.receipt,physics_observer_live::Receipt())||
     physics_observer_live::consumedSerial!=t.entry.serial||physics_observer_live::failed.load()||!physics_observer_live::OwnerUnchanged())return Fail(r,contract::Gate::Physics);
  return true;
 }
 bool CloseGate(contract::Receipt&out){
  if(!Fresh())return false;
  if(!Code())return Fail(out,contract::Gate::Code);
  gateHeld=vehicle_processor_live::counter.TryClose();
  if(!gateHeld)return Fail(out,contract::Gate::Counter);
  out.counterDuring=vehicle_processor_live::counter.Word();return true;
 }
 void OpenGate(contract::Receipt&out){if(gateHeld){out.gateReleased=vehicle_processor_live::counter.Release();gateHeld=false;out.counterAfter=vehicle_processor_live::counter.Word();
  // A new callback may enter immediately after the successful release CAS.
  // That post-scope count is diagnostic, not a new zero-count requirement.
  if(!out.gateReleased||vehicle_processor_live::counter.Faulted()){out.noopObserved=false;Fail(out,contract::Gate::Cleanup);}}}
 bool Validate(resource_scope_v1::Receipt&out){
  if(!gateHeld||vehicle_processor_live::counter.Word()!=vehicle_processor::Closed||vehicle_processor_live::counter.Faulted()){Fail(r,contract::Gate::Counter);return false;}
  return NativeLocks::Validate(out)&&Fresh();
 }
};
contract::Receipt After(Invocation&t,const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 app,volatile LONG&serialSource,
 const cloth_observation::Result&cloth,const physics_observation::Result&physics){
 if(!t.started)return {};
 contract::Receipt r=last;r.epoch=t.epoch;r.serial=t.entry.serial;r.thread=t.entry.thread;r.nonce=t.nonce;
 if(active!=&t||!t.ready){finished=true;if(r.gate==contract::Gate::Pending)Fail(r,contract::Gate::Query);return last=r;}
 Backend backend{t,w,o,self,caller,app,serialSource,cloth,physics,r};
 contract::HeldNoopSafely(backend,r);finished=true;return last=r;
}
void Leave(Invocation&t){if(active==&t){if(attempted&&!finished){finished=true;Fail(last,contract::Gate::Exception);}active=nullptr;}t.started=false;}
}
