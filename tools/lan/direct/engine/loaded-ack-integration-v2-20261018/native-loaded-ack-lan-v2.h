#pragma once
#include <cstdint>
#include <cstddef>
#include "native-lan-session-mode-guards-v1.h"
#include "../direct-bootstrap/native-components-v1.h"
#include "../native-loaded-ack-20261018/native-loaded-ack-code-guards-v1.h"

// LAN-only correction: type0 selects82F370, whose constructor writes byte1F4=1.
// Other bytes in that DWORD are not this flag. No write to the game field.
// Compile-only candidate. No hook, scheduler, asset loader or default completion
// verifier is provided. See findings.md for the mandatory caller proof boundary.
namespace native_loaded_ack {
using U32=std::uint32_t;
using U64=std::uint64_t;
enum class Status { Ready, WaitForCompletion, WaitForBB4, WaitForTransport,
                    AlreadyAttempted, Rejected, Published, NativeReadbackFailed };
struct Context {
 U32 imageBase{},updateTask{},network{},session{},data{},player{},ownerThread{};
 U64 transaction{};
 bool client{};
};
struct Plan { U32 publisher{},state{},stateTable{},fsm{},root{},latches{}; };
struct Attempt { U64 transaction{};bool attempted{},acknowledged{};U32 state{}; };
struct Event {U32 table{},code{};};
static_assert(sizeof(Event)==8);
inline bool Pointer(U32 p,U32 bytes=4) {return bytes && p>=0x10000 && p<=0x80000000u-bytes;}

template<class Read> class Snapshot {
 Read& source;
 struct Word {U32 address,value;};
 Word words[8192]{};std::size_t count{};
public:
 bool valid{true};
 explicit Snapshot(Read& read):source(read){}
 U32 operator()(U32 address) {
  U32 value{};
  if(!Pointer(address) || !source(address,value)){valid=false;return 0;}
  for(std::size_t i=0;i<count;++i)if(words[i].address==address){if(words[i].value!=value)valid=false;return value;}
  if(count==8192){valid=false;return value;}
  words[count++]={address,value};return value;
 }
 bool Stable() {
  if(!valid)return false;
  for(std::size_t i=0;i<count;++i){U32 value{};if(!source(words[i].address,value) || value!=words[i].value)return false;}
  return true;
 }
};

constexpr U32 StateTables[]{0x1337030,0x13370C0,0x1337240,0x13371B0,
 0x1337060,0x1337150,0x1337270,0x13372A0,0x1337120,0x1337210,
 0x133743C,0x1336FA0,0x1336FD0,0x1337180,0x1337000,0x13370F0,
 0x1337090,0x13371E0};
constexpr U32 IgnoredTables[]{0x1333ECC,0x1334A1C,0x1336114,0x1336498,
 0x13365CC,0x1336204,0x13347E8,0x13345B0};
inline U32 Rotate(U32 value,unsigned bits){return (value<<bits)|(value>>(32-bits));}
inline U32 PublisherKeyHash(U32 key) {
 // 594DE0 with word count1: seed DEADBEEF+4, then the exact final mixer.
 U32 a=0xDEADBEF3u+key,b=0xDEADBEF3u,c=0xDEADBEF3u;
 c=(c^b)-Rotate(b,14);a=(a^c)-Rotate(c,11);b=(b^a)-Rotate(a,25);
 c=(c^b)-Rotate(b,16);a=(a^c)-Rotate(c,4);b=(b^a)-Rotate(a,14);
 return (c^b)-Rotate(b,24);
}
template<class Read> bool PublisherLookupMatches(Read read,U32 publisher,U32 key,U32 expected) {
 // 8A8980 passes publisher+58 to602960. Check the actual hash-bucket path
 // agrees with the independently walked list before the native call can use it.
 const U32 buckets=read(publisher+0x90),end=read(publisher+0x94);
 if(!Pointer(buckets,8) || end<buckets || (end-buckets)%4)return false;
 const U32 length=(end-buckets)/4;
 if(length<2 || length>4097 || !Pointer(buckets,length*4))return false;
 const U32 index=PublisherKeyHash(key)%(length-1),stop=read(buckets+4*index+4);
 U32 node=read(buckets+4*index),seen[256]{};
 for(U32 n=0;node!=stop && n<256;++n){
  if(!Pointer(node,16))return false;
  for(U32 j=0;j<n;++j)if(seen[j]==node)return false;
  seen[n]=node;const U32 candidate=read(node+8);
  if(candidate>=key)return candidate==key && read(node+12)==expected;
  node=read(node+4);
 }
 return false;
}
template<class Read> bool IgnoredObserver(Read read,U32 base,U32 observer,U32 network,U32 data,U32 container) {
 const auto at=[=](U32 va){return base+va-0x400000u;};
 const auto find=[&](U32 root,U32 key){return native_components::Find(read,root,at(key));};
 const U32 table=read(observer),provider=read(data+0x68),log=read(network+0x1004);
 if(read(data)!=at(0x1336248) || read(data+0x64)!=log || read(log)!=at(0x12F0588) || provider!=network+0x1010)return false;
 if(table==at(0x1333ECC)) {const U32 o=read(network+0x2234);return read(o)==at(0x1333F1C) && read(o+0xC)==provider && read(o+0x10)==read(network+0x1094) && read(o+0x14)==read(network+0x1088) && read(o+0x1310)==observer && read(observer+4)==o;}
 if(table==at(0x1334A1C)) {const U32 o=read(network+0x2240);return read(o)==at(0x1334A28) && read(o+8)==provider && read(o+0xC)==network+0x1160 && read(o+0x10)==read(network+0x1088) && read(o+0x24)==observer && read(observer+4)==o;}
 if(table==at(0x1336114)) {const U32 o=read(network+0x2170);return read(o)==at(0x1336134) && observer==o+0x2550;}
 if(table==at(0x1336498))return read(container)==at(0x13364EC) && observer==container+0x2558;
 if(table==at(0x13365CC))return observer==read(data+0x1280) && read(observer+4)==provider && read(observer+8)==data && read(observer+0xC)==log;
 if(table==at(0x1336204))return observer==data+0x1254;
 if(table==at(0x13347E8)) {const U32 o=read(data+0x129C);return read(o)==at(0x1335C50) && read(observer+4)==o && find(o,0x14909D4)==observer && read(o+0x54)==read(data+0x5C) && read(o+0x58)==log && read(o+0x5C)==provider;}
 if(table==at(0x13345B0)) {const U32 o=read(data+0x1274);return read(o)==at(0x1335A38) && read(observer+4)==o && find(o,0x14909FC)==observer && read(o+0x54)==read(data+0x5C) && read(o+0x60)==read(data+0x5C) && read(o+0x64)==log && read(o+0x6C)==read(data+0x126C);}
 return false;
}

// Read(address,out) must report unreadable storage as false. This performs no
// native calls. Completed(context) is checked outside this function: a valid
// configuration, elapsed time, or signalled event alone cannot establish it.
template<class Read> Status Evaluate(Read source,const Context& c,Plan& out) {
 out={};
 const auto at=[&](U32 va){return c.imageBase+va-0x400000u;};
 if(!c.transaction || c.imageBase<0x10000 || c.imageBase>0x60000000 ||
    !Pointer(c.updateTask,0x2CF4) || !Pointer(c.network,0x2244) ||
    !Pointer(c.session,0x208) || !Pointer(c.data,0x12A0) || !Pointer(c.player,0x30))return Status::Rejected;
 Snapshot<Read> memory(source);const auto read=[&](U32 p){return memory(p);};
 const auto find=[&](U32 root,U32 key){return native_components::Find(read,root,at(key));};
 const U32 container=read(c.network+0x2174),holder=read(container+0x2450),transport=read(holder+0x24);
 const U32 role=c.client?1u:0u,connected=c.client?2u:1u;
 if(read(c.updateTask+0x2CF0)!=c.network || read(c.network)!=at(0x1336604) ||
    read(container)!=at(0x13364EC) || read(holder)!=at(0x133549C) || read(holder+4)!=at(0x1335474) ||
    read(holder+0x18)!=c.session || read(c.session)!=at(0x1335844) ||
    read(c.session+4)!=at(0x1335808) || read(c.session+8)!=at(0x1335800) ||
    read(c.session+0x24)!=role || read(c.session+0x1EC)!=c.data ||
    read(transport)!=at(0x135CFC0) || read(c.data)!=at(0x1336248) ||
    read(c.data+0x1028)!=c.player || (read(holder+0x800)&255))return Status::Rejected;
 if(read(holder+8)!=connected || read(transport+8)!=connected || !(read(c.session+0x6C)&255))return Status::WaitForTransport;
 const U32 fsm=find(c.player,0x1490690),state=read(fsm+0x64),table=read(state),flags=read(state+0x60),root=read(state+0x64);
 if(read(fsm)!=at(0x1335C88) || !Pointer(state,0x74) ||
    (table!=at(0x1337060) && table!=at(0x1337150)) ||
    (flags&0xFFFF)!=0x0101 || read(state+0x58)!=c.session || read(state+0x68)!=c.player)return Status::Rejected;
 // Predicate-false BD4 ignores its loaded latch when selecting BB4. The new
 // BB4 object clears its own latch on entry, so never acknowledge that branch early.
 if(table==at(0x1337060) && read(c.session+0x24)!=0)return Status::WaitForBB4;
 const U32 latches=read(state+0x70);
 if((latches&255)!=0 || (table==at(0x1337060)?((latches>>24)&255):((latches>>16)&255)) ||
    (read(c.session+0x1F4)&255)!=1)return Status::Rejected;
 const U32 publisher=read(root)==at(0x1335AB4)?root:find(root,0x14907CC);
 if(read(publisher)!=at(0x1335AB4) || read(publisher+0xAC)!=c.updateTask)return Status::Rejected;
 const U32 vector=find(publisher+0x54,0x14907E8),array=read(vector),capacity=read(vector+4),count=read(vector+8);
 // Only the exact observed set is audited. Extra, missing, retired/null, or new
 // loading-stage classes require another audit; no subscriber is filtered out.
 if(!vector || count!=26 || count>capacity || capacity>1024 || !Pointer(array,count*4))return Status::Rejected;
 if(!PublisherLookupMatches(read,publisher,at(0x14907E8),vector))return Status::Rejected;
 U32 observers[26]{},targets[18]{},stateMask{},ignoredMask{},activeCount{};
 for(U32 i=0;i<count;++i) {
  const U32 observer=read(array+4*i);observers[i]=observer;
  if(!Pointer(observer,8))return Status::Rejected;
  for(U32 j=0;j<i;++j)if(observers[j]==observer)return Status::Rejected;
  const U32 observerTable=read(observer);
  if(observerTable==at(0x1336EEC)) {
   const U32 target=read(observer+4),targetTable=read(target);U32 index=18;
   for(U32 j=0;j<18;++j)if(targetTable==at(StateTables[j]))index=j;
   if(index==18 || (stateMask&(1u<<index)) || !Pointer(target,0x74))return Status::Rejected;
   const U32 active=(read(target+0x60)>>8)&255,binding=read(target+0x58);
   // Base constructor835BE0 initializes an unbound state to zero; the native
   // bind callback8BB490 supplies the session. Inactive unbound states are inert.
   if((read(target+0x60)&255)!=1 || active>1 || (binding && binding!=c.session) ||
      read(target+0x64)!=root || read(target+0x68)!=c.player || find(target,0x1490C20)!=observer)return Status::Rejected;
   for(U32 j=0;j<18;++j)if(targets[j]==target)return Status::Rejected;
   targets[index]=target;stateMask|=1u<<index;
   if(active){if(target!=state || targetTable!=table || binding!=c.session)return Status::Rejected;++activeCount;}
  } else {
   U32 index=8;for(U32 j=0;j<8;++j)if(observerTable==at(IgnoredTables[j]))index=j;
   if(index==8 || (ignoredMask&(1u<<index)) || !IgnoredObserver(read,c.imageBase,observer,c.network,c.data,container))return Status::Rejected;
   ignoredMask|=1u<<index;
  }
 }
 if(stateMask!=0x3FFFF || ignoredMask!=0xFF || activeCount!=1 || !memory.Stable())return Status::Rejected;
 out={publisher,state,table,fsm,root,latches};return Status::Ready;
}

inline bool SamePlan(const Plan& a,const Plan& b) {
 return a.publisher==b.publisher && a.state==b.state && a.stateTable==b.stateTable &&
        a.fsm==b.fsm && a.root==b.root && a.latches==b.latches;
}

// Construct and Publish are the exact native ABI adapters, separately compiled
// in native-loaded-ack-windows-v1.cpp. Fixtures supply non-native spies. The
// complete verifier must bind actual assets, model/plugin finalisation and all
// application/update/render registration completion to this transaction.
template<class Read,class Equal,class Completed,class Construct,class Publish>
Status PublishOnce(Read read,Equal equal,Completed completed,Construct construct,
                   Publish publish,const Context& c,U32 currentThread,Attempt& attempt) {
 if(!c.ownerThread || currentThread!=c.ownerThread)return Status::Rejected;
 if(attempt.attempted)return Status::AlreadyAttempted;
 if(attempt.transaction && attempt.transaction!=c.transaction)return Status::Rejected;
 if(!completed(c))return Status::WaitForCompletion;
 Plan first{},second{};Status status=Evaluate(read,c,first);
 if(status!=Status::Ready)return status;
 if(!CodeSignatures(equal,c.imageBase) || !loaded_lan_mode_v1::Signatures(equal,c.imageBase) || !completed(c))return Status::Rejected;
 status=Evaluate(read,c,second);
 if(status!=Status::Ready || !SamePlan(first,second))return Status::Rejected;
 attempt.transaction=c.transaction;attempt.attempted=true;attempt.state=second.state;
 Event event{};construct(event,7);
 if(event.table!=c.imageBase+0x1333B78-0x400000 || event.code!=7)return Status::NativeReadbackFailed;
 publish(second.publisher,event);
 U32 current{},latches{};
 if(!read(second.fsm+0x64,current) || current!=second.state || !read(second.state+0x70,latches) ||
    latches!=(second.latches|1u))return Status::NativeReadbackFailed;
 attempt.acknowledged=true;return Status::Published;
}
}
