// Set only the native local-player readiness component; never force protocol state.
volatile LONG readyAttempted{};
U32 committedReadyOwner{};
bool readinessCommitted{};
void NativeReadyTick(void* self) {
 if(readyAttempted || !carCommitted || !hostObservedReady || hostObservationStopped)return;
 const U32 owner=reinterpret_cast<U32>(self),network=Read(owner+0x2CF0),container=Read(network+0x2174),holder=Read(container+0x2450),session=Read(holder+0x18),data=Read(session+0x1EC);
 if(owner!=loadOwner || network!=configuredNetwork || container!=hostContainer || holder!=hostHolder || session!=hostSession || !SelectedCareerLoaded() || Read(data+0x1028)!=committedPlayer) {readyAttempted=1;Log("READY REJECT owner changed\r\n");return;}
 if(Read(holder+8)!=1)return;
 const U32 ready=native_components::Find(Read,committedPlayer,At(0x14905E4));
 int livery{};
 if(Read(ready)!=At(0x1336370) || native_components::Find(Read,committedPlayer,At(0x1490784))!=committedVehicleOwner || !ReadStoredLivery(committedVehicleOwner,livery) || livery!=resolvedLivery) {readyAttempted=1;Log("READY REJECT local component or car\r\n");return;}
 if(InterlockedCompareExchange(&readyAttempted,1,0))return;
 const U32 allocator=Read(network+0x100C);NativeAllocationScope scope(allocator);
 if(!scope.Valid(allocator)) {Log("READY REJECT allocator\r\n");return;}
 const U32 before=Read(ready+0x5C)&0xff;
 char line[280]{};sprintf_s(line,"READY CALL lower-native owner=%08x player=%08x before=%u thread=%lu\r\n",ready,committedPlayer,before,GetCurrentThreadId());Log(line);
 reinterpret_cast<void(__thiscall*)(void*,bool)>(At(0x852BE0))(reinterpret_cast<void*>(ready),true);
 if(native_components::Find(Read,committedPlayer,At(0x14905E4))!=ready || Read(ready)!=At(0x1336370) || (Read(ready+0x5C)&0xff)!=1 || (Read(ready+0x88)==0xffffffff && Read(ready+0x8C)==0x7fffffff) || !ReadStoredLivery(committedVehicleOwner,livery) || livery!=resolvedLivery) {Log("READY REJECT native readback\r\n");return;}
 committedReadyOwner=ready;readinessCommitted=true;
 sprintf_s(line,"READY readback value=1 timestamp=%08x%08x network_livery=%d protocol_state_forced=0 race_calls=0\r\n",Read(ready+0x8C),Read(ready+0x88),livery);Log(line);
}
bool ReadySignatures() {
 constexpr unsigned char setter[]{0x8A,0x44,0x24,0x04,0x83,0xEC,0x08,0x84,0xC0,0x56,0x8B,0xF1,0x88,0x46,0x5C};
 constexpr unsigned char ret[]{0xC2,0x04,0x00};
 return trace::ReadableEqual(reinterpret_cast<void*>(At(0x852BE0)),setter,sizeof(setter)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x852C1B)),ret,sizeof(ret)) && trace::ReadableEqual(reinterpret_cast<void*>(At(0x852C3D)),ret,sizeof(ret));
}
