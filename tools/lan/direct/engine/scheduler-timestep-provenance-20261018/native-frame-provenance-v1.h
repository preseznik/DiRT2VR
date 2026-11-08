#pragma once
#include "../native-activation-candidate-20261018/native-activation-v1.h"
namespace native_frame_provenance_v1 {
using U32=std::uint32_t;using U64=std::uint64_t;
enum class Gate {Ready,Callsite,Owner,Tree,Duplicate,Row,Delta,Changed};
struct Snapshot {U32 scheduler{},header{},count{},row{},maskA{},maskB{},pending{},seen{};double delta{};};
// Run only inside the owned held577DE0 callback. No native calls or memory writes.
template<class Read> Gate InspectHeld(Read source,U32 imageBase,U32 game,U32 frontend,U32 step,U32 caller,Snapshot& out){
 out={};const auto at=[=](U32 va){return imageBase+va-0x400000u;};
 if(imageBase<0x10000||imageBase>0x60000000||caller!=at(0xC2AEE2)||step!=game+0x1010)return Gate::Callsite;
 if(!native_activation::Pointer(game,0x2254)||!native_activation::Pointer(frontend,0x48))return Gate::Owner;
 native_activation::Snapshot<Read> r(source);out.scheduler=game+0x20;
 if(r(game+0x2250)!=frontend||r(frontend)!=at(0x1300B10)||r(at(0x1300B10)+4)!=at(0x577DE0)||
    r(at(0x1300B10)+8)!=at(0x441A10)||r(r(out.scheduler)+4)!=at(0xC2AE70))return Gate::Owner;
 out.header=r(out.scheduler+8);out.count=r(out.scheduler+12);out.maskA=r(out.scheduler+0x10);out.maskB=r(out.scheduler+0x14);out.pending=r(out.scheduler+0x48);
 if(!native_activation::Pointer(out.header,0x3C)||!out.count||out.count>256||out.pending||r.Byte(out.header+0x39)!=1)return Gate::Tree;
 U32 stack[256]{},parents[256]{},seen[256]{},sp=1,n{};stack[0]=r(out.header+4);parents[0]=out.header;
 while(sp){const U32 node=stack[--sp],parent=parents[sp];if(node==out.header)continue;
  if(!native_activation::Pointer(node,0x3C)||n>=out.count||sp+2>256||r.Byte(node+0x39)||r(node+4)!=parent)return Gate::Tree;
  for(U32 i=0;i<n;++i)if(seen[i]==node)return Gate::Tree;seen[n++]=node;
  if(r(node+0x10)==frontend){
   if(out.row)return Gate::Duplicate;out.row=node;
   // This exact branch passes the original pointer once, not a fixed-rate substep.
   if(r.Byte(node+0x28)||r.Byte(node+0x35)||r.Byte(node+0x36)!=1||!(r(node+0x2C)&out.maskA)||!(r(node+0x30)&out.maskB))return Gate::Row;
  }
  stack[sp]=r(node);parents[sp++]=node;stack[sp]=r(node+8);parents[sp++]=node;
 }
 out.seen=n;if(n!=out.count||!out.row)return Gate::Tree;
 out.delta=r.Double(step);if(!std::isfinite(out.delta)||out.delta<=0)return Gate::Delta;
 return r.Stable()?Gate::Ready:Gate::Changed;
}
// Process-owned observation ledger, not an engine state mutation. Advance only
// AFTER original737CB0 returned and after ownership checks in the existing hook.
struct Epoch {
 U32 game{},thread{};U64 current{},consumed{};bool active{};
 bool Advance(U32 imageBase,U32 actualGame,U32 actualThread,U32 returnAddress){
  if(imageBase<0x10000||imageBase>0x60000000||returnAddress!=imageBase+0xBF17CA-0x400000u||
     !actualThread||!native_activation::Pointer(actualGame,0x2254)||active||current==UINT64_MAX||
     (game&&game!=actualGame)||(thread&&thread!=actualThread))return false;
  game=actualGame;thread=actualThread;++current;return true;
 }
 // Call after InspectHeld succeeds; consume BEFORE any activation operation.
 bool Enter(U32 actualGame,U32 actualThread,U64 capturedEpoch){
  if(active||!current||capturedEpoch!=current||consumed==current||actualGame!=game||actualThread!=thread)return false;
  consumed=current;active=true;return true;
 }
 void Leave(){active=false;}
};
}
