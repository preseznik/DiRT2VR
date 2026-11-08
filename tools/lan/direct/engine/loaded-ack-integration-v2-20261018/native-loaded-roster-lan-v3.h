#pragma once
#include <cstdint>
#include "native-loaded-ack-lan-v2.h"

// Native identity is immutable; only the audited initial-loading state set may
// progress. No spectator, disconnected, error, or current-race admission state.
namespace loaded_roster_v3 {
using U32=std::uint32_t;
struct StateType {U32 table,getter,descriptor;};
constexpr StateType PlayerStates[]{
 {0x13371B0,0x8C5940,0x1490B8C},{0x1337060,0x8C58D0,0x1490BD4},
 {0x1337150,0x8C5920,0x1490BB4},{0x1337270,0x8C5980,0x1490BF0},
 {0x13372A0,0x8C5990,0x1490BE4},{0x1337120,0x8C5910,0x1490BA0}};
constexpr StateType SessionStates[]{
 {0x1336D0C,0x8C1970,0x1490BBC},{0x1336E9C,0x8C1B00,0x1490BE0},
 {0x1336DAC,0x8C19F0,0x1490BD0}};
struct Player {U32 root{},identity{},vehicle{},nativeId{},record{};int livery{};};
struct Binding {U32 imageBase{},session{},data{},tree{},roster{},local{};Player players[2]{};};
struct Progress {U32 playerState[2]{},sessionState{};};
enum class Gate {Ready,Invalid,Changed};
struct Diagnostic {const char* reason{"none"};U32 address{},actual{},expected{};};
inline bool Check(bool valid,Diagnostic* detail,const char* reason,U32 address=0,U32 actual=0,U32 expected=0) {
 if(!valid && detail)*detail={reason,address,actual,expected};return valid;
}
inline bool Allowed(U32 descriptor,bool local,bool afterAck) {
 if(local && !afterAck)return descriptor==0x1490BD4 || descriptor==0x1490BB4;
 return (!local && descriptor==0x1490B8C) || descriptor==0x1490BD4 || descriptor==0x1490BB4 ||
        descriptor==0x1490BF0 || descriptor==0x1490BE4 || (afterAck && descriptor==0x1490BA0);
}
template<class Read> bool State(Read read,U32 imageBase,U32 root,U32 session,bool sessionState,U32& descriptor,Diagnostic* detail=nullptr) {
 const auto at=[imageBase](U32 va){return imageBase+va-0x400000;};
 const U32 fsm=native_components::Find(read,root,at(0x1490690)),state=read(fsm+0x64),table=read(state);
 if(!Check(read(fsm)==at(0x1335C88),detail,"state FSM table",fsm,read(fsm),at(0x1335C88)))return false;
 if(!Check(read(state+0x58)==session,detail,"state session binding",state+0x58,read(state+0x58),session))return false;
 if(!sessionState && !Check(read(state+0x68)==root,detail,"state player backlink",state+0x68,read(state+0x68),root))return false;
 const StateType* types=sessionState?SessionStates:PlayerStates;
 const unsigned count=sessionState?3u:6u;
 for(unsigned i=0;i<count;++i)if(table==at(types[i].table)) {
  if(read(table)!=at(types[i].getter) || (read(at(types[i].getter))&255)!=0xB8 ||
     read(at(types[i].getter)+1)!=at(types[i].descriptor) || (read(at(types[i].getter)+5)&255)!=0xC3)return Check(false,detail,"state getter identity",table,read(table),at(types[i].getter));
  descriptor=types[i].descriptor;return true;
 }
 return Check(false,detail,"state type not audited",state,table,0);
}
template<class Source,class Livery> Gate Inspect(Source source,Livery livery,const Binding& b,bool afterAck,Progress& out,Diagnostic* detail=nullptr) {
 out={};if(detail)*detail={};native_loaded_ack::Snapshot<Source> snapshot(source);
 const auto read=[&](U32 p){return snapshot(p);};
 const auto at=[&](U32 va){return b.imageBase+va-0x400000;};
 const U32 facade=read(b.data+0x1274),mapping=read(facade+0x6C),records=read(mapping+0x60);
 const U32 array=read(b.data+0x1060),ids=read(b.roster+0x5C);
 if(!Check(!(read(b.data)!=at(0x1336248)),detail,"owner check 01: read(b.data)!=at(0x1336248)"))return Gate::Invalid;
 if(!Check(!(read(b.data+0x1024)!=b.tree),detail,"owner check 02: read(b.data+0x1024)!=b.tree"))return Gate::Invalid;
 if(!Check(!(read(b.data+0x1028)!=b.local),detail,"owner check 03: read(b.data+0x1028)!=b.local"))return Gate::Invalid;
 if(!Check(!(read(b.session+0x1EC)!=b.data),detail,"owner check 04: read(b.session+0x1EC)!=b.data"))return Gate::Invalid;
 if(!Check((read(b.session+0x1F4)&255)==1,detail,"owner check 05: fixed LAN byte1F4 must equal1 (raw DWORD shown)",b.session+0x1F4,read(b.session+0x1F4),1))return Gate::Invalid;
 if(!Check(!(native_components::Find(read,b.tree,at(0x1490868))!=b.roster),detail,"owner check 06: native_components::Find(read,b.tree,at(0x1490868))!=b.roster"))return Gate::Invalid;
 if(!Check(!(read(b.roster)!=at(0x13360B4)),detail,"owner check 07: read(b.roster)!=at(0x13360B4)"))return Gate::Invalid;
 if(!Check(!(read(b.roster+0x58)!=b.session),detail,"owner check 08: roster session binding",b.roster+0x58,read(b.roster+0x58),b.session))return Gate::Invalid;
 if(!Check(!(read(b.roster+0x64)!=2),detail,"owner check 09: read(b.roster+0x64)!=2"))return Gate::Invalid;
 if(!Check(!(read(b.roster+0x60)!=8),detail,"owner check 10: read(b.roster+0x60)!=8"))return Gate::Invalid;
 if(!Check(!(read(b.data+0x1068)!=2),detail,"owner check 11: read(b.data+0x1068)!=2"))return Gate::Invalid;
 if(!Check(!(read(mapping+0x68)!=2),detail,"owner check 12: read(mapping+0x68)!=2"))return Gate::Invalid;
 if(!Check(!(read(facade)!=at(0x1335A38)),detail,"owner check 13: read(facade)!=at(0x1335A38)"))return Gate::Invalid;
 if(!Check(!(read(mapping)!=at(0x1335DA4)),detail,"owner check 14: read(mapping)!=at(0x1335DA4)"))return Gate::Invalid;
 if(!Check(!(read(at(0x1335DA4))!=at(0x8B0BF0)),detail,"owner check 15: read(at(0x1335DA4))!=at(0x8B0BF0)"))return Gate::Invalid;
 if(!Check(!(read(b.data+0x126C)!=mapping),detail,"owner check 16: read(b.data+0x126C)!=mapping"))return Gate::Invalid;
 if(!Check(!(read(mapping+0x54)!=read(b.data+0x5C)),detail,"owner check 17: read(mapping+0x54)!=read(b.data+0x5C)"))return Gate::Invalid;
 if(!Check(!(read(mapping+0x58)!=b.data),detail,"owner check 18: read(mapping+0x58)!=b.data"))return Gate::Invalid;
 if(!Check(!(records!=mapping+0x70),detail,"owner check 19: records!=mapping+0x70"))return Gate::Invalid;
 if(!Check(!(read(mapping+0x64)!=8),detail,"owner check 20: read(mapping+0x64)!=8"))return Gate::Invalid;
 if(!Check(!(!native_loaded_ack::Pointer(array,8)),detail,"owner check 21: !native_loaded_ack::Pointer(array,8)"))return Gate::Invalid;
 if(!Check(!(!native_loaded_ack::Pointer(ids,8)),detail,"owner check 22: !native_loaded_ack::Pointer(ids,8)"))return Gate::Invalid;
 unsigned localCount{};
 for(unsigned i=0;i<2;++i) {
  const auto& p=b.players[i];
  if(!Check(native_loaded_ack::Pointer(p.root,0x30),detail,"player root pointer",0,p.root,0) ||
     !Check(p.nativeId<=255,detail,"player native ID range",0,p.nativeId,255) ||
     !Check(p.livery>0,detail,"player livery range",0,static_cast<U32>(p.livery),1))return Gate::Invalid;
  if(i && (!Check(p.root!=b.players[0].root,detail,"duplicate player root",0,p.root,0) ||
     !Check(p.nativeId!=b.players[0].nativeId,detail,"duplicate player native ID",0,p.nativeId,0) ||
     !Check(p.record!=b.players[0].record,detail,"duplicate player record",0,p.record,0)))return Gate::Invalid;
  unsigned rootMatches{},idMatches{},recordMatches{};
  for(unsigned j=0;j<2;++j) {
   rootMatches+=read(array+4*j)==p.root;
   const U32 id=read(ids+4*j);if(!Check(id<=255,detail,"live roster ID range",ids+4*j,id,255))return Gate::Invalid;idMatches+=id==p.nativeId;
   recordMatches+=read(records+4*j)==p.record;
  }
  int currentLivery{};
  if(!Check(rootMatches==1,detail,"player root membership",p.root,rootMatches,1) ||
     !Check(idMatches==1,detail,"player ID membership",p.identity,idMatches,1) ||
     !Check(recordMatches==1,detail,"player record membership",p.record,recordMatches,1) ||
     !Check(read(p.record+0xC)==p.root,detail,"record root backlink",p.record+0xC,read(p.record+0xC),p.root) ||
     !Check(read(p.record+0x10)==p.identity,detail,"record identity backlink",p.record+0x10,read(p.record+0x10),p.identity))return Gate::Invalid;
  const U32 identity=native_components::Find(read,p.root,at(0x149077C));
  if(!Check(identity==p.identity,detail,"player identity component",p.root,identity,p.identity) ||
     !Check(read(p.identity)==at(0x1334B8C),detail,"player identity table",p.identity,read(p.identity),at(0x1334B8C)) ||
     !Check((read(p.identity+4)&255)==p.nativeId,detail,"player native ID byte",p.identity+4,read(p.identity+4)&255,p.nativeId))return Gate::Invalid;
  const U32 vehicle=native_components::Find(read,p.root,at(0x1490784));
  if(!Check(vehicle==p.vehicle,detail,"player vehicle component",p.root,vehicle,p.vehicle) ||
     !Check(livery(p.vehicle,currentLivery),detail,"protected livery read",p.vehicle,0,0) ||
     !Check(currentLivery==p.livery,detail,"protected livery changed",p.vehicle,static_cast<U32>(currentLivery),static_cast<U32>(p.livery)) ||
     !State(read,b.imageBase,p.root,b.session,false,out.playerState[i],detail) ||
     !Check(Allowed(out.playerState[i],p.root==b.local,afterAck),detail,"player state outside audited progress",p.root,out.playerState[i],0))return Gate::Invalid;
  if(p.root==b.local)++localCount;
 }
 if(!Check(localCount==1,detail,"local player coverage") || !State(read,b.imageBase,b.tree,b.session,true,out.sessionState,detail))return Gate::Invalid;
 // Before our acknowledgement another genuine peer can move the session BBC->BE0.
 // BD0 already passed the real loading barrier and is not an initial publish gate.
 if(!afterAck && out.sessionState==0x1490BD0){Check(false,detail,"session already passed initial barrier");return Gate::Invalid;}
 return Check(snapshot.Stable(),detail,"snapshot changed or unreadable")?Gate::Ready:Gate::Changed;
}
}
