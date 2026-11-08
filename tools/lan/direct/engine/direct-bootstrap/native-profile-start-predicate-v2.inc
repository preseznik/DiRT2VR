// Initial direct-start data dependency only; no frontend callback or value reset.
// Include after the native initial loader. Called on its existing scoped owner thread.
#include "native-profile-start-guards-v2.inc"
namespace profile_start_predicate_v2 {
struct Result { U32 object{},registry{},allocator{}; bool created{},ready{},failed{}; };
Result result;
bool KnownFalse(U32 object) {
    return initial_load_v2::Ptr(object,16) && Read(object)==At(0x12F1680) &&
        Read(Read(object))==At(0x4C67B0) && Read(Read(object)+0x14)==At(0x4C67D0) && initial_load_v2::Flag(object+0xC,0);
}
U32 Lookup(U32 registry) {
    return reinterpret_cast<U32(__thiscall*)(void*,U32,U32)>(At(0x484010))(
        reinterpret_cast<void*>(registry),At(0x147CFD4),At(0x1319D08));
}
bool Ensure(U32 game) {
    using namespace initial_load_v2;
    if(result.failed)return false;
    if(!ProfileStartSignaturesV2()){result.failed=true;return false;}
    const U32 frontend=Read(game+0x2250),context=Read(frontend+8);
    const U32 registryIndex=Read(At(0x147DD6C)),allocatorIndex=Read(At(0x147DD18));
    if(game!=loadOwner || game!=heldOwner || context!=loadContext || !SelectedCareerLoaded() ||
       holdFailed || GetCurrentThreadId()!=static_cast<DWORD>(holdThread) || registryIndex>=512 || allocatorIndex>=512) {
        result.failed=true;return false;
    }
    const U32 registry=Read(context+4*registryIndex),allocator=Read(context+4*allocatorIndex);
    U32 frame{};
    if(Read(registry)!=At(0x12F6378) || Read(allocator)!=At(0x13CC8E0) || Read(frontend+0xC)!=allocator ||
       Read(Read(allocator)+8)!=At(0x11A1740) || Read(Read(allocator)+0x10)!=At(0x119AA30) || !NativeFrame(allocator,frame)) {
        result.failed=true;return false;
    }
    const U32 existing=Lookup(registry);
    if(result.ready) {
        if(existing!=result.object || registry!=result.registry || allocator!=result.allocator || !KnownFalse(existing))result.failed=true;
        return !result.failed;
    }
    result.registry=registry;result.allocator=allocator;
    if(existing) {
        result.object=existing;result.ready=KnownFalse(existing);result.failed=!result.ready;
        Log(result.ready?"PREDICATE existing native false value preserved\r\n":"PREDICATE stopped: existing true or unknown value preserved\r\n");
        return result.ready;
    }
    // The registered pointer must come from the native context allocator.
    const U32 storage=reinterpret_cast<U32(__thiscall*)(void*,U32,U32)>(At(0x11A1740))(
        reinterpret_cast<void*>(allocator),16,1);
    if(!Ptr(storage,16)){result.failed=true;return false;}
    const U32 constructed=reinterpret_cast<U32(__thiscall*)(void*,bool)>(At(0x4C6790))(reinterpret_cast<void*>(storage),false);
    if(constructed!=storage || !KnownFalse(storage)) {
        reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x4C67F0))(reinterpret_cast<void*>(storage),0);
        reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x119AA30))(reinterpret_cast<void*>(allocator),storage);
        result.failed=true;Log("PREDICATE native constructor readback failed before registration\r\n");return false;
    }
    const bool inserted=reinterpret_cast<bool(__thiscall*)(void*,U32,U32,U32)>(At(0x49E750))(
        reinterpret_cast<void*>(registry),storage,At(0x147CFD4),At(0x1319D08)); // AL, never full EAX.
    if(!inserted) {
        reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x4C67F0))(reinterpret_cast<void*>(storage),0);
        reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x119AA30))(reinterpret_cast<void*>(allocator),storage);
        result.failed=true;Log("PREDICATE insert rejected; unregistered allocation released; existing key preserved\r\n");return false;
    }
    result.created=true;result.object=storage; // Keep native registration until process exit, even on failed readback.
    result.ready=Lookup(registry)==storage && KnownFalse(storage) && Read(game+0x2250)==frontend &&
        Read(frontend+8)==context && Read(context+4*registryIndex)==registry && Read(context+4*allocatorIndex)==allocator;
    result.failed=!result.ready;
    Log(result.ready?"PREDICATE created native false profile-change data; no race-start call\r\n":"PREDICATE insertion readback failed; retained until process exit\r\n");
    return result.ready;
}
}
