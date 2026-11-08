#pragma once
#include "../native-activation-candidate-20261018/native-activation-v1.h"

namespace native_broadcaster_v1 {
using U32=std::uint32_t;
enum class Gate { RoutesReady, Address, Class, Registry, Name, Tree, Count, Cycle,
                  Ordering, DuplicateDestination, NoRoute, Unstable, DestinationClass,
                  DestinationInactive, UnresolvedOwner };
struct Failure { U32 address{},actual{},expected{}; };
struct Route { U32 node{},messagePointer{},namePointer{};char name[65]{}; };
struct Inventory { U32 broadcaster{},table{},registry{},namePointer{},sentinel{},treeCount{},routeCount{};
 Route routes[8]{};Failure failure{}; };
struct Destination { U32 object{},table{},registry{},namePointer{},active{},callbackMap{},callbackBuckets{},callbackEnd{};
 U32 ownerCandidate{};char name[65]{};Failure failure{}; };
inline bool NameEqual(const char* a,const char* b){return std::strcmp(a,b)==0;}

// This is an inventory, not activation permission. The caller owns a strong
// 117B460 reference, the exclusive held update scope, and code-signature checks.
// 118B0D0 reads this same multimap: message node+14 -> recipient node+60.
template<class Read> Gate InspectRoutes(Read source,U32 imageBase,U32 broadcaster,Inventory& out){
 out={};out.broadcaster=broadcaster;
 if(imageBase<0x10000||imageBase>0x60000000||!native_activation::Pointer(broadcaster,0x19C))return Gate::Address;
 const auto at=[=](U32 va){return imageBase+va-0x400000u;};
 native_activation::Snapshot<Read> r(source);
 const auto eq=[&](U32 p,U32 expected){const U32 actual=r(p);if(actual!=expected&&!out.failure.address)out.failure={p,actual,expected};return r.valid&&actual==expected;};
 out.table=r(broadcaster);if(out.table!=at(0x13CD254))return Gate::Class;
 if(!eq(at(0x13CD254),at(0x11C65E0))||!eq(at(0x13CD254)+4,at(0x11776D0))||!eq(at(0x13CD254)+8,at(0x11776F0)))return Gate::Class;
 out.registry=r(broadcaster+12);out.namePointer=r(broadcaster+4);
 if(out.registry!=at(0x16A0058)||!eq(out.registry,at(0x13CD5D0))||!eq(at(0x13CD5D0)+4,at(0x118F900)))return Gate::Registry;
 char objectName[65]{};if(!r.String(out.namePointer,objectName,65)||!NameEqual(objectName,"game broadcaster"))return Gate::Name;
 out.sentinel=r(broadcaster+0x30);out.treeCount=r(broadcaster+0x34);
 if(!native_activation::Pointer(out.sentinel,0xA8)||r.Byte(out.sentinel+0xA5)!=1)return Gate::Tree;
 if(!out.treeCount||out.treeCount>256){out.failure={broadcaster+0x34,out.treeCount,256};return Gate::Count;}
 const U32 root=r(out.sentinel+4),leftmost=r(out.sentinel),rightmost=r(out.sentinel+8);
 U32 stack[256]{},parents[256]{},visited[256]{},depth=0,seen=0,node=root,parent=out.sentinel,lastNode=0,firstNode=0;
 char prior[65]{};bool havePrior=false;
 while(node!=out.sentinel||depth){
  while(node!=out.sentinel){
   if(!native_activation::Pointer(node,0xA8)||r.Byte(node+0xA5)!=0||!eq(node+4,parent))return Gate::Tree;
   if(seen==256||depth==256)return Gate::Count;
   for(U32 i=0;i<seen;++i)if(visited[i]==node)return Gate::Cycle;
   visited[seen++]=node;stack[depth]=node;parents[depth++]=parent;parent=node;node=r(node);
  }
  node=stack[--depth];parent=parents[depth];(void)parent;
  if(!firstNode)firstNode=node;lastNode=node;
  char message[65]{},destination[65]{};const U32 messagePointer=r(node+0x14),namePointer=r(node+0x60);
  if(!r.String(messagePointer,message,65)||!message[0]||!r.String(namePointer,destination,65)||!destination[0])return Gate::Name;
  if(havePrior&&std::strcmp(prior,message)>0)return Gate::Ordering;
  std::memcpy(prior,message,65);havePrior=true;
  if(NameEqual(message,"BroadcastMessage_CrowdState")){
   if(out.routeCount==8)return Gate::Count;
   for(U32 i=0;i<out.routeCount;++i)if(NameEqual(out.routes[i].name,destination))return Gate::DuplicateDestination;
   Route& route=out.routes[out.routeCount++];route.node=node;route.messagePointer=messagePointer;route.namePointer=namePointer;
   std::memcpy(route.name,destination,65);
  }
  parent=node;node=r(node+8);
 }
 if(seen!=out.treeCount||firstNode!=leftmost||lastNode!=rightmost)return Gate::Tree;
 if(!out.routeCount)return Gate::NoRoute;
 return r.Stable()?Gate::RoutesReady:Gate::Unstable;
}

// Parent may resolve only a discovered, bounded route.name using native117B460
// into stable retained storage. This inspects that result without calls/writes.
// Concrete recipient ownership is deliberately unresolved in this preflight;
// a valid dispatcher alone must never authorize activation.
template<class Read> Gate InspectRetainedDestination(Read source,U32 imageBase,const Route& route,U32 retained,Destination& out){
 out={};out.object=retained;
 if(imageBase<0x10000||imageBase>0x60000000||!native_activation::Pointer(retained,0x1BC)||!route.name[0]||!std::memchr(route.name,0,65))return Gate::Address;
 const auto at=[=](U32 va){return imageBase+va-0x400000u;};native_activation::Snapshot<Read> r(source);
 out.table=r(retained);out.registry=r(retained+12);out.namePointer=r(retained+4);out.active=r.Byte(retained+0x38);
 if(!r.String(out.namePointer,out.name,65)||!NameEqual(out.name,route.name))return Gate::Name;
 if(out.registry!=at(0x16A0058)||r(out.registry)!=at(0x13CD5D0))return Gate::Registry;
 if(out.table!=at(0x13CD1E8))return Gate::DestinationClass;
 out.callbackMap=retained+0x148;out.callbackBuckets=r(out.callbackMap+0x38);out.callbackEnd=r(out.callbackMap+0x3C);
 if(out.active!=1)return Gate::DestinationInactive;
 if(!r.Stable())return Gate::Unstable;
 return Gate::UnresolvedOwner;
}
inline bool ActivationPermitted(Gate){return false;} // V1 is a preflight only.
}
