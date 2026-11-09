// Include after initial-load-v5 (or a version preserving its ledger contract),
// before native-finalization-v4.inc. No entrypoint or native state mutation.
#include "../loaded-ack-integration-v2-20261018/native-loaded-roster-lan-v3.h"
#include "../native-loaded-ack-adapter-20261018/native-loaded-adapter-guards-v1.h"
#include "../native-loaded-ack-adapter-20261018/native-loaded-completion-policy-v1.h"
bool LoadedAdapterRead(U32 p,U32& value) {return initial_load_v2::Bytes(p,&value,sizeof(value));}
bool LoadedAdapterEqual(U32 p,const unsigned char* bytes,size_t count) {
 return trace::ReadableEqual(reinterpret_cast<const void*>(p),bytes,count);
}
loaded_roster_v3::Binding LoadedRosterBinding() {
 const auto& l=initial_load_v2::ledger;
 loaded_roster_v3::Binding b{base,l.session,l.data,l.tree,l.roster,committedPlayer,{}};
 for(unsigned i=0;i<2;++i) {
  const auto& p=l.players[i];b.players[i]={p.root,p.identity,p.vehicle,p.nativeId,p.record,p.livery};
 }
 return b;
}
bool NativeLoadedRosterPlayers(ConfigPlayer (&out)[2],bool afterAck=false) {
 loaded_roster_v3::Progress progress{};
 const auto livery=[](U32 owner,int& value){return ReadStoredLivery(owner,value);};
 if(!CarCommitSignatures() || !PeerIdentitySignatures() || !loaded_adapter_guards_v1::Signatures(LoadedAdapterEqual,base) || !loaded_lan_mode_v1::Signatures(LoadedAdapterEqual,base)) {Log("LOADED_ROSTER reject signatures\r\n");return false;}
 loaded_roster_v3::Diagnostic diagnostic{};
 const auto gate=loaded_roster_v3::Inspect(LoadedAdapterRead,livery,LoadedRosterBinding(),afterAck,progress,&diagnostic);
 if(gate!=loaded_roster_v3::Gate::Ready) {
  char text[700]{};sprintf_s(text,"LOADED_ROSTER reject gate=%u reason=%s address=%08x actual=%08x expected=%08x after_ack=%u\r\n",
   unsigned(gate),diagnostic.reason,diagnostic.address,diagnostic.actual,diagnostic.expected,unsigned(afterAck));Log(text);return false;
 }
 for(unsigned i=0;i<2;++i)out[i]=initial_load_v2::ledger.players[i];
 return true;
}
bool NativeLoadedSessionState(U32 tree,U32 session,bool afterAck=false) {
 U32 descriptor{};
 if(!loaded_adapter_guards_v1::Signatures(LoadedAdapterEqual,base) || tree!=initial_load_v2::ledger.tree || session!=initial_load_v2::ledger.session ||
    !loaded_roster_v3::State(Read,base,tree,session,true,descriptor))return false;
 return descriptor==0x1490BBC || descriptor==0x1490BE0 || (afterAck && descriptor==0x1490BD0);
}
// Current owning service/config references and native DB identities. This
// replaces only the initial-state-specific part of Configuration(false).
bool NativeLoadedConfiguration(bool afterAck=false) {
 using initial_load_v2::Ptr;const auto& l=initial_load_v2::ledger;
 if(!RxEventMatches(l.event) || !InitialConfigSignatures() || !initialConfigCommitted || l.config!=initialConfig || l.service!=initialConfigService ||
    Read(l.manager)!=At(0x132B1CC) || Read(l.manager+0x34)!=l.service || Read(l.manager+0xAC0)!=1)return false;
 ConfigRef service(0x132AC70),config(0x132A450);
 if(reinterpret_cast<config_abi::AcquireKind>(At(0x734A70))(reinterpret_cast<void*>(l.manager),&service.ref,1)!=&service.ref || !service.Valid() || service.ref.pointer!=l.service ||
    reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(l.service),&config.ref)!=&config.ref || !config.Valid() || config.ref.pointer!=l.config)return false;
 const U32 begin=Read(l.config+0x20);int trackId{};
 if(!Ptr(begin,8) || Read(l.config+0x24)!=begin+8 || Read(l.config+0x3C)!=l.database || Read(l.service+0x38)!=l.database ||
    Read(l.service+0x3C)!=static_cast<U32>(RxRequest().selected.model) || Read(l.config+0x40)!=l.configKind || Read(l.config+0x78)!=l.trackRow ||
    !ConfigRowId(l.database,"track_model",l.trackRow,trackId) || trackId!=RxRequest().selected.model)return false;
 // Preserve the fixed manifest's actual protected values/current round, not
 // merely the address of an event captured later by the finalizer.
 const U32 key=Read(At(0x147DE90)),shortKey=Read(At(0x147FC68)),round=l.event+0xA4;
 if(!(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) || Read(l.event)!=At(0x13366EC) ||
    Read(l.event+0x58)!=Read(l.data+0x64) || Read(l.event+0x5C)!=l.network+0x1010 ||
    Read(l.event+0x1B4)!=key || (Read(l.event+0x64)^key)!=static_cast<U32>(RxRequest().race) || (Read(l.event+0x68)^key)!=1 ||
    (Read(l.event+0x6C)^key)!=0 || (Read(l.event+0x74)^key)!=static_cast<U32>(RxRequest().vehicleClass) ||
    Read(round)!=At(0x1333840) || Read(round+4)!=l.event || (Read(round+8)^key)!=static_cast<U32>(RxRequest().race) ||
    config_abi::DecodeTrack(static_cast<unsigned short>(Read(round+0xC)),static_cast<unsigned short>(shortKey))!=RxRequest().selected.model ||
    (Read(round+0x10)^key)!=static_cast<U32>(RxRequest().selected.location) || (Read(round+0x18)^key)!=static_cast<U32>(RxLaps()) || (Read(round+0x1C)^key)!=0)return false;
 ConfigPlayer players[2]{};if(!NativeLoadedRosterPlayers(players,afterAck))return false;
 for(unsigned i=0;i<2;++i) {
  const auto& p=players[i];const U32 entry=l.entries[i],records=Read(entry+0x40);int liveryId{};
  if(Read(begin+4*i)!=entry || Read(entry)!=At(0x132A398) || Read(entry+0xC)!=l.database ||
     (Read(entry+0xC0)&255)!=l.nativeIds[i] || l.nativeIds[i]!=p.nativeId || l.identities[i]!=p.identity ||
     Read(entry+0x1C)!=(p.root==committedPlayer?4u:2u) || !Ptr(records,12) || Read(entry+0x48)!=1 ||
     Read(entry+0x44)<1 || Read(entry+0x44)>64 || Read(records)!=Read(At(0x134FB70)) || Read(records+4)!=l.liveries[i] ||
     !ConfigRowId(l.database,"vehicle_livery",l.liveries[i],liveryId) || liveryId!=p.livery)return false;
 }
 return NativeLoadedSessionState(l.tree,l.session,afterAck);
}
