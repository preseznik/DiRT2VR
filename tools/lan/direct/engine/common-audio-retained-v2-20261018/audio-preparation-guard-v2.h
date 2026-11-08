#pragma once
#include <cstdint>
#include <cstddef>
namespace audio_preparation_guard_v2 {
using U32=std::uint32_t;
struct Child {U32 offset,table;};
constexpr Child Children[]{
 {0xF890,0x13430AC},{0x10810,0x1347BAC},{0xF910,0x1347EF4},{0xA410,0x13434E0},
 {0xA5B0,0x1342DA0},{0x13814,0x134408C},{0xA780,0x134548C},{0x13DC0,0x13457BC},
 {0x12C80,0x1348FBC},{0xF2D0,0x1342E80},{0x9B70,0x1343E8C},{0x8160,0x1342EAC},
 {0x13FF8,0x1348E54},{0x13320,0x13445A0},{0x136C0,0x1344AA4},{0xF420,0x1349274},
 {0x139C0,0x1342FB8},{0x13CE0,0x1342DCC},{0x6B70,0x1346720}};
inline bool Ptr(U32 p,U32 n=4){return p>=0x10000&&n&&std::uint64_t(p)+n<=0x80000000ull;}
struct Snapshot {
 U32 game{},manager{},provider{},setup{},registry{},stage{},count{},mode{},active{},
     engineStage{},uninitMode{},bankStage{},bankMode{},initial{},fe{},race{},worker{},requested{},ack{},setupLoaded{},aiLoaded{},infoEnabled{},infoBound{};
 U32 nodes[19]{},objects[19]{},music[6]{},pending[19]{};
};
enum class Gate {Ready,Unreadable,Owner,Context,Registry,Child,Initial,Music,Car,OwnerRegistry,Queue};
template<class Read> struct Inspector {
 Read read;U32 base;Gate fault{Gate::Ready};
 U32 At(U32 v)const{return base+v-0x400000;}
 U32 Word(U32 p){U32 v{};if(!Ptr(p)||!read(p,v))fault=Gate::Unreadable;return v;}
 U32 Byte(U32 p){return(Word(p&~3u)>>((p&3u)*8))&255u;}
 bool Eq(U32 p,U32 value){return Word(p)==value&&fault==Gate::Ready;}
 bool Registry(U32 owner,U32 first,U32 second,Snapshot* output){
  const U32 header=Word(owner+8),count=Word(owner+12);
  if(!Ptr(header,0x3C)||!count||count>128||Word(owner+0x48)||Byte(header+0x39)!=1)return false;
  if(output&&count!=19)return false;
  U32 stack[128]{},parents[128]{},seen[128]{},n=0,sp=1,found=0,mask=0;
  stack[0]=Word(header+4);parents[0]=header;
  while(sp){const U32 node=stack[--sp],parent=parents[sp];if(node==header)continue;
   if(!Ptr(node,0x3C)||n>=count||sp+2>128||Byte(node+0x39)||Word(node+4)!=parent)return false;
   for(U32 i=0;i<n;++i)if(seen[i]==node)return false;
   seen[n++]=node;
   const U32 obj=Word(node+0x10);
   if(output){unsigned i=0;for(;i<19;++i)if(obj==owner+Children[i].offset)break;
    if(i==19||(mask&(1u<<i))||!Eq(obj,At(Children[i].table))||!Eq(obj+8,owner+0x1000)||Byte(obj+0x18)!=1)return false;
    output->nodes[i]=node;output->objects[i]=obj;mask|=1u<<i;
   } else if(obj==first||obj==second){
    const unsigned bit=obj==first?1u:2u;if(found&bit)return false;found|=bit;
    // Both are synchronous native children, not fixed-rate adapters or disabled rows.
    if(Byte(node+0x28)||Byte(node+0x35))return false;
   }
   stack[sp]=Word(node);parents[sp++]=node;stack[sp]=Word(node+8);parents[sp++]=node;
  }
  return fault==Gate::Ready&&n==count&&(output?mask==0x7ffffu:found==3u)&&Eq(owner+8,header)&&Eq(owner+12,count);
 }
 Gate Inspect(U32 game,Snapshot& s){
  s={};s.game=game;s.manager=Word(game+0x2278);s.setup=Word(game+0x2DFC);const U32 a=s.manager;
  if(!Ptr(a,0x15278)||!Eq(a,At(0x13497AC))||!Eq(s.setup,At(0x1324A2C))||!Eq(s.setup+0x10,game)||
     !Eq(At(0x1482D6C),a)||!Eq(game,At(0x1329A48)))return Gate::Owner;
  s.provider=Word(a+0x13F24);
  if(!Eq(s.provider,At(0x12EECE8))||!Eq(s.provider+0x34,game)||s.provider!=Word(game+0x2270)+0x1C)return Gate::Owner;
  for(const auto& p:Children)if(!Eq(a+p.offset+8,a+0x1000))return Gate::Context;
  constexpr U32 offsets[]{4,8,12,16,20,24,32};
  const U32 values[]{a+0x1024,a+0x1090,s.provider,a+0x8160,a+0x14FE0,a+0x13FF8,a};
  for(unsigned i=0;i<7;++i)if(!Eq(a+0x1000+offsets[i],values[i]))return Gate::Context;
  if(!Registry(a,0,0,&s))return Gate::Registry;
  s.registry=Word(a+8);
  if(!Eq(game+0x20,At(0x135A6A8))||!Registry(game+0x20,a,Word(game+0x2250),nullptr))return Gate::OwnerRegistry;
  s.engineStage=Word(a+0x5FB0);s.uninitMode=Word(a+0x6A38);s.bankStage=Word(a+0x6B70+0x14);s.bankMode=Word(a+0x6B70+0x1560);
  for(unsigned i=0;i<19;++i)s.pending[i]=Word(a+Children[i].offset+0x20);
  s.initial=Byte(a+0x14C0C);s.active=Word(a+0x14C04);s.fe=Byte(a+0x14C08);s.race=Byte(a+0x14C0D);
  s.worker=Byte(a+0x14C10);s.requested=Byte(a+0x15270);s.ack=Byte(a+0x15271);s.mode=Word(a+0x15274);
  s.stage=Word(a+0xA780+0x14);s.count=Word(a+0xA780+0xA0);
  s.setupLoaded=Byte(a+0xA780+0x4AA4);s.aiLoaded=Byte(a+0xA780+0x4AA5);
  s.infoEnabled=Byte(a+0x13FF8+0x19);s.infoBound=Byte(a+0x13FF8+0x618);
  if(Byte(a+0x6A3C)||s.initial!=1||s.engineStage>1||
     Byte(a+0xF420+0x118)!=1||Byte(a+0xF420+0x119)!=1||
     Byte(a+0x12C80+0x568)!=1||Byte(a+0x12C80+0x569)!=1||
     Byte(a+0x8160+0x1928)!=1||Byte(a+0x10810+0x2458)!=1)return Gate::Initial;
  for(unsigned i=0;i<6;++i){const U32 p=Word(a+0xF420+0x254+4*i);s.music[i]=p;
   if(!Ptr(p,16)||!Eq(p,At(0x1340AF8)))return Gate::Music;
   for(unsigned j=0;j<i;++j)if(s.music[j]==p)return Gate::Music;
  }
  if(s.stage>1||s.count||s.infoBound||s.race)return Gate::Car;
  for(unsigned i=0;i<8;++i)if(Word(a+0xA780+0x80+4*i))return Gate::Car;
  return fault;
 }
};
inline bool SameOwner(const Snapshot&a,const Snapshot&b){
 if(a.game!=b.game||a.manager!=b.manager||a.provider!=b.provider||a.setup!=b.setup||a.registry!=b.registry)return false;
 for(unsigned i=0;i<19;++i)if(a.nodes[i]!=b.nodes[i]||a.objects[i]!=b.objects[i])return false;
 for(unsigned i=0;i<6;++i)if(a.music[i]!=b.music[i])return false;
 return true;
}
inline bool Idle(const Snapshot&s){return !s.worker&&!s.requested&&!s.ack;}
inline bool ColdPrepared(const Snapshot&s){return s.engineStage==1&&Idle(s)&&!s.active&&!s.fe&&!s.race&&!s.stage&&!s.count&&!s.setupLoaded&&!s.aiLoaded&&!s.infoEnabled&&!s.infoBound;}
inline bool Mode1Finished(const Snapshot&s){return s.engineStage==1&&s.bankStage==1&&s.bankMode==1&&Idle(s)&&s.active==1&&s.fe==1&&!s.race&&s.stage==1&&s.setupLoaded==1&&s.aiLoaded==1&&!s.count&&!s.infoEnabled&&!s.infoBound&&s.mode==3;}
inline bool TeardownProgress(const Snapshot&s){return s.engineStage==0&&s.uninitMode==1&&s.bankMode==1&&s.bankStage<=1&&s.active<=1&&Idle(s)&&!s.fe&&!s.race&&s.stage==1&&s.setupLoaded==1&&s.aiLoaded==1&&!s.count&&!s.infoEnabled&&!s.infoBound;}
inline bool Retained(const Snapshot&s){if(!TeardownProgress(s)||s.active||s.bankStage)return false;for(U32 p:s.pending)if(p)return false;return true;}
}
