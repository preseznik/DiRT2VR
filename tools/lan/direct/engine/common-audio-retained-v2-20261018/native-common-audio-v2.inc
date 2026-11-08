// Compile-only bounded initial -> mode1 -> retained preparation adapter.
// Include after native-initial-load-live-v5.inc and resource-observer adapter.
// Call only from the held owner callback, before preload/cars/track.
#include "audio-preparation-guard-v2.h"
#include "audio-preparation-code-guards-v2.inc"
namespace common_audio_v2 {
namespace guard=audio_preparation_guard_v2;
enum class Phase:U32 {Dormant,Submitted,Uninitializing,Retained,Failed};
struct State {
 Phase phase{}; DWORD thread{};ULONGLONG began{};U32 resources{},allocator{},token{},previousToken{},serialBefore{},uninitCalls{}; bool lastUninitReturned{};
 bool completionObserved{};HMODULE pinned{};guard::Gate lastGate{};guard::Snapshot original{},last{};
 initial_load_v2::JobSample receipt{};
};
State state;
void Note(const char* reason){char line[500]{};sprintf_s(line,
 "COMMON_AUDIO phase=%u reason=%s token=%u stage=%u count=%u initial=%u active=%u fe=%u race=%u worker=%u handoff=%u/%u setup=%u/%u receipt=%u/%u retained=%u engine_stage=%u uninit_mode=%u bank_stage=%u bank_attempts=%u uninit_calls=%u returned=%u\r\n",
 unsigned(state.phase),reason,state.token,state.last.stage,state.last.count,state.last.initial,state.last.active,state.last.fe,state.last.race,
 state.last.worker,state.last.requested,state.last.ack,state.last.setupLoaded,state.last.aiLoaded,unsigned(state.receipt.retired),state.receipt.state,unsigned(state.phase==Phase::Retained),state.last.engineStage,state.last.uninitMode,state.last.bankStage,state.last.pending[18],state.uninitCalls,unsigned(state.lastUninitReturned));Log(line);}
void Fail(const char* reason){if(state.phase!=Phase::Failed){state.phase=Phase::Failed;Note(reason);}}
bool HeldEngine(U32 game){
 if(!heldFlow||heldOwner!=game||holdFailed||headlessFailed||!armed||
    holdThread!=static_cast<LONG>(GetCurrentThreadId())||!holdCalls||!coreReady||coreObserved!=7)return false;
 bootstrap_hold::Objects o{};
 if(bootstrap_hold::Inspect(Read,base,heldFlow,o)!=bootstrap_hold::Gate::Ready||o.owner!=game||
    o.frontend!=Read(game+0x2250)||!originalFrontendUpdate)return false;
 unsigned char jump[5]{};U32 relative{};
 if(!initial_load_v2::Bytes(At(0x577DE0),jump,5)||jump[0]!=0xE9)return false;
 memcpy(&relative,jump+1,4);
 return At(0x577DE0)+5+relative==reinterpret_cast<U32>(&HoldFrontendUpdate);
}
bool Snapshot(U32 game,guard::Snapshot& result){
 const auto read=[](U32 p,U32& v){return initial_load_v2::Bytes(p,&v,4);};
 guard::Inspector<decltype(read)> inspect{read,base};
 state.lastGate=inspect.Inspect(game,result);
 if(state.lastGate!=guard::Gate::Ready){char line[100]{};sprintf_s(line,"COMMON_AUDIO inspector_gate=%u read_fault=%u\r\n",unsigned(state.lastGate),unsigned(inspect.fault));Log(line);}
 return state.lastGate==guard::Gate::Ready;
}
bool Pristine(U32 game,U32 setup){
 const U32 vehicles=Read(At(0x1482D8C)),entity=Read(game+0x224C);
 return Read(vehicles)==At(0x1325688)&&Read(vehicles+0xC)==game&&Read(vehicles+0x1C)==0&&
 Read(vehicles+0x38)==0xffffffffu&&Read(vehicles+0x3C)==0xffffffffu&&Read(vehicles+0x40)==0&&
 Read(vehicles+0x44)==0xffffffffu&&initial_load_v2::Flag(vehicles+0x48,0)&&
 Read(entity)==At(0x1321674)&&initial_load_v2::Flag(entity+0xC,0)&&
 initial_load_v2::Flag(setup+0x78,0)&&initial_load_v2::Flag(setup+0x79,0)&&
 initial_load_v2::Flag(setup+0x7A,0)&&initial_load_v2::Flag(setup+0x7B,0)&&
 initial_load_v2::Flag(setup+0x7C,1)&&Read(setup+0xC)==0;
}
bool ChildQueuesIdle(U32 audio){
 // Native pending getter11779D0 reads active+38 and count+30.
 // No queue call or wait: the owner loop can advance while this stays Pending.
 const U32 queues[]{Read(audio+0xA410+0x104),Read(audio+0xF910+0xC54)};
 for(U32 q:queues){unsigned char active{};U32 count{};
  if(!initial_load_v2::Ptr(q,0x3C)||!initial_load_v2::Byte(q+0x38,active)||!initial_load_v2::Bytes(q+0x30,&count,4))return false;
  if(active&&count)return false;
 }
 return true;
}
bool ReadyForRace(U32 game,const initial_load_v2::Permit& permit){
 U32 frame{};guard::Snapshot current{};
 return state.phase==Phase::Retained&&state.completionObserved&&state.thread==GetCurrentThreadId()&&
  state.thread==permit.thread&&state.allocator==permit.setupAllocator&&permit.exactImageValidated&&
  permit.exclusiveInitialLoad&&permit.nativeQueuesAdvancing&&HeldEngine(game)&&
  initial_load_v2::NativeFrame(permit.setupAllocator,frame)&&Snapshot(game,current)&&
  guard::SameOwner(state.original,current)&&guard::Retained(current)&&
  Read(current.setup+0x6C)==state.token&&resource_observer_adapter_v1::Healthy(state.resources);
}
void TickBody(void* self,const initial_load_v2::Permit& permit){
 if(state.phase==Phase::Failed||state.phase==Phase::Retained)return;
 const U32 game=reinterpret_cast<U32>(self);U32 frame{};
 if(!initialConfigCommitted||!permit.exactImageValidated||!permit.exclusiveInitialLoad||!permit.nativeQueuesAdvancing||
    permit.thread!=GetCurrentThreadId()||!initial_load_v2::NativeFrame(permit.setupAllocator,frame)||!HeldEngine(game)){
  Fail("held engine/native owner context unavailable");return;}
 const ULONGLONG now=GetTickCount64();guard::Snapshot current{};
 if(!Snapshot(game,current)){Fail("immutable audio owner/context/initial graph mismatch");return;}
 state.last=current;
 if(state.phase==Phase::Dormant){
  if(!AudioPreparationSignatures()||!guard::ColdPrepared(current)||!Pristine(game,current.setup)){
   Fail("requires verified native initial and pristine loader; no mode0 fallback");return;}
  state.resources=Read(At(0x15D9A04));
  if(!resource_observer_adapter_v1::Healthy(state.resources)){
   Fail("exact resource observer must already be installed");return;}
  if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
       reinterpret_cast<LPCWSTR>(&TickBody),&state.pinned)){Fail("bridge lifetime pin");return;}
  state.thread=permit.thread;state.allocator=permit.setupAllocator;state.original=current;state.began=now;
  state.previousToken=Read(current.setup+0x6C);state.serialBefore=Read(state.resources+0x74);
  state.phase=Phase::Submitted; // No retry after any native submission begins.
  reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x6EAC50))(reinterpret_cast<void*>(current.setup),0);
  state.token=Read(current.setup+0x6C);
  if(!state.token||state.token==0xffffffffu||state.token==state.previousToken||state.token<=state.serialBefore){
   Fail("mode1 resource token did not advance native serial");return;}
  Note("native common audio queued; race audio remains blocked");return;
 }
 if(state.thread!=permit.thread||state.allocator!=permit.setupAllocator||!guard::SameOwner(state.original,current)||
    !Pristine(game,current.setup)||Read(current.setup+0x6C)!=state.token||!resource_observer_adapter_v1::Healthy(state.resources)){
  Fail("preparation ownership changed; process exit required");return;}
 if(now<state.began||now-state.began>=120000){Fail("preparation deadline; retain until process exit");return;}
 if(state.phase==Phase::Submitted){
  if(current.engineStage!=1){Fail("engine stage reset before this adapter entered native teardown");return;}
  state.receipt=initial_load_v2::Sample(state.resources,state.token);
  if(state.receipt.observerFault||(state.receipt.found&&state.receipt.state==4)){
   Fail("native resource failure or receipt ownership rejection");return;}
  // A pending lookup or intermediate child state is not a failure.
  if(!state.receipt.retired||state.receipt.state!=3||state.receipt.wait!=0)return;
  if(strcmp(state.receipt.path,"audio/audio_loader.xml")!=0&&strcmp(state.receipt.path,"audio\\audio_loader.xml")!=0){
   Fail("mode1 receipt path mismatch");return;}
  state.completionObserved=true;
  if(!guard::Mode1Finished(current))return;
  state.phase=Phase::Uninitializing;Note("mode1 complete; native retained teardown next owner tick");return;
 }
 if(!state.completionObserved||(state.uninitCalls?!guard::TeardownProgress(current):!guard::Mode1Finished(current))){
  Fail("retained teardown prerequisite lost");return;}
 if(!ChildQueuesIdle(current.manager))return;
 ++state.uninitCalls;
 const bool finished=reinterpret_cast<bool(__thiscall*)(void*,U32)>(At(0x9D9B10))(reinterpret_cast<void*>(current.manager),1);
 state.lastUninitReturned=finished;
 if(!Snapshot(game,state.last)||!guard::SameOwner(state.original,state.last)||!guard::TeardownProgress(state.last)){
  Fail("native retained teardown owner changed");return;}
 if(!finished){if(state.uninitCalls==1)Note("native teardown pending; engine stage reset is expected");return;}
 if(!guard::Retained(state.last)){Fail("native teardown returned without retained car setup");return;}
 state.phase=Phase::Retained;Note("native car audio preparation retained; loader may proceed");
}
void Tick(void* self,const initial_load_v2::Permit& permit){
 __try {TickBody(self,permit);}
 __except(EXCEPTION_EXECUTE_HANDLER){Fail("native exception; retain owned process until exit");}
}
}
