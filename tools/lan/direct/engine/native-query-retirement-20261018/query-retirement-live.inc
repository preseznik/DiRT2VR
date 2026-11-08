// Compile-only bounded native query retirement. No entrant construction.
#include "query-retirement.h"
#include "query-code-guards.h"
namespace query_retirement_live {
namespace contract=query_retirement;
using Wait=U32(__thiscall*)(void*,U32);
Wait originalWait{};
volatile LONG enabled{},active{},receiptCalls{},receiptReturns{},receiptResult{},receiptMismatch{};
U32 expectedGroup{},expectedThread{},expectedEpoch{},installedGame{};
bool installAttempted{},attempted{},failed{},retired{};
contract::Fault lastFault{};contract::Snapshot before{},after{};
struct Result {bool called{},retired{},failed{};contract::Fault fault{};U32 epoch{},serial{},waitCalls{},waitReturns{},waitResult{0xffffffffu};bool all_workers_retired{},construction_permitted{};};
Result last{};
bool Word(U32 address,U32&value){if(!contract::Ptr(address))return false;__try{value=*reinterpret_cast<const U32*>(address);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool Bytes(U32 p,const unsigned char*bytes,unsigned size){for(unsigned i=0;i<size;++i){U32 value{};if(!Word((p+i)&~3u,value)||((value>>(((p+i)&3u)*8))&255u)!=bytes[i])return false;}return true;}
bool Jump(U32 p,U32 destination){U32 first{},offset{};return Word(p,first)&&(first&255u)==0xe9&&Word(p+1,offset)&&p+5+offset==destination;}
U32 __fastcall WaitReturned(void*self,void*,U32 timeout){
 const U32 caller=reinterpret_cast<U32>(_ReturnAddress());
 const bool related=InterlockedCompareExchange(&active,0,0)&&reinterpret_cast<U32>(self)==expectedGroup;
 const bool exact=related&&caller==At(0x10318C3)&&GetCurrentThreadId()==expectedThread&&timeout==0xffffffffu;
 if(related){if(InterlockedIncrement(&receiptCalls)!=1||!exact)InterlockedExchange(&receiptMismatch,1);}
 const U32 result=originalWait(self,timeout); // Exactly one original wait, including unrelated callers.
 if(related){InterlockedExchange(&receiptResult,static_cast<LONG>(result));InterlockedIncrement(&receiptReturns);}
 return result; // Preserve the complete native U32 return value.
}
bool HookIntact(){
 const U32 target=At(0x1086FB0),trampoline=reinterpret_cast<U32>(originalWait);
 // This x86 prologue comprises4+6 bytes; MinHook replaces5 and relocates10.
 return InterlockedCompareExchange(&enabled,0,0)&&contract::Ptr(trampoline,15)&&
  Jump(target,reinterpret_cast<U32>(&WaitReturned))&&Bytes(target+5,query_code::Code2+5,sizeof(query_code::Code2)-5)&&
  Bytes(trampoline,query_code::Code2,10)&&Jump(trampoline+10,target+10);
}
bool Tables(){return Read(At(0x1398830))==At(0x57E5B0)&&Read(At(0x1398834))==At(0x1031C60)&&Read(At(0x13C88B4))==At(0xECC710)&&Read(At(0x13C88BC))==At(0x115FB90)&&Read(At(0x13C88C0))==At(0x115FBC0)&&Read(At(0x12EED28))==At(0x44EC50)&&Read(At(0x1328204))==At(0x767B90);}
bool Install(){
 if(!query_code::Verify(Word,base)||!Tables())return false;
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x1086FB0)),reinterpret_cast<void*>(&WaitReturned),reinterpret_cast<void**>(&originalWait))!=MH_OK)return false;
 if(MH_EnableHook(reinterpret_cast<void*>(At(0x1086FB0)))!=MH_OK){MH_RemoveHook(reinterpret_cast<void*>(At(0x1086FB0)));originalWait=nullptr;return false;}
 InterlockedExchange(&enabled,1);return HookIntact();
}
void AfterNativeStartup(){
 if(installAttempted||activation_live_v1::failed||activation_live_v1::attempt.stage!=native_activation::Stage::Complete)return;
 if(GetCurrentThreadId()!=static_cast<DWORD>(holdThread)||initial_load_v2::ledger.phase!=initial_load_v2::Phase::FinalizationBlocked||finalization_v2::state.phase!=finalization_v2::Phase::RegisteredBlocked||loaded_ack_adapter_v2::state.phase!=loaded_ack_adapter_v2::Phase::ProgressObserved)return;
 installAttempted=true;installedGame=loadOwner;
 if(Install())Log("QUERY_RETIRE observer installed after native startup; construction_permitted=0 all_workers_retired=0\r\n");
 else{failed=true;lastFault={contract::Gate::Hook,At(0x1086FB0),0,0};Log("QUERY_RETIRE install rejected; no native drain\r\n");}
}
bool NativeDrain(U32 world,U32&exception){__try{reinterpret_cast<void(__thiscall*)(void*)>(At(0x1082670))(reinterpret_cast<void*>(world));return true;}__except(EXCEPTION_EXECUTE_HANDLER){exception=GetExceptionCode();return false;}}
Result TryRetire(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG& serialSource){
 if(attempted||failed||!InterlockedCompareExchange(&enabled,0,0))return {}; // Never replay an old epoch's successful receipt.
 const U32 serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));
 if(!contract::Frame(w,o,self,caller,At(0x69471F),GetCurrentThreadId(),serial)||o.game!=installedGame)return {};
 // Consume no frame receipt here. Parent calls Window::Enter only after this returns.
 const U32 world=Read(Read(o.game+0x2148)+4),phase=Read(world+0x7c4);
 if(phase==0)return {}; // Nothing to retire; never call on simulation phases1..6.
 last={};last.epoch=w.epoch+1;last.serial=serial;
 auto reject=[&](contract::Fault f){failed=true;last.failed=true;last.fault=f;lastFault=f;return last;};
 if(phase!=7)return reject({contract::Gate::Phase,world+0x7c4,phase,7});
 if(!HookIntact()||!query_code::Verify(Word,base,true)||!Tables())return reject({contract::Gate::Code,At(0x1086FB0),0,0});
 contract::Inspector<decltype(&Word)> inspect{Word,base};contract::Snapshot repeat{};
 if(!inspect.Capture(o,before,7))return reject(inspect.fault);
 if(!inspect.Capture(o,repeat,7)||!contract::Same(before,repeat))return reject({contract::Gate::Changed,before.world,0,0});
 expectedGroup=before.query+0xbb4;expectedThread=o.thread;expectedEpoch=last.epoch;
 InterlockedExchange(&receiptCalls,0);InterlockedExchange(&receiptReturns,0);InterlockedExchange(&receiptResult,-1);InterlockedExchange(&receiptMismatch,0);
 attempted=true;last.called=true;MemoryBarrier();InterlockedExchange(&active,1);
 U32 exception{};const bool returned=NativeDrain(before.world,exception);
 InterlockedExchange(&active,0);MemoryBarrier();
 last.waitCalls=static_cast<U32>(InterlockedCompareExchange(&receiptCalls,0,0));last.waitReturns=static_cast<U32>(InterlockedCompareExchange(&receiptReturns,0,0));last.waitResult=static_cast<U32>(InterlockedCompareExchange(&receiptResult,0,0));
 if(!returned)return reject({contract::Gate::Exception,before.world,exception,0});
 const contract::Receipt receipt{last.waitCalls,last.waitReturns,last.waitResult,InterlockedCompareExchange(&receiptMismatch,0,0)!=0};
 if(!contract::Complete(receipt))return reject({contract::Gate::Receipt,expectedGroup,last.waitResult,0});
 if(!HookIntact()||!inspect.Capture(o,after,0)||!contract::Same(before,after,true))return reject({contract::Gate::Changed,before.world,0,0});
 if(static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0))!=serial||!contract::Frame(w,o,self,caller,At(0x69471F),GetCurrentThreadId(),serial)||expectedEpoch!=w.epoch+1)return reject({contract::Gate::Frame,o.collection,0,0});
 retired=true;last.retired=true;return last;
}
}
