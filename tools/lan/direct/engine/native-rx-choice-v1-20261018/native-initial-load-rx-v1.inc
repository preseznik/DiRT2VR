// Initial-load candidate: publish immutable filters before any asynchronous producer.
// Include after native-initial-config-v2.inc and its guards.
// No hook, export, automatic invocation, finalizer, registration or frontend call.
#include "../direct-bootstrap/native-initial-load-policy-live-v3.h"
#include "../direct-bootstrap/native-initial-load-code-guards-v2.inc"
#include "../direct-bootstrap/native-vehicle-physics-guard-v1.h"
namespace initial_load_v2 {
using NoArgs=void(__thiscall*)(void*);
using OneArg=void(__thiscall*)(void*,U32);
using VehicleRequest=U32(__thiscall*)(void*,U32,U32);
using VehicleReady=U32(__thiscall*)(void*,U32);

// The caller must establish these scheduling/ownership facts. An address inventory
// alone does not establish them. All false by default; no inferred permission.
struct Permit {
    DWORD thread{};
    U32 setupAllocator{}; // Verified existing native owner-task allocator, not N+100C by assumption.
    bool exactImageValidated{}, exclusiveInitialLoad{}, nativeQueuesAdvancing{};
};
struct Ledger {
    Phase phase{Phase::Dormant};
    DWORD thread{}; ULONGLONG began{}, stageBegan{};
    U32 game{},network{},container{},holder{},session{},transport{},data{},tree{},event{},roster{};
    U32 setup{},vehicles{},entity{},render{},resources{},manager{},service{},config{},database{},allocator{};
    U32 selectedMode{},priorMode{},configKind{},trackRow{},vehicleSerial{},vehiclePhase{},tlsSlot{},tickFrame{};
    U32 copies[20]{}, entries[2]{}, identities[2]{}, nativeIds[2]{}, liveries[2]{};
    ConfigPlayer players[2]{};
    Token audio{},track{},audioPatch{},trackPatch{};
    bool host{}, trackPatchIssued{}, trackPatchAbsent{}, audioPatchIssued{}, vehiclesComplete{};
    bool mutated{}, metadataApplied{}, audioIssued{}, physicsWaitLogged{}, resourceObservationOnly{};
};
Ledger ledger;

bool Ptr(U32 p,U32 size=4) { return p>=0x10000 && size && uint64_t(p)+size<=0x80000000ull; }
bool Bytes(U32 p,void* out,size_t size) {
    if(!out || size>0x4000 || !Ptr(p,static_cast<U32>(size)))return false;
    __try {memcpy(out,reinterpret_cast<void*>(p),size);return true;}
    __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
bool Byte(U32 p,unsigned char& value) { return Bytes(p,&value,1); }
bool Flag(U32 p,unsigned char value) { unsigned char actual{};return Byte(p,actual) && actual==value; }
bool NativeFrame(U32 allocator,U32& frame) {
    const U32 slot=Read(At(0x147D1F8));if(!Ptr(allocator) || slot>=1088)return false;
    frame=reinterpret_cast<U32>(TlsGetValue(slot));
    return Ptr(frame,12) && Read(frame)==At(0x147D1F8) && Read(frame+4)==allocator;
}
void Note(const char* reason) {
    char line[640]{};
    sprintf_s(line,"LOAD phase=%u reason=%s owner=%08x setup=%08x config=%08x mode=%u vehicle_serial=%u vehicle_phase=%u audio=%u/%u track=%u/%u audio_patch=%u/%u track_patch=%u/%u vehicles_complete=%u mutated=%u finalization=blocked registration=blocked\r\n",
        unsigned(ledger.phase),reason,ledger.game,ledger.setup,ledger.config,ledger.selectedMode,ledger.vehicleSerial,ledger.vehiclePhase,
        ledger.audio.value,unsigned(ledger.audio.state),ledger.track.value,unsigned(ledger.track.state),ledger.audioPatch.value,unsigned(ledger.audioPatch.state),ledger.trackPatch.value,unsigned(ledger.trackPatch.state),unsigned(ledger.vehiclesComplete),unsigned(ledger.mutated));Log(line);
}
void Fail(const char* reason) { if(ledger.phase!=Phase::Failed){ledger.phase=Phase::Failed;Note(reason);} }
void Stage(Phase phase,const char* reason) {ledger.phase=phase;ledger.stageBegan=GetTickCount64();Note(reason);}

constexpr U32 GameFields[]{0,0x1058,0x222C,0x2D38,0x20C0,0x209C,0x223C,0x2D90,0x2248,0x229C,0x22F8,0x2238,0x22F0,0x2244,0x2D40,0x2268,0x2234,0x224C,0x2E88,0x2E08};
bool Owners(bool capture) {
    auto& s=ledger;
    if(s.game!=loadOwner || s.network!=configuredNetwork || Read(s.game+0x2CF0)!=s.network || Read(s.network+0x2174)!=s.container ||
       Read(s.container+0x2450)!=s.holder || Read(s.holder)!=At(0x133549C) || Read(s.holder+0x18)!=s.session || Read(s.holder+0x24)!=s.transport ||
       Read(s.holder+8)!=(s.host?1u:2u) || !Flag(s.holder+0x800,0) || Read(s.transport)!=At(0x135CFC0) || Read(s.transport+8)!=(s.host?1u:2u) ||
       Read(s.session)!=At(0x1335844) || Read(s.session+0x24)!=(s.host?0u:1u) || Read(s.session+0x1EC)!=s.data || Read(s.data)!=At(0x1336248) ||
       Read(s.data+0x64)!=Read(s.network+0x1004) || Read(s.data+0x68)!=s.network+0x1010 || Read(s.data+0x1024)!=s.tree || Read(s.tree)!=At(0x1335B88) || Read(s.data+0x1028)!=committedPlayer ||
       native_components::Find(Read,s.tree,At(0x14906B0))!=s.event || native_components::Find(Read,s.tree,At(0x1490868))!=s.roster ||
       Read(s.game+0x2DFC)!=s.setup || Read(s.setup)!=At(0x1324A2C) || Read(At(0x1482D8C))!=s.vehicles || Read(s.vehicles)!=At(0x1325688) ||
       Read(s.vehicles+4)!=At(0x132567C) || Read(s.vehicles+0xC)!=s.game || Read(s.vehicles+0x10)!=s.render ||
       Read(At(0x1482D5C))!=s.render || Read(s.render)!=At(0x1324164) || s.entity!=s.render+0x1090 || Read(s.game+0x224C)!=s.entity || Read(s.entity)!=At(0x1321674) || Read(s.entity+4)!=At(0x1321668) ||
       Read(At(0x15D9A04))!=s.resources || Read(s.resources)!=At(0x135B85C) || Read(s.resources+0x58)!=s.resources+0x5C ||
       Read(s.game+0x2E88)!=s.manager || Read(s.manager)!=At(0x132B1CC) || Read(s.manager+0x34)!=s.service || Read(s.manager+0xAC0)!=1 ||
       Read(s.service)!=At(0x132AC70) || Read(s.service+0x38)!=s.database || Read(At(0x147D1F8))!=s.tlsSlot ||
       (Read(s.game+0x2E80)&0xffff)!=0 || !SelectedCareerLoaded())return false;
    U32 frame{};if(!NativeFrame(s.allocator,frame) || frame!=s.tickFrame)return false;
    const U32 localState=Read(native_components::Find(Read,committedPlayer,At(0x1490690))+0x64);
    const U32 sessionState=Read(native_components::Find(Read,s.tree,At(0x1490690))+0x64);
    if(!LocalLoadingState(Read(localState)-base+0x400000,Read(Read(localState))-base+0x400000,
                          NativeStateDescriptor(committedPlayer)-base+0x400000,Read(localState+0x60)&0xffff) ||
       !NativeLoadedSessionState(s.tree,s.session,false) || Read(sessionState+0x58)!=s.session)return false;
    unsigned char modeFlag{};
    if(!Byte(s.session+0x6C,modeFlag) || (modeFlag?3u:2u)!=s.selectedMode)return false;
    for(unsigned i=0;i<20;++i) {
        const U32 value=i?Read(s.game+GameFields[i]):s.game;
        if(!Ptr(value) || Read(s.setup+0x10+4*i)!=value)return false;
        if(capture)s.copies[i]=value;else if(s.copies[i]!=value)return false;
    }
    return true;
}

// Reacquire owning references every tick. Cached addresses are identity only.
bool Configuration(bool capture) {
    auto& s=ledger;if(!RxEventMatches(s.event))return false;ConfigRef service(0x132AC70),config(0x132A450);
    if(reinterpret_cast<config_abi::AcquireKind>(At(0x734A70))(reinterpret_cast<void*>(s.manager),&service.ref,1)!=&service.ref || !service.Valid() || service.ref.pointer!=s.service)return false;
    if(reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(s.service),&config.ref)!=&config.ref || !config.Valid() || config.ref.pointer!=s.config)return false;
    const U32 begin=Read(s.config+0x20),end=Read(s.config+0x24);
    if(!Ptr(begin,8) || end!=begin+8 || Read(s.config+0x3C)!=s.database || Read(s.service+0x3C)!=static_cast<U32>(RxRequest().selected.model))return false;
    const U32 kind=Read(s.config+0x40),track=Read(s.config+0x78);int trackId{};
    if(!ConfigRowId(s.database,"track_model",track,trackId) || trackId!=RxRequest().selected.model)return false;
    if(capture){s.configKind=kind;s.trackRow=track;}else if(s.configKind!=kind || s.trackRow!=track)return false;
    ConfigPlayer players[2]{};
    if(!(capture ? ConfigPlayers(s.data,s.roster,players) : NativeLoadedRosterPlayers(players,false)))return false;
    for(unsigned i=0;i<2;++i) {
        ConfigRef entry(0x132A398);
        if(reinterpret_cast<config_abi::AcquireEntry>(At(0xB28310))(reinterpret_cast<void*>(s.config),&entry.ref,i)!=&entry.ref || !entry.Valid() || entry.ref.pointer!=Read(begin+4*i))return false;
        const U32 id=Read(entry.ref.pointer+0xC0)&255,records=Read(entry.ref.pointer+0x40),livery=Read(records+4);
        unsigned p=2;for(unsigned j=0;j<2;++j)if(players[j].nativeId==id)p=j;
        int liveryId{};
        if(p==2 || Read(Read(s.roster+0x5C)+4*i)!=id || Read(entry.ref.pointer+0xC)!=s.database ||
           Read(entry.ref.pointer+0x1C)!=(players[p].root==committedPlayer?4u:2u) || !Ptr(records,12) || Read(entry.ref.pointer+0x48)!=1 ||
           Read(entry.ref.pointer+0x44)<1 || Read(entry.ref.pointer+0x44)>64 || Read(records)!=Read(At(0x134FB70)) ||
           !ConfigRowId(s.database,"vehicle_livery",livery,liveryId) || liveryId!=players[p].livery)return false;
        if(capture){s.entries[i]=entry.ref.pointer;s.nativeIds[i]=id;s.liveries[i]=livery;s.identities[i]=players[p].identity;s.players[i]=players[p];}
        else if(s.entries[i]!=entry.ref.pointer || s.nativeIds[i]!=id || s.liveries[i]!=livery || s.identities[i]!=players[p].identity ||
                s.players[i].root!=players[p].root || s.players[i].vehicle!=players[p].vehicle || s.players[i].record!=players[p].record)return false;
    }
    const U32 key=Read(At(0x147DE90)),shortKey=Read(At(0x147FC68)),round=s.event+0xA4;
    if(!(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) || Read(s.event)!=At(0x13366EC) || Read(s.event+0x58)!=Read(s.data+0x64) || Read(s.event+0x5C)!=s.network+0x1010 || Read(s.event+0x1B4)!=key ||
       (Read(s.event+0x64)^key)!=static_cast<U32>(RxRequest().race) || (Read(s.event+0x68)^key)!=1 || (Read(s.event+0x74)^key)!=static_cast<U32>(RxRequest().vehicleClass) || Read(round)!=At(0x1333840) || Read(round+4)!=s.event ||
       (Read(round+8)^key)!=static_cast<U32>(RxRequest().race) || config_abi::DecodeTrack(static_cast<unsigned short>(Read(round+0xC)),static_cast<unsigned short>(shortKey))!=RxRequest().selected.model ||
       (Read(round+0x10)^key)!=static_cast<U32>(RxRequest().selected.location) || (Read(round+0x18)^key)!=static_cast<U32>(RxLaps()))return false;
    ConfigRef again(0x132A450);
    return reinterpret_cast<config_abi::AcquireConfig>(At(0x733D30))(reinterpret_cast<void*>(s.service),&again.ref)==&again.ref && again.Valid() && again.ref.pointer==s.config && Owners(false);
}

struct JobSample { bool found{},retired{};U32 state{0xffffffffu},wait{0xffffffffu},observerFault{},jobIdentity{};char path[260]{}; };
JobSample Sample(U32 manager,U32 token) {
    JobSample result{};bool locked=false;
    __try {
        reinterpret_cast<NoArgs>(At(0x11588C0))(reinterpret_cast<void*>(manager+0x58));locked=true;
        const auto* completions=native_resource_observer_v1::ResourceObserverLedgerUnderNativeLock();
        if(!completions->armed || completions->manager!=manager || completions->base!=base ||
           completions->fault!=native_resource_observer_v1::resource_completion::Fault::None) {
            result.observerFault=completions->fault==native_resource_observer_v1::resource_completion::Fault::None?0xfffffffeu:static_cast<U32>(completions->fault);return result;
        }
        const auto* receipt=completions->Find(token);
        // Bound the exact native tree search before invoking it under its lock.
        const U32 sentinel=Read(manager+0x50);U32 walk=Read(sentinel+4),seen[256]{},count{};bool valid=Ptr(sentinel,0x18) && Flag(sentinel+0x15,1);
        while(valid && walk!=sentinel) {
            unsigned char nil{};
            if(!Ptr(walk,0x18) || !Byte(walk+0x15,nil) || nil || count==256){valid=false;break;}
            for(unsigned i=0;i<count;++i)if(seen[i]==walk)valid=false;
            if(!valid)break;seen[count++]=walk;
            walk=Read(walk+(Read(walk+0xC)<token?8:0));
        }
        U32 node{};
        if(valid && reinterpret_cast<U32*(__thiscall*)(void*,U32*,const U32*)>(At(0xC64230))(reinterpret_cast<void*>(manager+0x40),&node,&token)==&node && node!=sentinel && Ptr(node,0x18) && Read(node+0xC)==token) {
            if(receipt){result.observerFault=0xffffffffu;return result;} // No token reuse may hide behind a completed receipt.
            const U32 job=Read(node+0x10);
            if(Ptr(job,0x18) && Read(job)==At(0x135A5D8) && Read(job+0xC)==At(0x13C88A8)) {
                result.found=true;
                result.wait=reinterpret_cast<U32(__thiscall*)(void*,U32)>(At(0xC7EA20))(reinterpret_cast<void*>(job),0);
                result.state=reinterpret_cast<U32(__thiscall*)(void*)>(At(0xC7EA10))(reinterpret_cast<void*>(job));
            }
        } else if(valid && receipt && receipt->nativeZeroPoll && receipt->xmlEventSignaled &&
                  receipt->jobTable==At(0x135A5D8) &&
                  (receipt->outcome==native_resource_observer_v1::resource_completion::Outcome::Success ||
                   receipt->outcome==native_resource_observer_v1::resource_completion::Outcome::NativeFailure)) {
            result.found=true;result.retired=true;result.state=receipt->state;result.wait=0;result.jobIdentity=receipt->jobIdentity;
            memcpy(result.path,receipt->path,sizeof(result.path));
        }
    } __finally {if(locked)reinterpret_cast<NoArgs>(At(0x11588D0))(reinterpret_cast<void*>(manager+0x58));}
    return result; // Only receipt values escape; no borrowed node/job escapes the lock.
}
void Poll(Token& token,const char* name) {
    if(!token.submitted || token.state!=JobState::Pending)return;
    const auto before=token.state;const U32 prior=token.nativeState;const JobSample sample=Sample(ledger.resources,token.value);
    Observe(token,sample.found,sample.state,sample.wait);
    if(sample.retired){char text[480]{};sprintf_s(text,"RESOURCE_RECEIPT token=%u historical_job=%08x state=%u native_signaled=1 path=%s\r\n",token.value,sample.jobIdentity,sample.state,sample.path);Log(text);}
    if(sample.observerFault){char text[120]{};sprintf_s(text,"RESOURCE_OBSERVER rejected fault=%u token=%u\r\n",sample.observerFault,token.value);Log(text);}
    if(before!=token.state || prior!=token.nativeState){char text[180]{};sprintf_s(text,"%s token=%u native=%u wait=%u status=%u",name,token.value,token.nativeState,token.waitResult,unsigned(token.state));Note(text);}
}
bool Submit(Token& token,U32 value) {
    if(token.submitted)return false;
    for(const Token* other:{&ledger.audio,&ledger.track,&ledger.audioPatch,&ledger.trackPatch})if(other!=&token && other->submitted && other->value==value)return false;
    token.value=value;token.submitted=true;token.state=JobState::Pending;return true;
}

} // namespace initial_load_v2; common audio uses the verified native receipt sampler.
#include "../common-audio-retained-v2-20261018/native-common-audio-v2.inc"
namespace initial_load_v2 {

bool MetadataMatches() {
    auto& s=ledger;const auto f=ForKind(s.configKind);const U32 array=Read(s.entity+0x750),capacity=Read(s.entity+0x754);
    if(!Ptr(array,8) || array!=s.entity+0x760 || capacity!=2 || Read(s.entity+0x758)!=FilterCount(f) ||
       Read(s.entity+0x770)!=(f.checkpointInclude?At(0x1323F98):0))return false;
    unsigned i{};if(f.checkpointExclude && Read(array+4*i++)!=At(0x1323F98))return false;
    if(f.dominationExclude && Read(array+4*i)!=At(0x1323FA8))return false;
    return true;
}
bool Metadata() {
    auto& s=ledger;const auto f=ForKind(s.configKind);
    // Constructor-backed inline capacity and pristine inputs, before any resource producer.
    if(Read(s.entity+0x750)!=s.entity+0x760 || Read(s.entity+0x754)!=2 || Read(s.entity+0x758)!=0 || Read(s.entity+0x770)!=0)return false;
    reinterpret_cast<NoArgs>(At(0x707C50))(reinterpret_cast<void*>(s.entity));
    reinterpret_cast<OneArg>(At(0x6F85E0))(reinterpret_cast<void*>(s.entity),0);
    if(f.checkpointExclude)reinterpret_cast<OneArg>(At(0x707C20))(reinterpret_cast<void*>(s.entity),At(0x1323F98));
    if(f.dominationExclude)reinterpret_cast<OneArg>(At(0x707C20))(reinterpret_cast<void*>(s.entity),At(0x1323FA8));
    if(f.checkpointInclude)reinterpret_cast<OneArg>(At(0x6F85E0))(reinterpret_cast<void*>(s.entity),At(0x1323F98));
    return MetadataMatches();
}

void BeginPreload() {
    auto& s=ledger;
    // The resource callback reads +74. Initialize before any async producer,
    // then preserve this exact mode through the stock track call.
    s.mutated=true;*reinterpret_cast<U32*>(s.setup+0x74)=s.selectedMode;
    if(Read(s.setup+0x74)!=s.selectedMode){Fail("load mode metadata readback");return;}
    if(!Metadata()){Fail("prepublished initial entity metadata");return;}
    s.metadataApplied=true;MemoryBarrier(); // Publish before native queue locks release work to resource workers.
    Stage(Phase::Preload,"immutable metadata published; preload submit once");
    reinterpret_cast<OneArg>(At(0x6E0610))(reinterpret_cast<void*>(s.setup),1);
    if(!Owners(false) || !Configuration(false) || !s.metadataApplied || !MetadataMatches() || !Flag(s.setup+0x78,1) || !Flag(s.setup+0x79,1)){Fail("preload ownership/flags/metadata");return;}
    *reinterpret_cast<unsigned char*>(s.setup+8)=1; // Exact stock race metadata, never a flow/FSM field.
    if(!Flag(s.setup+8,1)){Fail("race metadata readback");return;}return;
}

void TickBody(void* self,U32 container,U32 holder,U32 session,U32 transport,bool host,const Permit& permit) {
    auto& s=ledger;
    if(s.phase==Phase::Failed || s.phase==Phase::FinalizationBlocked || s.phase==Phase::ResourceCheckpoint)return;
    if(!initialConfigCommitted || trace::Environment(L"DIRT2VR_ENGINE_INITIAL_LOAD")!=L"1") {if(s.phase!=Phase::Dormant)Fail("authorization/configuration lost; stop required");return;}
    U32 frame{};
    if(!permit.exactImageValidated || !permit.exclusiveInitialLoad || !permit.nativeQueuesAdvancing || !permit.thread || permit.thread!=GetCurrentThreadId() || !NativeFrame(permit.setupAllocator,frame)) {
        if(s.phase!=Phase::Dormant)Fail("native task/queue ownership lost");else {static bool logged{};if(!logged){logged=true;Note("blocked: caller must prove native thread, advancing queues and exclusive setup ownership");}}return;
    }
    const ULONGLONG now=GetTickCount64();
    if(s.phase==Phase::Dormant) {
        s.resourceObservationOnly=trace::Environment(L"DIRT2VR_ENGINE_RESOURCE_OBSERVATION_ONLY")==L"1";
        s.thread=permit.thread;s.game=reinterpret_cast<U32>(self);s.network=Read(s.game+0x2CF0);s.container=container;s.holder=holder;s.session=session;s.transport=transport;s.host=host;
        s.data=Read(session+0x1EC);s.tree=Read(s.data+0x1024);s.event=native_components::Find(Read,s.tree,At(0x14906B0));s.roster=native_components::Find(Read,s.tree,At(0x1490868));
        s.setup=Read(s.game+0x2DFC);s.vehicles=Read(At(0x1482D8C));s.render=Read(At(0x1482D5C));s.entity=Read(s.game+0x224C);s.resources=Read(At(0x15D9A04));
        s.manager=Read(s.game+0x2E88);s.service=initialConfigService;s.config=initialConfig;s.database=Read(s.service+0x38);s.allocator=permit.setupAllocator;s.tlsSlot=Read(At(0x147D1F8));s.tickFrame=frame;
        unsigned char flag{};if(!Byte(session+0x6C,flag)){Fail("unreadable session load mode");return;}s.selectedMode=flag?3:2;s.priorMode=Read(s.setup+0x74);s.began=s.stageBegan=now;
        if(!InitialLoadSignaturesV2() || !InitialConfigSignatures() || !Owners(true) || !Flag(s.setup+0x78,0) || !Flag(s.setup+0x79,0) || !Flag(s.setup+0x7A,0) || !Flag(s.setup+0x7B,0) || !Flag(s.setup+0x7C,1) || Read(s.setup+0xC)!=0 ||
           Read(s.vehicles+0x1C)!=0 || Read(s.vehicles+0x38)!=0xffffffffu || Read(s.vehicles+0x3C)!=0xffffffffu || Read(s.vehicles+0x40)!=0 || Read(s.vehicles+0x44)!=0xffffffffu || !Flag(s.vehicles+0x48,0) || !Flag(s.entity+0xC,0)) {Fail("not pristine initial loader/owner or byte guard");return;}
        if(!Configuration(true)){Fail("initial config/native allocation frame mismatch");return;}
        if(!resource_observer_adapter_v1::Install(s.resources)){Fail("resource retirement observer installation");return;}
        s.mutated=true;
        Stage(Phase::CommonAudio,"prepare retained native car-audio resources before preload");
        return;
    }
    if(s.resourceObservationOnly!=(trace::Environment(L"DIRT2VR_ENGINE_RESOURCE_OBSERVATION_ONLY")==L"1")){Fail("resource-only scope changed");return;}
    s.tickFrame=frame; // Native frame storage may differ between ticks; allocator/slot may not.
    if(s.thread!=permit.thread || s.allocator!=permit.setupAllocator || s.game!=reinterpret_cast<U32>(self) || s.container!=container || s.holder!=holder || s.session!=session || s.transport!=transport || s.host!=host || !Owners(false)) {Fail("bound loader/session changed");return;}
    if(Expired(now,s.stageBegan) || now<s.began || now-s.began>=300000) {Fail("stage/transaction deadline; no retry or cancellation");return;}
    if(s.phase==Phase::CommonAudio) {
        if(!Configuration(false) || !resource_observer_adapter_v1::Healthy(s.resources)){Fail("common audio configuration/resource owner changed");return;}
        common_audio_v2::Tick(self,permit);
        if(common_audio_v2::state.phase==common_audio_v2::Phase::Failed){Fail("native common audio preparation rejected; no preload submitted");return;}
        if(common_audio_v2::state.phase==common_audio_v2::Phase::Retained)BeginPreload();
        return;
    }
    if(!Configuration(false) || !s.metadataApplied || !MetadataMatches() || Read(s.setup+0x74)!=s.selectedMode || !Flag(s.setup+8,1)){Fail("configuration or metadata changed");return;}
    if(!resource_observer_adapter_v1::Healthy(s.resources)){Fail("resource completion observer lost ownership or rejected a receipt");return;}
    Poll(s.audio,"audio");Poll(s.track,"track");Poll(s.audioPatch,"audio_patch");Poll(s.trackPatch,"track_patch");
    if(Failed(s.audio)||Failed(s.track)||Failed(s.audioPatch)||Failed(s.trackPatch)){Fail("resource failure/completion_unknown; retain until owned process exit");return;}
    if(s.phase==Phase::Preload) {
        unsigned char pending{};if(!Byte(s.setup+0x7B,pending) || pending>1){Fail("invalid preload readiness");return;}if(pending)return;
        if(!Ptr(Read(s.setup+0xC)) || Read(s.vehicles+0x1C)!=0 || !Flag(s.vehicles+0x48,0) || Read(s.vehicles+0x40)!=0){Fail("preload archive/vehicle lifecycle changed");return;}
        Stage(Phase::Vehicles,"vehicle submit once; stock race audio follows track");
        s.vehicleSerial=reinterpret_cast<VehicleRequest>(At(0x6CA440))(reinterpret_cast<void*>(s.vehicles),0,1);
        if(s.vehicleSerial!=0 || !Flag(s.vehicles+0x48,1) || !Flag(s.vehicles+0x49,1) || !Flag(s.vehicles+0xE8,0)){Fail("vehicle request readback");return;}Note("vehicle requested; phase>5 is records-ready only");return;
    }
    s.vehiclePhase=Read(s.vehicles+0x1C);
    if(s.vehiclePhase>8){Fail("unknown vehicle phase");return;}
    s.vehiclesComplete=reinterpret_cast<VehicleReady>(At(0x6CA460))(reinterpret_cast<void*>(s.vehicles),s.vehicleSerial)!=0;
    if(!s.vehiclesComplete && !Flag(s.vehicles+0x48,1) && Read(s.vehicles+0x44)!=s.vehicleSerial){Fail("vehicle serial replaced");return;}
    if(s.phase==Phase::Vehicles) {
        if(!reinterpret_cast<VehicleReady>(At(0x6CA4A0))(reinterpret_cast<void*>(s.vehicles),s.vehicleSerial))return;
        const auto filters=ForKind(s.configKind);if(!Ptr(Read(s.entity+0x750),8) || Read(s.entity+0x754)<FilterCount(filters) || Read(s.entity+0x754)>256){Fail("entity metadata capacity");return;}
        Stage(Phase::Track,"track submit once; metadata already published before preload");
        reinterpret_cast<OneArg>(At(0x6E9810))(reinterpret_cast<void*>(s.setup),s.selectedMode);
        if(!Submit(s.track,Read(s.setup+0x60)) || !Owners(false) || !Configuration(false) || !Flag(s.setup+0x7C,0) || !MetadataMatches()){Fail("track token/metadata ownership/readback");return;}
        Note("track submitted; not race-ready");return;
    }
    if(!s.audioIssued) {
        native_vehicle_physics_v1::Snapshot physical{};
        const auto reader=[](U32 p,U32& value){return Bytes(p,&value,sizeof(value));};
        const auto ready=native_vehicle_physics_v1::Inspect(reader,base,s.game,s.copies[2],s.entries,s.liveries,physical);
        if(ready==native_vehicle_physics_v1::Gate::Reject){Fail("physical vehicle owner/config/backlink mismatch before audio");return;}
        if(ready==native_vehicle_physics_v1::Gate::Pending) {
            if(!s.physicsWaitLogged){s.physicsWaitLogged=true;Note("waiting for real model/companion before native audio callbacks");}return;
        }
        if(s.resourceObservationOnly) {
            if(s.track.state==JobState::Succeeded && s.vehiclesComplete)
                Stage(Phase::ResourceCheckpoint,"real physics and track completion observed; audio intentionally not submitted; no finalization");
            return;
        }
        // Native coordinator checks archive readiness before every stage selection.
        if(!Flag(s.setup+0x7B,0))return;
        if(!common_audio_v2::ReadyForRace(s.game,permit)){Fail("retained native audio setup lost before race resource submission");return;}
        s.audioIssued=true; // Stock race-specific coordinator loads cars, then track, then audio.
        reinterpret_cast<OneArg>(At(0x6EAC50))(reinterpret_cast<void*>(s.setup),1);
        if(!Submit(s.audio,Read(s.setup+0x6C)) || !Owners(false) || !Configuration(false)){Fail("audio token/owner");return;}
        Note("race audio submitted after both native model/companion backlinks verified");return;
    }
    if(s.audio.state==JobState::Succeeded && !s.audioPatchIssued) {
        s.audioPatchIssued=true;reinterpret_cast<NoArgs>(At(0x6C71C0))(reinterpret_cast<void*>(s.setup));
        if(!Submit(s.audioPatch,Read(s.setup+0x6C)) || !Owners(false)){Fail("audio patch token/owner");return;}Note("audio patch submitted after original audio success");return;
    }
    if(s.track.state==JobState::Succeeded && !s.trackPatchIssued) {
        s.trackPatchIssued=true;reinterpret_cast<NoArgs>(At(0x6C70B0))(reinterpret_cast<void*>(s.setup));
        unsigned char present{};if(!Byte(s.setup+0x68,present) || present>1 || !Owners(false)){Fail("track patch presence/owner");return;}
        s.trackPatchAbsent=present==0;
        if(present && !Submit(s.trackPatch,Read(s.setup+0x64))){Fail("track patch token");return;}Note(present?"optional track patch submitted":"native track patch absent");return;
    }
    if(s.audio.state==JobState::Succeeded && s.track.state==JobState::Succeeded && s.audioPatch.state==JobState::Succeeded && s.trackPatchIssued &&
       (s.trackPatchAbsent || s.trackPatch.state==JobState::Succeeded) && s.vehiclesComplete)
        Stage(Phase::FinalizationBlocked,"known jobs complete; subroute/finalizer retirement and registration ownership unresolved; no track pulse consumed");
}
}

void NativeInitialLoadTickV2(void* self,U32 container,U32 holder,U32 session,U32 transport,bool host,const initial_load_v2::Permit& permit) {
    static volatile LONG busy{};
    if(InterlockedCompareExchange(&busy,1,0)!=0)return; // Native callbacks cannot submit twice.
    __try {initial_load_v2::TickBody(self,container,holder,session,transport,host,permit);}
    __except(EXCEPTION_EXECUTE_HANDLER){initial_load_v2::Fail("native exception; stop required; partial resources retained");}
    InterlockedExchange(&busy,0);
}
