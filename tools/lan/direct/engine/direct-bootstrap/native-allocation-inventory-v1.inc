// Passive inventory on the existing owner callback, after all scoped calls return.
bool nativeAllocationObserved{};
void NativeAllocationInventory() {
 if(nativeAllocationObserved || careerPhase!=3 || !loadContext || !loadOwner)return;
 const U32 slot=Read(At(0x147D1F8));
 if(slot>=1088)return;
 const U32 frame=reinterpret_cast<U32>(TlsGetValue(slot)),allocator=Read(frame+4),index=Read(At(0x147DD18));
 nativeAllocationObserved=true;
 char line[460]{};
 sprintf_s(line,"ALLOC inventory thread=%lu slot=%u frame=%08x frame_key=%08x expected_key=%08x allocator=%08x allocator_vt=%08x prior=%08x frontend_allocator=%08x network_allocator=%08x context_allocator=%08x mutation=0\r\n",GetCurrentThreadId(),slot,frame,Read(frame),At(0x147D1F8),allocator,Read(allocator),Read(frame+8),Read(Read(loadOwner+0x2250)+0xC),Read(Read(loadOwner+0x2CF0)+0x100C),index<512?Read(loadContext+index*4):0);Log(line);
}
