// Initialize the chosen game discipline before applying the lobby's single round.
volatile LONG eventObserved{};
bool eventConfigured{};
U32 configuredEvent{},configuredNetwork{};
bool EventMutationSignatures();
bool EventSignatures() {
 constexpr unsigned char lookup[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xc8,0,0,0};
 constexpr unsigned char ancestry[]{0x85,0xc9,0x74,0x0e,0x8b,0x44,0x24,0x04,0x3b,0xc8};
 constexpr unsigned char edit[]{0x83,0xec,0x08,0xf6,0x05};
 return EventMutationSignatures() && trace::ReadableEqual(reinterpret_cast<void*>(At(0x533130)),lookup,sizeof(lookup)) &&
  trace::ReadableEqual(reinterpret_cast<void*>(At(0xEB9CF0)),ancestry,sizeof(ancestry)) &&
  trace::ReadableEqual(reinterpret_cast<void*>(At(0x84B130)),edit,sizeof(edit));
}
bool SelectedCareerLoaded() {
 if(careerPhase!=3 || headlessFailed || !selectedSave || Read(loadSdu+0x54)!=3) return false;
 const U32 begin=Read(loadProfile+0x24),end=Read(loadProfile+0x28);
 if(begin<0x10000 || end<begin || end-begin>0x128*4096 || (end-begin)%0x128) return false;
 std::string first,last;U32 user{};unsigned fields{};
 for(U32 p=begin;p<end;p+=0x128) {
  char key[65]{},value[65]{};if(!Text(p,key,sizeof(key))) return false;
  if(!strcmp(key,"savegame_user_id")) {if((Read(p+0x40)&0xff)!=2) return false;user=Read(p+0x48);fields|=1;}
  if(!strcmp(key,"Player_FirstName") || !strcmp(key,"Player_LastName")) {
   if((Read(p+0x40)&0xff)!=4 || !Text(p+0x48,value,sizeof(value))) return false;
   if(!strcmp(key,"Player_FirstName")){first=value;fields|=2;}else{last=value;fields|=4;}
  }
 }
 return fields==7 && user && first+(last.empty()?"":" ")+last==expectedProfileName;
}
void NativeEventTick(void* self) {
 if(eventObserved || !SelectedCareerLoaded()) return;
 if(InterlockedCompareExchange(&eventObserved,1,0)) return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),manager=Read(network+0x1088);
 const U32 event=Read(network+0x2244),provider=Read(event+0x5C),container=Read(network+0x2174),holder=Read(container+0x2450);
 if(owner!=loadOwner || Read(owner+0x2284)!=loadProfile || Read(network)!=At(0x1336604) ||
  Read(manager)!=At(0x135CCF8) || Read(At(0x14925E4))!=manager || Read(manager+0x54)!=0 ||
  Read(container)!=At(0x13364EC) || !holder || Read(holder+8)!=0 || Read(holder+0x18)!=0 ||
  Read(event)!=At(0x13366EC) || Read(event+0x58)!=Read(network+0x1004) ||
  provider!=network+0x1010 || Read(provider)!=At(0x1335AD8) || Read(At(0x1335AD8))!=At(0x8B08B0)) {
  Log("EVENT REJECT owned cold graph changed\r\n");return;
 }
 for(U32 i=0;i<5;++i) {const U32 r=event+0xA4+0x24*i;if(Read(r)!=At(0x1333840) || Read(r+4)!=event) {Log("EVENT REJECT round parent\r\n");return;}}
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)) {Log("EVENT REJECT allocator\r\n");return;}
 const U32 type=reinterpret_cast<U32(__thiscall*)(void*)>(At(0x8B08B0))(reinterpret_cast<void*>(provider));
 const bool itself=reinterpret_cast<bool(__thiscall*)(void*,U32)>(At(0xEB9CF0))(reinterpret_cast<void*>(type),At(0x14907D4));
 const U32 mapping=itself?provider:reinterpret_cast<U32(__thiscall*)(void*,U32)>(At(0x533130))(reinterpret_cast<void*>(provider),1);
 const bool editable=reinterpret_cast<bool(__thiscall*)(void*)>(At(0x84B130))(reinterpret_cast<void*>(event));
 char line[300]{};sprintf_s(line,"EVENT native-provider event=%08x root=%08x root_type=%08x mapping=%08x mapping_vt=%08x defaults_entry=%08x owner=%08x editable=%u setters_called=0\r\n",
 event,provider,type,mapping,Read(mapping),Read(Read(mapping)+0x50),Read(mapping+4),editable?1:0);Log(line);
 if(!mapping || Read(mapping)!=At(0x1333BA8) || Read(Read(mapping)+0x50)!=At(0x843E20) || Read(mapping+4)!=loadOwner || Read(network+0x107C)!=loadOwner || Read(loadOwner+0x2CF0)!=network || !editable) {
  Log("EVENT REJECT mapping provider unavailable or ownership unverified\r\n");return;
 }
 const U32 databaseService=Read(loadOwner+0x2E84);
 sprintf_s(line,"EVENT database service=%08x vtable=%08x loaded=%u database=%08x\r\n",databaseService,Read(databaseService),Read(databaseService+0x20)&0xff,Read(databaseService+0xC));Log(line);
 if(databaseService<0x10000 || !(Read(databaseService+0x20)&0xff) || Read(databaseService+0xC)<0x10000) {Log("EVENT REJECT database not loaded\r\n");return;}
 // Validate numeric domains through the game's own DB mapping before setters.
 using Mapping= int(__thiscall*)(void*,int);
 if(Read(At(0x1333BA8)+0x48)!=At(0x80FFE0) || Read(At(0x1333BA8)+0x5C)!=At(0x82EE70)) {Log("EVENT REJECT mapping dispatch\r\n");return;}
 const auto& request=RxRequest();
 const int raceRow=reinterpret_cast<Mapping>(At(0x80FFE0))(reinterpret_cast<void*>(mapping),request.race);
 const int classRow=reinterpret_cast<Mapping>(At(0x82EE70))(reinterpret_cast<void*>(mapping),request.vehicleClass);
 const bool rootRules=reinterpret_cast<bool(__thiscall*)(void*,U32)>(At(0xEB9CF0))(reinterpret_cast<void*>(type),At(0x1490600));
 const U32 rules=rootRules?provider:reinterpret_cast<U32(__thiscall*)(void*,U32)>(At(0x590380))(reinterpret_cast<void*>(provider),1);
 sprintf_s(line,"EVENT mapping race_enum=%d race_row=%d class_enum=%d class_row=%d rules=%08x rules_vt=%08x\r\n",request.race,raceRow,request.vehicleClass,classRow,rules,Read(rules));Log(line);
 if(raceRow!=request.race+1 || classRow!=rx_choice_v1::Classes[request.vehicleClass].event || rules<0x10000 || Read(rules)<base || Read(rules)>=At(0x1470000)) {Log("EVENT REJECT native mapping/rules\r\n");return;}
 if(!(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) || !(Read(At(0x147E2C8))&1) ||
    ((Read(event+0x60)^(Read(At(0x147E2C0))&1))&0xff)!=0 || (Read(event+0x1B4)^Read(At(0x147DE90)))!=0) {Log("EVENT REJECT event state\r\n");return;}
 if(!RxCatalogBound(owner,true)) {Log("EVENT REJECT native RX catalog relationship\r\n");return;}
 reinterpret_cast<void(__thiscall*)(void*,bool,int,int)>(At(0x84A8C0))(reinterpret_cast<void*>(event),true,request.race,-1);
 const U32 round=event+0xA4,key=Read(At(0x147DE90));
 const auto integer=[&](U32 ptr){return static_cast<int>(Read(ptr)^key);};
 const int track=static_cast<short>((Read(round+0xC)^Read(At(0x147FC68)))&0xffff);
 sprintf_s(line,"EVENT defaults race=%d track=%d location=%d laps=%d collision=%d\r\n",integer(round+8),track,integer(round+0x10),integer(round+0x18),integer(round+0x1C));Log(line);
 // First rounds from the stock net_event_default table in native discipline order.
 struct DefaultRound {int model, location, laps;};
 constexpr DefaultRound defaults[]{{137,1,0},{143,4,3},{161,8,0},{151,7,3},{132,2,0}};
 const auto first=defaults[request.race];
 if(integer(round+8)!=request.race || track!=first.model || integer(round+0x10)!=first.location ||
    (integer(round+0x18)!=first.laps && !(first.laps==0 && integer(round+0x18)==1)) || integer(round+0x1C)!=0) {
  Log("EVENT REJECT native defaults differ from selected discipline\r\n");return;
 }
 using SetEvent=void(__thiscall*)(void*,int,bool);
 using SetRound=void(__thiscall*)(void*,int);
 const U32 eventMethods[]{0x854640,0x854680,0x8546C0,0x854700};const int eventValues[]{request.race,1,0,request.vehicleClass};
 for(unsigned i=0;i<4;++i) reinterpret_cast<SetEvent>(At(eventMethods[i]))(reinterpret_cast<void*>(event),eventValues[i],false);
 const U32 roundMethods[]{0x5F94C0,0x5F94E0,0x5F9500,0x5F9520,0x5F9540};const int roundValues[]{request.race,request.selected.model,request.selected.location,RxLaps(),0};
 for(unsigned i=0;i<5;++i) reinterpret_cast<SetRound>(At(roundMethods[i]))(reinterpret_cast<void*>(round),roundValues[i]);
 if(integer(event+0x64)!=request.race || integer(event+0x68)!=1 || integer(event+0x6C)!=0 || integer(event+0x74)!=request.vehicleClass || integer(round+8)!=request.race ||
    static_cast<short>((Read(round+0xC)^Read(At(0x147FC68)))&0xffff)!=RxRequest().selected.model || integer(round+0x10)!=RxRequest().selected.location || integer(round+0x18)!=RxLaps() || integer(round+0x1C)!=0) {Log("EVENT REJECT native setter readback\r\n");return;}
 if(!RxEventMatches(event) || !RxCatalogBound(owner,false)) {Log("EVENT REJECT frozen RX postconditions\r\n");return;}
 reinterpret_cast<void(__thiscall*)(void*)>(At(0x83F710))(reinterpret_cast<void*>(event));
 sprintf_s(line,"EVENT configured native_race=%d native_class=%d rounds=%d track=%d location=%d laps=%d collision=0 notified=1 session_created=0 race_started=0\r\n",integer(event+0x64),integer(event+0x74),integer(event+0x68),RxRequest().selected.model,RxRequest().selected.location,RxLaps());Log(line);
 configuredEvent=event;configuredNetwork=network;eventConfigured=true;

}

bool EventMutationSignatures() {
 constexpr unsigned char lookup[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xc8,0,0,0};
 constexpr unsigned char defaults[]{0x53,0x8b,0xd9,0x56,0x8b,0x73,0x5c};
 constexpr unsigned char roundSetter[]{0x56,0x8b,0xf1,0x8b,0x4e,0x04};
 constexpr unsigned char notify[]{0x56,0x8b,0xf1,0x85,0xf6,0x74,0x36};
 if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0x590380)),lookup,sizeof(lookup)) ||
    !trace::ReadableEqual(reinterpret_cast<void*>(At(0x84A8C0)),defaults,sizeof(defaults)) ||
    !trace::ReadableEqual(reinterpret_cast<void*>(At(0x83F710)),notify,sizeof(notify))) return false;
 for(U32 va:{0x5F94C0u,0x5F94E0u,0x5F9500u,0x5F9520u,0x5F9540u})
  if(!trace::ReadableEqual(reinterpret_cast<void*>(At(va)),roundSetter,sizeof(roundSetter))) return false;
 return true;
}
