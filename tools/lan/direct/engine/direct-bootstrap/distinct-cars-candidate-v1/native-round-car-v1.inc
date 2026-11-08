// Preserve the harness selection after the native C1C -> B8C transition.
// One attempt at the real vehicle-selection state; no state writes or FE calls.
bool roundCarAttempted{},roundCarCommitted{};
U32 roundCarState{};
void NativeRoundCarTick(void* self,U32 container,U32 holder,U32 session,U32 transport,bool host) {
 if(roundCarAttempted || !lobbyAcknowledged || !carCommitted)return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),data=Read(session+0x1EC);
 const auto reject=[](const char* reason){roundCarAttempted=true;Log(reason);};
 const U32 wantedHolder=host?1u:2u,wantedRole=host?0u:1u;
 if(owner!=loadOwner || network!=configuredNetwork || Read(network+0x2174)!=container || Read(container)!=At(0x13364EC) ||
    Read(container+0x2450)!=holder || Read(holder)!=At(0x133549C) ||
    Read(holder+0x18)!=session || Read(holder+0x24)!=transport || (Read(holder+0x800)&0xff) ||
    Read(session)!=At(0x1335844) || Read(session+0x24)!=wantedRole || !(Read(session+0x6C)&0xff) ||
    Read(transport)!=At(0x135CFC0) || Read(transport+8)!=wantedHolder || Read(data)!=At(0x1336248) ||
    Read(data+0x1028)!=committedPlayer || !SelectedCareerLoaded()) {
  reject("ROUND CAR REJECT owned native session\r\n");return;
 }
 // The host legitimately passes through holder -1 after the C24 lobby ack.
 // Identity, active session and transport ownership above remain mandatory.
 const U32 holderState=Read(holder+8);
 if(host && holderState==0xffffffffu)return;
 if(holderState!=wantedHolder){reject("ROUND CAR REJECT native holder lifecycle\r\n");return;}
 const U32 component=native_components::Find(Read,committedPlayer,At(0x1490690)),state=Read(component+0x64),table=Read(state),getter=Read(table);
 if(Read(component)!=At(0x1335C88) || (Read(getter)&0xff)!=0xB8 || (Read(getter+5)&0xff)!=0xC3) {
  reject("ROUND CAR REJECT current-state getter\r\n");return;
 }
 const U32 descriptor=Read(getter+1);
 if(descriptor!=At(0x1490B8C)) {
  if(descriptor==At(0x1490BD4) || descriptor==At(0x1490B6C))reject("ROUND CAR REJECT missed selection or spectator transition\r\n");
  return;
 }
 roundCarAttempted=true;
 const U32 networkIndex=Read(At(0x147F3F8));
 if((Read(state+0x60)&0xffff)!=0x0101 || networkIndex>=512 || Read(loadContext+networkIndex*4)!=network ||
    Read(Read(owner+0x2250)+8)!=loadContext || native_components::Find(Read,committedPlayer,At(0x1490784))!=committedVehicleOwner ||
    Read(committedVehicleOwner)!=At(0x1336410) || native_components::Find(Read,committedVehicleOwner,At(0x1490730))!=committedLiveryAdapter ||
    Read(committedLiveryAdapter)!=At(0x1333720) || Read(committedLiveryAdapter+4)!=committedVehicleOwner ||
    (!localChoice.valid || resolvedVehicle!=vehicleRequest.vehicle) || resolvedLivery<=0 || !CarCommitSignatures()) {
  Log("ROUND CAR REJECT state/context/selection\r\n");return;
 }
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)){Log("ROUND CAR REJECT allocator\r\n");return;}
 if(!RevalidateLocalChoice()){Log("CAR REJECT local choice changed before native commit\r\n");return;}
 native_event_contract::LocalPlayerContext context{At(0x12F0B30),0,0};
 if(!reinterpret_cast<native_event_contract::InitializeLocalPlayerContextFn>(At(0x503E20))(&context,reinterpret_cast<void*>(loadContext),1) ||
    context.context!=loadContext || context.containerIndex!=1){Log("ROUND CAR REJECT local context\r\n");return;}
 int before{},after{};
 if(!ReadStoredLivery(committedVehicleOwner,before)){Log("ROUND CAR REJECT protected read\r\n");return;}
 // The native transition can choose an event default. Reapply through the same
 // checked setter used for the initial selection, never its protected storage.
 reinterpret_cast<native_event_contract::SetLocalPlayerLiveryFn>(At(0x51BC00))(&context,resolvedLivery);
 if(!ReadStoredLivery(committedVehicleOwner,after) || after!=resolvedLivery || Read(component+0x64)!=state ||
    Read(data+0x1028)!=committedPlayer || native_components::Find(Read,committedPlayer,At(0x1490784))!=committedVehicleOwner ||
    native_components::Find(Read,committedVehicleOwner,At(0x1490730))!=committedLiveryAdapter || !RevalidateLocalChoice()) {
  Log("ROUND CAR REJECT readback or ownership changed\r\n");return;
 }
 roundCarState=state;roundCarCommitted=true;
 char line[280]{};sprintf_s(line,"ROUND CAR committed role=%s natural_state=B8C before=%d selected=%d vehicle=%d frontend_calls=0 forced_state=0 thread=%lu\r\n",host?"host":"client",before,after,resolvedVehicle,GetCurrentThreadId());Log(line);
}
