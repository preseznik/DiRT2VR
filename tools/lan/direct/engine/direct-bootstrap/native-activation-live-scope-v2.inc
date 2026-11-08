// Scoped initial-load helper; invoked only by the owned held frontend job.
// Requires native-initial-load-v2.inc and the stock NativeAllocationScope.
#include "native-race-progress.inc"
void NativeInitialLoadAndActivateScopedV2(void* self,U32 container,U32 holder,U32 session,U32 transport,bool host,
                               initial_load_v2::Permit permit) {
    using namespace initial_load_v2;
    if(!initialConfigCommitted || trace::Environment(L"DIRT2VR_ENGINE_INITIAL_LOAD")!=L"1") {
        if(ledger.phase!=Phase::Dormant)Fail("scoped loader authorization/configuration lost");return;
    }
    if(!permit.exactImageValidated || !permit.exclusiveInitialLoad || !permit.nativeQueuesAdvancing ||
       permit.thread!=GetCurrentThreadId() || permit.thread!=static_cast<DWORD>(holdThread)) {
        if(ledger.phase!=Phase::Dormant)Fail("scoped loader task/queue permit lost");return;
    }
    direct_race_progress::Observe(reinterpret_cast<U32>(self));
    bootstrap_hold::Objects objects{};
    const U32 game=reinterpret_cast<U32>(self),frontend=Read(game+0x2250),context=Read(frontend+8),slot=Read(At(0x147D1F8)),index=Read(At(0x147DD18));
    if(bootstrap_hold::Inspect(Read,base,heldFlow,objects)!=bootstrap_hold::Gate::Ready || objects.owner!=game || objects.frontend!=frontend ||
       objects.context!=context || context!=loadContext || game!=loadOwner || Read(frontend)!=At(0x1300B10) || slot>=1088 || index>=512 ||
       !InitialLoadSignaturesV2()) {Fail("scoped setup owner/signature gate");return;}
    // Exact original577E29 reads this allocator before463FB0. Context identity
    // is corroboration; N+100C is deliberately not used as its source.
    const U32 allocator=Read(frontend+0xC),previous=reinterpret_cast<U32>(TlsGetValue(slot));
    if(!Ptr(allocator) || Read(context+4*index)!=allocator || (permit.setupAllocator && permit.setupAllocator!=allocator)) {Fail("scoped frontend allocator identity");return;}
    permit.setupAllocator=allocator;
    {
        NativeAllocationScope scope(allocator);
        if(!scope.Valid(allocator) || Read(game+0x2250)!=frontend || Read(frontend+8)!=context || Read(frontend+0xC)!=allocator) {Fail("native setup allocation scope");return;}
        if(!NativeDriverSettingsReady(game)) {Fail("selected-profile driving bindings unavailable");return;}
        NativeInitialLoadTickV2(self,container,holder,session,transport,host,permit);
        // Revalidate after loader callbacks before any finalizer native operation.
        if(!scope.Valid(allocator) || Read(game+0x2250)!=frontend || Read(frontend+8)!=context || Read(frontend+0xC)!=allocator || Read(context+4*index)!=allocator) {
            Fail("scoped setup identity changed after loader");return;
        }
        NativeFinalizationTickV7({permit});
        if(!scope.Valid(allocator) || Read(game+0x2250)!=frontend || Read(frontend+8)!=context || Read(frontend+0xC)!=allocator || Read(context+4*index)!=allocator) {
            Fail("scoped setup identity changed during native call");return;
        }
        // Only actual native registration completion can enter acknowledgement.
        // The adapter freshly verifies retirement, resources, audio, physics,
        // roster, settings and registrations. Activation has its own fresh native service/frame proof.
        if(ledger.phase==Phase::FinalizationBlocked && finalization_v2::state.phase==finalization_v2::Phase::RegisteredBlocked) {
            if(!NativeDriverSettingsReady(game)){loaded_ack_adapter_v2::Reject("selected-profile driving bindings changed");return;}
            NativeLoadedAckTickV2();
            NativeActivationTickV2();
            if(!scope.Valid(allocator) || Read(game+0x2250)!=frontend || Read(frontend+8)!=context || Read(frontend+0xC)!=allocator || Read(context+4*index)!=allocator)
                loaded_ack_adapter_v2::Reject("allocation scope changed across acknowledgement/activation");
        }
    }
    if(Read(At(0x147D1F8))!=slot || reinterpret_cast<U32>(TlsGetValue(slot))!=previous)Fail("native setup allocation frame restoration");
}
