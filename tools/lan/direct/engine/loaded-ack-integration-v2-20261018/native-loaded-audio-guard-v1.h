#pragma once
#include "../common-audio-retained-v2-20261018/audio-preparation-guard-v2.h"
#include "../direct-bootstrap/native-vehicle-physics-guard-v1.h"
#include "native-loaded-ack-lan-v2.h"

// Fresh read-only race graph. Historical mode1 retention remains a separate
// requirement in the adapter; ReadyForRace intentionally rejects this later phase.
namespace loaded_audio_v1 {
using U32=std::uint32_t;
namespace graph=audio_preparation_guard_v2;
enum class Gate {Ready,Graph,Owner,Phase,Record,Vehicle,Changed};
struct Snapshot {graph::Snapshot graph{};U32 records[2]{},cars[2]{},configuration[2]{},localCar{};};
template<class Read> Gate Inspect(Read source,U32 imageBase,U32 game,
 const graph::Snapshot& original,const native_vehicle_physics_v1::Snapshot& physical,Snapshot& out) {
 out={};native_loaded_ack::Snapshot<Read> memory(source);
 const auto read=[&](U32 p,U32& v){v=memory(p);return memory.valid;};
 graph::Inspector<decltype(read)> inspect{read,imageBase};
 const auto gate=inspect.Inspect(game,out.graph);
 // Gate::Car is reached only after every shared owner/context/registry/music
 // check. Its last predicate rejects race records because that inspector is
 // dedicated to mode1 preparation. Replace only that phase predicate here.
 if(inspect.fault!=graph::Gate::Ready || gate!=graph::Gate::Car)return Gate::Graph;
 const auto& s=out.graph;
 if(!graph::SameOwner(original,s))return Gate::Owner;
 if(s.stage!=4 || s.count!=2 || s.infoEnabled!=1 || s.infoBound!=1 || s.race!=1 ||
    s.active!=1 || s.initial!=1 || s.fe || s.engineStage || s.uninitMode!=1 ||
    s.setupLoaded!=1 || s.aiLoaded!=1 || s.mode!=3 || s.bankStage!=1 || s.bankMode!=2 || !graph::Idle(s))return Gate::Phase;
 for(U32 pending:s.pending)if(pending)return Gate::Phase;
 unsigned count{},mask{},locals{};
 for(unsigned slot=0;slot<8;++slot) {
  const U32 record=memory(s.manager+0xA780+0x80+4*slot);
  if(!record)continue;
  if(count==2 || !graph::Ptr(record,0x3C) || (count && out.records[0]==record))return Gate::Record;
  // Native audio car records are plain storage. The first DWORD is not a vtable.
  const U32 car=memory(record+0x2C),configuration=memory(record+0x38);
  if(!graph::Ptr(configuration,0x1E0))return Gate::Record;
  unsigned found=2;for(unsigned i=0;i<2;++i)if(physical.car[i]==car)found=i;
  if(found==2 || (mask&(1u<<found)))return Gate::Vehicle;
  mask|=1u<<found;
  if(memory(car+0x5588)==0){out.localCar=car;++locals;}
  out.records[count]=record;out.cars[count]=car;out.configuration[count]=configuration;++count;
 }
 if(count!=2 || mask!=3 || locals!=1 || memory(s.manager+0x14C00)!=out.localCar)return Gate::Vehicle;
 return memory.Stable()?Gate::Ready:Gate::Changed;
}
}
