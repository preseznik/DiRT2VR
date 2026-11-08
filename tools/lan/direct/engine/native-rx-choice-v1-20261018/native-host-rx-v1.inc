// One native host attempt on the owned engine update thread. No frontend calls.
volatile LONG hostAttempted{};
bool hostAccepted{},hostObservedReady{},hostObservationStopped{};
U32 hostContainer{},hostHolder{},hostSession{},hostTransport{};
int hostLastState=0x7fffffff;
ULONGLONG hostBegan{},hostLogAt{};
dirt2_engine_bootstrap::HostDescriptors hostDescriptors{};
constexpr char hostName[]="Stage1Direct",hostSecondary[]="";
bool HostSignatures() {
 constexpr unsigned char create[]{0x8b,0x44,0x24,0x0c,0x53,0x56,0x8b,0x74,0x24,0x0c,0x8a,0x5e,0x09};
 return trace::ReadableEqual(reinterpret_cast<void*>(At(0x864A90)),create,sizeof(create));
}
void NativeHostTick(void* self) {
 if(!eventConfigured || hostObservationStopped) return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),event=Read(network+0x2244);
 const U32 container=Read(network+0x2174),holder=Read(container+0x2450),manager=Read(network+0x1088);
 if(owner!=loadOwner || network!=configuredNetwork || event!=configuredEvent || !SelectedCareerLoaded() ||
    Read(network)!=At(0x1336604) || Read(container)!=At(0x13364EC) || holder<0x10000 || Read(holder)!=At(0x133549C) || Read(holder+4)!=At(0x1335474) ||
    Read(manager)!=At(0x135CCF8) || Read(At(0x14925E4))!=manager || Read(manager+0x54)!=0 ||
    Read(event)!=At(0x13366EC) || Read(event+0x58)!=Read(network+0x1004) || Read(event+0x5C)!=network+0x1010 ||
    !Read(At(0x15DAA04)) || Read(Read(At(0x15DAA04))+4)!=2) {
  hostObservationStopped=true;Log("HOST REJECT owned graph or LAN binding changed\r\n");return;
 }
 if(!hostAttempted) {
  if(InterlockedCompareExchange(&hostAttempted,1,0)) return;
  if(Read(holder+8)!=0 || Read(holder+0x18)!=0 || Read(holder+0x24)!=0 || (Read(holder+0x800)&0xff) ||
     !(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) || !(Read(At(0x147E2C8))&1) ||
     ((Read(event+0x60)^(Read(At(0x147E2C0))&1))&0xff)!=0) {
   hostObservationStopped=true;Log("HOST REJECT not idle/unbound or protected keys absent\r\n");return;
  }
  const U32 key=Read(At(0x147DE90)),round=event+0xA4;
  const auto integer=[&](U32 ptr){return static_cast<int>(Read(ptr)^key);};
  dirt2_engine_bootstrap::NativeEventSnapshot n{};
  n.race=integer(event+0x64);n.rounds=integer(event+0x68);n.team=integer(event+0x6C);n.event70=integer(event+0x70);
  n.vehicleClass=integer(event+0x74);n.event78=integer(event+0x78);n.event84=integer(event+0x84);n.event88=integer(event+0x88);
  n.event8C=integer(event+0x8C);n.event90=integer(event+0x90);n.event98=integer(event+0x98);n.currentRound=integer(event+0x1B4);
  n.location=integer(round+0x10);n.laps=integer(round+0x18);n.collision=integer(round+0x1C);
  n.trackModel=static_cast<short>((Read(round+0xC)^Read(At(0x147FC68)))&0xffff);
  dirt2_engine_bootstrap::Manifest manifest{};manifest.trackModel=RxRequest().selected.model;manifest.location=RxRequest().selected.location;
  if(!RxEventMatches(event)){hostObservationStopped=true;Log("HOST REJECT frozen RX event\r\n");return;}
  const auto prepared=dirt2_engine_bootstrap::PrepareLanHost(manifest,n,hostDescriptors);
  // The offline helper deliberately remains non-executable. This isolated
  // adapter adds the live ownership/career/thread/event/allocator guards.
  if(prepared!=dirt2_engine_bootstrap::PrepareResult::PreparedExecutionBlocked) {
   hostObservationStopped=true;Log("HOST REJECT descriptor translation\r\n");return;
  }
  const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
  if(!scope.Valid(allocator) || !RxCatalogBound(owner,false)) {hostObservationStopped=true;Log("HOST REJECT allocator\r\n");return;}
  hostContainer=container;hostHolder=holder;hostBegan=GetTickCount64();
  char line[300]{};sprintf_s(line,"HOST CALL native-create container=%08x holder=%08x holder_vt=%08x thread=%lu race=1 class=1 track=%d menu_calls=0\r\n",container,holder,Read(holder),GetCurrentThreadId(),RxRequest().selected.model);Log(line);
  const auto result=reinterpret_cast<dirt2_engine_bootstrap::CreateAbi>(At(0x864A90))(reinterpret_cast<void*>(container),&hostDescriptors.session,&hostDescriptors.event,&hostDescriptors.round,hostName,hostSecondary);
  hostAccepted=result!=0;
  sprintf_s(line,"HOST initiation al=%u state=%d session=%08x transport=%08x completed=0\r\n",result,static_cast<int>(Read(holder+8)),Read(holder+0x18),Read(holder+0x24));Log(line);
  if(!hostAccepted) {hostObservationStopped=true;return;}
 }
 if(container!=hostContainer || holder!=hostHolder) {hostObservationStopped=true;Log("HOST REJECT container identity changed\r\n");return;}
 const int state=static_cast<int>(Read(holder+8));const U32 session=Read(holder+0x18),transport=Read(holder+0x24);
 const ULONGLONG now=GetTickCount64();
 if((hostSession && session && hostSession!=session) || (hostTransport && transport && hostTransport!=transport)) {
  hostObservationStopped=true;Log("HOST REJECT allocated identity replaced\r\n");return;
 }
 if((session && (Read(session)!=At(0x1335844) || Read(session+4)!=At(0x1335808) || Read(session+8)!=At(0x1335800))) || (transport && Read(transport)!=At(0x135CFC0))) {
  hostObservationStopped=true;Log("HOST REJECT allocated type mismatch\r\n");return;
 }
 if(session)hostSession=session;if(transport)hostTransport=transport;
 const U32 transportState=transport?Read(transport+8):0xffffffff,active=session?(Read(session+0x6C)&0xff):0;
 const U32 role=session?Read(session+0x24):0xffffffff,treeOwner=session?Read(session+0x1EC):0;
 if(state!=hostLastState || now-hostLogAt>=5000) {
  char line[350]{};sprintf_s(line,"HOST observe elapsed=%llu state=%d session=%08x session_vt=%08x active=%u role=%u transport=%08x transport_vt=%08x transport_state=%u tree_owner=%08x tree_vt=%08x tree_getter=%08x cancel=%u\r\n",
    now-hostBegan,state,session,Read(session),active,role,transport,Read(transport),transportState,treeOwner,Read(treeOwner),Read(Read(treeOwner)+0x14),Read(holder+0x800)&0xff);Log(line);hostLogAt=now;hostLastState=state;
 }
 if(!hostObservedReady && state==1 && transport && transportState==1 && session && active && role==0 && treeOwner && !(Read(holder+0x800)&0xff)) {
  hostObservedReady=true;Log("HOST completion predicates observed; session-event copy and race execution disabled\r\n");
 }
 if(state==0 || state==-16 || state==-11 || state==-10 || state==-9 || (Read(holder+0x800)&0xff)) {hostObservationStopped=true;Log("HOST ended or failed; no retry\r\n");}
 if(!hostObservedReady && now-hostBegan>=45000) {hostObservationStopped=true;Log("HOST completion timeout; no retry or callback synthesis\r\n");}
}
