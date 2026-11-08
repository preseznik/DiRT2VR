#pragma once
#include "../direct-bootstrap/native-vehicle-physics-guard-v1.h"
namespace native_registration_prerequisites_v2 {
using U32=std::uint32_t;
using Gate=native_vehicle_physics_v1::Gate;
enum class Reason { None, Physics, Unreadable, CameraMissing, CameraOwner, CameraLayout, CameraViews, CameraBacklink, CurrentCameraOther, Changed };
enum class Field { None, PhysicalGraph, LocalCar, CameraService, Camera, Factory, Allocator, CameraAllocator, CameraSecondaryAllocator, BodyBacklink, ViewBegin, ViewEnd, ViewCapacity, ViewCount, View, ViewTransform, ViewBody, ViewAuxiliary, CurrentCamera, CameraFlags, SnapshotWord };
enum class Rule { None, Equal, Pointer, Range, Readable, Stable, Nonzero };
struct Failure { Field field{};Rule rule{};U32 address{},actual{},expected{}; };
struct Snapshot {
 native_vehicle_physics_v1::Snapshot physics{};
 U32 localCar{},localEntry{},localModel{},cameraService{},camera{},currentCamera{},factory{},allocator{},viewCount{};
 Reason reason{};Failure failure{};
};
// Passive pre-registration eligibility for the isolated two-car load.
// Retain the caller's configuration/settings/audio/finalizer/task guards.
// No camera constructor callback words are invariants: B3B529 clears them.
template<class Read> Gate Inspect(Read source,U32 imageBase,U32 game,U32 expectedSystem,
 const U32(&entries)[2],const U32(&liveries)[2],Snapshot& out) {
 out={};struct Word{U32 address,value;};Word words[320]{};unsigned count{};bool unreadable{},changed{};
 const auto ptr=[](U32 p,U32 n=4){return native_vehicle_physics_v1::Pointer(p,n);};
 const auto fail=[&](Reason why,Field field,Rule rule,U32 address,U32 actual,U32 expected){
  if(out.failure.field==Field::None)out.failure={field,rule,address,actual,expected};
  out.reason=unreadable?Reason::Unreadable:why;return Gate::Reject;
 };
 const auto read=[&](U32 a,U32& value){
  if(!ptr(a)||!source(a,value)){unreadable=true;fail(Reason::Unreadable,Field::SnapshotWord,Rule::Readable,a,0,1);return false;}
  for(unsigned i=0;i<count;++i)if(words[i].address==a){if(words[i].value!=value){changed=true;if(out.failure.field==Field::None)out.failure={Field::SnapshotWord,Rule::Stable,a,value,words[i].value};}return true;}
  if(count==320){unreadable=true;fail(Reason::Unreadable,Field::SnapshotWord,Rule::Range,a,count,319);return false;}
  words[count++]={a,value};return true;
 };
 const auto r=[&](U32 a){U32 v{};read(a,v);return v;};
 const auto eq=[&](U32 address,U32 expected,Reason why,Field field){const U32 actual=r(address);if(actual!=expected||unreadable){fail(why,field,Rule::Equal,address,actual,expected);return false;}return true;};
 const Gate physical=native_vehicle_physics_v1::Inspect(read,imageBase,game,expectedSystem,entries,liveries,out.physics);
 if(physical!=Gate::Ready){out.reason=unreadable?Reason::Unreadable:Reason::Physics;if(out.failure.field==Field::None)out.failure={Field::PhysicalGraph,Rule::None,0,static_cast<U32>(physical),static_cast<U32>(Gate::Ready)};return physical;}
 for(unsigned i=0;i<2;++i)if(r(out.physics.car[i]+0x5588)==0){out.localCar=out.physics.car[i];out.localModel=out.physics.model[i];out.localEntry=r(out.localCar+0x5584);}
 if(!out.localCar)return fail(Reason::Physics,Field::LocalCar,Rule::Nonzero,0,0,1);
 out.cameraService=r(game+0x22F4);out.camera=r(out.localCar+0x8384);
 if(!out.camera&&!unreadable){out.reason=Reason::CameraMissing;out.failure={Field::Camera,Rule::Nonzero,out.localCar+0x8384,0,1};return Gate::Pending;}
 if(!ptr(out.cameraService,0x28))return fail(Reason::CameraOwner,Field::CameraService,Rule::Pointer,game+0x22F4,out.cameraService,0x28);
 if(!ptr(out.camera,0x5B0))return fail(Reason::CameraOwner,Field::Camera,Rule::Pointer,out.localCar+0x8384,out.camera,0x5B0);
 out.factory=r(out.localCar+0x8380);
 if(!eq(out.localCar+0x8380,expectedSystem+0x168,Reason::CameraOwner,Field::Factory))return Gate::Reject;
 out.allocator=r(out.factory+4);
 if(!ptr(out.allocator))return fail(Reason::CameraOwner,Field::Allocator,Rule::Pointer,out.factory+4,out.allocator,4);
 if(!eq(out.camera,out.allocator,Reason::CameraOwner,Field::CameraAllocator)||!eq(out.camera+0x4E0,out.allocator,Reason::CameraOwner,Field::CameraSecondaryAllocator))return Gate::Reject;
 const U32 body=r(out.localCar+0x5598),auxiliary=r(out.camera+0x130);
 if(!eq(out.camera+0x12C,body,Reason::CameraBacklink,Field::BodyBacklink))return Gate::Reject;
 const U32 begin=r(out.camera+4),end=r(out.camera+8),capacity=r(out.camera+0xC);
 if(!ptr(begin))return fail(Reason::CameraViews,Field::ViewBegin,Rule::Pointer,out.camera+4,begin,4);
 if(end<=begin||(end-begin)%0x270)return fail(Reason::CameraViews,Field::ViewEnd,Rule::Range,out.camera+8,end,begin+0x270);
 if(capacity<end||(capacity-begin)%0x270)return fail(Reason::CameraViews,Field::ViewCapacity,Rule::Range,out.camera+0xC,capacity,end);
 out.viewCount=(end-begin)/0x270;
 // A bounded diagnostic policy, not an engine maximum.
 if(out.viewCount>64||std::uint64_t(capacity)-begin>64ull*0x270)return fail(Reason::CameraViews,Field::ViewCount,Rule::Range,out.camera+8,out.viewCount,64);
 for(U32 i=0;i<out.viewCount;++i){const U32 record=begin+i*0x270,view=r(record);
  if(!ptr(view,0x118))return fail(Reason::CameraBacklink,Field::View,Rule::Pointer,record,view,0x118);
  if(!eq(view+0x74,out.localCar+0x81D0,Reason::CameraBacklink,Field::ViewTransform)||!eq(view+0x100,body,Reason::CameraBacklink,Field::ViewBody)||!eq(view+0x114,auxiliary,Reason::CameraBacklink,Field::ViewAuxiliary))return Gate::Reject;
 }
 out.currentCamera=r(out.cameraService+0x24);
 // Restrict the probe to no previous camera or this exact owned local set.
 if(out.currentCamera&&out.currentCamera!=out.camera)return fail(Reason::CurrentCameraOther,Field::CurrentCamera,Rule::Equal,out.cameraService+0x24,out.currentCamera,out.camera);
 r(out.camera+0x120);
 if(unreadable)return Gate::Reject;
 for(unsigned i=0;i<count;++i){U32 v{};if(!source(words[i].address,v))return fail(Reason::Unreadable,Field::SnapshotWord,Rule::Readable,words[i].address,0,1);
  if(v!=words[i].value){changed=true;if(out.failure.field==Field::None)out.failure={Field::SnapshotWord,Rule::Stable,words[i].address,v,words[i].value};}}
 if(changed){out.reason=Reason::Changed;return Gate::Pending;}
 return Gate::Ready;
}
}
