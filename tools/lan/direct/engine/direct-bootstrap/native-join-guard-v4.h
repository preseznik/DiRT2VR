#pragma once
#include <array>
#include <cstdint>
#include "native-peer-address-v1.h"
namespace native_join_guard {
using U32=std::uint32_t;
struct Selection {U32 model{},record{},count{},aliases{};};
namespace detail {
// C94AE0 copies each complete 24-byte property entry. Compare conservatively,
// including opaque entry words; never reinterpret or invent missing properties.
struct Payload {
 std::array<U32,5> slots{};
 U32 schema{},propertyCount{},ranked{};
 std::array<U32,64*6> properties{};
 std::array<U32,2> key{};
 std::array<U32,4> secret{},name{};
 std::array<U32,2> extension{};
 bool operator==(const Payload& x)const {
  return slots==x.slots && schema==x.schema && propertyCount==x.propertyCount && ranked==x.ranked && properties==x.properties && key==x.key && secret==x.secret && name==x.name && extension==x.extension;
 }
};
template<class Reader> bool ReadPayload(Reader read,U32 record,Payload& p) {
 p={};
 for(U32 i=0;i<5;++i)p.slots[i]=read(record+0x10+i*4);
 p.schema=read(record+0x30);p.propertyCount=read(record+0x48);p.ranked=read(record+0x650)&255;
 if(read(record+0x40)!=record+0x50 || read(record+0x44)!=64 || p.propertyCount>64)return false;
 for(U32 i=0;i<p.propertyCount*6;++i)p.properties[i]=read(record+0x50+i*4);
 for(U32 i=0;i<2;++i)p.key[i]=read(record+0x698+i*4);
 // Secret words are temporary equality inputs only. Do not log or serialize p.
 for(U32 i=0;i<4;++i)p.secret[i]=read(record+0x6A0+i*4);
 // 115A690 copies bytes only through the first NUL and leaves the tail untouched.
 // Canonicalize that bounded string; reject an unterminated native record.
 bool terminated=false;U32 nameWord{};
 for(U32 i=0;i<16;++i) {
  if(!(i&3))nameWord=read(record+0x6B0+i);
  const U32 byte=(nameWord>>((i&3)*8))&255;
  if(!byte){terminated=true;break;}
  p.name[i/4]|=byte<<((i&3)*8);
 }
 if(!terminated)return false;
 for(U32 i=0;i<2;++i)p.extension[i]=read(record+0x6D0+i*4);
 return true;
}
}
// Native LAN enumeration appends one record per reply without deduplication.
// An intended live host may therefore have several equivalent genuine records.
// Caller retains same-thread ownership, loaded-code guards and search lifetime.
template<class Reader> bool Select(Reader read,U32 base,U32 container,U32 keyLow,U32 keyHigh,const native_peer_address::Token& target,Selection& out) {
 out={};const auto at=[&](U32 va){return base+va-0x400000;};
 if(!native_peer_address::Valid(target))return false;
 if(container<0x10000 || container>0x7fffd000 || !(keyLow|keyHigh) || read(container)!=at(0x13364EC))return false;
 const U32 searcher=container+0x2460,holder=read(container+0x2450),model=read(searcher+0x34),transport=read(searcher+0x2C);
 if(read(searcher)!=at(0x135D120) || read(searcher+0xC)!=0 || (read(searcher+0x10)&255) || (read(searcher+0x54)&255) || read(transport)!=at(0x135CFC0) || read(transport+8)!=0 || read(holder)!=at(0x133549C) || read(holder+8)!=0 || read(holder+0x18) || read(holder+0x24) || (read(holder+0x800)&255))return false;
 if(read(searcher+4)<0x10000 || read(searcher+8)<0x10000 || model<0x10000 || model>0x7fffff00 || read(model)!=container+0x1E20 || read(model+4)!=read(searcher+4) || read(model+8)!=read(searcher+8) || read(model+12)!=0 || read(model+0x70)!=model+0x80 || read(model+0x74)!=20)return false;
 const U32 count=read(model+0x78);if(!count || count>20)return false;
 U32 selected{},aliases{},seen[20]{};bool matched[20]{};
 detail::Payload selectedPayload{};
 for(U32 pass=0;pass<2;++pass) {
  U32 passAliases{};
  for(U32 i=0;i<count;++i) {
   const U32 record=read(model+0x80+i*4);
   if(record<0x10000 || record>0x7ffff920 || read(record)!=at(0x13357C0) || read(record+0x10)!=0 || read(record+0x30)!=read(container+0x1E20))return false;
   if(pass) {if(record!=seen[i])return false;}
   else {for(U32 j=0;j<i;++j)if(seen[j]==record)return false;seen[i]=record;}
   native_peer_address::Token address{};
   if(!native_peer_address::ReadToken(read,record+0x668,address))return false;
   const bool matches=address==target;
   // Same routing identity with conflicting advertised XNADDR is not an alias.
   if(!matches && native_peer_address::Instance(address)==native_peer_address::Instance(target) && address[36]==target[36] && address[37]==target[37])return false;
   if(pass && matches!=matched[i])return false;
   if(!pass)matched[i]=matches;
   if(!matches)continue;
   if(read(record+0x698)!=keyLow || read(record+0x69C)!=keyHigh)return false;
   detail::Payload payload{};if(!detail::ReadPayload(read,record,payload))return false;
   if(!selected){selected=record;selectedPayload=payload;}
   else if(!(payload==selectedPayload))return false;
   ++passAliases;
  }
  if(!selected || !passAliases || (pass && passAliases!=aliases))return false;
  aliases=passAliases;
  if(read(searcher+0x34)!=model || read(searcher+0xC)!=0 || read(model+0x78)!=count || read(container+0x2450)!=holder || read(holder+8)!=0 || read(holder+0x18) || read(holder+0x24) || (read(holder+0x800)&255))return false;
 }
 out={model,selected,count,aliases};return true;
}
}
