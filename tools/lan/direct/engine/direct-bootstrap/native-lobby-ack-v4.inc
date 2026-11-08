// Acknowledge the committed one-member test-harness lobby to the native protocol.
// This event is literally fe_in_lobby. No FE wrapper/state callback is invoked.
// These eight exact native handlers ignore code0x1E. Validate their actual
// ownership rather than interpreting embedded observer words as state pointers.
// D+64 is the separate NetworkLog service, not UpdateTask (sealed correction audit).
bool KnownIgnoredLobbyObserver(U32 observer,U32 network,U32 data) {
 const U32 table=Read(observer),provider=Read(data+0x68),networkLog=Read(network+0x1004);
 if(Read(data)!=At(0x1336248) || Read(data+0x64)!=networkLog || Read(networkLog)!=At(0x12F0588) || provider!=network+0x1010)return false;
 if(table==At(0x1333ECC)) {const U32 object=Read(network+0x2234);return Read(object)==At(0x1333F1C) && Read(object+0xC)==network+0x1010 && Read(object+0x10)==Read(network+0x1094) && Read(object+0x14)==Read(network+0x1088) && Read(object+0x1310)==observer && Read(observer+4)==object && Read(table+8)==At(0x8B8DE0);}
 if(table==At(0x1334A1C)) {const U32 object=Read(network+0x2240);return Read(object)==At(0x1334A28) && Read(object+8)==network+0x1010 && Read(object+0xC)==network+0x1160 && Read(object+0x10)==Read(network+0x1088) && Read(object+0x24)==observer && Read(observer+4)==object && Read(table+8)==At(0x8B7FB0);}
 if(table==At(0x1336114)) {const U32 object=Read(network+0x2170);return Read(object)==At(0x1336134) && observer==object+0x2550 && Read(table+8)==At(0x8116B0);}
 if(table==At(0x1336498))return observer==hostContainer+0x2558 && Read(table+8)==At(0x860AA0);
 if(table==At(0x13365CC))return observer==Read(data+0x1280) && Read(observer+4)==provider && Read(observer+8)==data && Read(observer+0xC)==networkLog && Read(table+8)==At(0x857C50);
 if(table==At(0x1336204))return Read(data)==At(0x1336248) && observer==data+0x1254 && Read(table+8)==At(0x852370);
 if(table==At(0x13347E8)) {const U32 object=Read(data+0x129C);return Read(object)==At(0x1335C50) && Read(observer+4)==object && native_components::Find(Read,object,At(0x14909D4))==observer && Read(object+0x54)==Read(data+0x5C) && Read(object+0x58)==networkLog && Read(object+0x5C)==provider && Read(table+8)==At(0x8B8260);}
 if(table==At(0x13345B0)) {const U32 object=Read(data+0x1274);return Read(object)==At(0x1335A38) && Read(observer+4)==object && native_components::Find(Read,object,At(0x14909FC))==observer && Read(object+0x54)==Read(data+0x5C) && Read(object+0x60)==Read(data+0x5C) && Read(object+0x64)==networkLog && Read(object+0x6C)==Read(data+0x126C) && Read(table+8)==At(0x8B7100);}
 return false;
}

volatile LONG lobbyAttempted{};
bool lobbyAcknowledged{};
U32 observedPlayerState{};
ULONGLONG lobbyLogAt{};
void NativeLobbyTick(void* self) {
 if(!readinessCommitted || !carCommitted || !hostObservedReady || hostObservationStopped)return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),holder=Read(hostContainer+0x2450),data=Read(hostSession+0x1EC);
 if(owner!=loadOwner || network!=configuredNetwork || holder!=hostHolder || Read(holder+0x18)!=hostSession || Read(data+0x1028)!=committedPlayer || !SelectedCareerLoaded()) {if(!lobbyAttempted)Log("LOBBY REJECT ownership\r\n");lobbyAttempted=1;return;}
 if(Read(holder+8)!=1)return;
 const U32 component=native_components::Find(Read,committedPlayer,At(0x1490690)),state=Read(component+0x64),table=Read(state),getter=Read(table);
 U32 descriptor{};if((Read(getter)&0xff)==0xB8 && (Read(getter+5)&0xff)==0xC3)descriptor=Read(getter+1);
 int livery{};if(!ReadStoredLivery(committedVehicleOwner,livery))livery=-2;
 char line[420]{};
 if(state!=observedPlayerState || GetTickCount64()-lobbyLogAt>5000) {
  sprintf_s(line,"LOBBY observe state=%08x vtable=%08x descriptor=%08x active=%u ready=%u livery=%d ack=%u\r\n",state,table,descriptor,(Read(state+0x60)>>8)&0xff,Read(committedReadyOwner+0x5C)&0xff,livery,lobbyAcknowledged?1:0);Log(line);observedPlayerState=state;lobbyLogAt=GetTickCount64();
 }
 if(lobbyAttempted)return;
 if(Read(component)!=At(0x1335C88) || table!=At(0x1337030) || descriptor!=At(0x1490C24) || (Read(state+0x60)&0xffff)!=0x0101 || (Read(state+0x70)&0xff)!=0 || livery!=resolvedLivery || (Read(committedReadyOwner+0x5C)&0xff)!=1) {lobbyAttempted=1;Log("LOBBY REJECT committed lobby/state prerequisites\r\n");return;}
 if(!reinterpret_cast<U32(__thiscall*)(void*)>(At(0x816260))(reinterpret_cast<void*>(state)))return;
 const U32 root=Read(state+0x64);
 const U32 publisher=Read(root)==At(0x1335AB4)?root:native_components::Find(Read,root,At(0x14907CC));
 const U32 vector=native_components::Find(Read,publisher+0x54,At(0x14907E8)),array=Read(vector),capacity=Read(vector+4),count=Read(vector+8);
 sprintf_s(line,"LOBBY publisher root=%08x root_vt=%08x publisher=%08x publisher_vt=%08x game_backlink=%08x vector=%08x array=%08x capacity=%u count=%u\r\n",root,Read(root),publisher,Read(publisher),Read(publisher+0xAC),vector,array,capacity,count);Log(line);
 if(Read(publisher)!=At(0x1335AB4) || Read(publisher+0xAC)!=owner || Read(Read(publisher)+0x10)!=At(0x831F50) || !vector || array<0x10000 || !count || count>128 || count>capacity || capacity>1024 || array>0x7fffffff-count*4) {lobbyAttempted=1;Log("LOBBY REJECT publisher/vector\r\n");return;}
 U32 activeCurrent{},observerSnapshot[128]{};bool subscribersSafe=true;
 for(U32 i=0;i<count;++i) {
  const U32 observer=Read(array+i*4);observerSnapshot[i]=observer;if(!observer)continue;
  for(U32 j=0;j<i;++j)if(observerSnapshot[j]==observer)subscribersSafe=false;
  const U32 observerTable=Read(observer);
  if(observerTable!=At(0x1336EEC)) {
   const bool ignored=KnownIgnoredLobbyObserver(observer,network,data);
   sprintf_s(line,"LOBBY subscriber index=%u observer=%08x table=%08x code30_ignored=%u ownership_verified=%u\r\n",i,observer,observerTable,ignored?1:0,ignored?1:0);Log(line);
   if(!ignored)subscribersSafe=false;continue;
  }
  const U32 target=Read(observer+4),targetTable=Read(target),active=(Read(target+0x60)>>8)&0xff;
  sprintf_s(line,"LOBBY subscriber index=%u observer=%08x table=%08x target=%08x target_vt=%08x active=%u\r\n",i,observer,observerTable,target,targetTable,active);Log(line);
  if(observerTable!=At(0x1336EEC) || target<0x10000 || target>0x7fffff00 || targetTable<At(0x12F0000) || targetTable>At(0x1400000) || active>1) {subscribersSafe=false;continue;}
  if(active) {
   if(target!=state || targetTable!=At(0x1337030) || Read(target+0x64)!=root || Read(targetTable+0x28)!=At(0x8B9D90))subscribersSafe=false;
   else ++activeCurrent;
  }
 }
 for(U32 i=0;i<count;++i)if(Read(array+i*4)!=observerSnapshot[i])subscribersSafe=false;
 if(!IgnoredLobbyCodeSignatures())subscribersSafe=false;
 if(!subscribersSafe || activeCurrent!=1 || Read(vector)!=array || Read(vector+4)!=capacity || Read(vector+8)!=count || Read(component+0x64)!=state) {lobbyAttempted=1;Log("LOBBY REJECT unaudited or changed subscriber\r\n");return;}
 if(InterlockedCompareExchange(&lobbyAttempted,1,0))return;
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)) {Log("LOBBY REJECT allocator\r\n");return;}
 struct Event {U32 table,code;};static_assert(sizeof(Event)==8);Event event{};
 reinterpret_cast<void*(__thiscall*)(void*,U32)>(At(0x80FA10))(&event,0x1E);
 if(event.table!=At(0x1333B78) || event.code!=0x1E) {Log("LOBBY REJECT event constructor\r\n");return;}
 sprintf_s(line,"LOBBY CALL native-publisher event=fe_in_lobby code=30 publisher=%08x state=%08x real_host=1 selected_car=%d ready=1 harness_members=1 thread=%lu\r\n",publisher,state,resolvedLivery,GetCurrentThreadId());Log(line);
 reinterpret_cast<void(__thiscall*)(void*,const Event*)>(At(0x831F50))(reinterpret_cast<void*>(publisher),&event);
 if(Read(component+0x64)!=state || (Read(state+0x70)&0xff)!=1) {Log("LOBBY REJECT acknowledgement latch\r\n");return;}
 lobbyAcknowledged=true;Log("LOBBY native acknowledgement latched; engine owns next transition; frontend_calls=0 forced_state=0\r\n");
}
bool LobbySignatures() {
 constexpr unsigned char ctor[]{0x8B,0xC1,0x8B,0x4C,0x24,0x04,0xC7,0x00};
 constexpr unsigned char dispatch[]{0x51,0x53,0x8B,0x5C,0x24,0x0C,0x8B,0x03,0x8B,0x10};
 constexpr unsigned char guard[]{0x8B,0x41,0x58,0x85,0xC0,0x74,0x12};
 constexpr unsigned char ret[]{0xC2,0x04,0x00};
 return IgnoredLobbyCodeSignatures() && trace::ReadableEqual(reinterpret_cast<void*>(At(0x80FA10)),ctor,sizeof(ctor)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x8A8980)),dispatch,sizeof(dispatch)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x816260)),guard,sizeof(guard)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x8A89DB)),ret,sizeof(ret)) && Read(At(0x1335AB4)+0x10)==At(0x831F50) && Read(At(0x1336EEC)+8)==At(0x8C1B70) && Read(At(0x1337030)+0x28)==At(0x8B9D90) && (Read(At(0x831F50))&0xff)==0xE9 && Read(At(0x831F50)+1)+At(0x831F50)+5==At(0x8A8980);
}
