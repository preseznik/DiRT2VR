// Owned-lab finalizer v6 with fresh physical/camera registration preconditions. Include after native-loaded-roster-bridge-v1.inc; no runtime entrypoint.
// No automatic entrypoint, DLL export, frontend call or protocol acknowledgement.
#include "../plugin-task-ownership-20261018/finalization-policy-v2.h"
#include "../plugin-task-ownership-20261018/run-observation-v2.h"
#include "../native-finalization-candidate-20261018/finalization-code-guards-v1.inc"
#include "../plugin-task-ownership-20261018/plugin-worker-guard-v1.h"
#include "../plugin-task-ownership-20261018/task-ownership-code-guards-v1.inc"
#include "../plugin-task-ownership-v3-20261018/live-session-guard-v3.h"
#include "../native-subroute-placement-20261018/subroute-contract-v1.h"
#include "../native-subroute-placement-20261018/subroute-code-guards-v1.h"
#include "../registration-prerequisites-v2-20261018/native-registration-prerequisites-v2.h"
namespace finalization_v2 {
using NoArgs=void(__thiscall*)(void*);
using Query=U32(__thiscall*)(void*);
struct Permit {
    initial_load_v2::Permit load;
};
struct State : RunObservation {
    Phase phase{Phase::Dormant}; DWORD thread{};ULONGLONG began{};
    U32 game{},render{},entity{},setup{},session{},data{},tree{},event{},roster{},config{},service{};
    U32 taskManager{},task{},routeOwner{},pluginOwner{},eventHandle{},mailbox[3]{},mailboxTable[3]{};
    U32 eventSnapshot[13]{},protectedKey{},protectedShortKey{};
    U32 entry[2]{},id[2]{},livery[2]{};
    bool pulseConsumed{},queueAccepted{},signalSeen{},registrationSubmitted{},hookInstalled{};
    HMODULE pinnedModule{};
    plugin_worker_v1::Snapshot workerBinding;
    Job job; // Never freed/reused; native queue and adapter retain this pointer.
};
State state;
NoArgs originalRun{};
void Note(const char* why) {
    char text[420]{};sprintf_s(text,"FINAL phase=%u reason=%s task=%08x job=%08x entered=%ld returned=%ld worker=%ld signal=%u registered=%u drive=blocked countdown=blocked event7=blocked\r\n",
        unsigned(state.phase),why,state.task,reinterpret_cast<U32>(&state.job),Load(state.entered),Load(state.returned),Load(state.worker),unsigned(state.signalSeen),unsigned(state.registrationSubmitted));Log(text);
}
void Fail(const char* why){if(state.phase!=Phase::Failed){state.phase=Phase::Failed;Note(why);}}

// Observe the exact original Run return, after its tail-called native unlock.
// Always preserve native behavior for every job. No exception is swallowed here.
void __fastcall ObserveRun(void* self,void*) {
    ObserveOriginal(state,self,originalRun);
}

bool HookIdentity() {
    if(!state.hookInstalled || !originalRun)return false;
    unsigned char code[5]{};
    if(!initial_load_v2::Bytes(At(0x6E05D0),code,5) || code[0]!=0xE9)return false;
    U32 relative{};memcpy(&relative,code+1,4);
    if(At(0x6E05D0)+5+relative!=reinterpret_cast<U32>(&ObserveRun))return false;
    // Exact first instruction is A1 abs32, then MinHook's five-byte return jump.
    unsigned char trampoline[10]{};U32 operand{},back{};
    if(!initial_load_v2::Bytes(reinterpret_cast<U32>(originalRun),trampoline,sizeof(trampoline)) || trampoline[0]!=0xA1 || trampoline[5]!=0xE9)return false;
    memcpy(&operand,trampoline+1,4);memcpy(&back,trampoline+6,4);
    return operand==At(0x148319C) && reinterpret_cast<U32>(originalRun)+10+back==At(0x6E05D0)+5;
}
bool InstallObservation() {
    if(state.hookInstalled || originalRun || !FinalizationSignatures(false))return false;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&ObserveRun),&state.pinnedModule))return false;
    // Existing harness initializes MinHook. No all-hooks enable/disable operation.
    if(MH_CreateHook(reinterpret_cast<void*>(At(0x6E05D0)),&ObserveRun,reinterpret_cast<void**>(&originalRun))!=MH_OK || !originalRun)return false;
    if(MH_EnableHook(reinterpret_cast<void*>(At(0x6E05D0)))!=MH_OK)return false;
    state.hookInstalled=true;
    return HookIdentity() && FinalizationSignatures(true);
}
// Source-specific lifetime boundary: existing pinned bridge, whole frontend
// job held, native core preparation actually completed, and same callback owner.
// No boolean can authorize a different loader or a general shutdown/reload.
bool HeldEngine(U32 game) {
    if(!heldFlow || heldOwner!=game || holdFailed || headlessFailed || !armed ||
       holdThread!=static_cast<LONG>(GetCurrentThreadId()) || !holdCalls || !coreReady || coreObserved!=7)return false;
    bootstrap_hold::Objects o{};
    if(bootstrap_hold::Inspect(Read,base,heldFlow,o)!=bootstrap_hold::Gate::Ready || o.owner!=game ||
       o.frontend!=Read(game+0x2250) || !originalFrontendUpdate)return false;
    unsigned char jump[5]{};U32 relative{};
    if(!initial_load_v2::Bytes(At(0x577DE0),jump,5) || jump[0]!=0xE9)return false;
    memcpy(&relative,jump+1,4);
    return At(0x577DE0)+5+relative==reinterpret_cast<U32>(&HoldFrontendUpdate);
}
bool WorkerBinding(bool capture) {
    plugin_worker_v1::Snapshot current{};
    const auto wait=[](U32 handle){return static_cast<U32>(WaitForSingleObject(reinterpret_cast<HANDLE>(handle),0));};
    const auto gate=plugin_worker_v1::Inspect(Read,wait,base,state.taskManager,state.task,current);
    if(gate!=plugin_worker_v1::Gate::Ready)return false;
    if(capture){state.workerBinding=current;return true;}
    return plugin_worker_v1::Same(current,state.workerBinding);
}
bool EventIdentity() {
    return Read(state.game+0x2E78)==At(0x13C88A8) && Read(state.game+0x2E7C)==state.eventHandle && state.eventHandle &&
        Read(At(0x13C88B0))==At(0x4568A0) && Read(At(0x13C88BC))==At(0x115FB90) && Read(At(0x13C88C0))==At(0x115FBC0);
}
// Restore the live owner chain lost when the initial loader becomes terminal.
// The sole lifecycle relaxation is boolean registration acknowledgement flags
// after this candidate has submitted the original native messages.
bool BoundLiveSession(bool registered) {
    auto& l=initial_load_v2::ledger;
    if(l.game!=loadOwner || l.network!=configuredNetwork || l.selectedMode!=3)return false;
    unsigned localCount{};U32 localId{};
    for(const auto& player:l.players)if(player.root==committedPlayer){++localCount;localId=player.nativeId;}
    if(localCount!=1)return false;
    const finalization_live_v3::SessionBinding binding{l.game,l.network,l.container,l.holder,l.session,
        l.transport,l.data,l.tree,committedPlayer,localId,l.host};
    if(finalization_live_v3::Inspect(Read,base,binding,registered)!=finalization_live_v3::Gate::Ready)return false;
    // Other original Owners(false) identities are still live requirements, not
    // permission to rely on cached loader success after native fields change.
    if(Read(At(0x1482D8C))!=l.vehicles || Read(l.vehicles)!=At(0x1325688) || Read(l.vehicles+4)!=At(0x132567C) ||
       Read(l.vehicles+0xC)!=l.game || Read(l.vehicles+0x10)!=l.render || Read(l.entity+4)!=At(0x1321668) ||
       Read(At(0x15D9A04))!=l.resources || Read(l.resources)!=At(0x135B85C) || Read(l.resources+0x58)!=l.resources+0x5C ||
       Read(l.game+0x2E88)!=l.manager || Read(l.manager)!=At(0x132B1CC) || Read(l.manager+0x34)!=l.service ||
       Read(l.manager+0xAC0)!=1 || Read(l.service+0x38)!=l.database || Read(At(0x147D1F8))!=l.tlsSlot)return false;
    U32 frame{};
    return initial_load_v2::NativeFrame(l.allocator,frame) && frame==l.tickFrame;
}
constexpr U32 EventFields[]{0,0x58,0x5C,0x64,0x68,0x74,0x1B4,0xA4,0xA8,0xAC,0xB0,0xB4,0xBC};
bool BoundOwners(bool registered,bool afterAck=false) {
    using initial_load_v2::Ptr;using initial_load_v2::Flag;
    auto& s=state;auto& l=initial_load_v2::ledger;
    if(!BoundLiveSession(registered))return false;
    if(l.phase!=initial_load_v2::Phase::FinalizationBlocked || s.game!=l.game || s.render!=l.render || s.session!=l.session ||
       s.roster!=l.roster || s.config!=l.config || s.service!=l.service || s.entity!=l.entity || s.setup!=l.setup ||
       Read(s.game+0x2DFC)!=s.setup || Read(s.setup)!=At(0x1324A2C) || Read(At(0x1482D5C))!=s.render || Read(s.render)!=At(0x1324164) ||
       Read(s.game+0x224C)!=s.entity || s.entity!=s.render+0x1090 || Read(s.entity)!=At(0x1321674) ||
       Read(s.game+0x1054)!=s.taskManager || Read(s.game+0x22FC)!=s.routeOwner || Read(s.game+0x2278)!=s.pluginOwner ||
       Read(s.session+0x1EC)!=s.data || Read(s.data+0x1024)!=s.tree || Read(s.data+0x1028)!=committedPlayer ||
       native_components::Find(Read,s.tree,At(0x14906B0))!=s.event || native_components::Find(Read,s.tree,At(0x1490868))!=s.roster || Read(s.roster)!=At(0x13360B4) || Read(s.roster+0x64)!=2 ||
       Read(s.roster+0x60)!=8 || !Ptr(Read(s.roster+0x5C),8) || !EventIdentity() || !SelectedCareerLoaded())return false;
    const U32 local=Read(native_components::Find(Read,committedPlayer,At(0x1490690))+0x64);
    const U32 sessionState=Read(native_components::Find(Read,s.tree,At(0x1490690))+0x64);
    if((!afterAck && !initial_load_v2::LocalLoadingState(Read(local)-base+0x400000,Read(Read(local))-base+0x400000,
        NativeStateDescriptor(committedPlayer)-base+0x400000,Read(local+0x60)&0xffff)) || Read(local+0x58)!=s.session ||
        !NativeLoadedSessionState(s.tree,s.session,afterAck) || Read(sessionState+0x58)!=s.session)return false;
    ConfigRef service(0x132AC70),config(0x132A450);
    if(reinterpret_cast<config_abi::AcquireKind>(At(0x734A70))(reinterpret_cast<void*>(l.manager),&service.ref,1)!=&service.ref || !service.Valid() || service.ref.pointer!=s.service ||
       reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(s.service),&config.ref)!=&config.ref || !config.Valid() || config.ref.pointer!=s.config)return false;
    const U32 begin=Read(s.config+0x20);
    if(!Ptr(begin,8) || Read(s.config+0x24)!=begin+8)return false;
    for(unsigned i=0;i<2;++i)if(Read(begin+4*i)!=s.entry[i] || Read(Read(s.roster+0x5C)+4*i)!=s.id[i] ||
        (Read(s.entry[i]+0xC0)&255)!=s.id[i] || Read(Read(s.entry[i]+0x40)+4)!=s.livery[i])return false;
    if(Read(At(0x147DE90))!=s.protectedKey || Read(At(0x147FC68))!=s.protectedShortKey)return false;
    for(unsigned i=0;i<13;++i)if(Read(s.event+EventFields[i])!=s.eventSnapshot[i])return false;
    ConfigPlayer players[2]{};if(!NativeLoadedRosterPlayers(players,afterAck))return false;
    for(unsigned i=0;i<2;++i) {
        unsigned found=2;for(unsigned j=0;j<2;++j)if(players[j].nativeId==l.players[i].nativeId)found=j;
        if(found==2 || players[found].identity!=l.players[i].identity || players[found].root!=l.players[i].root ||
           players[found].livery!=l.players[i].livery || players[found].vehicle!=l.players[i].vehicle || players[found].record!=l.players[i].record)return false;
    }
    if(!HeldEngine(s.game) || !l.metadataApplied || !initial_load_v2::MetadataMatches() || !TaskOwnershipSignatures())return false;
    if(s.task && !WorkerBinding(false))return false;
    if(!registered && (!Flag(s.game+0x2E80,0) || !Flag(s.game+0x2E81,0)))return false;
    for(unsigned i=0;i<20;++i)if(Read(s.setup+0x10+4*i)!=l.copies[i] ||
        (i?Read(s.game+initial_load_v2::GameFields[i]):s.game)!=l.copies[i])return false;
    if(!l.vehiclesComplete || l.audio.state!=initial_load_v2::JobState::Succeeded || l.track.state!=initial_load_v2::JobState::Succeeded ||
       l.audioPatch.state!=initial_load_v2::JobState::Succeeded || !l.trackPatchIssued ||
       (!l.trackPatchAbsent && l.trackPatch.state!=initial_load_v2::JobState::Succeeded))return false;
    for(unsigned i=0;i<3;++i)if(s.mailbox[i] && (Read(s.mailbox[i])!=s.mailboxTable[i] || !Flag(s.mailbox[i]+0x38,1)))return false;
    return true;
}
bool CaptureMailboxes() {
    const char* channels[]{"application_object","update_task","render_task"};
    for(unsigned i=0;i<3;++i) {
        if(state.mailbox[i])return false;
        // This function returns caller-owned strong storage; retain it whole run.
        const U32 result=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0x117B460))(&state.mailbox[i],channels[i]);
        if(result!=reinterpret_cast<U32>(&state.mailbox[i]) || !initial_load_v2::Ptr(state.mailbox[i],0x1A4) ||
           !initial_load_v2::Flag(state.mailbox[i]+0x38,1))return false;
        state.mailboxTable[i]=Read(state.mailbox[i]);
        if(!initial_load_v2::Ptr(state.mailboxTable[i],12) || Read(state.mailboxTable[i]+4)==0 || Read(state.mailboxTable[i]+8)==0)return false;
    }
    return state.mailbox[0]!=state.mailbox[1] && state.mailbox[0]!=state.mailbox[2] && state.mailbox[1]!=state.mailbox[2];
}
#include "../native-subroute-placement-20261018/optional-streaming-hint-v1.inc"
bool PollEvent() {
    if(state.signalSeen)return true;
    const U32 code=reinterpret_cast<Query>(At(0x4568A0))(reinterpret_cast<void*>(state.game+0x2E78));
    if(code==0)state.signalSeen=true;
    else if(code!=0x3C){Fail("native finalization event failed");return false;}
    return true;
}
Completion SampleCompletion(bool owners) {
    return {owners,state.queueAccepted,state.signalSeen,Read(state.pluginOwner+0x14C04)==1,
        static_cast<unsigned>(Load(state.entered)),static_cast<unsigned>(Load(state.returned)),static_cast<unsigned>(Load(state.runFault)),
        static_cast<unsigned>(Load(state.unrelated)),state.thread,static_cast<DWORD>(Load(state.worker))};
}
bool QueueRegistration() {
    if(state.registrationSubmitted || !BoundOwners(false) || !HookIdentity())return false;
    auto& load=initial_load_v2::ledger;
    native_registration_prerequisites_v2::Snapshot ready{};
    const auto reader=[](U32 p,U32& value){return initial_load_v2::Bytes(p,&value,sizeof(value));};
    const auto gate=native_registration_prerequisites_v2::Inspect(reader,base,state.game,load.copies[2],load.entries,load.liveries,ready);
    char line[320]{};sprintf_s(line,"REGISTRATION_PREREQUISITES gate=%u reason=%u car=%08x model=%08x camera=%08x views=%u\r\n",
        unsigned(gate),unsigned(ready.reason),ready.localCar,ready.localModel,ready.camera,ready.viewCount);Log(line);
    if(gate!=native_registration_prerequisites_v2::Gate::Ready) {sprintf_s(line,"REGISTRATION_FIELD field=%u rule=%u address=%08x actual=%08x expected=%08x\r\n",unsigned(ready.failure.field),unsigned(ready.failure.rule),ready.failure.address,ready.failure.actual,ready.failure.expected);Log(line);}
    if(gate!=native_registration_prerequisites_v2::Gate::Ready)return false;
    U32 init[2]{},inGame[3]{};
    reinterpret_cast<NoArgs>(At(0x659450))(init);
    reinterpret_cast<NoArgs>(At(0x6594E0))(inGame);
    *reinterpret_cast<unsigned char*>(&inGame[2])=1; // Actual native message payload.
    if(init[0]!=At(0x1313BD4) || init[1] || inGame[0]!=At(0x1313C30) || inGame[1] || inGame[2]!=1)return false;
    const U32 mailbox=reinterpret_cast<Query>(At(0x1177A50))(&state.mailbox[0]);
    if(mailbox!=state.mailbox[0])return false;
    reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0x720200))(reinterpret_cast<void*>(state.game+0x2DB0),1);
    if(!initial_load_v2::Flag(state.game+0x2DB5,1))return false;
    state.registrationSubmitted=true; // No retry even if only the first message queues.
    if(reinterpret_cast<U32(__thiscall*)(void*,void*)>(At(0x662560))(reinterpret_cast<void*>(mailbox),init)!=0)return false;
    if(reinterpret_cast<U32(__thiscall*)(void*,void*)>(At(0x662600))(reinterpret_cast<void*>(mailbox),inGame)!=0)return false;
    return true;
}
void TickBody(const Permit& permit) {
    using initial_load_v2::Ptr;using initial_load_v2::Flag;
    auto& s=state;auto& l=initial_load_v2::ledger;
    if(s.phase==Phase::Failed || s.phase==Phase::PlacementBlocked || s.phase==Phase::RegisteredBlocked)return;
    if(trace::Environment(L"DIRT2VR_ENGINE_FINALIZATION_CANDIDATE")!=L"1") {if(s.phase!=Phase::Dormant)Fail("candidate authorization removed");return;}
    U32 frame{};
    if(!permit.load.exactImageValidated || !permit.load.exclusiveInitialLoad || !permit.load.nativeQueuesAdvancing ||
       !l.metadataApplied || !initial_load_v2::MetadataMatches() || !HeldEngine(l.game) || !TaskOwnershipSignatures() ||
       !permit.load.thread || permit.load.thread!=GetCurrentThreadId() || permit.load.thread!=l.thread || permit.load.setupAllocator!=l.allocator ||
       !initial_load_v2::NativeFrame(l.allocator,frame)) {if(s.phase!=Phase::Dormant)Fail("native task/retention permit lost");return;}
    if(l.phase!=initial_load_v2::Phase::FinalizationBlocked) {if(s.phase!=Phase::Dormant)Fail("initial loader checkpoint lost");return;}
    l.tickFrame=frame; // Refresh the current validated scope on every active finalizer tick.
    const ULONGLONG now=GetTickCount64();
    if(s.phase==Phase::Dormant) {
        l.tickFrame=frame; // Current native scope identity; no native field mutation.
        if(!BoundLiveSession(false) || !NativeLoadedConfiguration(false) || !FinalizationSignatures(false)) {Fail("initial loader/byte guard");return;}
        s.game=l.game;s.render=l.render;s.entity=l.entity;s.setup=l.setup;s.session=l.session;s.data=l.data;s.tree=l.tree;s.event=l.event;s.roster=l.roster;s.config=l.config;s.service=l.service;
        for(unsigned i=0;i<13;++i)s.eventSnapshot[i]=Read(s.event+EventFields[i]);
        s.protectedKey=Read(At(0x147DE90));s.protectedShortKey=Read(At(0x147FC68));
        s.taskManager=Read(s.game+0x1054);s.routeOwner=Read(s.game+0x22FC);s.pluginOwner=Read(s.game+0x2278);s.eventHandle=Read(s.game+0x2E7C);
        for(unsigned i=0;i<2;++i){s.entry[i]=l.entries[i];s.id[i]=l.nativeIds[i];s.livery[i]=l.liveries[i];}
        s.thread=permit.load.thread;s.began=now;
        if(!Ptr(s.taskManager,0x64) || !Ptr(s.pluginOwner,0x14C08) || !EventIdentity() || !BoundOwners(false) || !Flag(s.game+0x2DB5,0)) {Fail("finalization baseline/owner");return;}
        s.phase=Phase::AwaitTrackPulse;Note("await owned track pulse");
    }
    if(s.thread!=GetCurrentThreadId() || Expired(now,s.began) || !BoundOwners(s.registrationSubmitted) ||
       !FinalizationSignatures(s.hookInstalled) || (s.hookInstalled && !HookIdentity())) {Fail("deadline or bound owner/code changed; retain until process exit");return;}
    if(s.phase==Phase::AwaitTrackPulse) {
        unsigned char pulse{};if(!initial_load_v2::Byte(s.entity+0xC,pulse) || pulse>1){Fail("invalid track pulse");return;}if(!pulse)return;
        if(!OptionalStreamingHintAllowsFinalization()){Fail("optional streaming hint owner/code verification failed; pulse retained");return;}
        s.task=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0x1183440))(reinterpret_cast<void*>(s.taskManager),"plugin_finalise");
        if(!Ptr(s.task,0x120) || !WorkerBinding(true) || !CaptureMailboxes() || !BoundOwners(false) || !InstallObservation()) {Fail("task/mailbox/Run observer not ready");return;}
        reinterpret_cast<NoArgs>(At(0x6F99A0))(&s.job);s.job.render=s.render;s.job.game=s.game;
        s.ownedJob=&s.job;s.expectedTable=At(0x1323FBC);s.expectedRender=s.render;s.expectedGame=s.game;
        if(s.job.table!=At(0x1323FBC) || reinterpret_cast<Query>(At(0x115FBC0))(reinterpret_cast<void*>(s.game+0x2E78))!=0) {Fail("native job/reset failed");return;}
        if(reinterpret_cast<Query>(At(0x4568A0))(reinterpret_cast<void*>(s.game+0x2E78))!=0x3C) {Fail("native event did not reset");return;}
        if(!reinterpret_cast<bool(__thiscall*)(void*)>(At(0x6F8610))(reinterpret_cast<void*>(s.entity)) || !Flag(s.entity+0xC,0)) {Fail("track pulse consumption");return;}
        s.pulseConsumed=true;s.phase=Phase::Queued;InterlockedExchange(&s.armed,1);
        const U32 result=reinterpret_cast<U32(__thiscall*)(void*,void*,U32)>(At(0x117B7E0))(reinterpret_cast<void*>(s.task),&s.job,0);
        s.queueAccepted=result==0;
        if(!s.queueAccepted || !WorkerBinding(false) || Finalized(SampleCompletion(true))==Gate::Reject) {Fail("plugin queue rejected or synchronous fallback/foreign job");return;}
        Note("native plugin job queued; retained through process lifetime");return;
    }
    if(Load(s.runFault) || Load(s.unrelated) || Load(s.entered)>1 || Load(s.returned)>1){Fail("Run observation invalid");return;}
    if(s.phase==Phase::Queued) {
        if(!PollEvent())return;
        const Gate gate=Finalized(SampleCompletion(true));
        if(gate==Gate::Reject){Fail("finalizer return/worker gate");return;}if(gate!=Gate::Ready)return;
        if(!QueueRegistration()){Fail("registration submission failed; no retry/deregister");return;}
        s.phase=Phase::AwaitRegistration;s.began=now;Note("native registration queued; awaiting actual update/render acknowledgements");return;
    }
    unsigned char update{},render{};
    if(!initial_load_v2::Byte(s.game+0x2E80,update) || !initial_load_v2::Byte(s.game+0x2E81,render)){Fail("registration readback unavailable");return;}
    const Gate registered=Registration(s.registrationSubmitted,BoundOwners(true),update,render);
    if(registered==Gate::Reject){Fail("registration ownership/flag mismatch");return;}
    if(registered==Gate::Ready){s.phase=Phase::RegisteredBlocked;Note("native update/render acknowledged; stop before event7/countdown/driving");}
}
}

// Call only inside the existing proven setup allocation scope. Not wired anywhere.
void NativeFinalizationTickV7(const finalization_v2::Permit& permit) {
    static volatile LONG busy{};if(InterlockedCompareExchange(&busy,1,0))return;
    __try {finalization_v2::TickBody(permit);}
    __except(EXCEPTION_EXECUTE_HANDLER){finalization_v2::Fail("native exception; retain job/module/owners and stop owned process");}
    InterlockedExchange(&busy,0);
}
