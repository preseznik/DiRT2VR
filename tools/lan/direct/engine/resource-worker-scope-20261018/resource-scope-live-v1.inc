// No hook installation here. Called synchronously from the actual App
// collection shim after its original returns, before Window::Enter.
#include "resource-scope-v1.h"
#include "resource-code-guards-v1.h"
namespace resource_scope_live_v1 {
namespace contract=resource_scope_v1;
contract::Receipt last{};contract::Owners pinned{};bool completed{},failed{},inside{};U32 attemptedEpoch{},appThread{};
bool Word(U32 address,U32&value){if(!contract::Ptr(address))return false;__try{value=*reinterpret_cast<const U32*>(address);return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
bool Imports(){const HMODULE kernel=GetModuleHandleW(L"kernel32.dll");return kernel&&Read(At(0x128C15C))==reinterpret_cast<U32>(GetProcAddress(kernel,"TryEnterCriticalSection"))&&Read(At(0x128C320))==reinterpret_cast<U32>(GetProcAddress(kernel,"LeaveCriticalSection"))&&Read(At(0x128C324))==reinterpret_cast<U32>(GetProcAddress(kernel,"EnterCriticalSection"));}
bool StartupReady(){return !activation_live_v1::failed&&activation_live_v1::attempt.stage==native_activation::Stage::Complete&&initial_load_v2::ledger.phase==initial_load_v2::Phase::FinalizationBlocked&&finalization_v2::state.phase==finalization_v2::Phase::RegisteredBlocked&&loaded_ack_adapter_v2::state.phase==loaded_ack_adapter_v2::Phase::ProgressObserved;}
struct NativeLocks {
 const contract::Entry&e;const late_frame::Window&w;volatile LONG&serialSource;contract::Owners owners;bool schedulerHeld{},workerHeld{};
 bool Try(U32 wrapper,bool&held,contract::Gate busy,contract::Receipt&r){
  if(!contract::LockOwnership(Word,wrapper,e.thread,false,r.fault)){r.gate=r.fault.gate;return false;}
  held=reinterpret_cast<U32(__thiscall*)(void*)>(At(0x11588E0))(reinterpret_cast<void*>(wrapper))==1;
  if(!held)r.gate=busy;
  return held;
 }
 bool TryScheduler(contract::Receipt&r){return Try(owners.schedulerLock,schedulerHeld,contract::Gate::BusyScheduler,r);}
 bool TryWorker(contract::Receipt&r){return Try(owners.workerLock,workerHeld,contract::Gate::BusyWorker,r);}
 void ReleaseWorker(contract::Receipt&r){if(workerHeld){reinterpret_cast<void(__thiscall*)(void*)>(At(0x11588D0))(reinterpret_cast<void*>(owners.workerLock));workerHeld=false;r.workerReleased=true;}}
 void ReleaseScheduler(contract::Receipt&r){if(schedulerHeld){reinterpret_cast<void(__thiscall*)(void*)>(At(0x11588D0))(reinterpret_cast<void*>(owners.schedulerLock));schedulerHeld=false;r.schedulerReleased=true;}}
 U32 Count(){U32 count{};return Word(owners.worker+4,count)?count:0xffffffffu;}
 bool Held(U32 wrapper,contract::Receipt&r){
  if(contract::LockOwnership(Word,wrapper,e.thread,true,r.fault))return true;
  r.gate=r.fault.gate;return false;
 }
 bool Validate(contract::Receipt&r){
  contract::Inspector<decltype(&Word)> inspector{Word,e,w};contract::Owners now{};
  if(!inspector.Capture(now)){r.gate=inspector.fault.gate;r.fault=inspector.fault;return false;}
  if(!contract::Same(owners,now)||GetCurrentThreadId()!=e.thread||static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0))!=e.serial){r.gate=contract::Gate::Changed;return false;}
  return schedulerHeld&&workerHeld&&Held(owners.schedulerLock,r)&&Held(owners.workerLock,r);
 }
};
// Exact App pointer must come from ESI in the native 69471F entry thunk.
// Neither a permission Boolean nor a caller-provided callback is accepted.
contract::Receipt ObserveImpl(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 actualApp,volatile LONG&serialSource){
 contract::Receipt r{};
 if(completed||failed)return last;
 if(inside){failed=true;r.gate=contract::Gate::Recursive;return last=r;}
 if(&w!=&frame_observer::window||&serialSource!=&frame_observer::physicsSerial||!InterlockedCompareExchange(&frame_observer::enabled,0,0)||!StartupReady())return r;
 const U32 thread=GetCurrentThreadId(),serial=static_cast<U32>(InterlockedCompareExchange(&serialSource,0,0));
 r.epoch=w.epoch&&w.epoch!=0xffffffffu?w.epoch+1:0;r.serial=serial;r.thread=thread;
 const auto*tib=reinterpret_cast<const NT_TIB*>(NtCurrentTeb());
 contract::Entry e{base,actualApp,self,caller,thread,reinterpret_cast<U32>(tib->StackLimit),reinterpret_cast<U32>(tib->StackBase),serial,o};
 contract::Inspector<decltype(&Word)> inspector{Word,e,w};contract::Owners owners{};
 if(!inspector.Capture(owners)){r.gate=inspector.fault.gate;r.fault=inspector.fault;return r;}
 if(attemptedEpoch==r.epoch)return last;
 if((appThread&&appThread!=thread)||o.game!=loadOwner){r.gate=contract::Gate::Frame;failed=true;return last=r;}
 if(!resource_code_v1::Verify(Word,base)||!Imports()){r.gate=contract::Gate::Code;failed=true;return last=r;}
 attemptedEpoch=r.epoch;appThread=thread;pinned=owners;inside=true;
 NativeLocks locks{e,w,serialSource,owners};
 contract::ProbeSafely(locks,r);
 inside=false;
 completed=r.gate==contract::Gate::Ready&&r.noopObserved&&r.returned&&r.schedulerReleased&&r.workerReleased;
 failed=!completed&&r.gate!=contract::Gate::BusyScheduler&&r.gate!=contract::Gate::BusyWorker&&r.gate!=contract::Gate::ActiveJobs;
 return last=r;
}
contract::Receipt ObserveAtAppBoundary(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 actualApp,volatile LONG&serialSource){
 const DWORD savedError=GetLastError();const auto result=ObserveImpl(w,o,self,caller,actualApp,serialSource);SetLastError(savedError);return result;
}
}
