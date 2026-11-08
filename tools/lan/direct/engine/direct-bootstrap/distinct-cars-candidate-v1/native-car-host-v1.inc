// Native eligibility and selected profile livery only. No selection commit yet.
volatile LONG carAttempted{};
using NativeView=native_event_contract::DatabaseView;
struct ClassRecord {U32 context;NativeView view;};
static_assert(sizeof(ClassRecord)==12);
ClassRecord nativeClass{};
native_event_contract::VehicleRecord nativeVehicle{};
native_event_contract::LiveryRecord nativeLivery{};
int resolvedVehicle{},resolvedLivery{};
bool CarSignatures() {
 constexpr unsigned char view[]{0x8b,0xc1,0xc7,0,0,0,0,0,0xc7,0x40,4,0,0,0,0,0xc3};
 constexpr unsigned char enumerate[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xd4,0,0,0};
 constexpr unsigned char resolve[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x8b,0x45,0x08};
 constexpr unsigned char available[]{0x51,0x56,0x8d,0x44,0x24,4,0x8b,0xf1};
 if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0xAECFB0)),view,sizeof(view)) ||
    !trace::ReadableEqual(reinterpret_cast<void*>(At(0x517720)),enumerate,sizeof(enumerate)) ||
    !trace::ReadableEqual(reinterpret_cast<void*>(At(0x50E3D0)),available,sizeof(available)))return false;
 for(U32 va:{0x50DE00u,0x50DF80u,0x50E850u})if(!trace::ReadableEqual(reinterpret_cast<void*>(At(va)),resolve,sizeof(resolve)))return false;
 return Read(At(0x1333BA8)+0x54)==At(0x82EB70) && Read(At(0x1333BA8)+0x58)==At(0x82ECF0);
}
void NativeCarTick(void* self) {
 if(!hostObservedReady || hostObservationStopped || carAttempted) return;
 if(InterlockedCompareExchange(&carAttempted,1,0))return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),container=Read(network+0x2174),holder=Read(container+0x2450);
 const U32 session=Read(holder+0x18),transport=Read(holder+0x24),data=Read(session+0x1EC),tree=Read(data+0x1024);
 if(owner!=loadOwner || network!=configuredNetwork || container!=hostContainer || holder!=hostHolder || session!=hostSession || transport!=hostTransport ||
    Read(holder+8)!=1 || Read(transport+8)!=1 || !(Read(session+0x6C)&0xff) || Read(session+0x24)!=0 ||
    Read(data)!=At(0x1336248) || Read(tree)!=At(0x1335B88) || !SelectedCareerLoaded()) {Log("CAR REJECT completed host graph\r\n");return;}
 const U32 event=native_components::Find(Read,tree,At(0x14906B0));
 const U32 provider=Read(event+0x5C),key=Read(At(0x147DE90));
 if(Read(event)!=At(0x13366EC) || Read(event+0x58)!=Read(data+0x64) || provider!=Read(data+0x68) || provider!=network+0x1010 ||
    Read(event+0x74)!=(key^1) || Read(event+0x1B4)!=key || ((Read(event+0x60)^(Read(At(0x147E2C0))&1))&0xff)!=1) {Log("CAR REJECT replicated event\r\n");return;}
 const U32 context=Read(Read(owner+0x2250)+8),dbIndex=Read(At(0x147F3D4)),ownerIndex=Read(At(0x147F3D8)),availabilityIndex=Read(At(0x147F404));
 if(context!=loadContext || dbIndex>=512 || ownerIndex>=512 || availabilityIndex>=512 || Read(context+ownerIndex*4)!=owner || Read(context+dbIndex*4)!=Read(owner+0x2E84)) {Log("CAR REJECT service context\r\n");return;}
 const U32 availability=Read(context+availabilityIndex*4),selections=Read(owner+0x2288);
 char line[300]{};sprintf_s(line,"CAR providers availability=%08x availability_vt=%08x selection=%08x selection_vt=%08x database=%08x\r\n",availability,Read(availability),selections,Read(selections),Read(owner+0x2E84));Log(line);
 if(availability<0x10000 || availability!=Read(owner+0x2DF0) || selections<0x10000 || Read(availability+0xE0)!=Read(owner+0x2E84) || Read(availability+0x104)!=network ||
    Read(availability)!=At(0x132AD20) || Read(availability+0x1C)!=At(0x132AD0C) || Read(availability+0x20)!=At(0x132AD00) || Read(selections)!=At(0x1324EA0) || Read(selections+4)!=At(0x1324EB4)) {Log("CAR REJECT availability or selected-profile owner\r\n");return;}
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)) {Log("CAR REJECT allocator\r\n");return;}
 ChoiceRule rule{};if(!CaptureChoiceRule(owner,event,rule)){Log("CAR REJECT bound rule/profile\r\n");return;}
 const U32 mapping=reinterpret_cast<U32(__thiscall*)(void*,U32)>(At(0x533130))(reinterpret_cast<void*>(provider),1);
 if(Read(mapping)!=At(0x1333BA8) || Read(mapping+4)!=owner) {Log("CAR REJECT native mapping\r\n");return;}
 using Map=int(__thiscall*)(void*,int);
 const int a=reinterpret_cast<Map>(At(0x82EB70))(reinterpret_cast<void*>(mapping),1),b=reinterpret_cast<Map>(At(0x82ECF0))(reinterpret_cast<void*>(mapping),1);
 const int grade=static_cast<int>(Read(event+0x78)^key),threshold=(grade==-1 || grade==0)?0:(grade==1?1:2);
 sprintf_s(line,"CAR native-class first=%d second=%d event_grade=%d grade_threshold=%d\r\n",a,b,grade,threshold);Log(line);
 if(a!=17 || b!=17 || grade!=0) {Log("CAR REJECT fixed RX class/grade mapping\r\n");return;}
 using Init=void(__thiscall*)(void*);
 const auto init=reinterpret_cast<Init>(At(0xAECFB0));init(&nativeClass.view);
 if(!reinterpret_cast<bool(__thiscall*)(void*,void*,int)>(At(0x50DE00))(&nativeClass,reinterpret_cast<void*>(context),a)) {Log("CAR REJECT DB class\r\n");return;}
 int candidates[12]{};
 const int count=reinterpret_cast<int(__thiscall*)(void*,int*,int,int,int,int)>(At(0x517720))(&nativeClass,candidates,12,1,threshold,0);
 if(count<0 || count>12) {Log("CAR REJECT native candidate count\r\n");return;}
 bool found=false;
 for(int i=0;i<count;++i) {sprintf_s(line,"CAR native-filter candidate=%d vehicle_id=%d\r\n",i,candidates[i]);Log(line);if(candidates[i]==vehicleRequest.vehicle)found=true;}
 if(!found) {Log("CAR REJECT requested vehicle absent from native candidate set\r\n");return;}
 init(&nativeVehicle.vehicle);init(&nativeVehicle.model);init(&nativeVehicle.vehicleClass);
 const bool vehicle=reinterpret_cast<native_event_contract::ResolveVehicleFn>(At(0x50DF80))(&nativeVehicle,reinterpret_cast<void*>(context),vehicleRequest.vehicle);
 const bool available=vehicle && reinterpret_cast<bool(__thiscall*)(void*)>(At(0x50E3D0))(&nativeVehicle);
 if(!vehicle || !available) {Log("CAR REJECT requested vehicle unavailable\r\n");return;}
 const int livery=reinterpret_cast<native_event_contract::ResolveProfileLiveryFn>(At(0x50E4B0))(&nativeVehicle);
 init(&nativeLivery.livery);
 if(!reinterpret_cast<native_event_contract::ResolveLiveryFn>(At(0x50E850))(&nativeLivery,reinterpret_cast<void*>(context),livery)) {Log("CAR REJECT native livery DB row\r\n");return;}
 distinct_cars::Choice actual{};ChoiceRule afterRule{};
 if(!InspectChoice(rule,livery,actual) || !distinct_cars::LocalEligible(vehicleRequest.vehicle,candidates,count,vehicle,available,livery,actual) ||
    nativeVehicle.vehicle.opaque[0]!=actual.rows[1].row || nativeVehicle.vehicle.opaque[1]!=actual.rows[1].table ||
    !CaptureChoiceRule(owner,event,afterRule) || !(afterRule==rule)){Log("CAR REJECT livery schema or changed rule/profile\r\n");return;}
 localChoice={rule,actual,true};
 if(!RevalidateLocalChoice()){localChoice.valid=false;Log("CAR REJECT final local selection binding\r\n");return;}
 resolvedVehicle=vehicleRequest.vehicle;resolvedLivery=livery;
 sprintf_s(line,"CAR resolved vehicle_id=%d profile_livery_id=%d native_available=1 registry_call=0 selection_commit=0 race_calls=0\r\n",resolvedVehicle,livery);Log(line);
}
