#pragma once
#include "../native-activation-candidate-20261018/native-activation-v1.h"
namespace native_timeline_plugins_v1 {
using U32=std::uint32_t;
enum class Gate {Ready, Address, Layout, Class, Option, Owner, Service, Unstable};
struct Failure {U32 address{},actual{},expected{};};
struct Graph {
 U32 wrapper{},resource{},frames{},events[10]{},lights{},lightDispatcher{},lightCount{},lightStage{};
 U32 audioObject{},audioManager{},audioDispatcher{},updateDispatcher{},aiOwner{},aiController{};
 U32 frameStatus[4]{};double time{};Failure failure{};
};
struct Class {const char* name;U32 table,slots[6];};
constexpr Class Classes[]{
 {"VehicleState",0x13287FC,{0x7218D0,0x7218E0,0x7218F0,0x721900,0x72D6E0,0x6F8080}},
 {"Replay",0x132832C,{0x71EA90,0x71EB70,0x744E40,0x71EBE0,0x727520,0x6F8080}},
 {"OSD",0x13280E4,{0x71E940,0x71EA20,0x727320,0x71EA30,0x732600,0x6F8080}},
 {"WaitForNetwork",0x13285B8,{0x721A00,0x750C40,0x721A10,0x721A20,0x721A30,0x6F8080}},
 {"StartLights",0x13320D8,{0x80AB90,0x80ACF0,0x80BB50,0x80AD00,0x80BC80,0x6F8080}},
 {"Controller",0x13287AC,{0x721580,0x721740,0x721750,0x721880,0x72D6B0,0x6F8080}},
 {"AI",0x1324614,{0x6D4500,0x6CB500,0x6CB5C0,0x6CB660,0x6D4670,0x6F8080}},
 {"WaitForLights",0x1328828,{0x721950,0x721960,0x721980,0x721990,0x72D730,0x6F8080}},
 {"StartFlag",0x1328C18,{0x725730,0x725750,0x736A40,0x736AB0,0x730660,0x6F8080}}};

// Pure read-only graph. This is not a permit to reset/tick: the caller must also
// prove named service bindings, native frame ownership, barrier and code identity.
template<class Read> Gate Inspect(Read source,U32 imageBase,U32 game,U32 wrapper,Graph& g){
 g={};g.wrapper=wrapper;
 if(imageBase<0x10000||imageBase>0x60000000||!native_activation::Pointer(game,0x2EC4)||!native_activation::Pointer(wrapper,12))return Gate::Address;
 const auto at=[=](U32 va){return imageBase+va-0x400000u;};native_activation::Snapshot<Read> r(source);
 const auto eq=[&](U32 p,U32 wanted,bool byte=false){const U32 value=byte?r.Byte(p):r(p);if(value!=wanted&&!g.failure.address)g.failure={p,value,wanted};return value==wanted&&r.valid;};
 const auto ptr=[&](U32 p,U32 span=4){if(!native_activation::Pointer(p,span)){if(!g.failure.address)g.failure={p,p,span};return false;}return true;};
 if(!eq(wrapper,at(0x13287A8)))return Gate::Class;
 g.resource=r(wrapper+8);if(!ptr(g.resource,0x20))return Gate::Layout;g.frames=r(g.resource+8);
 if(!ptr(g.frames,0x60)||!eq(g.resource+12,g.frames+0x60))return Gate::Layout;
 const U32 network=r(game+0x2CF0),osd=r(game+0x2248),textOwner=r(game+0x2D9C),staggered=r(game+0x2EC0);
 g.aiOwner=r(game+0x2D90);g.lights=r(at(0x15C4258));g.audioObject=r(game+0x2274);g.audioManager=r(game+0x2278);g.updateDispatcher=r(game+0x2098);
 if(!ptr(network)||!ptr(osd)||!ptr(g.aiOwner,0x44)||!ptr(g.lights,0x230)||!ptr(g.audioObject,0xC4)||!ptr(g.audioManager)||!ptr(g.updateDispatcher,0x1BC))return Gate::Owner;
 if(!eq(at(0x15C41C0),at(0x13388AC))||!eq(g.lights,at(0x1332360))||!eq(at(0x1332360)+0x58,at(0x80AD10))||
    !eq(g.audioObject,at(0x12EEB48))||!eq(g.audioObject+0x94,g.audioManager))return Gate::Owner;
 g.lightDispatcher=g.lights+0x60;g.audioDispatcher=r(g.audioObject+0x90);
 // Native constructors118F970/118FC90 install this same final dispatcher type.
 if(!ptr(g.audioDispatcher,0x1BC)||!eq(g.audioDispatcher,at(0x13CD1E8))||!eq(g.lightDispatcher,at(0x13CD1E8))||
    !eq(g.audioDispatcher+0x38,1,true)||!eq(g.lightDispatcher+0x38,1,true))return Gate::Service;
 g.lightCount=r(g.lights+0x14);g.lightStage=r(g.lights+0x58);
 if(!g.lightCount||g.lightCount>64||(g.lightStage!=0xFFFFFFFFu&&g.lightStage>=g.lightCount))return Gate::Layout;
 const U32 aiBody=r(g.aiOwner+0x40);if(aiBody&&!ptr(aiBody,0xB04))return Gate::Owner;g.aiController=aiBody?aiBody+0xB00:0;
 constexpr U32 counts[]{3,1,5,1},indices[]{0,1,2,3,4,5,6,5,7,8};U32 n{};
 for(U32 fi=0;fi<4;++fi){
  const U32 record=g.frames+fi*0x18,frame=r(record);if(!ptr(frame,0x68))return Gate::Layout;
  const U32 list=r(frame+0x10);if(!ptr(list,4*counts[fi])||!eq(frame+0x14,list+4*counts[fi]))return Gate::Layout;
  const U32 relative=r(record+0x14);if((fi?relative!=fi-1:static_cast<std::int32_t>(relative)>=0)||r.Double(frame+0x60)!=0)return Gate::Layout;
  g.frameStatus[fi]=r(record+0x10);if(g.frameStatus[fi]>2)return Gate::Layout;
  for(U32 ei=0;ei<counts[fi];++ei,++n){
   const U32 p=r(list+ei*4),ci=indices[n];if(!ptr(p,0x44))return Gate::Layout;
   for(U32 j=0;j<n;++j)if(g.events[j]==p)return Gate::Layout;g.events[n]=p;
   const auto& c=Classes[ci];if(!eq(p,at(c.table)))return Gate::Class;
   for(U32 s=0;s<6;++s)if(!eq(at(c.table)+4*(s+1),at(c.slots[s])))return Gate::Class;
   char name[64]{};if(!r.String(p+4,name,64)||std::strcmp(name,c.name))return Gate::Class;
   if((ci==0||ci==1||ci==5)&&!eq(p+0x44,game))return Gate::Owner;
   if(ci==1){if(!eq(p+0x48,0,true)||!eq(p+0x50,1,true)||!eq(p+0x54,1)||!ptr(r(game+0x2300)))return Gate::Option;}
   if(ci==2){char layout[64]{};if(!eq(p+0x44,1,true)||!eq(p+0x45,1,true)||!eq(p+0x46,0,true)||
      !eq(p+0xC8,osd)||!eq(p+0xCC,textOwner)||!r.String(p+0x87,layout,64)||std::strcmp(layout,"race"))return Gate::Option;}
   if(ci==3&&!eq(p+0x44,network))return Gate::Owner;
   if(ci==4){for(U32 off=0x44;off<=0x49;++off)if(!eq(p+off,off==0x45?1u:0u,true))return Gate::Option;
    if(!eq(p+0x54,0,true)||!eq(p+0x4C,0xBF800000)||!eq(p+0x50,staggered))return Gate::Option;}
   if(ci==5){if(!eq(p+0x48,1,true)||!eq(p+0x49,1,true)||!eq(p+0x4A,1,true)||!eq(p+0x4C,1)||
      !eq(p+0x50,ei==1?1u:0u,true)||!eq(p+0x58,0,true)||(ei==1&&!eq(p+0x54,1))||!ptr(r(game+0x22F4))||!ptr(r(game+0x222C)))return Gate::Option;}
   if(ci==6){if(!eq(p+0x48,g.aiOwner)||!eq(p+0x4C,g.lights)||!eq(p+0x50,g.aiController))return Gate::Owner;
    for(U32 off=0x54;off<=0x58;++off)if(!eq(p+off,off==0x56?1u:0u,true))return Gate::Option;}
   if(ci==8&&!eq(p+0x44,0x41200000))return Gate::Option;
  }
 }
 g.time=r.Double(g.resource+0x18);if(!std::isfinite(g.time)||g.time<0)return Gate::Layout;
 return r.Stable()?Gate::Ready:Gate::Unstable;
}
inline bool BoundLightAndAudio(const Graph& g,U32 lightStrong,U32 audioStrong){
 return g.lightDispatcher&&g.audioDispatcher&&lightStrong==g.lightDispatcher&&audioStrong==g.audioDispatcher&&lightStrong!=audioStrong;
}
}
