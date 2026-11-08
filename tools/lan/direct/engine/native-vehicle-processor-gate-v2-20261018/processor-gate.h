#pragma once
#include <atomic>
#include <cstdint>
namespace vehicle_processor {
using U32=std::uint32_t;
constexpr U32 Closed=0x80000000u, CountMask=Closed-1, Pending=0x24;
enum class Entry {Counted, Deferred};
// One atomic word arbitrates callback entry and the short main-thread lease.
// Arming is a separate, one-time retirement receipt. It is never a future lease.
class Counter {
 std::atomic<U32> word_{0}, armed_{0}, fault_{0};
public:
 static_assert(std::atomic<U32>::is_always_lock_free);
 U32 Word()const{return word_.load(std::memory_order_acquire);}
 bool Armed()const{return armed_.load(std::memory_order_acquire)!=0;}
 bool Faulted()const{return fault_.load(std::memory_order_acquire)!=0;}
 void Fault(){fault_.store(1,std::memory_order_release);}
 Entry Enter(){
  U32 seen=word_.load(std::memory_order_acquire);
  for(;;){
   if(seen&Closed)return Entry::Deferred;
   if(seen==CountMask){Fault();return Entry::Deferred;}
   if(word_.compare_exchange_weak(seen,seen+1,std::memory_order_acq_rel,std::memory_order_acquire))return Entry::Counted;
  }
 }
 bool Leave(){
  U32 seen=word_.load(std::memory_order_acquire);
  for(;;){
   if(!seen||(seen&Closed)){Fault();return false;}
   if(word_.compare_exchange_weak(seen,seen-1,std::memory_order_acq_rel,std::memory_order_acquire))return true;
  }
 }
 // Adapter-private use: called only while both native locks are owned, after
 // the installed hook and an empty native worker list were freshly verified.
 bool ArmAfterRetirement(){if(Faulted()||Word()!=0)return false;armed_.store(1,std::memory_order_release);return true;}
 bool TryClose(){
  if(!Armed()||Faulted())return false;
  U32 expected=0;
  if(!word_.compare_exchange_strong(expected,Closed,std::memory_order_acq_rel,std::memory_order_acquire))return false;
  if(Faulted()){Release();return false;}
  return true;
 }
 bool Release(){U32 expected=Closed;if(word_.compare_exchange_strong(expected,0,std::memory_order_acq_rel,std::memory_order_acquire))return true;Fault();return false;}
};
enum class Gate {Ready,Unreadable,Owner,Lock,Code,Hook,Frame,Busy,ActiveJobs,Counted,Exception,Faulted};
struct Fault {Gate gate{Gate::Ready};U32 address{},actual{},expected{};};
inline bool Ptr(U32 p,U32 n=4){return p>=0x10000&&n&&std::uint64_t(p)+n<=0x80000000ull;}
struct Owners {U32 game{},render{},collection{},loader{},processor{},manager{},scheduler{},worker{},schedulerLock{},workerLock{},notification{},notificationOwner{};};
inline bool Same(const Owners&a,const Owners&b){return a.game==b.game&&a.render==b.render&&a.collection==b.collection&&a.loader==b.loader&&a.processor==b.processor&&a.manager==b.manager&&a.scheduler==b.scheduler&&a.worker==b.worker&&a.schedulerLock==b.schedulerLock&&a.workerLock==b.workerLock&&a.notification==b.notification&&a.notificationOwner==b.notificationOwner;}
template<class Read> struct Inspector {
 Read read;U32 imageBase;Fault fault{};
 U32 At(U32 va)const{return imageBase+va-0x400000;}
 bool Fail(Gate g,U32 p,U32 actual,U32 expected){if(fault.gate==Gate::Ready)fault={g,p,actual,expected};return false;}
 U32 Word(U32 p){U32 v{};if(!Ptr(p)||!read(p,v))Fail(Gate::Unreadable,p,0,0);return v;}
 bool Eq(U32 p,U32 expected,Gate g=Gate::Owner){const U32 v=Word(p);return v==expected&&fault.gate==Gate::Ready?true:Fail(g,p,v,expected);}
 bool Capture(U32 game,U32 render,U32 collection,Owners&s){
  s={};s.game=game;s.render=render;s.collection=collection;s.loader=Word(At(0x1482D8C));s.processor=s.loader+4;
  s.manager=Word(At(0x15D9A04));s.scheduler=Word(s.manager+0x3c);s.worker=s.scheduler+0xa0;s.schedulerLock=s.scheduler+0x2108;s.workerLock=s.worker+0x2010;
  if(!Ptr(s.loader,0x1bc8)||!Ptr(s.manager,0x7c)||!Ptr(s.scheduler,0x2148))return Fail(Gate::Owner,s.loader,0,0);
  if(!Eq(game,At(0x1329A48))||!Eq(render,At(0x1324164))||!Eq(collection,At(0x135A6A8))||!Eq(s.loader,At(0x1325688))||!Eq(s.processor,At(0x132567C))||!Eq(s.loader+12,game)||!Eq(s.loader+16,render)||!Eq(s.loader+20,collection)||!Eq(s.manager,At(0x135B85C))||!Eq(s.scheduler,At(0x1359FD8))||!Eq(At(0x15D9A14),s.worker))return false;
  // Constructor initializes each wrapper to its embedded CRITICAL_SECTION.
  if(!Eq(s.schedulerLock,s.schedulerLock+4,Gate::Lock)||!Eq(s.workerLock,s.workerLock+4,Gate::Lock))return false;
  // Native 69F3B0 constructs owner+21C; 6A16F0 installs that exact
  // embedded receiver through BE3730 and the same owner's +910 scheduler.
  s.notification=Word(s.manager+0x34);
  if(!Ptr(s.notification)||s.notification<0x21c)return Fail(Gate::Owner,s.manager+0x34,s.notification,0);
  s.notificationOwner=s.notification-0x21c;
  if(!Ptr(s.notificationOwner,0x918)||!Eq(s.notificationOwner,At(0x1321624))||!Eq(s.notificationOwner+0x910,s.scheduler)||!Eq(s.notification,At(0x1356E88)))return false;
  if(Word(s.worker)&255u)return Fail(Gate::Owner,s.worker,Word(s.worker)&255u,0);
  return fault.gate==Gate::Ready;
 }
 bool Callback(const Owners&s,U32 caller,U32 envelope){
  const U32 va=caller-imageBase+0x400000;U32 offset{},table{},processorOffset{},schedulerOffset{},notificationOffset{},stateOffset{};
  if(va==0xBFD941||va==0xBFD951){offset=va==0xBFD941?0x18c:0x1b0;table=0x1359ff0;processorOffset=0x188;schedulerOffset=0x24;notificationOffset=0x3c;stateOffset=0x10;}
  else if(va==0xC29067||va==0xC29163||va==0xC29357){offset=va==0xC29163?0x1a0:0x1b4;table=0x135a5d8;processorOffset=0x19c;schedulerOffset=0x28;notificationOffset=0x40;stateOffset=0x14;}
  else return Fail(Gate::Owner,caller,va,0);
  if(!Ptr(envelope)||envelope<offset)return Fail(Gate::Owner,envelope,0,0);
  const U32 job=envelope-offset;
  return Eq(job,At(table))&&Eq(job+processorOffset,s.processor)&&Eq(job+schedulerOffset,s.scheduler)&&Eq(job+notificationOffset,s.notification)&&Eq(job+stateOffset,2)&&Eq(s.manager+0x34,s.notification)&&Eq(s.notificationOwner,At(0x1321624))&&Eq(s.notificationOwner+0x910,s.scheduler)&&Eq(s.notification,At(0x1356E88));
 }
};
inline bool KnownReturn(U32 caller,U32 imageBase){const U32 va=caller-imageBase+0x400000;return va==0xBFD941||va==0xBFD951||va==0xC29067||va==0xC29163||va==0xC29357;}
// The fixture and native adapter use the same finite lock-order algorithm.
// Lock operations are try-only; NativeCount is read with BOTH locks held.
template<class Locks> Gate ObserveRetirement(Counter&counter,Locks&locks){
 if(!locks.TryScheduler())return Gate::Busy;
 if(!locks.TryWorker()){locks.ReleaseScheduler();return Gate::Busy;}
 Gate result=Gate::Ready;
 if(!locks.Validate())result=Gate::Owner;
 else if(locks.NativeCount()!=0)result=Gate::ActiveJobs;
 else if(!counter.ArmAfterRetirement())result=Gate::Counted;
 locks.ReleaseWorker();locks.ReleaseScheduler();return result;
}
}
