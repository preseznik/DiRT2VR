// Included after career/event helpers; all native queries require the owner scope.
#include "../direct-bootstrap/distinct-cars-candidate-v1/choice-guard.h"
#include "../direct-bootstrap/distinct-cars-candidate-v1/choice-code-guards.inc"
distinct_cars::Request vehicleRequest{};
struct ChoiceRule {
 U32 owner{},network{},context{},event{},databaseService{},database{},profile{},sdu{},save{},selections{},availability{},key{};
 U32 container{},holder{},session{},data{},tree{};
 DWORD thread{};
 bool operator==(const ChoiceRule&) const=default;
};
struct BoundChoice {ChoiceRule rule{};distinct_cars::Choice actual{};bool valid{};};
BoundChoice localChoice{};
bool InitializeVehicleRequest() {
 wchar_t value[12]{};SetLastError(ERROR_SUCCESS);
 const DWORD length=GetEnvironmentVariableW(L"DIRT2VR_ENGINE_VEHICLE_ID",value,12),error=GetLastError();
 if(length>=12)return false;
 return vehicleRequest.Initialize(value,length!=0 || error!=ERROR_ENVVAR_NOT_FOUND);
}
bool ChoiceRead(U32 address,U32& out) {
 if(address<0x10000 || address>0x7ffffffb)return false;
 __try {out=*reinterpret_cast<const U32*>(address);return true;}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
struct NativeChoiceOps {
 U32 Sentinel() const{return ::Read(At(0x134FB70));}
 bool Read(U32 address,U32& out) const{return ChoiceRead(address,out);}
 U32 Table(U32 database,const char* name) const {
  return reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0xB2FCA0))(reinterpret_cast<void*>(database),name);
 }
 bool Resolve(U32 context,int livery,distinct_cars::View& out) const {
  native_event_contract::LiveryRecord record{};
  reinterpret_cast<void(__thiscall*)(void*)>(At(0xAECFB0))(&record.livery);
  if(!reinterpret_cast<native_event_contract::ResolveLiveryFn>(At(0x50E850))(&record,reinterpret_cast<void*>(context),livery) || record.context!=context)return false;
  out={record.livery.opaque[0],record.livery.opaque[1]};return true;
 }
 int Integer(U32 table,U32 row,const char* field,int& out) const {
  return reinterpret_cast<int(__thiscall*)(void*,int*,U32,const char*)>(At(0xB19260))(reinterpret_cast<void*>(table),&out,row,field);
 }
 U32 Linked(U32 table,U32 row,const char* field) const {
  return reinterpret_cast<U32(__thiscall*)(void*,U32,const char*)>(At(0xB36330))(reinterpret_cast<void*>(table),row,field);
 }
};
bool CaptureChoiceRule(U32 owner,U32 event,ChoiceRule& out) {
 const U32 network=Read(owner+0x2CF0),context=Read(Read(owner+0x2250)+8),databaseService=Read(owner+0x2E84);
 const U32 dbIndex=Read(At(0x147F3D4)),ownerIndex=Read(At(0x147F3D8)),availableIndex=Read(At(0x147F404));
 const U32 key=Read(At(0x147DE90)),profile=Read(owner+0x2284),sdu=Read(owner+0x2294),selections=Read(owner+0x2288),availability=Read(owner+0x2DF0);
 const U32 container=Read(network+0x2174),holder=Read(container+0x2450),session=Read(holder+0x18),data=Read(session+0x1EC),tree=Read(data+0x1024);
 if(!vehicleRequest.valid || !SelectedCareerLoaded() || owner!=loadOwner || network!=configuredNetwork || context!=loadContext ||
    GetCurrentThreadId()!=static_cast<DWORD>(holdThread) || profile!=loadProfile || sdu!=loadSdu || !selectedSave ||
    dbIndex>=512 || ownerIndex>=512 || availableIndex>=512 || Read(context+dbIndex*4)!=databaseService ||
    Read(context+ownerIndex*4)!=owner || Read(context+availableIndex*4)!=availability || !(Read(databaseService+0x20)&0xff) ||
    Read(databaseService+0xC)<0x10000 || Read(availability)!=At(0x132AD20) || Read(availability+0x1C)!=At(0x132AD0C) ||
    Read(availability+0x20)!=At(0x132AD00) || Read(availability+0xE0)!=databaseService || Read(availability+0x104)!=network ||
    Read(selections)!=At(0x1324EA0) || Read(selections+4)!=At(0x1324EB4) || Read(event)!=At(0x13366EC) ||
    Read(event+0x5C)!=network+0x1010 || !(Read(At(0x147DE98))&1) || Read(event+0x1B4)!=key ||
    (Read(event+0x64)^key)!=1 || (Read(event+0x68)^key)!=1 || (Read(event+0x74)^key)!=1 || (Read(event+0x78)^key)!=0 ||
    Read(network)!=At(0x1336604) || Read(container)!=At(0x13364EC) ||
    Read(holder)!=At(0x133549C) || (Read(holder+0x800)&0xff) || Read(session)!=At(0x1335844) || (Read(session+0x6C)&0xff)!=1 ||
    Read(data)!=At(0x1336248) || Read(tree)!=At(0x1335B88) || Read(event+0x58)!=Read(data+0x64) ||
    Read(data+0x68)!=network+0x1010 || native_components::Find(Read,tree,At(0x14906B0))!=event)return false;
 if(!RxEventMatches(event) || !RxCatalogBound(owner,false))return false;
 out={owner,network,context,event,databaseService,Read(databaseService+0xC),profile,sdu,selectedSave,selections,availability,key,
      container,holder,session,data,tree,GetCurrentThreadId()};return true;
}
bool ChoiceScope(const ChoiceRule& rule) {
 const U32 slot=Read(At(0x147D1F8));if(slot==0xffffffffu)return false;
 const U32 frame=reinterpret_cast<U32>(TlsGetValue(slot));
 return GetCurrentThreadId()==rule.thread && Read(frame)==At(0x147D1F8) && Read(frame+4)==Read(rule.network+0x100C);
}
bool InspectChoice(const ChoiceRule& rule,int livery,distinct_cars::Choice& out) {
 ChoiceRule current{};
 if(!ChoiceScope(rule) || !CaptureChoiceRule(rule.owner,rule.event,current) || !(current==rule))return false;
 NativeChoiceOps ops{};
 return distinct_cars::Inspect(ops,rule.context,rule.database,livery,out) &&
        CaptureChoiceRule(rule.owner,rule.event,current) && current==rule && ChoiceScope(rule);
}
bool RevalidateLocalChoice() {
 if(!localChoice.valid || !vehicleRequest.valid || localChoice.actual.vehicle!=vehicleRequest.vehicle)return false;
 distinct_cars::Choice now{};
 if(!InspectChoice(localChoice.rule,localChoice.actual.livery,now) || !(now==localChoice.actual))return false;
 native_event_contract::VehicleRecord current{};
 const auto init=reinterpret_cast<void(__thiscall*)(void*)>(At(0xAECFB0));
 init(&current.vehicle);init(&current.model);init(&current.vehicleClass);
 if(!reinterpret_cast<native_event_contract::ResolveVehicleFn>(At(0x50DF80))(&current,reinterpret_cast<void*>(localChoice.rule.context),vehicleRequest.vehicle) ||
    current.vehicle.opaque[0]!=localChoice.actual.rows[1].row || current.vehicle.opaque[1]!=localChoice.actual.rows[1].table ||
    !reinterpret_cast<bool(__thiscall*)(void*)>(At(0x50E3D0))(&current) ||
    reinterpret_cast<native_event_contract::ResolveProfileLiveryFn>(At(0x50E4B0))(&current)!=localChoice.actual.livery)return false;
 ChoiceRule after{};
 return CaptureChoiceRule(localChoice.rule.owner,localChoice.rule.event,after) && after==localChoice.rule &&
        InspectChoice(after,localChoice.actual.livery,now) && now==localChoice.actual;
}
