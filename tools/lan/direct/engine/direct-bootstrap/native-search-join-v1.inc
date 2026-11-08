// One native LAN search; records remain owned by the completed native model.
// This probe does not join any discovered server or forge enumeration records.
volatile LONG searchAttempted{};
bool searchCompleted{};
bool searchAccepted{},searchActiveObserved{},searchStopped{};
U32 searchContainer{},searchHolder{},searchObject{},searchModel{};
ULONGLONG searchBegan{};
dirt2_engine_bootstrap::SessionDescriptor searchDescriptor{0,1,0,0,{0,0},{0,0,0}};
dirt2_engine_bootstrap::EventAdvertisement searchFilter{};
void NativeSearchTick(void* self) {
 if(!eventConfigured || searchStopped)return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),event=Read(network+0x2244),container=Read(network+0x2174),holder=Read(container+0x2450),searcher=container+0x2460;
 const U32 manager=Read(network+0x1088),helper=Read(container+0x259C),post=Read(container+0x25A0);
 if(owner!=loadOwner || network!=configuredNetwork || event!=configuredEvent || !SelectedCareerLoaded() || Read(network)!=At(0x1336604) || Read(container)!=At(0x13364EC) || Read(holder)!=At(0x133549C) || Read(holder+4)!=At(0x1335474) || Read(manager)!=At(0x135CCF8) || Read(At(0x14925E4))!=manager || Read(manager+0x54)!=0 || Read(searcher)!=At(0x135D120) || Read(helper)!=At(0x13340DC) || Read(helper+0xC)!=Read(container+0x2578) || Read(post)!=At(0x1334FD4) || Read(post+0x164)!=helper || Read(post+0x168)!=Read(container+0x2578) || Read(searcher+0xD0)!=post || !Read(At(0x15DAA04)) || Read(Read(At(0x15DAA04))+4)!=2) {searchStopped=true;Log("SEARCH REJECT graph or LAN identity\r\n");return;}
 if(Read(holder+8)!=0 || Read(holder+0x18) || Read(holder+0x24) || (Read(holder+0x800)&0xff)) {searchStopped=true;Log("SEARCH REJECT occupied holder\r\n");return;}
 if(!searchAttempted) {
  if(InterlockedCompareExchange(&searchAttempted,1,0))return;
  if(Read(searcher+0xC)!=0 || !reinterpret_cast<U32(__thiscall*)(void*)>(At(0x818E40))(reinterpret_cast<void*>(container))) {searchStopped=true;Log("SEARCH REJECT native preflight\r\n");return;}
  const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
  if(!scope.Valid(allocator)) {searchStopped=true;Log("SEARCH REJECT allocator\r\n");return;}
  const U32 key=Read(At(0x147DE90));const auto integer=[&](U32 offset){return static_cast<int>(Read(event+offset)^key);};
  searchFilter={integer(0x6C),integer(0x68),integer(0x74),integer(0x78),integer(0x8C),integer(0x98),integer(0x64)};
  searchContainer=container;searchHolder=holder;searchObject=searcher;searchBegan=GetTickCount64();
  char line[300]{};sprintf_s(line,"SEARCH CALL native-lan container=%08x searcher=%08x thread=%lu race=%d class=%d rounds=%d join_calls=0\r\n",container,searcher,GetCurrentThreadId(),searchFilter.race,searchFilter.vehicleClass,searchFilter.roundCount);Log(line);
  const auto result=reinterpret_cast<unsigned char(__thiscall*)(void*,const dirt2_engine_bootstrap::SessionDescriptor*,const dirt2_engine_bootstrap::EventAdvertisement*)>(At(0x864AE0))(reinterpret_cast<void*>(container),&searchDescriptor,&searchFilter);
  searchAccepted=result!=0;searchActiveObserved=Read(searcher+0xC)==1;
  sprintf_s(line,"SEARCH initiation al=%u state=%u model=%08x completed=0\r\n",result,Read(searcher+0xC),Read(searcher+0x34));Log(line);
  if(!searchAccepted || !searchActiveObserved) {searchStopped=true;Log("SEARCH REJECT initiation or missing active phase\r\n");return;}
 }
 if(container!=searchContainer || holder!=searchHolder || searcher!=searchObject) {searchStopped=true;Log("SEARCH REJECT ownership changed\r\n");return;}
 const U32 state=Read(searcher+0xC);const auto elapsed=GetTickCount64()-searchBegan;
 if(state==1) {if(elapsed>10000) {searchStopped=true;Log("SEARCH timeout; no retry or synthetic completion\r\n");}return;}
 if(state!=0 || (Read(searcher+0x10)&0xff) || (Read(searcher+0x54)&0xff) || Read(Read(searcher+0x2C))!=At(0x135CFC0) || Read(Read(searcher+0x2C)+8)!=0) {searchStopped=true;Log("SEARCH native failure\r\n");return;}
 searchStopped=true;searchModel=Read(searcher+0x34);
 const int count=reinterpret_cast<int(__thiscall*)(void*)>(At(0xC8FD30))(reinterpret_cast<void*>(searcher));
 if(searchModel<0x10000 || count<0 || count>20 || Read(searchModel)!=container+0x1E20 || Read(searchModel+4)!=Read(searcher+4) || Read(searchModel+8)!=Read(searcher+8) || Read(searchModel+0xC)!=0 || Read(searchModel+0x78)!=static_cast<U32>(count)) {Log("SEARCH REJECT completed model bounds\r\n");return;}
 const U32 array=Read(searchModel+0x70),capacity=Read(searchModel+0x74);
 if(array!=searchModel+0x80 || capacity!=20 || static_cast<U32>(count)>capacity || (count && (array<0x10000 || array>0x7fffffff-static_cast<U32>(count)*4))) {Log("SEARCH REJECT completed record array\r\n");return;}
 char line[320]{};sprintf_s(line,"SEARCH complete elapsed=%llu model=%08x count=%d capacity=%u error=0 join_calls=0\r\n",elapsed,searchModel,count,capacity);Log(line);
 for(int i=0;i<count;++i) {
  const U32 record=reinterpret_cast<U32(__thiscall*)(void*,int)>(At(0xC94F00))(reinterpret_cast<void*>(searcher),i);
  if(record<0x10000 || record>0x7ffff920 || Read(record)!=At(0x13357C0) || Read(record+0x10)!=0 || Read(record+0x30)!=Read(container+0x1E20) || record!=Read(array+i*4) || Read(searcher+0x34)!=searchModel || Read(searcher+0xC)!=0) {Log("SEARCH REJECT native record identity\r\n");return;}
  sprintf_s(line,"SEARCH native-record index=%d record=%08x selection=none join_calls=0\r\n",i,record);Log(line);
 }
 searchCompleted=true;
}
bool SearchSignatures() {
 constexpr unsigned char begin[]{0x53,0x56,0x57,0x8B,0xF1,0x8B,0x8E,0x9C,0x25,0,0};
 constexpr unsigned char preflight[]{0x8B,0x81,0x50,0x24,0,0,0x85,0xC0};
 constexpr unsigned char count[]{0x8B,0x49,0x34,0x33,0xC0,0x85,0xC9};
 constexpr unsigned char ret[]{0xC2,0x08,0x00};
 return trace::ReadableEqual(reinterpret_cast<void*>(At(0x864AE0)),begin,sizeof(begin)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x818E40)),preflight,sizeof(preflight)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0xC8FD30)),count,sizeof(count)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x864B3D)),ret,sizeof(ret)) && Read(At(0x135D120)+4)==At(0xC9E140) && Read(At(0x1334FD4)+4)==At(0x864290);
}
