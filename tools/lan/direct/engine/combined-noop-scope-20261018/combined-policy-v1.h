#pragma once
#include "../resource-worker-scope-20261018/resource-scope-v1.h"
namespace combined_noop_v1 {
using U32=std::uint32_t;
enum class Gate {Pending,Observed,Frame,Code,Query,Cloth,Physics,Loader,Resource,Counter,Changed,Exception,Cleanup};
struct Fault {Gate gate{Gate::Pending};U32 address{},actual{},expected{};};
struct Receipt {Gate gate{Gate::Pending};Fault fault{};U32 epoch{},serial{},thread{},nonce{},exception{},counterDuring{},counterAfter{};
 U32 queryCalls{},queryReturns{},queryResult{0xffffffffu},queryBefore{0xffffffffu},queryAfter{0xffffffffu},queryRecheck{0xffffffffu},queryChecks{};
 bool attempted{},queryCalled{},gateClosed{},gateReleased{},noopObserved{},returned{},allWorkersRetired{},constructionPermitted{};
 resource_scope_v1::Receipt resource{};};
// Separate one-shot diagnostics for a terminal preflight rejection. Reporting
// must neither spend nor fabricate the native query attempt or a scope proof.
inline bool TakePreflightFailure(bool finished,bool&reported,const Receipt&latest,Receipt&out){
 if(!finished||reported||latest.attempted||latest.queryCalled||latest.gate==Gate::Pending||latest.gate==Gate::Observed)return false;
 reported=true;out=latest;return true;
}
struct Loader {U32 pointer{},phase{},first{},last{},issued{},active{},flags{},processorActive{};};
inline bool LoaderIdle(const Loader&l,U32 serial){
 // Qualification uses the original first initial load, no signed wrap.
 return l.pointer&&serial==0&&l.phase==0&&l.first==0&&l.last==0&&l.issued==1&&l.active==0&&
  !(l.flags&0x00ff00ffu)&&!l.processorActive;
}
inline bool Same(const Loader&a,const Loader&b){return a.pointer==b.pointer&&a.phase==b.phase&&a.first==b.first&&a.last==b.last&&a.issued==b.issued&&a.active==b.active&&a.flags==b.flags&&a.processorActive==b.processorActive;}
// Single bounded transaction. Gate is acquired first, so S/W release before
// reopening it on every path. Resource's fixed body has no callback argument.
template<class Backend> void HeldNoop(Backend&b,Receipt&r){
 __try {
  if(!b.CloseGate(r))return;
  r.gateClosed=true;
  resource_scope_v1::ProbeSafely(b,r.resource);
  if(r.resource.noopObserved&&r.resource.returned&&r.resource.gate==resource_scope_v1::Gate::Ready){r.noopObserved=true;r.gate=Gate::Observed;}
  else if(r.gate==Gate::Pending)r.gate=Gate::Resource;
 } __finally {b.OpenGate(r);}
}
template<class Backend> void HeldNoopSafely(Backend&b,Receipt&r){
 __try{HeldNoop(b,r);r.returned=true;}
 __except(EXCEPTION_EXECUTE_HANDLER){r.exception=GetExceptionCode();r.gate=Gate::Exception;r.noopObserved=false;}
 if(r.gateClosed&&!r.gateReleased){r.gate=Gate::Cleanup;r.noopObserved=false;}
}
}
