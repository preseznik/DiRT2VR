// Native lower-level race activation. Includes remain frozen once executed.
#include "../activation-timeline-prereqs-20261018/native-timeline-plugins-v1.h"
#include "../activation-timeline-prereqs-20261018/native-timeline-code-guards-v1.h"
#include "native-profile-start-predicate-v2.inc"
#include "../scheduler-timestep-provenance-20261018/native-frame-code-guards-v1.h"
// These contracts are supplied by the separate broadcaster/scheduler audits.
#include "../activation-broadcaster-20261018/native-broadcaster-inventory-v1.h"
#include "../activation-broadcaster-20261018/native-broadcaster-code-guards-v1.h"
// V2 binds the actual crowd system and per-game buffering receiver.
#include "../activation-broadcaster-v2-20261018/native-broadcaster-owned-v2.h"
#include "../activation-broadcaster-v2-20261018/native-broadcaster-owned-code-guards-v2.h"
bool ActivationBroadcasterBound(U32 game,U32 broadcaster){
 static U32 refs[2]{},boundBroadcaster{};static bool rejected{},reported{};
 if(rejected || (boundBroadcaster && boundBroadcaster!=broadcaster))return false;
 if(!native_broadcaster_v1::CodeSignatures(LoadedAdapterEqual,base) || !native_broadcaster_v2::CodeSignatures(LoadedAdapterEqual,base))return false;
 if(!boundBroadcaster){
  native_broadcaster_v1::Inventory inventory{};
  if(native_broadcaster_v1::InspectRoutes(LoadedAdapterRead,base,broadcaster,inventory)!=native_broadcaster_v1::Gate::RoutesReady || inventory.routeCount!=2){rejected=true;return false;}
  const char* names[]{"organisms","BroadcastMessage_CrowdState_brmq"};
  for(U32 i=0;i<2;++i){
   bool found=false;for(U32 j=0;j<2;++j)found=found||!strcmp(inventory.routes[j].name,names[i]);
   if(!found){rejected=true;return false;}
   const U32 result=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0x117B460))(&refs[i],names[i]);
   if(result!=reinterpret_cast<U32>(&refs[i]) || !initial_load_v2::Ptr(refs[i])){rejected=true;return false;}
  }
  boundBroadcaster=broadcaster;
 }
 native_broadcaster_v2::Snapshot snapshot{};
 const auto gate=native_broadcaster_v2::InspectOwnedRoutes(LoadedAdapterRead,base,game,broadcaster,refs,snapshot);
 if(gate!=native_broadcaster_v2::Gate::Ready){char line[260]{};sprintf_s(line,"ACT_ROUTE owned_reject gate=%u field=%s address=%08x actual=%08x expected=%08x\r\n",unsigned(gate),snapshot.failure.field?snapshot.failure.field:"none",snapshot.failure.address,snapshot.failure.actual,snapshot.failure.expected);Log(line);rejected=true;return false;}
 if(!reported){char line[220]{};sprintf_s(line,"ACT_ROUTE owned_ready crowd=%08x relay=%08x dispatcher0=%08x dispatcher1=%08x routes=2\r\n",snapshot.owners[0],snapshot.owners[1],refs[0],refs[1]);Log(line);reported=true;}
 return true;
}
bool ActivationNativeStepBound(U32 game,const double* step,U32 caller){
 native_frame_provenance_v1::Snapshot snapshot{};
 return !activationEpochFailed && activationEpoch.active && activationEpoch.current==activationCapturedEpoch && activationEpoch.consumed==activationEpoch.current &&
  native_frame_provenance_v1::InspectHeld(LoadedAdapterRead,base,game,Read(game+0x2250),reinterpret_cast<U32>(step),caller,snapshot)==native_frame_provenance_v1::Gate::Ready && snapshot.delta==activationNativeStep;
}
namespace activation_live_v1 {
using native_activation::Status;using native_activation::Stage;using native_activation::Phase;
using Handler=U32(__thiscall*)(void*,const void*);
Handler originalGreen{};bool hookInstalled{},failed{},initialized{};
U32 services[3]{};native_activation::Context context{};native_activation::Attempt attempt{};
struct GreenReceipt {U32 game{},thread{},message{},words[4]{},result{};LONG ordinal{};bool originalReturned{};} green;
ULONGLONG began{},lastNote{};
constexpr unsigned char GreenPrefix[]{0x83,0xEC,0x08,0x56,0x57};
U32 __fastcall ObserveGreen(void* self,void*,const void* message) {
 GreenReceipt sample{};
 const bool owned=reinterpret_cast<U32>(self)==heldOwner && GetCurrentThreadId()==static_cast<DWORD>(holdThread);
 if(owned){sample.game=reinterpret_cast<U32>(self);sample.thread=GetCurrentThreadId();sample.message=reinterpret_cast<U32>(message);
  if(!initial_load_v2::Bytes(sample.message,reinterpret_cast<unsigned char*>(sample.words),16))sample.message=0;}
 const U32 result=originalGreen(self,message); // Forward exactly once; never synthesize a native message.
 if(owned && sample.message && sample.words[0]==At(0x13280B4) && sample.words[2]==1 && ((sample.words[3]>>16)&255)==0){
  sample.originalReturned=true;sample.result=result;sample.ordinal=green.ordinal+1;green=sample;
  char line[220]{};sprintf_s(line,"ACT_GREEN original_return ordinal=%ld game=%08x message=%08x kind=%u flags=%08x result=%u\r\n",green.ordinal,green.game,green.message,green.words[2],green.words[3],result);Log(line);
 }
 return result;
}
bool HookIdentity(){
 if(!hookInstalled || !originalGreen)return false;
 unsigned char patch[5]{},trampoline[10]{};U32 target{},back{};
 if(!initial_load_v2::Bytes(At(0x74FA90),patch,5)||patch[0]!=0xE9||
    !initial_load_v2::Bytes(reinterpret_cast<U32>(originalGreen),trampoline,10)||memcmp(trampoline,GreenPrefix,5)||trampoline[5]!=0xE9)return false;
 memcpy(&target,patch+1,4);memcpy(&back,trampoline+6,4);
 return At(0x74FA90)+5+target==reinterpret_cast<U32>(&ObserveGreen) && reinterpret_cast<U32>(originalGreen)+10+back==At(0x74FA90)+5;
}
bool Equal(U32 address,const unsigned char* expected,size_t count){
 if(hookInstalled && address==At(0x74FA90))return count>=5 && !memcmp(expected,GreenPrefix,5) && HookIdentity() &&
  trace::ReadableEqual(reinterpret_cast<void*>(address+5),expected+5,count-5);
 return trace::ReadableEqual(reinterpret_cast<void*>(address),expected,count);
}
void Reject(const char* reason){if(!failed){failed=true;char line[240]{};sprintf_s(line,"ACT rejected stage=%u reason=%s green=%u\r\n",unsigned(attempt.stage),reason,unsigned(attempt.naturalGreenObserved));Log(line);}}
bool Install(){
 if(hookInstalled || originalGreen || !native_activation::CodeSignatures(Equal,base))return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x74FA90)),&ObserveGreen,reinterpret_cast<void**>(&originalGreen))!=MH_OK || !originalGreen)return false;
 if(MH_EnableHook(reinterpret_cast<void*>(At(0x74FA90)))!=MH_OK)return false;
 hookInstalled=true;return HookIdentity() && native_activation::CodeSignatures(Equal,base);
}
bool Services(){
 native_activation::Plan p{};native_timeline_plugins_v1::Graph g{};
 return native_activation::Inspect(LoadedAdapterRead,context,p)==Status::Ready &&
  native_timeline_plugins_v1::Inspect(LoadedAdapterRead,base,context.game,p.wrapper,g)==native_timeline_plugins_v1::Gate::Ready &&
  native_timeline_plugins_v1::BoundLightAndAudio(g,services[0],services[1]) && ActivationBroadcasterBound(context.game,services[2]);
}
bool Proof(const native_activation::Context& c,Phase phase,std::uint64_t frame,double dt){
 if(failed || !initialized || c.game!=context.game || c.transaction!=context.transaction || c.ownerThread!=GetCurrentThreadId() ||
    loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved || !loaded_ack_adapter_v2::state.attempt.acknowledged ||
    !loaded_ack_adapter_v2::Completed(loaded_ack_adapter_v2::state.context,true) || !profile_start_predicate_v2::Ensure(c.game) ||
    !native_timeline_plugins_v1::CodeSignatures(Equal,base) || !native_frame_provenance_v1::CodeSignatures(Equal,base) || !Services() || !HookIdentity())return false;
 if(phase==Phase::TickTimeline && (frame!=activationEpoch.current || dt!=activationNativeStep || !ActivationNativeStepBound(c.game,activationStepPointer,activationStepCaller)))return false;
 return true;
}
struct Native {
 U32 Lookup(U32 image,U32 manager){return reinterpret_cast<U32(__thiscall*)(void*,const char*)>(image+0x733A70-0x400000)(reinterpret_cast<void*>(manager),"startsequence");}
 U32 Local(U32 image,U32 system){return reinterpret_cast<U32(__thiscall*)(void*,U32)>(image+0xDCA040-0x400000)(reinterpret_cast<void*>(system),0);}
 bool QueueRender(U32 image,U32 renderOwner){
  if(image!=base || renderOwner!=context.renderOwner)return false;
  const U32 mailbox=finalization_v2::state.mailbox[2];if(Read(renderOwner+0x1064)!=mailbox || !initial_load_v2::Flag(mailbox+0x38,1))return false;
  struct Message{U32 table,next,mode;}message{};
  reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x659630))(&message,0);
  if(message.table!=At(0x1313C80)||message.next||message.mode)return false;
  return reinterpret_cast<U32(__thiscall*)(void*,const void*)>(At(0x662BB0))(reinterpret_cast<void*>(mailbox),&message)==0;
 }
 void Camera(U32 image,U32 game,U32 camera){reinterpret_cast<void(__thiscall*)(void*,void*)>(image+0x7280B0-0x400000)(reinterpret_cast<void*>(game),reinterpret_cast<void*>(camera));}
 void Reset(U32 image,U32 wrapper){reinterpret_cast<void(__thiscall*)(void*)>(image+0x7214F0-0x400000)(reinterpret_cast<void*>(wrapper));}
 U32 Tick(U32 image,U32 wrapper,double dt){return reinterpret_cast<U32(__thiscall*)(void*,double)>(image+0x721500-0x400000)(reinterpret_cast<void*>(wrapper),dt);}
};
void TickBody(){
 if(failed || attempt.stage==Stage::Complete || loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved)return;
 if(trace::Environment(L"DIRT2VR_ENGINE_ACTIVATION_CANDIDATE")!=L"1")return;
 auto& l=initial_load_v2::ledger;
 if(!initialized){
  if(!loaded_ack_adapter_v2::Completed(loaded_ack_adapter_v2::state.context,true)){Reject("actual completion");return;}
  U32 local{};for(unsigned i=0;i<2;++i)if(Read(l.entries[i]+0x1C)==4)local=i+1;
  if(!local){Reject("actual local entry");return;}
  context={base,l.game,l.render,l.thread,l.entries[local-1],l.liveries[local-1],loaded_ack_adapter_v2::state.context.transaction};
  if(!native_timeline_plugins_v1::CodeSignatures(Equal,base) || !native_frame_provenance_v1::CodeSignatures(Equal,base) || !native_activation::CodeSignatures(Equal,base)){Reject("activation code identity");return;}
  const char* names[]{"start_lights_manager","Audio Message Handler","game broadcaster"};
  for(unsigned i=0;i<3;++i){const U32 result=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0x117B460))(&services[i],names[i]);if(result!=reinterpret_cast<U32>(&services[i]) || !initial_load_v2::Ptr(services[i])){Reject("retained native service");return;}}
  if(!Services()){Reject("native service ownership");return;}
  if(!profile_start_predicate_v2::Ensure(l.game)){Reject("native profile predicate");return;}
  if(!Install()){Reject("native green observer install");return;}
  initialized=true;began=GetTickCount64();
 }
 if(GetTickCount64()-began>45000){Reject("activation deadline");return;}
 // Receipt contains bytes copied during the actual handler lifetime; no retired
 // native message pointer is ever read, and no adapter-created event is emitted.
 if(green.ordinal && !attempt.naturalGreenObserved){
  if(green.ordinal!=1 || !green.originalReturned || green.game!=context.game || green.thread!=context.ownerThread ||
     green.words[0]!=At(0x13280B4) || green.words[2]!=1 || ((green.words[3]>>16)&255)!=0 ||
     (attempt.stage!=Stage::Running && attempt.stage!=Stage::Ticking) || !Proof(context,Phase::ObserveGreen,activationEpoch.current,0)){Reject("native green receipt");return;}
  attempt.naturalGreenObserved=true;attempt.greenFrame=activationEpoch.current;
 }
 Native native;Status status=Status::Rejected;const Stage before=attempt.stage;
 if(before==Stage::New)status=native_activation::QueueRendering(LoadedAdapterRead,Equal,Proof,native,context,GetCurrentThreadId(),attempt);
 else if(before==Stage::RenderQueued)status=native_activation::Begin(LoadedAdapterRead,Equal,Proof,native,context,GetCurrentThreadId(),attempt);
 else if(before==Stage::Running)status=native_activation::Tick(LoadedAdapterRead,Equal,Proof,native,context,GetCurrentThreadId(),activationEpoch.current,activationNativeStep,attempt);
 if(status==Status::Rejected){Reject("native activation guard or readback");return;}
 const ULONGLONG now=GetTickCount64();
 if(before!=attempt.stage || now-lastNote>=1000){native_activation::Plan p{};native_activation::Inspect(LoadedAdapterRead,context,p);char line[350]{};
  sprintf_s(line,"ACT stage=%u status=%u frame=%ld dt=%.17g time=%.9f frames=%u,%u,%u,%u lights=%u render=%u green=%u\r\n",unsigned(attempt.stage),unsigned(status),holdCalls,activationNativeStep,p.timelineTime,p.frameStatus[0],p.frameStatus[1],p.frameStatus[2],p.frameStatus[3],unsigned(p.lightsFlag),p.renderMode,unsigned(attempt.naturalGreenObserved));Log(line);lastNote=now;}
}
}
void NativeActivationTickV2(){
 if(loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved || activation_live_v1::failed || activation_live_v1::attempt.stage==native_activation::Stage::Complete)return;
 native_frame_provenance_v1::Snapshot snapshot{};
 const auto gate=native_frame_provenance_v1::InspectHeld(LoadedAdapterRead,base,heldOwner,Read(heldOwner+0x2250),reinterpret_cast<U32>(activationStepPointer),activationStepCaller,snapshot);
 if(activationEpochFailed || gate!=native_frame_provenance_v1::Gate::Ready || snapshot.delta!=activationNativeStep || !activationEpoch.Enter(heldOwner,GetCurrentThreadId(),activationCapturedEpoch)){
  char line[180]{};sprintf_s(line,"ACT frame_reject gate=%u row=%08x epoch=%llu consumed=%llu caller=%08x\r\n",unsigned(gate),snapshot.row,activationEpoch.current,activationEpoch.consumed,activationStepCaller);Log(line);
  activation_live_v1::Reject("native frame ownership");return;
 }
 __try{activation_live_v1::TickBody();}
 __except(EXCEPTION_EXECUTE_HANDLER){activation_live_v1::Reject("native fault; preserve owned process evidence");}
 activationEpoch.Leave();
}
