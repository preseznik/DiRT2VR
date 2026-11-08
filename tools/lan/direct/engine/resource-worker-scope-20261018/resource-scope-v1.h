#pragma once
#include <cstdint>
#include "../late-frame-observer-20261018/frame-window.h"
namespace resource_scope_v1 {
using U32=std::uint32_t;
enum class Gate {Ready,Pending,Frame,Unreadable,Owner,Code,Recursive,BusyScheduler,BusyWorker,ActiveJobs,Changed,Exception,ReleaseFault};
struct Fault {Gate gate{Gate::Ready};U32 address{},actual{},expected{};};
struct Entry {U32 image{},app{},self{},caller{},thread{},stackLow{},stackHigh{},serial{};late_frame::Owners frame{};};
struct Owners {U32 app{},game{},render{},collection{},loader{},manager{},scheduler{},worker{},schedulerLock{},workerLock{},notification{},bootstrap{};};
inline bool Ptr(U32 p,U32 size=4){return p>=0x10000&&size&&std::uint64_t(p)+size<=0x80000000ull;}
inline bool Same(const Owners&a,const Owners&b){return a.app==b.app&&a.game==b.game&&a.render==b.render&&a.collection==b.collection&&a.loader==b.loader&&a.manager==b.manager&&a.scheduler==b.scheduler&&a.worker==b.worker&&a.schedulerLock==b.schedulerLock&&a.workerLock==b.workerLock&&a.notification==b.notification&&a.bootstrap==b.bootstrap;}
template<class Read> bool LockOwnership(Read read,U32 wrapper,U32 thread,bool held,Fault&fault){
 const U32 ownerAddress=wrapper+4+offsetof(CRITICAL_SECTION,OwningThread),recursionAddress=wrapper+4+offsetof(CRITICAL_SECTION,RecursionCount);
 U32 owner{},recursion{};
 if(!Ptr(wrapper,4+sizeof(CRITICAL_SECTION))||!read(ownerAddress,owner)){fault={Gate::Unreadable,ownerAddress,0,0};return false;}
 if(!held){if(owner==thread){fault={Gate::Recursive,ownerAddress,owner,0};return false;}return true;}
 if(!read(recursionAddress,recursion)){fault={Gate::Unreadable,recursionAddress,0,0};return false;}
 if(owner!=thread||recursion!=1){fault={Gate::Recursive,owner!=thread?ownerAddress:recursionAddress,owner!=thread?owner:recursion,owner!=thread?thread:1};return false;}
 return true;
}
template<class Read> struct Inspector {
 Read read;const Entry&e;const late_frame::Window&w;Fault fault{};
 U32 At(U32 va)const{return e.image+va-0x400000;}
 bool Fail(Gate g,U32 p,U32 a,U32 x){if(fault.gate==Gate::Ready)fault={g,p,a,x};return false;}
 U32 Word(U32 p){U32 v{};if(!Ptr(p)||!read(p,v))Fail(Gate::Unreadable,p,0,0);return v;}
 bool Eq(U32 p,U32 x){const U32 a=Word(p);return a==x&&fault.gate==Gate::Ready?true:Fail(Gate::Owner,p,a,x);}
 bool Capture(Owners&s){
  s={};s.app=e.app;s.game=e.frame.game;s.render=e.frame.render;s.collection=e.frame.collection;
  if(e.caller!=At(0x69471F)||e.self!=s.collection||!e.thread||e.thread!=e.frame.thread||
     !w.bound||!w.primed||w.inside||w.failed||!w.update||!w.render||!w.phase2||!w.epoch||w.epoch==0xffffffffu||
     !late_frame::Same(w.owners,e.frame)||e.serial<=w.lastPhysics)
   return Fail(Gate::Frame,e.caller,e.serial,w.lastPhysics+1);
  if(!Ptr(e.app,0x1F8)||e.stackLow>=e.stackHigh||e.app<e.stackLow||std::uint64_t(e.app)+0x1F8>e.stackHigh)
   return Fail(Gate::Frame,e.app,e.stackLow,e.stackHigh);
  s.loader=Word(At(0x1482D8C));s.manager=Word(At(0x15D9A04));
  if(!Ptr(s.loader,0x1bc8)||!Ptr(s.manager,0x7c))return Fail(Gate::Owner,s.manager,0,0);
  s.scheduler=Word(s.manager+0x3c);s.worker=s.scheduler+0xa0;s.schedulerLock=s.scheduler+0x2108;s.workerLock=s.worker+0x2010;
  if(!Ptr(s.scheduler,0x2148))return Fail(Gate::Owner,s.scheduler,0,0);
  s.notification=Word(s.manager+0x34);s.bootstrap=Word(e.app+0x114);
  if(!Ptr(s.bootstrap,0x918)||s.notification!=s.bootstrap+0x21c)return Fail(Gate::Owner,s.manager+0x34,s.notification,s.bootstrap+0x21c);
  if(!Eq(s.game,At(0x1329A48))||!Eq(s.render,At(0x1324164))||!Eq(s.collection,At(0x135A6A8))||
     !Eq(s.loader,At(0x1325688))||!Eq(s.loader+4,At(0x132567C))||!Eq(s.loader+12,s.game)||!Eq(s.loader+16,s.render)||!Eq(s.loader+20,s.collection)||
     !Eq(e.app+0xA8,s.game)||!Eq(e.app+0xAC,s.render)||!Eq(e.app+0xB8,s.collection)||
     !Eq(s.manager,At(0x135B85C))||!Eq(s.scheduler,At(0x1359FD8))||!Eq(At(0x15D9A14),s.worker)||
     !Eq(s.bootstrap,At(0x1321624))||!Eq(s.bootstrap+0x910,s.scheduler)||!Eq(s.notification,At(0x1356E88))||
     !Eq(s.schedulerLock,s.schedulerLock+4)||!Eq(s.workerLock,s.workerLock+4))return false;
  const U32 stop=Word(s.worker)&255u;
  return stop==0&&fault.gate==Gate::Ready?true:Fail(Gate::Owner,s.worker,stop,0);
 }
};
// Historical copies only. No held lock, callback, or job pointer escapes.
struct Receipt {
 Gate gate{Gate::Pending};Fault fault{};U32 epoch{},serial{},thread{},activeJobs{0xffffffffu},finalJobs{0xffffffffu},exception{};
 bool schedulerAcquired{},workerAcquired{},schedulerReleased{},workerReleased{},noopObserved{},returned{};
 bool allWorkersRetired{},clothPermitted{},constructionPermitted{}; // Always false in this version.
};
// The production backend below invokes the exact native wrappers. Tests use
// real Windows critical sections with the same ordering and exception cleanup.
// There is deliberately no caller-supplied body and no lease-returning API.
template<class Locks> void ProbeUnderLocks(Locks&locks,Receipt&r){
 __try {
  if(!locks.TryScheduler(r)){return;}
  r.schedulerAcquired=true;
  if(!locks.TryWorker(r)){return;}
  r.workerAcquired=true;
  if(!locks.Validate(r)){return;}
  r.activeJobs=locks.Count();
  if(r.activeJobs>2048){r.gate=Gate::Unreadable;return;}
  if(r.activeJobs!=0){r.gate=Gate::ActiveJobs;return;}
  // Fixed read-only no-op. No native allocation/dispatch/wait or callback.
  r.finalJobs=locks.Count();
  if(r.finalJobs!=0||!locks.Validate(r)){r.gate=Gate::Changed;return;}
  r.noopObserved=true;r.gate=Gate::Ready;
 } __finally {
  __try {locks.ReleaseWorker(r);}
  __finally {locks.ReleaseScheduler(r);}
 }
}
template<class Locks> void ProbeSafely(Locks&locks,Receipt&r){
 __try {ProbeUnderLocks(locks,r);r.returned=true;}
 __except(EXCEPTION_EXECUTE_HANDLER){r.exception=GetExceptionCode();r.gate=Gate::Exception;r.noopObserved=false;}
 if((r.workerAcquired&&!r.workerReleased)||(r.schedulerAcquired&&!r.schedulerReleased)){r.gate=Gate::ReleaseFault;r.noopObserved=false;}
}
}
