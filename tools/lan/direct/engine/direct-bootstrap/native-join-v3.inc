// This adapter needs an explicitly supplied real native host session-key ID.
// The observed LAN key may be shared; exact peer-address matching is required.
native_peer_address::Token joinAddress{};
unsigned long long hostXuid{};
U32 joinKeyLow{},joinKeyHigh{};
volatile LONG joinAttempted{};
bool joinAccepted{},joinStopped{},joinObservedReady{};
U32 joinedSession{},joinedTransport{};
ULONGLONG joinBegan{},joinLogAt{};
int joinedLastState=0x7fffffff;
bool ParseJoinTarget() {
 const auto key=trace::Environment(L"DIRT2VR_ENGINE_JOIN_KEY");if(key.size()!=16)return false;
 unsigned char bytes[8]{};
 for(unsigned i=0;i<16;++i) {const wchar_t c=key[i];const int n=c>=L'0'&&c<=L'9'?c-L'0':c>=L'a'&&c<=L'f'?c-L'a'+10:c>=L'A'&&c<=L'F'?c-L'A'+10:-1;if(n<0)return false;bytes[i/2]=static_cast<unsigned char>((bytes[i/2]<<4)|n);}
 memcpy(&joinKeyLow,bytes,4);memcpy(&joinKeyHigh,bytes+4,4);
 const auto address=trace::Environment(L"DIRT2VR_ENGINE_JOIN_ADDRESS");if(address.size()!=76)return false;
 for(unsigned i=0;i<76;++i){const wchar_t c=address[i];const int n=c>=L'0'&&c<=L'9'?c-L'0':c>=L'a'&&c<=L'f'?c-L'a'+10:c>=L'A'&&c<=L'F'?c-L'A'+10:-1;if(n<0)return false;joinAddress[i/2]=static_cast<unsigned char>((joinAddress[i/2]<<4)|n);}
 const auto uid=trace::Environment(L"DIRT2VR_ENGINE_HOST_XUID");if(uid.size()!=16)return false;
 for(const wchar_t c:uid){const int n=c>=L'0'&&c<=L'9'?c-L'0':c>=L'a'&&c<=L'f'?c-L'a'+10:c>=L'A'&&c<=L'F'?c-L'A'+10:-1;if(n<0)return false;hostXuid=(hostXuid<<4)|n;}
 return (joinKeyLow|joinKeyHigh)!=0 && hostXuid && native_peer_address::Valid(joinAddress);
}
void NativeJoinTick(void* self) {
 if(!searchCompleted || joinStopped)return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),container=Read(network+0x2174),holder=Read(container+0x2450);
 if(owner!=loadOwner || network!=configuredNetwork || container!=searchContainer || holder!=searchHolder || Read(container)!=At(0x13364EC) || Read(holder)!=At(0x133549C) || Read(holder+4)!=At(0x1335474) || !SelectedCareerLoaded()) {joinStopped=true;Log("JOIN REJECT owned graph\r\n");return;}
 if(!joinAttempted) {
  if(InterlockedCompareExchange(&joinAttempted,1,0))return;
  unsigned long long localXuid{};U32 localInstance{};
  if(!NativeLocalIdentity(localXuid,localInstance) || localXuid==hostXuid || localInstance==native_peer_address::Instance(joinAddress) || !PeerIdentitySignatures()) {joinStopped=true;Log("JOIN REJECT self identity or loaded code\r\n");return;}
  native_join_guard::Selection selected{};
  if(!native_join_guard::Select(Read,base,container,joinKeyLow,joinKeyHigh,joinAddress,selected) || selected.model!=searchModel) {joinStopped=true;Log("JOIN REJECT no consistent genuine native host identity\r\n");return;}
  const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);if(!scope.Valid(allocator)) {joinStopped=true;Log("JOIN REJECT allocator\r\n");return;}
  static constexpr char name[]="Stage1Direct",empty[]="";joinBegan=GetTickCount64();
  char line[300]{};sprintf_s(line,"JOIN CALL native-record container=%08x model=%08x record=%08x aliases=%u thread=%lu menu_calls=0\r\n",container,selected.model,selected.record,selected.aliases,GetCurrentThreadId());Log(line);
  const auto accepted=reinterpret_cast<dirt2_native_join::JoinAbi>(At(0x8510D0))(reinterpret_cast<void*>(container),reinterpret_cast<void*>(selected.record),1,1,name,1,empty);
  joinAccepted=accepted!=0;const U32 copy=Read(holder+0x684);
  sprintf_s(line,"JOIN initiation al=%u state=%d owned_record=%08x completed=0\r\n",accepted,static_cast<int>(Read(holder+8)),copy);Log(line);
  if(!joinAccepted || copy<0x10000 || copy==selected.record || Read(copy)!=At(0x13357C0) || Read(copy+0x698)!=joinKeyLow || Read(copy+0x69C)!=joinKeyHigh || !native_peer_address::Matches(Read,copy+0x668,joinAddress)) {joinStopped=true;Log("JOIN REJECT initiation or owned native record\r\n");return;}
 }
 const int state=static_cast<int>(Read(holder+8));const U32 session=Read(holder+0x18),transport=Read(holder+0x24),data=Read(session+0x1EC);
 if((joinedSession && session!=joinedSession) || (joinedTransport && transport!=joinedTransport) || (session && (Read(session)!=At(0x1335844) || Read(session+4)!=At(0x1335808) || Read(session+8)!=At(0x1335800))) || (transport && Read(transport)!=At(0x135CFC0))) {joinStopped=true;Log("JOIN REJECT session identity changed\r\n");return;}
 if(session)joinedSession=session;if(transport)joinedTransport=transport;
 const auto now=GetTickCount64();
 if(state!=joinedLastState || now-joinLogAt>5000) {char line[350]{};sprintf_s(line,"JOIN observe elapsed=%llu state=%d session=%08x transport=%08x transport_state=%u active=%u role=%u data=%08x cancel=%u\r\n",now-joinBegan,state,session,transport,Read(transport+8),Read(session+0x6C)&255,Read(session+0x24),data,Read(holder+0x800)&255);Log(line);joinedLastState=state;joinLogAt=now;}
 if(!joinObservedReady && state==2 && transport && Read(transport+8)==2 && session && (Read(session+0x6C)&255) && Read(session+0x24)==1 && Read(data)==At(0x1336248) && !(Read(holder+0x800)&255)) {joinObservedReady=true;Log("JOIN native client completion observed; race_start=0 current_race_admission=0\r\n");}
 if(state==0 || state==-16 || state==-11 || state==-10 || state==-9 || (Read(holder+0x800)&255)) {joinStopped=true;Log("JOIN native failure or teardown; no retry\r\n");}
 if(!joinObservedReady && now-joinBegan>45000) {joinStopped=true;Log("JOIN completion timeout; no synthetic completion\r\n");}
}
