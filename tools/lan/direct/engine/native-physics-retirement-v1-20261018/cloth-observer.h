#pragma once
#include <cstdint>
#include <cstring>
namespace cloth_observation {
using U32=std::uint32_t;
enum class Gate {Observed,Pending,Frame,Owner,Unreadable,Changed,Code,Hook,Nested,Receipt,Exception};
struct Fault {Gate gate{Gate::Observed};U32 address{},actual{},expected{};};
inline bool Ptr(U32 p,U32 size=4){return p>=0x10000&&size&&std::uint64_t(p)+size<=0x80000000ull;}
struct Owners {
 U32 game{},render{},collection{},app{},updatable{},cloth{},facade{},master{},masterTask{},coordinator{},renderer{},syncTask{},event{},handle{},jobs{},count{},array{};
 U32 events[64]{},handles[64]{},kindCount{},kinds[10]{};
};
inline bool Same(const Owners&a,const Owners&b){return std::memcmp(&a,&b,sizeof(a))==0;}
template<class Read> struct Inspector {
 Read read;U32 imageBase;Fault fault{};
 U32 At(U32 va)const{return imageBase+va-0x400000;}
 bool Fail(Gate g,U32 p,U32 actual,U32 expected){if(fault.gate==Gate::Observed)fault={g,p,actual,expected};return false;}
 U32 Word(U32 p){U32 v{};if(!Ptr(p)||!read(p,v))Fail(Gate::Unreadable,p,0,0);return v;}
 U32 Byte(U32 p){return(Word(p&~3u)>>((p&3u)*8))&255u;}
 bool Eq(U32 p,U32 value){const U32 v=Word(p);return v==value&&fault.gate==Gate::Observed?true:Fail(Gate::Owner,p,v,value);}
 bool Capture(U32 game,U32 render,U32 collection,Owners&s){
  s={};s.game=game;s.render=render;s.collection=collection;s.coordinator=At(0x1491980);
  if(!Eq(game,At(0x1329A48))||!Eq(render,At(0x1324164))||!Eq(collection,At(0x135A6A8)))return false;
  s.updatable=Word(At(0x1482D88));s.app=Word(s.updatable+4);
  if(!Eq(s.updatable,At(0x1324CAC))||!Eq(s.updatable+8,game)||!Eq(s.updatable+12,render)||!Eq(s.app+0xA8,game)||!Eq(s.app+0xAC,render)||!Eq(s.app+0xB8,collection)||!Eq(s.app+0xC4,s.updatable))return false;
  if(!Eq(game+0x21A0,At(0x13247D4))||!Eq(game+0x2120,At(0x1323D74)))return false;
  s.cloth=Word(game+0x21A4);s.facade=Word(game+0x20C0);s.master=game+0x21E0;s.masterTask=Word(game+0x2210);
  if(!Ptr(s.cloth,0x2A0)||!Ptr(s.facade,8)||!Eq(game+0x20C4,s.cloth)||!Eq(game+0x214C,s.cloth)||!Eq(s.cloth+0x30,s.facade)||!Eq(s.master,At(0x135DDD4))||!Eq(s.master+4,s.cloth))return false;
  s.renderer=Word(s.coordinator+0x98);s.syncTask=Word(s.coordinator+0x11C);s.event=s.renderer+0x2F38;
  if(Byte(s.coordinator+0xF4)!=1)return Fail(Gate::Pending,s.coordinator+0xF4,Byte(s.coordinator+0xF4),1);
  if(!Ptr(s.renderer,0x2F70)||!Eq(s.renderer+0x2F30,At(0x134FCFC))||!Eq(s.event,At(0x13C88A8)))return false;
  s.handle=Word(s.event+4);if(!s.handle||s.handle==0xffffffffu)return Fail(Gate::Owner,s.event+4,s.handle,1);
  s.jobs=Word(s.cloth+0x24);s.count=Word(s.cloth+0x38);s.array=s.cloth+0x14C;
  if(s.count>64||!Eq(s.cloth+0x24C,s.count))return Fail(Gate::Owner,s.cloth+0x38,s.count,64);
  if(s.count&&!Ptr(s.jobs,s.count*0x90))return Fail(Gate::Owner,s.cloth+0x24,s.jobs,1);
  for(U32 i=0;i<s.count;++i){s.events[i]=Word(s.array+i*4);s.handles[i]=Word(s.events[i]+4);
   if(!Eq(s.jobs+i*0x90,At(0x1398A94))||s.events[i]!=s.jobs+i*0x90+4||!Eq(s.events[i],At(0x13C88A8))||!s.handles[i]||s.handles[i]==0xffffffffu)return Fail(Gate::Owner,s.array+i*4,s.events[i],s.jobs+i*0x90+4);
  }
  s.kindCount=Word(s.cloth+0x278);
  if(s.kindCount>10)return Fail(Gate::Owner,s.cloth+0x278,s.kindCount,10);
  for(U32 i=0;i<s.kindCount;++i)s.kinds[i]=Word(s.cloth+0x250+i*4);
  return fault.gate==Gate::Observed;
 }
};
// Exact machine-state proof at107DE74, before original TEST DL/JE. This
// deliberately does not accept the earlier zero-delta or empty-kind exits.
inline bool PredicateValid(const Owners&o,U32 selector,U32 found,U32 loopCount,U32 loopIndex,U32 deltaBits){
 if((selector!=1&&selector!=2)||found>1||!o.kindCount||o.kindCount>10||loopCount!=o.kindCount||!deltaBits||deltaBits>=0x7f800000u)return false;
 bool present=false;for(U32 i=0;i<o.kindCount;++i)present=present||o.kinds[i]==selector;
 return found==U32(present)&&loopIndex==loopCount+U32(present);
}
inline U32 MaskCount(U32 mask){return((mask>>1)&1u)+((mask>>2)&1u);}
template<class Read> bool PredicateTrampoline(Read read,U32 p,U32 skip,U32 continuation){
 U32 v{};return Ptr(p,13)&&read(p,v)&&v==0x840FD284u&&read(p+4,v)&&p+8+v==skip&&read(p+8,v)&&(v&255u)==0xE9&&read(p+9,v)&&p+13+v==continuation;
}
struct Receipt {U32 started{},completed{},active{},calls{},returns{},result{},bad{},mask{},predicateCalls{},predicateMask{},absentMask{},requiredMask{};};
inline bool MasterComplete(const Receipt&r,U32 sequence){return sequence&&r.started==sequence&&r.completed==sequence&&!r.active&&!r.result&&!r.bad&&r.predicateCalls==2&&r.predicateMask==6&&r.requiredMask==r.mask&&(r.mask|r.absentMask)==6&&!(r.mask&r.absentMask)&&r.calls==MaskCount(r.mask)&&r.returns==r.calls;}
struct Token {U32 epoch{},serial{},thread{},master{},nonce{};bool valid{};};
struct Result {Gate gate{Gate::Pending};U32 epoch{},serial{},master{},aggregateCalls{},aggregateReturns{},aggregateResult{},aggregateMask{},syncCalls{},syncReturns{},syncResult{},unbound{},predicateCalls{},predicateMask{},absentMask{},requiredMask{},kindCount{},kinds[10]{};bool observed{},original_returned{},all_workers_retired{},construction_permitted{};Fault fault{};};
inline bool SameInvocation(const Token&a,const Token&b){return a.valid&&b.valid&&a.epoch==b.epoch&&a.serial==b.serial&&a.thread==b.thread&&a.master==b.master&&a.nonce==b.nonce;}
}
