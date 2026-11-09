// Initial native race configuration only. Never invoke for an existing race.
// Real pair: each replicated livery is independently validated against fixed RX rules.
#include "../initial-config-readback-20261018/native-readback-abi.h"
namespace config_abi=dirt2_initial_config_readback;
bool initialConfigAttempted{},initialConfigCommitted{};
U32 initialConfig{},initialConfigService{};
bool InitialConfigSignatures();
U32 NativeStateDescriptor(U32 root) {
 const U32 component=native_components::Find(Read,root,At(0x1490690)),state=Read(component+0x64),getter=Read(Read(state));
 if(Read(component)!=At(0x1335C88) || (Read(getter)&0xff)!=0xB8 || (Read(getter+5)&0xff)!=0xC3)return 0;
 return Read(getter+1);
}
bool ConfigNormalLoadingState(U32 state) {
 const U32 table=Read(state),getter=Read(table),descriptor=Read(getter+1);
 return (table==At(0x1337060) && getter==At(0x8C58D0) && descriptor==At(0x1490BD4)) ||
        (table==At(0x1337150) && getter==At(0x8C5920) && descriptor==At(0x1490BB4));
}
using ConfigPlayer=distinct_cars::Participant;
bool ConfigPlayers(U32 data,U32 roster,U32 database,ConfigPlayer (&out)[2]) {
 const U32 facade=Read(data+0x1274),mapping=Read(facade+0x6C),records=Read(mapping+0x60),recordCount=Read(mapping+0x68);
 const U32 array=Read(data+0x1060),count=Read(data+0x1068),ids=Read(roster+0x5C);
 if(Read(data)!=At(0x1336248) || count!=2 || recordCount!=2 || Read(roster+0x64)!=2 || Read(roster+0x60)<2 || Read(roster+0x60)>64 ||
    array<0x10000 || array>0x7ffffff7 || ids<0x10000 || ids>0x7ffffff7 || records<0x10000 || records>0x7ffffff7 ||
    Read(facade)!=At(0x1335A38) || Read(mapping)!=At(0x1335DA4) || Read(Read(mapping))!=At(0x8B0BF0) || Read(data+0x126C)!=mapping || Read(mapping+0x54)!=Read(data+0x5C) || Read(mapping+0x58)!=data || records!=mapping+0x70 || Read(mapping+0x64)!=8)return false;
 unsigned localCount{};
 for(U32 i=0;i<2;++i) {
  auto& player=out[i];player.root=Read(array+4*i);
  player.identity=native_components::Find(Read,player.root,At(0x149077C));player.vehicle=native_components::Find(Read,player.root,At(0x1490784));
  if(Read(player.identity)!=At(0x1334B8C) || Read(At(0x1334B8C)+0x18)!=At(0x87CA10) || !ReadStoredLivery(player.vehicle,player.livery) || player.livery<=0 || database!=localChoice.rule.database || !InspectChoice(localChoice.rule,player.livery,player.choice))return false;
  const int expectedCar=player.root==committedPlayer?RxRequest().localCar:RxRequest().peerCar;
  if(expectedCar && player.choice.vehicle!=expectedCar){Log("CONFIG REJECT replicated car differs from lobby selection\r\n");return false;}
  player.nativeId=Read(player.identity+4)&0xff;
  unsigned rosterMatches{},recordMatches{};
  for(U32 j=0;j<2;++j) {
   const U32 rosterId=Read(ids+4*j);if(rosterId>255)return false;
   if(rosterId==player.nativeId)++rosterMatches;
   const U32 record=Read(records+4*j);
   if(record<0x10000 || record>0x7fffffeb)return false;
   if(Read(record+0xC)==player.root && Read(record+0x10)==player.identity){++recordMatches;player.record=record;}
  }
  const U32 descriptor=NativeStateDescriptor(player.root);
  const U32 currentState=Read(native_components::Find(Read,player.root,At(0x1490690))+0x64),stateTable=Read(currentState),getter=Read(stateTable);
  const bool expectedState=(descriptor==At(0x1490B8C) && stateTable==At(0x13371B0) && getter==At(0x8C5940)) || ConfigNormalLoadingState(currentState);
  if(rosterMatches!=1 || recordMatches!=1 || !expectedState)return false;
  if(player.root==committedPlayer) {++localCount;if(player.vehicle!=committedVehicleOwner || player.livery!=resolvedLivery)return false;}
 }
 for(U32 i=0;i<2;++i) {
  int currentLivery{};
  if(Read(array+4*i)!=out[i].root || Read(out[i].record+0xC)!=out[i].root || Read(out[i].record+0x10)!=out[i].identity ||
     native_components::Find(Read,out[i].root,At(0x149077C))!=out[i].identity ||
     native_components::Find(Read,out[i].root,At(0x1490784))!=out[i].vehicle ||
     !ReadStoredLivery(out[i].vehicle,currentLivery) || currentLivery!=out[i].livery || (Read(out[i].identity+4)&0xff)!=out[i].nativeId)return false;
 }
 if(Read(data+0x1274)!=facade || Read(facade+0x6C)!=mapping || Read(mapping+0x58)!=data || Read(mapping+0x60)!=records || Read(mapping+0x68)!=recordCount)return false;
 const U32 rosterIds[]{Read(ids),Read(ids+4)};
 return localCount==1 && distinct_cars::Participants(out,rosterIds,committedPlayer,committedVehicleOwner,localChoice.actual,RxRequest().vehicleClass) && Read(data+0x1060)==array && Read(data+0x1068)==count && Read(roster+0x5C)==ids && Read(roster+0x64)==2;
}
// Preserve the existing loader/finalizer signature. It must be called inside
// a validated native allocator scope; InspectChoice fails closed otherwise.
bool ConfigPlayers(U32 data,U32 roster,ConfigPlayer (&out)[2]) {
 return ConfigPlayers(data,roster,localChoice.rule.database,out);
}
struct ConfigRef {
 config_abi::Ref32 ref{};U32 expectedTable{};
 explicit ConfigRef(U32 table):expectedTable(At(table)){}
 bool Valid() const {return ref.pointer>=0x10000 && Read(ref.pointer)==expectedTable && Read(ref.pointer+4)>0 && Read(ref.pointer+4)<0x100000 && Read(ref.pointer+8)>=0x10000;}
 ~ConfigRef() {
  const U32 object=ref.pointer;ref.pointer=0;
  if(!object)return;
  if(Read(object)!=expectedTable || Read(object+4)==0 || Read(object+4)>=0x100000){Log("CONFIG REJECT invalid owning-reference release; retained until process exit\r\n");return;}
  if(InterlockedDecrement(reinterpret_cast<volatile LONG*>(object+4))==0)
   reinterpret_cast<config_abi::ReleaseZero>(Read(expectedTable+4))(reinterpret_cast<void*>(object));
 }
 ConfigRef(const ConfigRef&)=delete;ConfigRef& operator=(const ConfigRef&)=delete;
};
bool ConfigRowId(U32 database,const char* tableName,U32 row,int& value) {
 NativeChoiceOps ops{};distinct_cars::Row binding{};
 const U32 table=ops.Table(database,tableName);
 return distinct_cars::Capture(ops,database,table,row,binding) && distinct_cars::Integer(ops,binding,"id",value) && ops.Table(database,tableName)==table;
}
void NativeInitialConfigTick(void* self,U32 container,U32 holder,U32 session,U32 transport,bool host) {
 if(initialConfigAttempted || !roundCarCommitted || trace::Environment(L"DIRT2VR_ENGINE_INITIAL_CONFIG")!=L"1")return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),data=Read(session+0x1EC),tree=Read(data+0x1024);
 const U32 localDescriptor=NativeStateDescriptor(committedPlayer),sessionDescriptor=NativeStateDescriptor(tree);
 if((localDescriptor!=At(0x1490BD4) && localDescriptor!=At(0x1490BB4)) || sessionDescriptor!=At(0x1490BBC))return;
 const U32 localState=Read(native_components::Find(Read,committedPlayer,At(0x1490690))+0x64),sessionState=Read(native_components::Find(Read,tree,At(0x1490690))+0x64);
 if(!ConfigNormalLoadingState(localState) || (Read(localState+0x60)&0xffff)!=0x0101 ||
    Read(sessionState)!=At(0x1336D0C) || Read(Read(sessionState))!=At(0x8C1970) || Read(sessionState+0x58)!=session) return;
 const U32 state=host?1u:2u,role=host?0u:1u;
 if(owner!=loadOwner || network!=configuredNetwork || Read(network+0x2174)!=container || Read(container+0x2450)!=holder ||
    Read(holder)!=At(0x133549C) || Read(holder+0x18)!=session || Read(holder+0x24)!=transport || (Read(holder+0x800)&0xff) ||
    Read(session)!=At(0x1335844) || Read(session+0x24)!=role || !(Read(session+0x6C)&0xff) || Read(transport)!=At(0x135CFC0) || Read(transport+8)!=state ||
    Read(data)!=At(0x1336248) || Read(data+0x64)!=Read(network+0x1004) || Read(data+0x68)!=network+0x1010 || Read(data+0x1028)!=committedPlayer || Read(tree)!=At(0x1335B88) || !SelectedCareerLoaded() ||
    (Read(owner+0x2E80)&0xffff)!=0 || !InitialConfigSignatures()) {initialConfigAttempted=true;Log("CONFIG REJECT owned initial session/lifecycle\r\n");return;}
 // Native host advertisement refresh temporarily sets holder -1 at BBC entry.
 // All session/provider/transport identities above must remain valid while waiting.
 if(host && Read(holder+8)==0xffffffffu)return;
 initialConfigAttempted=true;
 if(Read(holder+8)!=state){Log("CONFIG REJECT native holder phase\r\n");return;}
 const U32 event=native_components::Find(Read,tree,At(0x14906B0)),roster=native_components::Find(Read,tree,At(0x1490868));
 const U32 integerKey=Read(At(0x147DE90)),shortKey=Read(At(0x147FC68)),round=event+0xA4;
 if(Read(event)!=At(0x13366EC) || Read(event+0x58)!=Read(data+0x64) || Read(event+0x5C)!=network+0x1010 || Read(round)!=At(0x1333840) || Read(round+4)!=event ||
    !(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) || Read(event+0x1B4)!=integerKey || (Read(event+0x64)^integerKey)!=static_cast<U32>(RxRequest().race) ||
    (Read(event+0x68)^integerKey)!=1 || (Read(event+0x74)^integerKey)!=static_cast<U32>(RxRequest().vehicleClass) || (Read(event+0x78)^integerKey)!=0 || (Read(round+8)^integerKey)!=static_cast<U32>(RxRequest().race) ||
    config_abi::DecodeTrack(static_cast<unsigned short>(Read(round+0xC)),static_cast<unsigned short>(shortKey))!=RxRequest().selected.model ||
    (Read(round+0x10)^integerKey)!=static_cast<U32>(RxRequest().selected.location) || (Read(round+0x18)^integerKey)!=static_cast<U32>(RxLaps())) {Log("CONFIG REJECT fixed event/round\r\n");return;}
 const U32 manager=Read(owner+0x2E88),service=Read(manager+0x34),databaseService=Read(owner+0x2E84),database=Read(databaseService+0xC);
 if(Read(manager)!=At(0x132B1CC) || Read(manager+8)!=At(0x132B1B8) || Read(service)!=At(0x132AC70) || Read(service+0x38)!=database ||
    database<0x10000 || !(Read(databaseService+0x20)&0xff) || Read(manager+0xAC0)>8){Log("CONFIG REJECT kind1/database ownership\r\n");return;}
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)){Log("CONFIG REJECT allocator\r\n");return;}
 if(!RxEventMatches(event) || !RevalidateLocalChoice()){Log("CONFIG REJECT local choice/rule changed\r\n");return;}
 ConfigPlayer players[2]{};
 if(!ConfigPlayers(data,roster,database,players)){Log("CONFIG REJECT real roster/selected cars\r\n");return;}
 const U32 localId=Read(native_components::Find(Read,committedPlayer,At(0x149077C))+4)&0xff;
 if((Read(session+0x28)&0xff)!=localId){Log("CONFIG REJECT session local native ID\r\n");return;}
 ConfigRef heldService(0x132AC70);
 const auto serviceResult=reinterpret_cast<config_abi::AcquireKind>(At(0x734A70))(reinterpret_cast<void*>(manager),&heldService.ref,1);
 if(serviceResult!=&heldService.ref || !heldService.Valid() || heldService.ref.pointer!=service){Log("CONFIG REJECT acquired service\r\n");return;}
 reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x521860))(reinterpret_cast<void*>(manager),1);
 if(Read(manager+0xAC0)!=1){Log("CONFIG REJECT selected kind\r\n");return;}
 {char text[180]{};sprintf_s(text,"CONFIG CALL lower initial builder real_participants=2 track=%d frontend_calls=0 roster_rebuild_calls=0\r\n",RxRequest().selected.model);Log(text);}
 reinterpret_cast<config_abi::BuildInitial>(At(0x75F460))(reinterpret_cast<void*>(service),reinterpret_cast<void*>(event),reinterpret_cast<void*>(network));
 ConfigRef config(0x132A450);
 const auto configResult=reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(service),&config.ref);
 const U32 entryBegin=Read(config.ref.pointer+0x20),entryEnd=Read(config.ref.pointer+0x24);
 if(entryBegin<0x10000 || entryBegin>0x7ffffff7 || entryEnd!=entryBegin+8){Log("CONFIG REJECT entry vector range\r\n");return;}
 if(configResult!=&config.ref || !config.Valid() || Read(config.ref.pointer+0x3C)!=database || Read(config.ref.pointer+0x44)!=static_cast<U32>(RxLaps()) || Read(service+0x3C)!=static_cast<U32>(RxRequest().selected.model) ||
    reinterpret_cast<config_abi::EntryCount>(At(0x773000))(reinterpret_cast<void*>(config.ref.pointer))!=2){Log("CONFIG REJECT entry count/database/track\r\n");return;}
 int track{};const U32 trackRow=Read(config.ref.pointer+0x78);
 if(!ConfigRowId(database,"track_model",trackRow,track) || track!=RxRequest().selected.model){Log("CONFIG REJECT track_model schema id\r\n");return;}
 bool matched[2]{};char line[320]{};
 for(U32 i=0;i<2;++i) {
  ConfigRef entry(0x132A398);
  const auto entryResult=reinterpret_cast<config_abi::AcquireEntry>(At(0xB28310))(reinterpret_cast<void*>(config.ref.pointer),&entry.ref,i);
  if(entryResult!=&entry.ref || entry.ref.pointer!=Read(entryBegin+4*i) || !entry.Valid() || Read(entry.ref.pointer+0xC)!=database){Log("CONFIG REJECT entry ownership\r\n");return;}
  const U32 id=Read(entry.ref.pointer+0xC0)&0xff;unsigned playerIndex=2;
  for(unsigned j=0;j<2;++j)if(players[j].nativeId==id)playerIndex=j;
  if(playerIndex==2 || matched[playerIndex] || id!=Read(Read(roster+0x5C)+4*i)){Log("CONFIG REJECT native ID coverage\r\n");return;}
  const auto& player=players[playerIndex];const U32 expectedRole=player.root==committedPlayer?4u:2u;
  const U32 records=Read(entry.ref.pointer+0x40),capacity=Read(entry.ref.pointer+0x44),count=Read(entry.ref.pointer+0x48);
  if(Read(entry.ref.pointer+0x1C)!=expectedRole || count!=1 || capacity<count || capacity>64 || records<0x10000 || records>0x7ffffff3 || Read(records)!=Read(At(0x134FB70))) {Log("CONFIG REJECT role/livery record\r\n");return;}
  distinct_cars::Choice entryChoice{};
  if(!InspectChoice(localChoice.rule,player.livery,entryChoice) ||
     !distinct_cars::EntryMatches(player,committedPlayer,Read(entry.ref.pointer+0x1C),Read(records+4),entryChoice)){
   Log("CONFIG REJECT per-participant livery/car/class/grade schema\r\n");return;
  }
  const int livery=entryChoice.livery;
  matched[playerIndex]=true;sprintf_s(line,"CONFIG entry native_id=%u role=%u livery=%d vehicle=%d class=%d grade=%d grade_order=%d source_player=%08x\r\n",id,expectedRole,livery,entryChoice.vehicle,entryChoice.carClass,entryChoice.grade,entryChoice.gradeOrder,player.root);Log(line);
 }
 ConfigRef currentConfig(0x132A450);
 const auto currentResult=reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(service),&currentConfig.ref);
 if(currentResult!=&currentConfig.ref || !currentConfig.Valid() || currentConfig.ref.pointer!=config.ref.pointer ||
    Read(manager+0x34)!=service || Read(manager+0xAC0)!=1 || Read(service+0x38)!=database || Read(databaseService+0xC)!=database ||
    Read(config.ref.pointer+0x20)!=entryBegin || Read(config.ref.pointer+0x24)!=entryEnd || Read(config.ref.pointer+0x78)!=trackRow) {Log("CONFIG REJECT published configuration changed\r\n");return;}
 if(Read(owner+0x2CF0)!=network || Read(network+0x2174)!=container || Read(container+0x2450)!=holder || Read(holder+0x18)!=session ||
    Read(holder+0x24)!=transport || Read(holder+8)!=state || (Read(holder+0x800)&0xff) || Read(session+0x1EC)!=data ||
    Read(data+0x1024)!=tree || Read(data+0x1028)!=committedPlayer || Read(data+0x64)!=Read(network+0x1004) || Read(data+0x68)!=network+0x1010 ||
    Read(owner+0x2E88)!=manager || Read(owner+0x2E84)!=databaseService || (Read(session+0x28)&0xff)!=localId ||
    native_components::Find(Read,tree,At(0x14906B0))!=event || native_components::Find(Read,tree,At(0x1490868))!=roster ||
    Read(event+0x1B4)!=integerKey || Read(event+0x5C)!=network+0x1010 || (Read(event+0x64)^integerKey)!=static_cast<U32>(RxRequest().race) || (Read(event+0x68)^integerKey)!=1 ||
    (Read(event+0x74)^integerKey)!=static_cast<U32>(RxRequest().vehicleClass) || (Read(event+0x78)^integerKey)!=0 || (Read(round+8)^integerKey)!=static_cast<U32>(RxRequest().race) || (Read(round+0x10)^integerKey)!=static_cast<U32>(RxRequest().selected.location) || (Read(round+0x18)^integerKey)!=static_cast<U32>(RxLaps()) ||
    config_abi::DecodeTrack(static_cast<unsigned short>(Read(round+0xC)),static_cast<unsigned short>(shortKey))!=RxRequest().selected.model) {Log("CONFIG REJECT current session/event graph changed\r\n");return;}
 ConfigPlayer after[2]{};
 if(!RevalidateLocalChoice() || !ConfigPlayers(data,roster,database,after) || memcmp(players,after,sizeof(players)) || Read(At(0x147DE90))!=integerKey || Read(At(0x147FC68))!=shortKey ||
    !ConfigNormalLoadingState(Read(native_components::Find(Read,committedPlayer,At(0x1490690))+0x64)) || NativeStateDescriptor(tree)!=At(0x1490BBC)) {Log("CONFIG REJECT changed native inputs\r\n");return;}
 initialConfig=config.ref.pointer;initialConfigService=service;initialConfigCommitted=true;
 sprintf_s(line,"CONFIG verified config=%08x service=%08x count=2 track_model=%d role=%s load_calls=0 race_start_proven=0\r\n",initialConfig,initialConfigService,RxRequest().selected.model,host?"host":"client");Log(line);
}
