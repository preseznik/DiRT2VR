// Read through the game's real protected-buffer import, into caller-owned bytes.
bool ReadStoredLivery(U32 owner,int& livery) {
 if(Read(owner)!=At(0x1336410) || Read(owner+0x58)!=At(0x133497C) || Read(owner+0x78)!=owner+0x70 || (Read(owner+0x64)&0xff))return false;
 const U32 manager=Read(owner+0x5C),buffer=Read(manager+4),offset=Read(owner+0x60),size=Read(buffer);
 if(manager<0x10000 || buffer<0x10000 || size<8 || size>0x1000000 || offset>size-8 || buffer>0x7ffffff3-offset)return false;
 U32 values[2]{};
 const auto status=reinterpret_cast<HRESULT(__stdcall*)(void*,U32,void*,U32)>(At(0xE09022))(reinterpret_cast<void*>(buffer),offset,values,8);
 if(FAILED(status) || Read(owner+0x5C)!=manager || Read(manager+4)!=buffer || Read(owner+0x60)!=offset)return false;
 livery=static_cast<int>(values[0]);return true;
}
volatile LONG carCommitAttempted{};
U32 committedPlayer{},committedVehicleOwner{},committedLiveryAdapter{};
bool carCommitted{};
void NativeCarCommitTick(void* self) {
 if(carCommitAttempted || (!localChoice.valid || resolvedVehicle!=vehicleRequest.vehicle) || resolvedLivery<=0 || !joinObservedReady || joinStopped) return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),container=Read(network+0x2174),holder=Read(container+0x2450);
 const U32 session=Read(holder+0x18),data=Read(session+0x1EC),root=Read(data+0x1028);
 if(owner!=loadOwner || network!=configuredNetwork || container!=searchContainer || holder!=searchHolder || session!=joinedSession || !SelectedCareerLoaded()) {
  carCommitAttempted=1;Log("CAR COMMIT REJECT owner changed\r\n");return;
 }
 if(Read(holder+8)!=2 || !(Read(session+0x6C)&0xff) || Read(session+0x24)!=1 || Read(data)!=At(0x1336248) || Read(root)!=At(0x13359AC)) {
  if(GetTickCount64()-joinBegan>5000) {carCommitAttempted=1;Log("CAR COMMIT REJECT local player not ready\r\n");}return;
 }
 const U32 vehicleOwner=native_components::Find(Read,root,At(0x1490784));
 const U32 liveryAdapter=native_components::Find(Read,vehicleOwner,At(0x1490730));
 if(Read(vehicleOwner)!=At(0x1336410) || Read(liveryAdapter)!=At(0x1333720) || Read(liveryAdapter+4)!=vehicleOwner) {
  if(GetTickCount64()-joinBegan>5000) {carCommitAttempted=1;Log("CAR COMMIT REJECT local vehicle component chain\r\n");}return;
 }
 if(InterlockedCompareExchange(&carCommitAttempted,1,0))return;
 const U32 networkIndex=Read(At(0x147F3F8));
 if(networkIndex>=512 || Read(loadContext+networkIndex*4)!=network || Read(Read(owner+0x2250)+8)!=loadContext) {Log("CAR COMMIT REJECT context network identity\r\n");return;}
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)) {Log("CAR COMMIT REJECT allocator\r\n");return;}
 if(!RevalidateLocalChoice()){Log("CAR REJECT local choice changed before native commit\r\n");return;}
 native_event_contract::LocalPlayerContext context{At(0x12F0B30),0,0};
 if(!reinterpret_cast<native_event_contract::InitializeLocalPlayerContextFn>(At(0x503E20))(&context,reinterpret_cast<void*>(loadContext),1) || context.context!=loadContext || context.containerIndex!=1) {Log("CAR COMMIT REJECT local context initialization\r\n");return;}
 int before{};if(!ReadStoredLivery(vehicleOwner,before)) {Log("CAR COMMIT REJECT protected read before\r\n");return;}
 char line[300]{};sprintf_s(line,"CAR COMMIT CALL native-livery player=%08x vehicle_owner=%08x adapter=%08x before=%d requested=%d vehicle_id=%d thread=%lu\r\n",root,vehicleOwner,liveryAdapter,before,resolvedLivery,resolvedVehicle,GetCurrentThreadId());Log(line);
 reinterpret_cast<native_event_contract::SetLocalPlayerLiveryFn>(At(0x51BC00))(&context,resolvedLivery);
 int after{};
 if(!ReadStoredLivery(vehicleOwner,after) || Read(data+0x1028)!=root || native_components::Find(Read,root,At(0x1490784))!=vehicleOwner || native_components::Find(Read,vehicleOwner,At(0x1490730))!=liveryAdapter || Read(liveryAdapter+4)!=vehicleOwner || after!=resolvedLivery || !RevalidateLocalChoice()) {
  Log("CAR COMMIT REJECT native readback\r\n");return;
 }
 committedPlayer=root;committedVehicleOwner=vehicleOwner;committedLiveryAdapter=liveryAdapter;carCommitted=true;
 sprintf_s(line,"CAR COMMIT readback vehicle_id=%d profile_livery=%d network_livery=%d registry_call=0 race_calls=0\r\n",resolvedVehicle,resolvedLivery,after);Log(line);
}
bool CarCommitSignatures() {
 constexpr unsigned char commit[]{0x8b,0x41,0x04,0x8b,0x15};
 constexpr unsigned char context[]{0x8b,0x44,0x24,0x08,0x89,0x41,0x08,0x8b,0x44,0x24,0x04};
 constexpr unsigned char rootGetter[]{0x8b,0x81,0x28,0x10,0,0,0xc3};
 return trace::ReadableEqual(reinterpret_cast<void*>(At(0x51BC00)),commit,sizeof(commit)) &&
  trace::ReadableEqual(reinterpret_cast<void*>(At(0x503E20)),context,sizeof(context)) &&
  trace::ReadableEqual(reinterpret_cast<void*>(At(0x8118A0)),rootGetter,sizeof(rootGetter)) &&
  (Read(At(0xE09022))&0xffff)==0x25FF && Read(At(0xE09022)+2)==At(0x128C750) &&
  Read(At(0x1336248)+0x30)==At(0x8118A0) && Read(At(0x1336410))==At(0x8B4640) && Read(At(0x1333720))==At(0x866E40);
}
