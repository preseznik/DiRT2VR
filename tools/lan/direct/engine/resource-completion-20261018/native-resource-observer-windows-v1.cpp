// Compile-only callsite observer. No patch installation or automatic invocation.
#include <Windows.h>
#include "native-resource-completion-v1.h"
using resource_completion::U32;
static resource_completion::Ledger ledger;
static U32 originalRetire{};
static U32 expectedWait{};
extern "C" void ResourceRetireCallsiteObserver();
#if defined(RESOURCE_OBSERVER_ABI_FIXTURE)
static void(__cdecl* fixtureObserve)(const resource_completion::SavedFrame*);
extern "C" void SetObserverAbiFixture(U32 original,void(__cdecl* observe)(const resource_completion::SavedFrame*)){
 originalRetire=original;fixtureObserve=observe;
}
#endif
static bool Read(U32 p,U32& value){
 if(!resource_completion::Ptr(p))return false;
 __try{value=*reinterpret_cast<const U32*>(p);return true;}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
static bool Equal(U32 p,const void* expected,std::size_t length){
 if(length>0x4000||!resource_completion::Ptr(p,static_cast<U32>(length)))return false;
 __try{return std::memcmp(reinterpret_cast<const void*>(p),expected,length)==0;}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
extern "C" bool ConfigureResourceObserverUnderNativeLock(U32 imageBase,U32 manager){
 if(originalRetire||!resource_completion::Signatures(Equal,imageBase))return false;
 const auto kernel=GetModuleHandleW(L"kernel32.dll");
 expectedWait=reinterpret_cast<U32>(GetProcAddress(kernel,"WaitForSingleObject"));
 U32 imported{};
 if(!expectedWait||!Read(imageBase+0x128C2C4-0x400000,imported)||imported!=expectedWait)return false;
 if(!resource_completion::Arm(ledger,Read,imageBase,manager))return false;
 originalRetire=imageBase+0xC23420-0x400000;
 return true;
}
extern "C" const resource_completion::Ledger* ResourceObserverLedgerUnderNativeLock(){return &ledger;}
static void __cdecl ObserveSavedFrame(const resource_completion::SavedFrame* frame){
#if defined(RESOURCE_OBSERVER_ABI_FIXTURE)
 fixtureObserve(frame);
#else
 __try{
  U32 imported{};
  if(!Read(ledger.base+0x128C2C4-0x400000,imported)||imported!=expectedWait){ledger.Fail(resource_completion::Fault::Code);return;}
  resource_completion::Observe(ledger,Read,Equal,*frame,
   reinterpret_cast<U32>(&ResourceRetireCallsiteObserver),GetCurrentThreadId());
 }__except(EXCEPTION_EXECUTE_HANDLER){ledger.Fail(resource_completion::Fault::Unreadable);}
#endif
}
extern "C" __declspec(naked) void ResourceRetireCallsiteObserver(){
 __asm {
  pushfd
  pushad
  mov ebx,esp
  sub esp,528
  and esp,0FFFFFFF0h
  fxsave [esp]
  push ebx
  call ObserveSavedFrame
  add esp,4
  fxrstor [esp]
  mov esp,ebx
  popad
  popfd
  jmp dword ptr [originalRetire]
 }
}
// Parent owns installation of only the original CALL C28490 after all original
// hash/code/lifetime prerequisites and a quiescent safe patch window. The hook
// always tail-calls the original C23420 once, even after observation rejection.
// Never detach/unload this observer while a native call can still reach it.
