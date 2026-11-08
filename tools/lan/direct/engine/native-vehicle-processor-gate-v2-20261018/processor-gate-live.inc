// Compile-only. Include after the real parent and query-retirement-live.inc.
// All storage and the trampoline remain pinned until owned-process exit.
#include "processor-gate.h"
#include "processor-invoke.h"
#include "processor-code-guards.h"
namespace vehicle_processor_live {
namespace contract=vehicle_processor;
contract::Counter counter;
contract::Process originalProcessor{};
contract::Owners pinned{};
contract::Fault fault{};
std::atomic<U32> enabled{0},entries{0},deferred{0},unknownCallers{0};
bool installAttempted{},failed{},scopeInside{};
U32 installationThread{},appThread{},armEpoch{},armSerial{},consumedEpoch{};
struct Result {bool armed{},closed{},returned{},failed{};contract::Gate gate{};U32 epoch{},serial{},activeJobs{},count{};bool all_workers_retired{},construction_permitted{};};
Result last{};
bool Word(U32 address,U32&value){if(!contract::Ptr(address))return false;__try{value=*reinterpret_cast<const U32*>(address);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool Jump(U32 address,U32 destination){U32 first{},relative{};return Word(address,first)&&(first&255u)==0xe9&&Word(address+1,relative)&&address+5+relative==destination;}
U32 __fastcall Processor(void*self,void*,void*envelope){
 const DWORD incomingError=GetLastError();
 // An unrelated instance retains original behavior. It does not participate
 // in, or receive permission from, this exact-owned-processor gate.
 if(reinterpret_cast<U32>(self)!=pinned.processor){SetLastError(incomingError);return originalProcessor(self,envelope);}
 contract::Inspector<decltype(&Word)> inspector{Word,base};
 const bool known=inspector.Callback(pinned,reinterpret_cast<U32>(_ReturnAddress()),reinterpret_cast<U32>(envelope));
 if(!known){unknownCallers.fetch_add(1,std::memory_order_relaxed);counter.Fault();}
 const auto entry=counter.Enter();
 if(entry==contract::Entry::Deferred){deferred.fetch_add(1,std::memory_order_relaxed);SetLastError(incomingError);return contract::Pending;}
 entries.fetch_add(1,std::memory_order_relaxed);
 return contract::InvokeCounted(counter,originalProcessor,self,envelope,incomingError);
}
bool HookIntact(){
 const U32 target=At(0x6DCEE0),trampoline=reinterpret_cast<U32>(originalProcessor);const auto&b=processor_code::Blocks[0];
 // Three-byte SUB plus five-byte relocated absolute MOV: exactly eight bytes.
 return enabled.load(std::memory_order_acquire)&&contract::Ptr(trampoline,13)&&Jump(target,reinterpret_cast<U32>(&Processor))&&
  processor_code::Range(Word,b,base,target+5,5,b.size-5)&&processor_code::Range(Word,b,base,trampoline,0,8)&&Jump(trampoline+8,target+8);
}
bool Imports(){const HMODULE kernel=GetModuleHandleW(L"kernel32.dll");return kernel&&Read(At(0x128C15C))==reinterpret_cast<U32>(GetProcAddress(kernel,"TryEnterCriticalSection"))&&Read(At(0x128C320))==reinterpret_cast<U32>(GetProcAddress(kernel,"LeaveCriticalSection"))&&Read(At(0x128C324))==reinterpret_cast<U32>(GetProcAddress(kernel,"EnterCriticalSection"));}
bool OwnersUnchanged(){contract::Inspector<decltype(&Word)> inspector{Word,base};contract::Owners now{};if(!inspector.Capture(pinned.game,pinned.render,pinned.collection,now)){fault=inspector.fault;return false;}if(!contract::Same(pinned,now)){fault={contract::Gate::Owner,pinned.loader,now.loader,pinned.loader};return false;}return true;}
bool StartupReady(){return !activation_live_v1::failed&&activation_live_v1::attempt.stage==native_activation::Stage::Complete&&initial_load_v2::ledger.phase==initial_load_v2::Phase::FinalizationBlocked&&finalization_v2::state.phase==finalization_v2::Phase::RegisteredBlocked&&loaded_ack_adapter_v2::state.phase==loaded_ack_adapter_v2::Phase::ProgressObserved;}
bool Install(){
 if(!processor_code::Verify(Word,base)||!Imports())return false;
 contract::Inspector<decltype(&Word)> inspector{Word,base};const U32 loader=Read(At(0x1482D8C));
 if(!inspector.Capture(loadOwner,Read(loader+16),Read(loader+20),pinned)){fault=inspector.fault;return false;}
 installationThread=GetCurrentThreadId();
 if(MH_CreateHook(reinterpret_cast<void*>(At(0x6DCEE0)),reinterpret_cast<void*>(&Processor),reinterpret_cast<void**>(&originalProcessor))!=MH_OK)return false;
 if(MH_EnableHook(reinterpret_cast<void*>(At(0x6DCEE0)))!=MH_OK){MH_RemoveHook(reinterpret_cast<void*>(At(0x6DCEE0)));originalProcessor=nullptr;return false;}
 enabled.store(1,std::memory_order_release);
 return HookIntact(); // A failed post-enable check remains open and pinned.
}
void AfterNativeStartup(){
 if(installAttempted||!StartupReady()||GetCurrentThreadId()!=static_cast<DWORD>(holdThread))return;
 installAttempted=true;
 if(!Install()){failed=true;counter.Fault();Log("VEHICLE_GATE install rejected; unarmed; construction_permitted=0\r\n");return;}
 Log("VEHICLE_GATE callback accounting enabled; unarmed; construction_permitted=0\r\n");
}
struct NativeLocks {
 bool schedulerHeld{},workerHeld{};U32 activeJobs{};
 bool Try(U32 object){return reinterpret_cast<U32(__thiscall*)(void*)>(At(0x11588E0))(reinterpret_cast<void*>(object))==1;}
 void Release(U32 object){reinterpret_cast<void(__thiscall*)(void*)>(At(0x11588D0))(reinterpret_cast<void*>(object));}
 bool TryScheduler(){schedulerHeld=Try(pinned.schedulerLock);return schedulerHeld;}
 bool TryWorker(){workerHeld=Try(pinned.workerLock);return workerHeld;}
 void ReleaseScheduler(){if(schedulerHeld){schedulerHeld=false;Release(pinned.schedulerLock);}}
 void ReleaseWorker(){if(workerHeld){workerHeld=false;Release(pinned.workerLock);}}
 bool Validate(){return enabled.load(std::memory_order_acquire)&&HookIntact()&&OwnersUnchanged()&&!counter.Faulted();}
 U32 NativeCount(){if(!Word(pinned.worker+4,activeJobs)){activeJobs=0xffffffffu;counter.Fault();}return activeJobs;}
};
contract::Gate NativeRetirement(NativeLocks&locks){
 contract::Gate result=contract::Gate::Exception;
 __try {result=contract::ObserveRetirement(counter,locks);}
 __finally {locks.ReleaseWorker();locks.ReleaseScheduler();}
 return result;
}
contract::Gate ObserveRetirementSafely(U32&activeJobs){
 NativeLocks locks{};contract::Gate result=contract::Gate::Exception;
 __try {result=NativeRetirement(locks);activeJobs=locks.activeJobs;}
 __except(EXCEPTION_EXECUTE_HANDLER){fault={contract::Gate::Exception,pinned.schedulerLock,GetExceptionCode(),0};counter.Fault();}
 return result;
}
bool Frame(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 serial){
 // Installation runs on the held game worker. Bind the distinct App thread
 // only from the actual observer window's original-wait/caller receipts.
 const U32 current=GetCurrentThreadId();
 if(&w!=&frame_observer::window||!InterlockedCompareExchange(&frame_observer::enabled,0,0)||!query_retirement::Frame(w,o,self,caller,At(0x69471F),current,serial)||o.game!=pinned.game||o.render!=pinned.render||o.collection!=pinned.collection)return false;
 if(appThread&&appThread!=current)return false;
 appThread=current;return true;
}
// Call at the actual app callback before invoking its original body. Arming
// itself neither waits for physics nor closes the callback gate.
Result TryArmAtAppBoundary(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource){
 Result r{};r.armed=counter.Armed();
 if(&serialSource!=&frame_observer::physicsSerial)return r;
 if(!enabled.load(std::memory_order_acquire)||failed||counter.Faulted()){r.failed=failed||counter.Faulted();return r;}
 const U32 serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));
 if(!Frame(w,o,self,caller,serial)||!StartupReady())return r;
 r.epoch=w.epoch+1;r.serial=serial;
 if(counter.Armed()){r.gate=contract::Gate::Ready;return r;}
 if(!HookIntact()||!processor_code::Verify(Word,base,true)||!Imports()||!OwnersUnchanged()){failed=true;counter.Fault();r.failed=true;r.gate=contract::Gate::Code;return last=r;}
 r.gate=ObserveRetirementSafely(r.activeJobs);r.count=counter.Word();r.armed=counter.Armed();
 if(r.gate==contract::Gate::Ready){armEpoch=r.epoch;armSerial=serial;}
 else if(r.gate!=contract::Gate::Busy&&r.gate!=contract::Gate::ActiveJobs&&r.gate!=contract::Gate::Counted){failed=true;counter.Fault();r.failed=true;}
 return last=r;
}
using ScopeBody=contract::ScopeBody;
// This API cannot return a held lease. Body must return synchronously without
// pumping messages, yielding, shutdown, job submission, or a native wait.
// It excludes only exact-owned 6DCEE0; it grants no model/publication permission.
Result TryClosedScope(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,volatile LONG&serialSource,const query_retirement_live::Result&query,ScopeBody body,void*context){
 Result r{};r.armed=counter.Armed();
 if(&serialSource!=&frame_observer::physicsSerial)return r;
 const U32 serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));
 if(!body||scopeInside||failed||counter.Faulted()||!counter.Armed()||!StartupReady()||!Frame(w,o,self,caller,serial))return r;
 r.epoch=w.epoch+1;r.serial=serial;
 if(r.epoch==consumedEpoch||!query.called||!query.retired||query.failed||query.epoch!=r.epoch||query.serial!=serial||query.waitCalls!=1||query.waitReturns!=1||query.waitResult!=0)return r;
 if(!HookIntact()||!OwnersUnchanged()){failed=true;counter.Fault();r.failed=true;r.gate=contract::Gate::Owner;return last=r;}
 const U32 world=Read(Read(pinned.game+0x2148)+4);
 if(!contract::Ptr(world,0x7c8)||Read(world+0x7c4)!=0)return r;
 if(!counter.TryClose()){r.gate=contract::Gate::Counted;r.count=counter.Word();return last=r;}
 scopeInside=true;consumedEpoch=r.epoch;r.closed=true;
 contract::InvokeClosed(counter,body,context,scopeInside,r.returned,r.failed);
 if(counter.Faulted()||!OwnersUnchanged()||static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0))!=serial){counter.Fault();failed=true;r.failed=true;}
 return last=r;
}
}
