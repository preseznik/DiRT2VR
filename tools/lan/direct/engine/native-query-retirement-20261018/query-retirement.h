#pragma once
#include <cstdint>
#include "../late-frame-observer-20261018/frame-window.h"
namespace query_retirement {
using U32=std::uint32_t;
enum class Gate {Ready,Frame,Unreadable,Owner,Audio,Registry,Phase,Group,Job,Records,Changed,Code,Hook,Receipt,Exception};
struct Fault {Gate gate{Gate::Ready};U32 address{},actual{},expected{};};
inline bool Ptr(U32 p,U32 size=4){return p>=0x10000&&size&&std::uint64_t(p)+size<=0x80000000ull;}
struct Snapshot {
 U32 game{},render{},collection{},loader{},facade{},world{},query{},audio{},provider{},caster{},handler{},registry{},registryCount{},casterNode{},handlerNode{},task{},array{},count{},recordCount{},phase{};
 U32 events[64]{},handles[64]{},records[64]{},sizes[64]{};
};
inline bool Same(const Snapshot&a,const Snapshot&b,bool ignorePhase=false){
 if(a.game!=b.game||a.render!=b.render||a.collection!=b.collection||a.loader!=b.loader||a.facade!=b.facade||a.world!=b.world||a.query!=b.query||a.audio!=b.audio||a.provider!=b.provider||a.caster!=b.caster||a.handler!=b.handler||a.registry!=b.registry||a.registryCount!=b.registryCount||a.casterNode!=b.casterNode||a.handlerNode!=b.handlerNode||a.task!=b.task||a.array!=b.array||a.count!=b.count||a.recordCount!=b.recordCount||(!ignorePhase&&a.phase!=b.phase))return false;
 for(unsigned i=0;i<a.count;++i)if(a.events[i]!=b.events[i]||a.handles[i]!=b.handles[i]||a.records[i]!=b.records[i]||a.sizes[i]!=b.sizes[i])return false;
 return true;
}
inline bool Frame(const late_frame::Window&w,const late_frame::Owners&o,U32 self,U32 caller,U32 appCaller,U32 thread,U32 serial){
 return w.bound&&w.primed&&!w.failed&&!w.inside&&w.update&&w.render&&w.phase2&&w.epoch&&w.epoch!=0xffffffffu&&serial>w.lastPhysics&&late_frame::Same(w.owners,o)&&o.thread==thread&&self==o.collection&&caller==appCaller;
}
template<class Read> struct Inspector {
 Read read;U32 imageBase;Fault fault{};
 U32 At(U32 va)const{return imageBase+va-0x400000;}
 bool Fail(Gate g,U32 p,U32 actual,U32 expected){if(fault.gate==Gate::Ready)fault={g,p,actual,expected};return false;}
 U32 Word(U32 p){U32 v{};if(!Ptr(p)||!read(p,v))Fail(Gate::Unreadable,p,0,0);return v;}
 U32 Byte(U32 p){return(Word(p&~3u)>>((p&3u)*8))&255u;}
 bool Eq(U32 p,U32 v,Gate g){const U32 actual=Word(p);return actual==v&&fault.gate==Gate::Ready?true:Fail(g,p,actual,v);}
 bool Registry(Snapshot&s){
  const U32 owner=s.game+0x20,treeHeader=Word(owner+8),count=Word(owner+12);s.registry=treeHeader;s.registryCount=count;
  if(!Ptr(treeHeader,0x3c)||!count||count>128||Word(owner+0x48)||Byte(treeHeader+0x39)!=1)return Fail(Gate::Registry,owner,count,128);
  U32 stack[128]{},parents[128]{},seen[128]{},sp=1,n=0,found=0;stack[0]=Word(treeHeader+4);parents[0]=treeHeader;
  while(sp){const U32 node=stack[--sp],parent=parents[sp];if(node==treeHeader)continue;
   if(!Ptr(node,0x3c)||n>=count||sp+2>128||Byte(node+0x39)||Word(node+4)!=parent)return Fail(Gate::Registry,node,n,count);
   for(U32 i=0;i<n;++i)if(seen[i]==node)return Fail(Gate::Registry,node,1,0);
   seen[n++]=node;const U32 object=Word(node+0x10);
   if(object==s.caster||object==s.handler){const U32 bit=object==s.caster?1u:2u;if((found&bit)||Byte(node+0x28)||Byte(node+0x35))return Fail(Gate::Registry,node,found,bit);found|=bit;if(bit==1)s.casterNode=node;else s.handlerNode=node;}
   stack[sp]=Word(node);parents[sp++]=node;stack[sp]=Word(node+8);parents[sp++]=node;
  }
  return n==count&&found==3&&Eq(owner+8,treeHeader,Gate::Registry)&&Eq(owner+12,count,Gate::Registry)?true:Fail(Gate::Registry,owner,found,3);
 }
 bool Capture(const late_frame::Owners&o,Snapshot&s,U32 expectedPhase){
  s={};s.game=o.game;s.render=o.render;s.collection=o.collection;s.loader=Word(At(0x1482D8C));
  if(!Eq(s.loader,At(0x1325688),Gate::Owner)||!Eq(s.loader+12,o.game,Gate::Owner)||!Eq(s.loader+16,o.render,Gate::Owner)||!Eq(s.loader+20,o.collection,Gate::Owner)||!Eq(o.game,At(0x1329A48),Gate::Owner)||!Eq(o.render,At(0x1324164),Gate::Owner)||!Eq(o.collection,At(0x135A6A8),Gate::Owner)||!Eq(o.game+0x20,At(0x135A6A8),Gate::Owner)||!Eq(o.game+0x2120,At(0x1323D74),Gate::Owner))return false;
  s.facade=Word(o.game+0x2148);s.world=Word(s.facade+4);s.query=Word(s.world+0x18);s.phase=Word(s.world+0x7c4);
  if(!Ptr(s.facade,8)||!Ptr(s.world,0x7c8)||!Ptr(s.query,0xcbc)||!Eq(o.game+0x20c0,s.facade,Gate::Owner)||!Eq(s.query+0xa84,s.world,Gate::Owner))return Fail(Gate::Owner,s.query,0,0);
  if(s.phase!=expectedPhase)return Fail(Gate::Phase,s.world+0x7c4,s.phase,expectedPhase);
  s.audio=Word(o.game+0x2278);s.provider=Word(s.audio+0x13f24);s.caster=Word(o.game+0x227c);s.handler=Word(o.game+0x2280);
  if(!Ptr(s.audio,0x15278)||!Eq(s.audio,At(0x13497ac),Gate::Audio)||!Eq(At(0x1482D6C),s.audio,Gate::Audio)||!Eq(s.provider,At(0x12eece8),Gate::Audio)||!Eq(s.provider+0x34,o.game,Gate::Audio)||s.provider!=Word(o.game+0x2270)+0x1c||!Eq(s.provider+0x2c,0,Gate::Audio)||!Eq(s.audio+0x9b70,At(0x1343e8c),Gate::Audio)||!Eq(s.audio+0x9b78,s.audio+0x1000,Gate::Audio)||!Eq(s.audio+0x100c,s.provider,Gate::Audio)||!Eq(s.caster,At(0x1328200),Gate::Audio)||!Eq(s.caster+4,s.audio,Gate::Audio)||!Eq(s.handler,At(0x1328214),Gate::Audio)||!Eq(s.handler+4,s.audio,Gate::Audio))return Fail(Gate::Audio,s.audio,0,0);
  if(Byte(s.audio+0x14c0d)!=1||Byte(s.audio+0xa400)!=1||Byte(s.audio+0x14c10)||Byte(s.audio+0x15270)||Byte(s.audio+0x15271))return Fail(Gate::Audio,s.audio+0x14c0d,Byte(s.audio+0x14c0d),1);
  if(!Registry(s))return false;
  s.task=Word(s.query+0xbb0);s.array=Word(s.query+0xcb8);s.count=Word(s.query+0x974);
  if(Byte(s.query+0x935)!=1||!Ptr(s.task,0x1c)||!s.count||s.count>64||!Ptr(s.array,s.count*0x1c)||!Eq(s.query+0xcb4,s.count,Gate::Group)||!Eq(s.task+0x18,s.count,Gate::Group))return Fail(Gate::Group,s.query+0x974,s.count,64);
  U32 next=s.audio+0x9bf0,total=0;
  for(U32 i=0;i<s.count;++i){const U32 job=s.array+i*0x1c;s.events[i]=Word(s.query+0xbb4+4*i);s.handles[i]=Word(job+8);s.records[i]=Word(job+12);s.sizes[i]=Word(job+16);
   if(!Eq(job,At(0x139882c),Gate::Job)||!Eq(job+24,s.query,Gate::Job)||!Eq(job+4,At(0x13c88a8),Gate::Job)||s.events[i]!=job+4||!s.handles[i]||s.handles[i]==0xffffffffu)return Fail(Gate::Job,job,s.events[i],job+4);
   for(U32 j=0;j<i;++j)if(s.handles[j]==s.handles[i])return Fail(Gate::Job,job+8,s.handles[i],0);
   if(s.records[i]!=next||s.sizes[i]>6-total)return Fail(Gate::Records,job+12,s.records[i],next);
   total+=s.sizes[i];next+=s.sizes[i]*0xb0;
  }
  s.recordCount=total;if(total!=5&&total!=6)return Fail(Gate::Records,s.array,total,6);
  return fault.gate==Gate::Ready;
 }
};
struct Receipt {U32 calls{},returns{},result{0xffffffffu};bool mismatch{};};
inline bool Complete(const Receipt&r){return r.calls==1&&r.returns==1&&!r.result&&!r.mismatch;}
}
