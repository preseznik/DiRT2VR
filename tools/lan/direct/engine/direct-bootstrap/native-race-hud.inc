// The held menu flow never runs StateSetOsdEnabled/StateSetOsdRenderEnabled.
// Use those states' native operations after the verified Direct race start.
// The stock OSD jobs still own widget updates, rendering and race data.
namespace direct_race_hud {
struct RenderMessage { U32 table{},enabled{}; };
static_assert(sizeof(RenderMessage)==8);
bool attempted{};

bool Signatures() {
    constexpr unsigned char enable[]{0x8A,0x44,0x24,0x04,0x88,0x81,0x20,0x01,0x00,0x00,0xC2,0x04,0x00};
    constexpr unsigned char enqueue[]{0x53,0x55,0x56,0x8B,0x74,0x24,0x10,0x8B,0x06,0x8B,0x50,0x04,0x8B,0xD9};
    return trace::ReadableEqual(reinterpret_cast<void*>(At(0x7A1B50)),enable,sizeof(enable)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x45CF70)),enqueue,sizeof(enqueue)) &&
        Read(At(0x13305D0))==At(0x7D2CB0) &&
        Read(At(0x1313D30))==At(0x659880) && Read(At(0x1313D34))==At(0x659890) &&
        Read(At(0x1313D3C))==At(0x4B3B50) && Read(At(0x1313D40))==At(0x4B3B60);
}

void Reject(unsigned reason,const char* why) {
    attempted=true;
    char line[160]{};sprintf_s(line,"HUD status=-1 reason=%u detail=%s\r\n",reason,why);Log(line);
}

void Tick(void* self) {
    if(attempted)return;
    using namespace initial_load_v2;
    const auto& l=ledger;
    const U32 game=reinterpret_cast<U32>(self);
    if(!activation_live_v1::attempt.naturalGreenObserved || game!=l.game ||
       direct_race_progress::verifiedDrivingSession.load(std::memory_order_acquire)!=l.session)return;
    if(l.thread!=GetCurrentThreadId() || game!=heldOwner || game!=loadOwner || holdFailed ||
       headlessFailed || !coreReady || coreObserved!=7 || !direct_race_progress::BoundRoster(l)) {
        Reject(1,"race owner/session changed");return;
    }
    bootstrap_hold::Objects held{};
    if(bootstrap_hold::Inspect(Read,base,heldFlow,held)!=bootstrap_hold::Gate::Ready || held.owner!=game ||
       held.context!=loadContext || GetCurrentThreadId()!=static_cast<DWORD>(holdThread)) {
        Reject(2,"held frontend owner changed");return;
    }
    const U32 context=held.context,frontend=held.frontend;
    const U32 registryIndex=Read(At(0x147DD6C)),queueIndex=Read(At(0x147DD0C)),allocatorIndex=Read(At(0x147DD18));
    if(registryIndex>=512 || queueIndex>=512 || allocatorIndex>=512) {
        Reject(3,"native context indices");return;
    }
    const U32 registry=Read(context+4*registryIndex),queue=Read(context+4*queueIndex);
    const U32 allocator=Read(context+4*allocatorIndex),glue=Read(game+0x2248);
    if(Read(frontend+8)!=context || allocator!=Read(frontend+0xC) || Read(allocator)!=At(0x13CC8E0) ||
       Read(registry)!=At(0x12F6378) || !Ptr(queue,4) || !Ptr(Read(queue),0x18) ||
       Read(glue)!=At(0x13305CC) || Read(glue+0x108)!=game ||
       Read(At(0x147E180))!=At(0x1436264) || Read(At(0x147E184))!=sizeof(RenderMessage) || !Signatures()) {
        Reject(4,"native OSD bindings");return;
    }
    unsigned char enabled{},network{},peerReady{};
    if(!initial_load_v2::Byte(glue+0x120,enabled) || enabled>1 ||
       !initial_load_v2::Byte(glue+0x167,network) || network>1 ||
       !initial_load_v2::Byte(glue+0x168,peerReady) || peerReady>1 || (network && !peerReady)) {
        Reject(5,"native OSD peer state");return;
    }
    NativeAllocationScope scope(allocator);
    if(!scope.Valid(allocator)) {Reject(6,"native UI allocator");return;}
    // Same typed lookup and getter as both stock OSD-enable states. Respect
    // a player's disabled overall HUD instead of replacing their preference.
    const U32 setting=reinterpret_cast<U32(__thiscall*)(void*,U32,const char*)>(At(0x484010))(
        reinterpret_cast<void*>(registry),At(0x147E270),reinterpret_cast<const char*>(At(0x12FF498)));
    const U32 table=Read(setting),getter=Read(table+0x14);
    if(!Ptr(setting,4) || table<At(0x12F0000) || table>=At(0x1400000) ||
       getter<base || getter>=At(0x12F0000)) {Reject(7,"overall HUD preference unavailable");return;}
    const bool overall=reinterpret_cast<U32(__thiscall*)(void*)>(getter)(reinterpret_cast<void*>(setting))!=0;
    if(!overall) {attempted=true;Log("HUD status=0 overall=0\r\n");return;}
    if(Read(game+0x2248)!=glue || Read(glue+0x108)!=game || Read(frontend+8)!=context ||
       Read(context+4*queueIndex)!=queue || !scope.Valid(allocator)) {
        Reject(8,"native OSD ownership changed");return;
    }
    attempted=true;
    RenderMessage message{At(0x1313D2C),1};
    // Queue copies the eight-byte message; no stack object is retained.
    reinterpret_cast<void(__thiscall*)(void*,const RenderMessage*)>(At(0x45CF70))(
        reinterpret_cast<void*>(queue),&message);
    reinterpret_cast<void(__thiscall*)(void*,unsigned char)>(At(0x7A1B50))(
        reinterpret_cast<void*>(glue),1);
    char line[240]{};sprintf_s(line,"HUD status=1 overall=1 enabled=1 render_requested=1 frontend_calls=0 owner=%08x session=%08x glue=%08x\r\n",
        game,l.session,glue);Log(line);
}
}
