#pragma once
#include <cstdint>
#include <cstring>
#include <cmath>
#include "native-activation-code-guards-v1.h"

// Compile-only standard RX start candidate. No default completion proof, hook,
// service scheduler, asset loader, green event or frontend calls are supplied.
namespace native_activation {
using U32=std::uint32_t; using U64=std::uint64_t;
inline bool Pointer(U32 p,U32 bytes=4){return bytes && p>=0x10000 && p<=0x80000000u-bytes;}
enum class Phase { Inspect, QueueRender, BeginTimeline, TickTimeline, ObserveGreen };
enum class Status { Ready, WaitForRender, Running, TimelineComplete, Rejected, AlreadyStarted, DuplicateFrame };
enum class Stage { New, RenderQueued, Running, Ticking, Complete, Failed };
struct Context {
 U32 imageBase{},game{},renderOwner{},ownerThread{},localEntry{},selectedLivery{};
 U64 transaction{};
};
struct Plan {
 U32 dispatcher{},callback{},vehicleSystem{},localVehicle{},cameraService{},camera{};
 U32 manager{},wrapper{},resource{},frames{},renderer{},currentCamera{},renderMode{};
 U32 frameStatus[4]{}; double timelineTime{};bool lightsFlag{};
};
struct Attempt {
 U64 transaction{},lastFrame{};Stage stage{Stage::New};Plan pinned{};
 bool naturalGreenObserved{};U64 greenFrame{};U32 nativeTickStatus{};
};

// Read-only bounded snapshot. Read(address,out) reports failure explicitly.
template<class Read> class Snapshot {
 Read& source;struct Word{U32 address,value;};Word words[8192]{};U32 count{};
public:
 bool valid{true};explicit Snapshot(Read& r):source(r){}
 U32 operator()(U32 address){
  U32 value{};if(!Pointer(address)||!source(address,value)){valid=false;return 0;}
  for(U32 i=0;i<count;++i)if(words[i].address==address){if(words[i].value!=value)valid=false;return value;}
  if(count==8192){valid=false;return value;}words[count++]={address,value};return value;
 }
 U32 Byte(U32 address){return ((*this)(address&~3u)>>((address&3u)*8))&255u;}
 double Double(U32 address){U64 raw=(*this)(address);raw|=U64((*this)(address+4))<<32;double d{};std::memcpy(&d,&raw,8);return d;}
 bool String(U32 address,char* out,U32 capacity){
  if(!Pointer(address,capacity))return false;
  for(U32 i=0;i<capacity;++i){out[i]=static_cast<char>(Byte(address+i));if(!out[i])return valid;}
  return false;
 }
 bool Stable(){if(!valid)return false;for(U32 i=0;i<count;++i){U32 v{};if(!source(words[i].address,v)||v!=words[i].value)return false;}return true;}
};
inline U32 NameHash(const char* value){U32 h=0x1505;for(;*value;++value)h=h*33u+static_cast<std::int8_t>(*value);return h;}

template<class Read> bool RegisteredGreen(Snapshot<Read>& read,U32 base,U32 dispatcher,U32 game,U32& callback){
 callback=0;const U32 map=dispatcher+0x148;
 if(!Pointer(dispatcher,0x1A4)||!read.Byte(dispatcher+0x38))return false;
 const U32 begin=read(map+0x38),end=read(map+0x3C);
 if(!Pointer(begin,8)||end<begin||(end-begin)%4)return false;
 const U32 length=(end-begin)/4;
 if(length<2||length>4097||!Pointer(begin,length*4))return false;
 constexpr char key[]="StartLightsMessage";
 const U32 bucket=NameHash(key)%(length-1),stop=read(begin+4*bucket+4);
 U32 node=read(begin+4*bucket),seen[512]{},matches{};bool passed{};
 for(U32 i=0;node!=stop;++i){
  if(i==512||!Pointer(node,16))return false;
  for(U32 j=0;j<i;++j)if(seen[j]==node)return false;
  seen[i]=node;char name[96]{};
  if(!read.String(read(node+8),name,sizeof(name)))return false;
  const int order=std::strcmp(name,key);
  if(order>0)passed=true;
  if(order==0){
   // A duplicate equivalent key is not accepted, even if callbacks agree.
   if(passed||++matches!=1)return false;
   callback=read(node+12);
   if(!Pointer(callback,8)||read(callback)!=base+0x74FA90-0x400000||read(callback+4)!=game)return false;
  }else if(order<0 && (passed||matches))return false;
  node=read(node+4);
 }
 return matches==1 && read.valid;
}

template<class Read> Status Inspect(Read source,const Context& c,Plan& out){
 out={};if(!c.transaction||c.imageBase<0x10000||c.imageBase>0x60000000||
  !Pointer(c.game,0x2E3C)||!Pointer(c.renderOwner,0x1880)||!Pointer(c.localEntry)||
  !c.ownerThread||!c.selectedLivery)return Status::Rejected;
 const auto at=[&](U32 p){return c.imageBase+p-0x400000u;};Snapshot<Read> r(source);Plan p{};
 p.dispatcher=r(c.game+0x2098);
 if(!RegisteredGreen(r,c.imageBase,p.dispatcher,c.game,p.callback))return Status::Rejected;
 p.vehicleSystem=r(c.game+0x222C);p.cameraService=r(c.game+0x22F4);
 if(!Pointer(p.vehicleSystem,0x164)||!Pointer(p.cameraService,0x28))return Status::Rejected;
 const U32 begin=r(p.vehicleSystem+0x54),end=r(p.vehicleSystem+0x58),cap=r(p.vehicleSystem+0x5C);
 // This candidate is deliberately restricted to the proved genuine pair.
 if(!Pointer(begin,8)||end!=begin+8||cap<end||cap-begin>32*4)return Status::Rejected;
 U32 localCount{},remoteCount{};
 for(U32 i=0;i<2;++i){
  const U32 car=r(begin+4*i);
  if(!Pointer(car,0x8450)||r(car)!=at(0x1328788)||r(car+0x5564)!=i||r(car+0x55A0)!=p.vehicleSystem||!Pointer(r(car+0x30)))return Status::Rejected;
  const U32 role=r(car+0x5588);
  if(role==0){
   ++localCount;p.localVehicle=car;
   if(r(car+0x5584)!=c.localEntry||r(car+0x558C)!=c.selectedLivery)return Status::Rejected;
   p.camera=r(car+0x8384);if(!Pointer(p.camera,0x124))return Status::Rejected;r(p.camera+0x120);
  }else if(role==1)++remoteCount;else return Status::Rejected;
 }
 if(localCount!=1||remoteCount!=1)return Status::Rejected;
 p.currentCamera=r(p.cameraService+0x24);
 if(p.currentCamera){if(!Pointer(p.currentCamera,0x124))return Status::Rejected;r(p.currentCamera+0x120);}
 p.renderer=r(c.renderOwner+0x187C);if(!Pointer(p.renderer,0x1184))return Status::Rejected;
 p.renderMode=r(p.renderer+0x1180);
 p.manager=r(c.game+0x2E34);
 if(!Pointer(p.manager,0x190)||r(p.manager)!=at(0x132982C)||!Pointer(r(c.game+0x2E38))||r(p.manager+0xC)!=r(c.game+0x2E38))return Status::Rejected;
 const U32 catalog=r(p.manager+0x10),capacity=r(p.manager+0x14),count=r(p.manager+0x18);
 if(!count||count>capacity||capacity>128||!Pointer(catalog,capacity*0x24))return Status::Rejected;
 for(U32 i=0;i<count;++i){char name[32]{};if(!r.String(catalog+0x24*i,name,sizeof(name)))return Status::Rejected;
  if(std::strcmp(name,"startsequence")==0){if(p.wrapper)return Status::Rejected;p.wrapper=r(catalog+0x24*i+0x20);}}
 if(!Pointer(p.wrapper,12)||r(p.wrapper)!=at(0x13287A8))return Status::Rejected;
 p.resource=r(p.wrapper+8);if(!Pointer(p.resource,0x20))return Status::Rejected;
 p.frames=r(p.resource+8);
 if(!Pointer(p.frames,4*0x18)||r(p.resource+0xC)!=p.frames+4*0x18)return Status::Rejected;
 constexpr U32 eventCounts[]{3,1,5,1};
 for(U32 i=0;i<4;++i){
  const U32 frame=r(p.frames+i*0x18),events=r(frame+0x10),eventEnd=r(frame+0x14);
  if(!Pointer(frame,0x68)||!Pointer(events,eventCounts[i]*4)||eventEnd!=events+eventCounts[i]*4)return Status::Rejected;
  for(U32 j=0;j<eventCounts[i];++j)if(!Pointer(r(events+j*4)))return Status::Rejected;
  // Exact shipped relative chain, not just a nonempty timeline.
  const U32 relative=r(p.frames+i*0x18+0x14);
  if((i?relative!=i-1:static_cast<std::int32_t>(relative)>=0)||r.Double(frame+0x60)!=0.0)return Status::Rejected;
  p.frameStatus[i]=r(p.frames+i*0x18+0x10);if(p.frameStatus[i]>2)return Status::Rejected;
 }
 p.timelineTime=r.Double(p.resource+0x18);
 p.lightsFlag=r.Byte(at(0x148E9F0))!=0;
 if(!std::isfinite(p.timelineTime)||p.timelineTime<0||!r.Stable())return Status::Rejected;
 out=p;return Status::Ready;
}
inline bool SameObjects(const Plan& a,const Plan& b){
 return a.dispatcher==b.dispatcher&&a.callback==b.callback&&a.vehicleSystem==b.vehicleSystem&&
 a.localVehicle==b.localVehicle&&a.cameraService==b.cameraService&&a.camera==b.camera&&
 a.manager==b.manager&&a.wrapper==b.wrapper&&a.resource==b.resource&&a.frames==b.frames&&a.renderer==b.renderer;
}

// Proof(context,phase,frame,dt) MUST establish all obligations in findings.md,
// including event7 success, native barrier, original hash, safe owner/thread,
// real loaded graph, exact timeline plugins, services, profile false predicate,
// exclusive timeline consumer, and genuine update delta. There is no fallback.
template<class Read,class Equal,class Proof,class Native>
Status QueueRendering(Read read,Equal equal,Proof proof,Native& native,const Context& c,U32 thread,Attempt& a){
 if(a.stage!=Stage::New)return Status::AlreadyStarted;
 if(thread!=c.ownerThread||!proof(c,Phase::QueueRender,0,0)||!CodeSignatures(equal,c.imageBase))return Status::Rejected;
 Plan p{};if(Inspect(read,c,p)!=Status::Ready||p.lightsFlag)return Status::Rejected;
 if(!proof(c,Phase::QueueRender,0,0))return Status::Rejected;
 a.transaction=c.transaction;a.pinned=p;a.stage=Stage::Failed; // terminal on partial native failure
 if(native.Lookup(c.imageBase,p.manager)!=p.wrapper||native.Local(c.imageBase,p.vehicleSystem)!=p.localVehicle||
    !native.QueueRender(c.imageBase,c.renderOwner))return Status::Rejected;
 a.stage=Stage::RenderQueued;return Status::WaitForRender;
}
template<class Read,class Equal,class Proof,class Native>
Status Begin(Read read,Equal equal,Proof proof,Native& native,const Context& c,U32 thread,Attempt& a){
 if(a.stage!=Stage::RenderQueued||a.transaction!=c.transaction)return Status::Rejected;
 if(thread!=c.ownerThread||!proof(c,Phase::BeginTimeline,0,0)||!CodeSignatures(equal,c.imageBase)){a.stage=Stage::Failed;return Status::Rejected;}
 Plan p{};if(Inspect(read,c,p)!=Status::Ready||!SameObjects(p,a.pinned)||p.lightsFlag){a.stage=Stage::Failed;return Status::Rejected;}
 if(p.renderMode!=0)return Status::WaitForRender;
 a.stage=Stage::Failed;
 if(p.currentCamera!=p.camera)native.Camera(c.imageBase,c.game,p.camera);
 Plan after{};if(Inspect(read,c,after)!=Status::Ready||!SameObjects(p,after)||after.currentCamera!=p.camera||
  !proof(c,Phase::BeginTimeline,0,0))return Status::Rejected;
 native.Reset(c.imageBase,p.wrapper);
 if(Inspect(read,c,after)!=Status::Ready||!SameObjects(p,after)||after.timelineTime!=0)return Status::Rejected;
 for(U32 status:after.frameStatus)if(status!=0)return Status::Rejected;
 a.stage=Stage::Running;return Status::Running;
}
template<class Read,class Equal,class Proof,class Native>
Status Tick(Read read,Equal equal,Proof proof,Native& native,const Context& c,U32 thread,U64 frame,double dt,Attempt& a){
 if(a.stage!=Stage::Running||a.transaction!=c.transaction)return Status::Rejected;
 if(frame&&frame==a.lastFrame)return Status::DuplicateFrame;
 // 250ms is this diagnostic's rejection policy, not a native engine limit.
 if(!frame||frame<a.lastFrame||thread!=c.ownerThread||!std::isfinite(dt)||dt<0||dt>0.25||
  !proof(c,Phase::TickTimeline,frame,dt)||!CodeSignatures(equal,c.imageBase)){a.stage=Stage::Failed;return Status::Rejected;}
 Plan p{};if(Inspect(read,c,p)!=Status::Ready||!SameObjects(p,a.pinned)||p.renderMode!=0||p.currentCamera!=p.camera){a.stage=Stage::Failed;return Status::Rejected;}
 a.lastFrame=frame;a.stage=Stage::Ticking;
 const U32 result=native.Tick(c.imageBase,p.wrapper,dt);a.nativeTickStatus=result;a.stage=Stage::Failed;
 Plan after{};if((result!=1&&result!=2)||Inspect(read,c,after)!=Status::Ready||!SameObjects(p,after)||
  after.timelineTime!=p.timelineTime+dt)return Status::Rejected;
 if(result==2){
  for(U32 status:after.frameStatus)if(status!=2)return Status::Rejected;
  if(!a.naturalGreenObserved)return Status::Rejected;
  a.stage=Stage::Complete;return Status::TimelineComplete;
 }
 a.stage=Stage::Running;return Status::Running;
}

// Observation only: caller has already observed the actual native handler's
// return on the owner task, preserving this message before its lifetime ends.
// Does NOT call the handler, publish event1C, set lights, or prove drivability.
template<class Read,class Equal,class Proof>
bool ObserveNativeGreen(Read source,Equal equal,Proof proof,const Context& c,U32 thread,U64 frame,U32 handler,U32 owner,U32 message,Attempt& a){
 if((a.stage!=Stage::Running&&a.stage!=Stage::Ticking)||a.transaction!=c.transaction||a.naturalGreenObserved||!frame||
  thread!=c.ownerThread||owner!=c.game||handler!=c.imageBase+0x74FA90-0x400000||
  !proof(c,Phase::ObserveGreen,frame,0)||!CodeSignatures(equal,c.imageBase))return false;
 Snapshot<Read> r(source);
 if(!Pointer(message,16)||r(message)!=c.imageBase+0x13280B4-0x400000||r(message+8)!=1||r.Byte(message+0xE)!=0||!r.Stable())return false;
 a.naturalGreenObserved=true;a.greenFrame=frame;return true;
}
struct ControlSnapshot {U32 vehicle{},model{},camera{},mode{},humanOwner{};U32 restriction[7]{};double timelineTime{};bool greenFlag{},released{};};
template<class Read> bool ReadControls(Read source,const Context& c,ControlSnapshot& out){
 out={};Plan p{};if(Inspect(source,c,p)!=Status::Ready)return false;Snapshot<Read> r(source);ControlSnapshot s{};
 s.vehicle=p.localVehicle;s.model=r(s.vehicle+0x30);s.camera=r(p.cameraService+0x24);
 s.mode=r(p.vehicleSystem+0x15C);s.humanOwner=r(p.vehicleSystem+0x160);s.timelineTime=p.timelineTime;
 s.greenFlag=r.Byte(c.imageBase+0x148E9F0-0x400000)!=0;
 const U32 controls=r(s.vehicle+0x8438);if(!Pointer(controls,0x24))return false;
 constexpr U32 offsets[]{0,1,2,3,4,5,0x21};bool clear=true;
 for(U32 i=0;i<7;++i){s.restriction[i]=r.Byte(controls+offsets[i]);clear=clear&&s.restriction[i]==0;}
 s.released=clear&&s.mode==4&&s.humanOwner==0&&s.camera==p.camera;
 if(!r.Stable())return false;out=s;return true;
}
}
