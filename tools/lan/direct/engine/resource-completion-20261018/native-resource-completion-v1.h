#pragma once
#include <cstdint>
#include <cstddef>
#include "native-resource-completion-guards-v1.h"
namespace resource_completion {
using U32=std::uint32_t;
constexpr unsigned Capacity=256;
inline bool Ptr(U32 p,U32 n=4){return p>=0x10000 && n && std::uint64_t(p)+n<=0x80000000ull;}
// PUSHFD/PUSHAD at the replacement CALL target; original arguments remain above.
struct SavedFrame {
 U32 edi,esi,ebp,pushadEsp,ebx,edx,ecx,eax,flags,returnAddress,job,allocator;
};
static_assert(sizeof(SavedFrame)==48);
static_assert(offsetof(SavedFrame,returnAddress)==36);
enum class Fault:U32 { None,AlreadyArmed,Ownership,Code,Origin,Tree,Serial,Reuse,Overflow,Unreadable,Changed };
enum class Outcome:U32 { UnknownClass,UnknownState,Success,NativeFailure };
struct Receipt {
 U32 token{},jobIdentity{},jobTable{},state{0xffffffffu},eventHandle{},nativeSerial{},thread{},caller{};
 Outcome outcome{Outcome::UnknownClass};
 bool nativeZeroPoll{},xmlEventSignaled{};
 char path[260]{};
};
struct Ledger {
 bool armed{};Fault fault{Fault::None};
 U32 base{},manager{},allocator{},scheduler{},firstSerial{},ignoredBeforeEpoch{},count{};
 Receipt receipts[Capacity]{};
 void Fail(Fault f){if(fault==Fault::None)fault=f;}
 const Receipt* Find(U32 token)const{
  if(!armed||fault!=Fault::None)return nullptr;
  for(U32 i=0;i<count;++i)if(receipts[i].token==token)return &receipts[i];
  return nullptr;
 }
};
// Both Arm and Observe require the original manager+58 lock to be held. The
// injected callsite already has it. Consumers also acquire it before reading
// this ledger. Never hold an independent lock while acquiring the native lock.
template<class Read> bool Arm(Ledger& l,Read read,U32 base,U32 manager){
 if(l.armed){l.Fail(Fault::AlreadyArmed);return false;}
 U32 vt{},canonical{},lock{},allocator{},scheduler{},serial{};
 if(base<0x10000||base>0x60000000||!Ptr(manager,0x7C)||
    !read(base+0x15D9A04-0x400000,canonical)||canonical!=manager||
    !read(manager,vt)||vt!=base+0x135B85C-0x400000||
    !read(manager+0x58,lock)||lock!=manager+0x5C||
    !read(manager+4,allocator)||!Ptr(allocator)||
    !read(manager+0x3C,scheduler)||!Ptr(scheduler,0x2148)||
    !read(manager+0x74,serial)||serial==0xffffffffu){l.Fail(Fault::Ownership);return false;}
 l.base=base;l.manager=manager;l.allocator=allocator;l.scheduler=scheduler;l.firstSerial=serial;l.armed=true;
 return true;
}
template<class Read,class Equal> void Observe(Ledger& l,Read read,Equal equal,
 const SavedFrame& f,U32 hookAddress,U32 thread){
 if(!l.armed||l.fault!=Fault::None)return;
 const auto at=[&](U32 a){return l.base+a-0x400000u;};
 if(!Signatures(equal,l.base,hookAddress)){l.Fail(Fault::Code);return;}
 U32 value{},outerReturn{};
 if(!Ptr(f.pushadEsp,0x38)||!read(f.pushadEsp+0x34,outerReturn)) {l.Fail(Fault::Unreadable);return;}
 if(f.returnAddress!=at(0xC28495)||
    (outerReturn!=at(0xC285C5)&&outerReturn!=at(0xC381DB)&&outerReturn!=at(0xC3821A))||
    f.ebp!=l.manager||f.ecx!=l.scheduler||f.edi!=f.job||f.eax!=l.allocator||f.allocator!=l.allocator||!thread){l.Fail(Fault::Origin);return;}
 if(!read(at(0x15D9A04),value)||value!=l.manager||!read(l.manager,value)||value!=at(0x135B85C)||
    !read(l.manager+0x58,value)||value!=l.manager+0x5C||!read(l.manager+4,value)||value!=l.allocator||
    !read(l.manager+0x3C,value)||value!=l.scheduler){l.Fail(Fault::Ownership);return;}
 U32 sentinel{},walk{},nodeKey{},nodeJob{},serial{};
 if(!read(l.manager+0x50,sentinel)||!Ptr(sentinel,0x18)||
    !read(sentinel+0x14,value)||((value>>8)&255)!=1||
    !Ptr(f.esi,0x18)||f.esi==sentinel||!read(f.esi+0xC,nodeKey)||!read(f.esi+0x10,nodeJob)||
    nodeJob!=f.job||!Ptr(nodeJob)||!read(l.manager+0x74,serial)){l.Fail(Fault::Tree);return;}
 if(serial<l.firstSerial||!nodeKey||nodeKey>serial){l.Fail(Fault::Serial);return;}
 if(!read(sentinel+4,walk)){l.Fail(Fault::Tree);return;}
 U32 seen[256]{},visits{},candidate=sentinel;
 while(walk!=sentinel){
  if(!Ptr(walk,0x18)||visits==256||!read(walk+0x14,value)||((value>>8)&255)!=0){l.Fail(Fault::Tree);return;}
  for(U32 i=0;i<visits;++i)if(seen[i]==walk){l.Fail(Fault::Tree);return;}
  seen[visits++]=walk;U32 key{};
  if(!read(walk+0xC,key)){l.Fail(Fault::Tree);return;}
  if(key>=nodeKey){candidate=walk;if(!read(walk,walk)){l.Fail(Fault::Tree);return;}}
  else if(!read(walk+8,walk)){l.Fail(Fault::Tree);return;}
 }
 if(candidate!=f.esi){l.Fail(Fault::Tree);return;}
 if(nodeKey<=l.firstSerial){++l.ignoredBeforeEpoch;return;}
 for(U32 i=0;i<l.count;++i)if(l.receipts[i].token==nodeKey){l.Fail(Fault::Reuse);return;}
 if(l.count==Capacity){l.Fail(Fault::Overflow);return;}
 Receipt r{};r.token=nodeKey;r.jobIdentity=nodeJob;r.nativeSerial=serial;r.thread=thread;r.caller=outerReturn;
 r.nativeZeroPoll=true; // Original C28482 result==0 is the sole path to this CALL.
 if(!read(nodeJob,r.jobTable)){l.Fail(Fault::Unreadable);return;}
 if(r.jobTable==at(0x135A5D8)){
  U32 eventTable{},path{},capacity{};
  if(!Ptr(nodeJob,0x200)||!read(nodeJob+0xC,eventTable)||eventTable!=at(0x13C88A8)||
     !read(nodeJob+0x10,r.eventHandle)||!r.eventHandle||r.eventHandle==0xffffffffu||
     !read(nodeJob+0x14,r.state)||!read(nodeJob+0x50,path)||path!=nodeJob+0x54||
     !read(nodeJob+0x4C,capacity)||capacity!=260){l.Fail(Fault::Ownership);return;}
  bool terminated=false;
  for(unsigned i=0;i<260;++i){
   if(!read((path+i)&~3u,value)){l.Fail(Fault::Unreadable);return;}
   r.path[i]=static_cast<char>((value>>(((path+i)&3)*8))&255);
   if(!r.path[i]){terminated=true;break;}
  }
  if(!terminated){l.Fail(Fault::Ownership);return;}
  r.xmlEventSignaled=true;
  r.outcome=r.state==3?Outcome::Success:r.state==4?Outcome::NativeFailure:Outcome::UnknownState;
  if(!read(nodeJob+0x14,value)||value!=r.state||!read(nodeJob+0x10,value)||value!=r.eventHandle){l.Fail(Fault::Changed);return;}
 }
 // Other job classes retain identity only. Never interpret XML offsets or
 // dispatch additional virtual methods through an unknown class.
 if(!read(nodeJob,value)||value!=r.jobTable||!read(f.esi+0xC,value)||value!=nodeKey||
    !read(f.esi+0x10,value)||value!=nodeJob||!read(l.manager+0x74,value)||value!=serial){l.Fail(Fault::Changed);return;}
 l.receipts[l.count++]=r;
}
// Numeric jobIdentity is historical evidence, never a retained native object.
// No AddRef/Release, wait, cancellation, destructor or resource API is called.
}
