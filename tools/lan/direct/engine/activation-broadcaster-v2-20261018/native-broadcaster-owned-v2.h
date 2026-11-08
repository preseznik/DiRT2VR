#pragma once
#include "../activation-broadcaster-20261018/native-broadcaster-inventory-v1.h"
namespace native_broadcaster_v2 {
using U32=std::uint32_t;
enum class Gate { Ready, Address, Inventory, RouteSet, Destination, Class, Owner, Callback, Unstable };
struct Failure {const char* field{};U32 address{},actual{},expected{};};
struct Snapshot {native_broadcaster_v1::Inventory inventory{};native_broadcaster_v1::Destination destinations[2]{};
 U32 callbacks[2]{},owners[2]{},callbackNodes[2]{};Failure failure{};};

// All reads belong to one stable snapshot. refs are real process-retained
// 117B460 results, ordered ["organisms","BroadcastMessage_CrowdState_brmq"].
// No native calls, writes, registry lookup, reference mutation or event dispatch.
// This proves ONLY this routing prerequisite; parent owns every activation gate.
template<class Read> Gate InspectOwnedRoutes(Read source,U32 imageBase,U32 game,U32 broadcaster,const U32 (&refs)[2],Snapshot& out){
 out={};
 if(imageBase<0x10000||imageBase>0x60000000||!native_activation::Pointer(game,0x22DC))return Gate::Address;
 const auto at=[=](U32 va){return imageBase+va-0x400000u;};
 native_activation::Snapshot<Read> r(source);
 const auto cached=[&](U32 p,U32& value){value=r(p);return r.valid;};
 const auto fail=[&](Gate g,const char* field,U32 p,U32 value,U32 expected){if(!out.failure.field)out.failure={field,p,value,expected};return g;};
 const auto eq=[&](const char* field,U32 p,U32 expected){const U32 value=r(p);if(value!=expected&&!out.failure.field)out.failure={field,p,value,expected};return r.valid&&value==expected;};
 const auto inv=native_broadcaster_v1::InspectRoutes(cached,imageBase,broadcaster,out.inventory);
 if(inv!=native_broadcaster_v1::Gate::RoutesReady)return fail(Gate::Inventory,"route_inventory",out.inventory.failure.address,static_cast<U32>(inv),0);
 if(out.inventory.routeCount!=2)return fail(Gate::RouteSet,"route_count",broadcaster+0x34,out.inventory.routeCount,2);
 constexpr const char* names[]{"organisms","BroadcastMessage_CrowdState_brmq"};
 U32 routeIndex[2]{0xFFFFFFFFu,0xFFFFFFFFu};
 for(U32 i=0;i<2;++i){bool found=false;for(U32 k=0;k<2;++k)if(native_broadcaster_v1::NameEqual(out.inventory.routes[i].name,names[k])){routeIndex[k]=i;found=true;}
  if(!found)return fail(Gate::RouteSet,"unknown_destination",out.inventory.routes[i].namePointer,i,0);}
 for(U32 i=0;i<2;++i){
  if(routeIndex[i]==0xFFFFFFFFu||!native_activation::Pointer(refs[i],0x1BC))return fail(Gate::Destination,"retained_destination",0,refs[i],i);
  const auto gate=native_broadcaster_v1::InspectRetainedDestination(cached,imageBase,out.inventory.routes[routeIndex[i]],refs[i],out.destinations[i]);
  if(gate!=native_broadcaster_v1::Gate::UnresolvedOwner)return fail(Gate::Destination,"destination_inspection",refs[i],static_cast<U32>(gate),static_cast<U32>(native_broadcaster_v1::Gate::UnresolvedOwner));
 }
 if(refs[0]==refs[1])return fail(Gate::Destination,"aliased_destinations",refs[0],refs[1],0);
 const U32 plugin=at(0x1515C00),organisms=at(0x1515D10),buffer=r(game+0x22D8);
 out.owners[0]=organisms;out.owners[1]=buffer;
 if(!eq("organism_plugin_type",plugin,at(0x133846C))||!eq("organism_owner_type",organisms,at(0x132A1A8)))return Gate::Class;
 if(!eq("organism_plugin_backlink",organisms+0x14,plugin)||!eq("organism_dispatcher_backlink",organisms+0x10,refs[0]))return Gate::Owner;
 if(!native_activation::Pointer(buffer,0xF0))return fail(Gate::Owner,"buffer_owner",game+0x22D8,buffer,0);
 if(!eq("buffer_owner_type",buffer,at(0x132A628)))return Gate::Class;
 if(!eq("buffer_dispatcher_backlink",buffer+0xE8,refs[1]))return Gate::Owner;
 char name[65]{};
 if(!r.String(buffer+0xC,name,32)||!native_broadcaster_v1::NameEqual(name,"game broadcaster"))return fail(Gate::Owner,"buffer_broadcaster_name",buffer+0xC,0,0);
 const U32 bufferedName=r(buffer+0xDC);
 if(!r.String(bufferedName,name,65)||!native_broadcaster_v1::NameEqual(name,names[1]))return fail(Gate::Owner,"buffer_dispatcher_name",buffer+0xDC,bufferedName,0);
 // Native buffering callback has bounded capacity10, and naturally no-ops
 // when disabled/full; no requirement for an artificial empty buffer.
 const U32 enabled=r.Byte(buffer+0x2C),count=r(buffer+0xD0);
 if(enabled>1)return fail(Gate::Owner,"buffer_enabled",buffer+0x2C,enabled,1);
 if(count>10)return fail(Gate::Owner,"buffer_count",buffer+0xD0,count,10);
 constexpr char key[]="BroadcastMessage_CrowdState";
 constexpr U32 functions[]{0x7254A0,0x76B4F0};
 for(U32 i=0;i<2;++i){
  const U32 map=refs[i]+0x148,begin=r(map+0x38),end=r(map+0x3C);
  if(!native_activation::Pointer(begin,8)||end<begin||(end-begin)%4)return fail(Gate::Callback,"callback_buckets",map+0x38,begin,end);
  const U32 length=(end-begin)/4;
  if(length<2||length>4097||!native_activation::Pointer(begin,length*4))return fail(Gate::Callback,"callback_bucket_count",map+0x3C,length,4097);
  const U32 bucket=native_activation::NameHash(key)%(length-1),stop=r(begin+bucket*4+4);
  U32 node=r(begin+bucket*4),seen[512]{},matches{};bool pastKey=false;
  for(U32 n=0;node!=stop;++n){
   if(n==512||!native_activation::Pointer(node,16))return fail(Gate::Callback,"callback_chain_bound",node,n,512);
   for(U32 j=0;j<n;++j)if(seen[j]==node)return fail(Gate::Callback,"callback_cycle",node,n,j);
   seen[n]=node;char callbackName[96]{};const U32 keyPointer=r(node+8);
   if(!r.String(keyPointer,callbackName,96))return fail(Gate::Callback,"callback_key",node+8,keyPointer,0);
   const int order=std::strcmp(callbackName,key);
   if(order>0)pastKey=true;
   if(order==0){
    if(pastKey||++matches!=1)return fail(Gate::Callback,"duplicate_callback",node,matches,1);
    out.callbackNodes[i]=node;const U32 callback=r(node+12);out.callbacks[i]=callback;
    if(!native_activation::Pointer(callback,8))return fail(Gate::Callback,"callback_pair",node+12,callback,0);
    if(!eq("callback_function",callback,at(functions[i]))||!eq("callback_owner",callback+4,out.owners[i]))return Gate::Callback;
   }else if(order<0&&(pastKey||matches))return fail(Gate::Callback,"callback_order",node,0,0);
   node=r(node+4);
  }
  if(matches!=1)return fail(Gate::Callback,"missing_callback",map,matches,1);
 }
 if(!r.Stable())return fail(Gate::Unstable,"snapshot_changed",0,0,0);
 return Gate::Ready;
}
}
