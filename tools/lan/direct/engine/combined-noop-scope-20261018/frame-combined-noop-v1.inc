#include "../late-frame-observer-20261018/frame-window.h"
#include "../native-query-retirement-20261018/query-retirement-live.inc"
namespace frame_observer {extern volatile LONG enabled;extern late_frame::Window window;extern volatile LONG physicsSerial;}
#include "../native-vehicle-processor-gate-v2-20261018/processor-gate-live.inc"
#include "../native-physics-retirement-v1-20261018/cloth-observer-live.inc"
#include "combined-live-v1.inc"
namespace vehicle_gate_probe {
unsigned calls{},reports{},armReports{},clothReports{},clothSuccesses{},physicsReports{},physicsSuccesses{};U32 closedWord{},thread{};
void Body(void*){++calls;closedWord=vehicle_processor_live::counter.Word();thread=GetCurrentThreadId();}
}
namespace frame_observer {
using WaitFn=U32(__thiscall*)(void*);using UpdateFn=void(__thiscall*)(void*,const double*);
WaitFn waitOriginal{};UpdateFn collectionOriginal{},physicsOriginal{};
volatile LONG enabled{};bool attempted{};late_frame::Window window{};volatile LONG physicsSerial{};bool lastGood{},reportedFailure{};unsigned entryDiagnostics{};
volatile LONG waitCalls{},collectionCalls{},physicsCalls{},updateSeen{},renderSeen{},phase2Seen{},collectionSeen{},ownerSeen{};
volatile LONG lastCaller{},lastSelf{};unsigned diagnostics{};ULONGLONG diagnosticAt{};
bool Word(U32 address,U32&value){if(address<0x10000||address>0x7ffffffb)return false;__try{value=*reinterpret_cast<const U32*>(address);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool Typed(U32 p,U32 table){U32 actual{};return Word(p,actual)&&actual==At(table);}
bool Owners(late_frame::Owners&o){U32 loader{};if(!Word(At(0x1482D8C),loader)||!Typed(loader,0x1325688))return false;
 if(!Word(loader+0xC,o.game)||!Word(loader+0x10,o.render)||!Word(loader+0x14,o.collection))return false;
 o.thread=GetCurrentThreadId();return Typed(o.game,0x1329A48)&&Typed(o.render,0x1324164)&&Typed(o.collection,0x135A6A8);}
bool PhysicsIdle(U32 game,U32&facade,U32&world){U32 same{},phase{};return Typed(game+0x2120,0x1323D74)&&Word(game+0x2148,facade)&&facade>=0x10000&&Word(game+0x20C0,same)&&same==facade&&Word(facade+4,world)&&world>=0x10000&&Word(world+0x7C4,phase)&&phase==0;}
U32 __fastcall WaitReturned(void*self,void*){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress());const U32 result=waitOriginal(self);InterlockedIncrement(&waitCalls); // The only wait; preserve its exact result.
 const bool update=caller==At(0x694919)||caller==At(0x6949A0),render=caller==At(0x694924)||caller==At(0x694974),phase2=caller==At(0x694700);
 if(InterlockedCompareExchange(&enabled,0,0) && (update||render||phase2)){InterlockedIncrement(update?&updateSeen:(render?&renderSeen:&phase2Seen));late_frame::Owners o{};if(Owners(o)&&reinterpret_cast<U32>(self)==(update?o.game:o.render))window.WaitReturned(o,update?late_frame::Wait::Update:(render?late_frame::Wait::Render:late_frame::Wait::Phase2),result);}
 return result;
}
void __fastcall PhysicsReturned(void*self,void*,const double*delta){
 const auto token=physics_observer_live::BeforeStage7(self,reinterpret_cast<U32>(_ReturnAddress()));
 bool returned=false;U32 serial{};
 __try {
  physicsOriginal(self,delta);returned=true;InterlockedIncrement(&physicsCalls);
  late_frame::Owners o{};U32 facade{},world{};
  if(InterlockedCompareExchange(&enabled,0,0)&&Owners(o)&&reinterpret_cast<U32>(self)==o.game+0x2120&&PhysicsIdle(o.game,facade,world))serial=static_cast<U32>(InterlockedIncrement(&physicsSerial));
 } __finally {physics_observer_live::AfterStage7(token,returned,serial);}
}
void __fastcall Collection(void*self,void*actualApp,const double*delta){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress());late_frame::Owners o{};InterlockedIncrement(&collectionCalls);InterlockedExchange(&lastCaller,static_cast<LONG>(caller));InterlockedExchange(&lastSelf,reinterpret_cast<LONG>(self));
 if(caller==At(0x69471F)){InterlockedIncrement(&collectionSeen);if(Owners(o))InterlockedIncrement(&ownerSeen);}
 const bool relevant=InterlockedCompareExchange(&enabled,0,0)&&caller==At(0x69471F)&&Owners(o)&&reinterpret_cast<U32>(self)==o.collection;
 if(!relevant){collectionOriginal(self,delta);return;}
 if(window.inside){window.failed=true;collectionOriginal(self,delta);return;} // Preserve the outer observation on reentrancy.
 combined_live_v1::Invocation invocation{};
 cloth_observation::Result cloth{};physics_observation::Result physics{};combined_noop_v1::Receipt combined{};
 __try {
  combined_live_v1::Before(invocation,window,o,reinterpret_cast<U32>(self),caller,reinterpret_cast<U32>(actualApp),physicsSerial);
  cloth=cloth_observer_live::InvokeOriginal(collectionOriginal,self,delta,caller,window,o,physicsSerial);
  physics=physics_observer_live::AtApp(window,o,reinterpret_cast<U32>(self),caller,physicsSerial,cloth);
  combined=combined_live_v1::After(invocation,window,o,reinterpret_cast<U32>(self),caller,reinterpret_cast<U32>(actualApp),physicsSerial,cloth,physics);
 } __finally {combined_live_v1::Leave(invocation);}
 combined_noop_v1::Receipt preflight{};
 if(combined_live_v1::TakePreflightFailure(preflight)){char line[768]{};
  sprintf_s(line,"COMBINED_PREFLIGHT terminal=1 gate=%u epoch=%u serial=%u thread=%u nonce=%u fault_gate=%u address=%08x actual=%08x expected=%08x exception=%08x attempted=%u query_called=%u observed=%u construction_permitted=0 model_calls=0 publication=0\r\n",unsigned(preflight.gate),preflight.epoch,preflight.serial,preflight.thread,preflight.nonce,unsigned(preflight.fault.gate),preflight.fault.address,preflight.fault.actual,preflight.fault.expected,preflight.exception,unsigned(preflight.attempted),unsigned(preflight.queryCalled),unsigned(preflight.noopObserved));Log(line);
 }
 if(combined.attempted){const auto&r=combined.resource;char line[2048]{};
  sprintf_s(line,"COMBINED_NOOP observed=%u returned=%u gate=%u epoch=%u serial=%u thread=%u nonce=%u query_called=%u query_epoch=%u query_serial=%u query_wait_calls=%u query_wait_returns=%u query_wait_result=%08x query_phase_before=%u query_phase_after=%u query_phase_recheck=%u query_rechecks=%u cloth_epoch=%u cloth_serial=%u physics_epoch=%u physics_serial=%u physics_sequence=%u gate_closed=%u gate_released=%u gate_during=%08x gate_after=%08x acquired_s=%u acquired_w=%u released_w=%u released_s=%u jobs_before=%u jobs_after=%u resource_gate=%u fault_gate=%u address=%08x actual=%08x expected=%08x exception=%08x resource_exception=%08x all_workers_retired=0 construction_permitted=0 model_calls=0 publication=0\r\n",
   unsigned(combined.noopObserved),unsigned(combined.returned),unsigned(combined.gate),combined.epoch,combined.serial,combined.thread,combined.nonce,unsigned(combined.queryCalled),invocation.drain.epoch,invocation.drain.serial,combined.queryCalls,combined.queryReturns,combined.queryResult,combined.queryBefore,combined.queryAfter,combined.queryRecheck,combined.queryChecks,cloth.epoch,cloth.serial,physics.epoch,physics.receipt.serial,physics.receipt.sequence,unsigned(combined.gateClosed),unsigned(combined.gateReleased),combined.counterDuring,combined.counterAfter,unsigned(r.schedulerAcquired),unsigned(r.workerAcquired),unsigned(r.workerReleased),unsigned(r.schedulerReleased),r.activeJobs,r.finalJobs,unsigned(r.gate),unsigned(combined.fault.gate),combined.fault.address,combined.fault.actual,combined.fault.expected,combined.exception,r.exception);Log(line);
  const auto&p=invocation.owners;sprintf_s(line,"COMBINED_OWNERS app=%08x game=%08x render=%08x collection=%08x bootstrap=%08x loader=%08x manager=%08x scheduler=%08x worker=%08x world=%08x query=%08x app_thread=%u physics_thread=%u construction_permitted=0\r\n",p.app,p.game,p.render,p.collection,p.bootstrap,p.loader,p.manager,p.scheduler,p.worker,invocation.query.world,invocation.query.query,combined.thread,physics.receipt.thread);Log(line);
 }
 if(vehicle_gate_probe::physicsReports++<12||physics.observed&&vehicle_gate_probe::physicsSuccesses++<4){const auto& p=physics.receipt;char line[660]{};sprintf_s(line,"PHYSICS_RETIRE observed=%u gate=%u epoch=%u sequence=%u serial=%u thread=%u handling_calls=%u handling_result=%u predicate_calls=%u found=%u aggregate_calls=%u aggregate_result=%u active1=%u active7=%u bad=%u unbound=%u models=%u cloth_jobs=%u all_workers_retired=0 construction_permitted=0\r\n",unsigned(physics.observed),unsigned(physics.gate),physics.epoch,p.sequence,p.serial,p.thread,p.handlingCalls,p.handlingResult,p.predicateCalls,p.found,p.aggregateCalls,p.aggregateResult,p.active1,p.active7,p.bad,physics.unbound,physics.modelCount,physics.clothCount);Log(line);}

 if((cloth.observed&&vehicle_gate_probe::clothSuccesses++<4)||(!cloth.observed&&vehicle_gate_probe::clothReports++<8)){
  const auto& p=cloth_observer_live::pinned;char line[1200]{};
  sprintf_s(line,"CLOTH proof observed=%u original_returned=%u gate=%u epoch=%u serial=%u master=%u aggregate_calls=%u aggregate_returns=%u aggregate_result=%08x aggregate_mask=%u sync_calls=%u sync_returns=%u sync_result=%08x unbound=%u game=%08x render=%08x collection=%08x app=%08x cloth=%08x master_owner=%08x renderer=%08x thread=%u current_epoch=%u current_serial=%ld fault_gate=%u address=%08x actual=%08x expected=%08x all_workers_retired=0 construction_permitted=0 model_calls=0 publication=0\r\n",unsigned(cloth.observed),unsigned(cloth.original_returned),unsigned(cloth.gate),cloth.epoch,cloth.serial,cloth.master,cloth.aggregateCalls,cloth.aggregateReturns,cloth.aggregateResult,cloth.aggregateMask,cloth.syncCalls,cloth.syncReturns,cloth.syncResult,cloth.unbound,p.game,p.render,p.collection,p.app,p.cloth,p.master,p.renderer,GetCurrentThreadId(),window.epoch+1,InterlockedCompareExchange(&physicsSerial,0,0),unsigned(cloth.fault.gate),cloth.fault.address,cloth.fault.actual,cloth.fault.expected);Log(line);
  sprintf_s(line,"CLOTH_BRANCH epoch=%u serial=%u master=%u predicate_calls=%u predicate_mask=%u absent_mask=%u required_mask=%u kind_count=%u kind0=%u kind1=%u kind2=%u kind3=%u kind4=%u kind5=%u kind6=%u kind7=%u kind8=%u kind9=%u\r\n",cloth.epoch,cloth.serial,cloth.master,cloth.predicateCalls,cloth.predicateMask,cloth.absentMask,cloth.requiredMask,cloth.kindCount,cloth.kinds[0],cloth.kinds[1],cloth.kinds[2],cloth.kinds[3],cloth.kinds[4],cloth.kinds[5],cloth.kinds[6],cloth.kinds[7],cloth.kinds[8],cloth.kinds[9]);Log(line);
 }
 U32 facade{},world{};const U32 serial=static_cast<U32>(InterlockedCompareExchange(&physicsSerial,0,0));
 const bool idle=PhysicsIdle(o.game,facade,world);
 const bool good=window.Enter(o,serial,idle);
 if(entryDiagnostics++<5){U32 phase{};Word(world+0x7C4,phase);char detail[500]{};sprintf_s(detail,"FRAME_ENTRY good=%u epoch=%u windows=%u serial=%u idle=%u phase=%u update=%u render=%u phase2=%u primed=%u failed=%u facade=%08x world=%08x thread=%u\r\n",unsigned(good),window.epoch,window.windows,serial,unsigned(idle),phase,unsigned(window.update),unsigned(window.render),unsigned(window.phase2),unsigned(window.primed),unsigned(window.failed),facade,world,o.thread);Log(detail);}
 if(good!=lastGood||(good&&window.windows%300==1)||(window.failed&&!reportedFailure)){
  char line[360]{};sprintf_s(line,"FRAME_WINDOW observed=%u epoch=%u windows=%u physics=%u update_wait=%u render_wait=%u phase2_wait=%u game=%08x render=%08x collection=%08x facade=%08x world=%08x failed=%u all_workers_retired=0 construction_permitted=0\r\n",unsigned(good),window.epoch,window.windows,serial,unsigned(window.update),unsigned(window.render),unsigned(window.phase2),o.game,o.render,o.collection,facade,world,unsigned(window.failed));Log(line);lastGood=good;reportedFailure=window.failed;
 }
 // Original collection already returned through the observation-only wrapper above.
 window.Leave();
}
#include "../private-handling-app-wrapper-20261018/collection-entry-thunk.inc"
bool Install(){
 constexpr unsigned char waitBytes[]{0x8b,0x81,0x20,0x10,0,0,0x8b,0x50,4};
 constexpr unsigned char collectionBytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf8,0x83,0xec,0x1c};
 constexpr unsigned char physicsBytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x44};
 if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0xBE6500)),waitBytes,sizeof(waitBytes))||!trace::ReadableEqual(reinterpret_cast<void*>(At(0xC2AE70)),collectionBytes,sizeof(collectionBytes))||!trace::ReadableEqual(reinterpret_cast<void*>(At(0x6CD440)),physicsBytes,sizeof(physicsBytes)))return false;
 const bool created=MH_CreateHook(reinterpret_cast<void*>(At(0xBE6500)),reinterpret_cast<void*>(WaitReturned),reinterpret_cast<void**>(&waitOriginal))==MH_OK&&MH_CreateHook(reinterpret_cast<void*>(At(0xC2AE70)),reinterpret_cast<void*>(CollectionEntry),reinterpret_cast<void**>(&collectionOriginal))==MH_OK&&MH_CreateHook(reinterpret_cast<void*>(At(0x6CD440)),reinterpret_cast<void*>(PhysicsReturned),reinterpret_cast<void**>(&physicsOriginal))==MH_OK;
 if(!created)return false;
 for(const U32 va:{0xBE6500u,0xC2AE70u,0x6CD440u})if(MH_EnableHook(reinterpret_cast<void*>(At(va)))!=MH_OK)return false;
 InterlockedExchange(&enabled,1);return true;
}
void Diagnose(){
 if(!InterlockedCompareExchange(&enabled,0,0)||diagnostics>=4||GetTickCount64()<diagnosticAt)return;
 ++diagnostics;diagnosticAt=GetTickCount64()+1000;
 U32 l{},lt{},g{},gt{},r{},rt{},c{},ct{},facade{},world{};Word(At(0x1482D8C),l);Word(l,lt);Word(l+12,g);Word(g,gt);Word(l+16,r);Word(r,rt);Word(l+20,c);Word(c,ct);
 char line[720]{};sprintf_s(line,"FRAME_DIAG wait=%ld collection=%ld physics=%ld update_seen=%ld render_seen=%ld phase2_seen=%ld collection_seen=%ld owner_seen=%ld caller=%08x self=%08x loader=%08x/%08x game=%08x/%08x render=%08x/%08x collection=%08x/%08x serial=%ld idle=%u\r\n",waitCalls,collectionCalls,physicsCalls,updateSeen,renderSeen,phase2Seen,collectionSeen,ownerSeen,lastCaller,lastSelf,l,lt,g,gt,r,rt,c,ct,physicsSerial,unsigned(PhysicsIdle(g,facade,world)));Log(line);
}
void AfterNativeStartup(){
 Diagnose();
 if(attempted || activation_live_v1::failed || activation_live_v1::attempt.stage!=native_activation::Stage::Complete)return;
 // All original audio/activation byte guards have completed before these code hooks exist.
 // No guard is skipped: every terminal startup path retains its original behavior.
 if(GetCurrentThreadId()!=static_cast<DWORD>(holdThread)||initial_load_v2::ledger.phase!=initial_load_v2::Phase::FinalizationBlocked||finalization_v2::state.phase!=finalization_v2::Phase::RegisteredBlocked||loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved)return;
 attempted=true;if(Install())Log("FRAME_WINDOW hooks installed after native startup completed; observation only\r\n");else Log("FRAME_WINDOW installation rejected; no construction\r\n");
}

}
