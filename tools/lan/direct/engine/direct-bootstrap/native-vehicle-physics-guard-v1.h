#pragma once
#include <cstdint>
namespace native_vehicle_physics_v1 {
using U32=std::uint32_t;
enum class Gate { Pending, Ready, Reject };
struct Snapshot {U32 system{},car[2]{},model[2]{},companion[2]{};};
inline bool Pointer(U32 p,U32 size=4){return p>=0x10000 && size && std::uint64_t(p)+size<=0x80000000ull;}
// Structural ownership and producer backlinks only. Never asserts full resource readiness.
template<class Read> Gate Inspect(Read source,U32 base,U32 game,U32 expectedSystem,const U32(&entries)[2],const U32(&liveries)[2],Snapshot& out) {
 out={};struct Word{U32 address,value;};Word words[64]{};unsigned count{};bool unreadable{},changed{},pending{};
 const auto at=[=](U32 va){return base+va-0x400000;};
 const auto read=[&](U32 a){U32 v{};if(!Pointer(a)||!source(a,v)){unreadable=true;return U32{};}
  for(unsigned i=0;i<count;++i)if(words[i].address==a){if(words[i].value!=v)changed=true;return v;}
  if(count==64){unreadable=true;return U32{};}words[count++]={a,v};return v;};
 out.system=read(game+0x222C);if(!Pointer(out.system,0x60)||out.system!=expectedSystem)return Gate::Reject;
 const U32 begin=read(out.system+0x54),end=read(out.system+0x58),cap=read(out.system+0x5C);
 if(!Pointer(begin,8)||end!=begin+8||cap<end||(cap-begin)%4||cap-begin>128)return Gate::Reject;
 unsigned mask{},local{},remote{};
 for(unsigned i=0;i<2;++i){
  const U32 car=read(begin+4*i);out.car[i]=car;
  if(!Pointer(car,0x8450)||read(car)!=at(0x1328788)||read(car+0x5564)!=i||read(car+0x55A0)!=out.system||(i&&car==out.car[0]))return Gate::Reject;
  const U32 entry=read(car+0x5584);unsigned j=2;for(unsigned k=0;k<2;++k)if(entries[k]==entry)j=k;
  if(j==2||(mask&(1u<<j))||!Pointer(entry,0xD0)||read(entry)!=at(0x132A398)||read(car+0x558C)!=liveries[j])return Gate::Reject;
  mask|=1u<<j;const U32 entryRole=read(entry+0x1C),role=read(car+0x5588);
  if(entryRole==4&&role==0)++local;else if(entryRole==2&&role==1)++remote;else return Gate::Reject;
  const U32 model=read(car+0x30),companion=read(car+0x10);out.model[i]=model;out.companion[i]=companion;
  if(!model||!companion){pending=true;continue;}
  if(!Pointer(model,0x580)||!Pointer(companion,0x968)||read(model+0x57C)!=car||read(companion+0x964)!=model||read(companion+0x958)!=car+0x40)return Gate::Reject;
  if(i&&(model==out.model[0]||companion==out.companion[0]))return Gate::Reject;
 }
 if(mask!=3||local!=1||remote!=1||unreadable)return Gate::Reject;
 for(unsigned i=0;i<count;++i){U32 v{};if(!source(words[i].address,v))return Gate::Reject;if(v!=words[i].value)changed=true;}
 return pending||changed?Gate::Pending:Gate::Ready;
}
}
