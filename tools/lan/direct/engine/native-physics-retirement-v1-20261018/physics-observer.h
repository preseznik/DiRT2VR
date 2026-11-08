#pragma once
#include "cloth-observer.h"
namespace physics_observation {
using U32=std::uint32_t;
struct Owners {
 U32 game{},stage1{},stage7{},facade{},world{},task{},cars{},cloth{},jobs{},count{},array{},modelCount{};
 U32 models[24]{},handlingEvents[24]{},handlingHandles[24]{},clothEvents[64]{},clothHandles[64]{};
};
inline bool Same(const Owners&a,const Owners&b){return std::memcmp(&a,&b,sizeof(a))==0;}
template<class Read>struct Inspector:cloth_observation::Inspector<Read>{
 using Base=cloth_observation::Inspector<Read>;
 Inspector(Read read,U32 imageBase):Base{read,imageBase,{}}{}
 bool Capture(const cloth_observation::Owners&c,Owners&s){
  s={};s.game=c.game;s.stage1=c.game+0x20B8;s.stage7=c.game+0x2120;s.facade=c.facade;s.cloth=c.cloth;
  s.world=this->Word(s.facade+4);s.task=this->Word(s.stage1+0x1C);s.cars=this->Word(s.game+0x222C);
  if(!this->Eq(s.stage1,this->At(0x1323CAC))||!this->Eq(s.stage1+4,s.stage7)||!this->Eq(s.stage1+8,s.facade)||!this->Eq(s.stage1+0x20,s.cars)||!this->Eq(s.stage7,this->At(0x1323D74))||!this->Eq(s.stage7+4,s.game)||!this->Eq(s.stage7+0x28,s.facade)||!this->Eq(s.stage7+0x2C,s.cloth)||!cloth_observation::Ptr(s.world,0x7C8)||!cloth_observation::Ptr(s.task)||!cloth_observation::Ptr(s.cars))return false;
  s.modelCount=this->Word(this->At(0x168B5BC));
  if(!s.modelCount||s.modelCount>24||!this->Eq(this->At(0x168B840),24))return this->Fail(cloth_observation::Gate::Owner,this->At(0x168B5BC),s.modelCount,24);
  for(U32 i=0;i<24;++i){
   const U32 job=this->At(0x168B600)+i*0x14;
   s.handlingEvents[i]=this->Word(this->At(0x168B7E0)+i*4);s.handlingHandles[i]=this->Word(job+0x10);
   if(!this->Eq(job,this->At(0x1399964))||s.handlingEvents[i]!=job+12||!this->Eq(job+12,this->At(0x13C88A8))||!s.handlingHandles[i]||s.handlingHandles[i]==0xffffffffu)return this->Fail(cloth_observation::Gate::Owner,job,0,1);
   for(U32 j=0;j<i;++j)if(s.handlingHandles[j]==s.handlingHandles[i])return this->Fail(cloth_observation::Gate::Owner,job+16,s.handlingHandles[i],0);
   if(i<s.modelCount){s.models[i]=this->Word(this->At(0x168B530)+i*4);if(!cloth_observation::Ptr(s.models[i],0x1184))return this->Fail(cloth_observation::Gate::Owner,this->At(0x168B530)+i*4,s.models[i],1);for(U32 j=0;j<i;++j)if(s.models[j]==s.models[i])return this->Fail(cloth_observation::Gate::Owner,this->At(0x168B530)+i*4,s.models[i],0);}
  }
  s.jobs=this->Word(s.cloth+0x20);s.count=this->Word(s.cloth+0x34);s.array=s.cloth+0x48;
  if(s.count>64||!this->Eq(s.cloth+0x148,s.count)||(s.count&&!cloth_observation::Ptr(s.jobs,s.count*0x90)))return this->Fail(cloth_observation::Gate::Owner,s.cloth+0x34,s.count,64);
  for(U32 i=0;i<s.count;++i){const U32 job=s.jobs+i*0x90;s.clothEvents[i]=this->Word(s.array+i*4);s.clothHandles[i]=this->Word(job+8);
   if(!this->Eq(job,this->At(0x1398A94))||s.clothEvents[i]!=job+4||!this->Eq(job+4,this->At(0x13C88A8))||!s.clothHandles[i]||s.clothHandles[i]==0xffffffffu)return this->Fail(cloth_observation::Gate::Owner,job,0,1);
  }
  return this->fault.gate==cloth_observation::Gate::Observed;
 }
};
struct Receipt {
 U32 sequence{},stage1Finished{},stage7Finished{},serial{},thread{},active1{},active7{},handlingCalls{},handlingResult{},predicateCalls{},found{},aggregateCalls{},aggregateResult{},bad{};
};
inline bool Complete(const Receipt&r,U32 serial){return r.sequence&&r.stage1Finished==r.sequence&&r.stage7Finished==r.sequence&&serial&&r.serial==serial&&r.thread&&!r.active1&&!r.active7&&r.handlingCalls==1&&!r.handlingResult&&r.predicateCalls==1&&r.found<=1&&r.aggregateCalls==r.found&&!r.aggregateResult&&!r.bad;}
inline bool PredicateValid(const cloth_observation::Owners&o,U32 found,U32 count,U32 index,U32 deltaBits){
 if(found>1||!o.kindCount||o.kindCount>10||count!=o.kindCount||!deltaBits||deltaBits>=0x7f800000u)return false;
 bool present=false;for(U32 i=0;i<o.kindCount;++i)present=present||o.kinds[i]==0;
 return found==U32(present)&&index==count+U32(present);
}
struct StageToken {U32 sequence{},thread{};bool valid{};};
struct Result {cloth_observation::Gate gate{cloth_observation::Gate::Pending};Receipt receipt{};U32 epoch{},unbound{},modelCount{},clothCount{};bool observed{},all_workers_retired{},construction_permitted{};cloth_observation::Fault fault{};};
}
