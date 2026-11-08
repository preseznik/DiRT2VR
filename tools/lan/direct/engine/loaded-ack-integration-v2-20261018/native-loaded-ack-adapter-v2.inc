#include "native-loaded-audio-guard-v1.h"
// Compile-only. Include after native-finalization-live-v7.inc. Call explicitly only
// inside the existing owned native allocation scope, after the finalizer tick.
// No Permit/Completed callback is accepted from a caller. No activation calls.
namespace loaded_ack_adapter_v2 {
enum class Phase {Waiting,Published,ProgressObserved,Rejected};
struct State {
 Phase phase{Phase::Waiting};native_loaded_ack::Context context{};
 native_loaded_ack::Attempt attempt{};loaded_roster_v3::Progress progress{};
 native_loaded_ack::Status last{native_loaded_ack::Status::WaitForCompletion};
};
State state;
bool SameContext(const native_loaded_ack::Context& a,const native_loaded_ack::Context& b) {
 return a.imageBase==b.imageBase && a.updateTask==b.updateTask && a.network==b.network && a.session==b.session &&
        a.data==b.data && a.player==b.player && a.ownerThread==b.ownerThread && a.transaction==b.transaction && a.client==b.client;
}
native_loaded_ack::Context Context() {
 const auto& l=initial_load_v2::ledger;
 return {base,l.game,l.network,l.session,l.data,committedPlayer,l.thread,l.began,!l.host};
}
void Reject(const char* reason) {
 if(state.phase==Phase::Rejected)return;
 state.phase=Phase::Rejected;char line[220]{};
 sprintf_s(line,"LOADED_ACK rejected reason=%s attempted=%u acknowledged=%u activation=blocked\r\n",
           reason,unsigned(state.attempt.attempted),unsigned(state.attempt.acknowledged));Log(line);
}
bool ResourceProof(const initial_load_v2::Token& token) {
 if(!loaded_completion_v1::Successful(token,static_cast<unsigned>(initial_load_v2::JobState::Succeeded)))return false;
 // Fresh active job or immutable retirement receipt under the same native lock.
 // Do not trust a latched Succeeded field after observer failure/token reuse.
 const auto sample=initial_load_v2::Sample(initial_load_v2::ledger.resources,token.value);
 return sample.found && !sample.observerFault && sample.state==3 && sample.wait==0;
}
// Real retained mode1 receipt and teardown history, followed by a fresh race
// audio graph tied to the same two actual physical cars. No permission flag can
// stand in for these native observations.
bool AudioProof(const native_vehicle_physics_v1::Snapshot& physical) {
 const auto& l=initial_load_v2::ledger;const auto& preparation=common_audio_v2::state;
 if(preparation.phase!=common_audio_v2::Phase::Retained || !preparation.completionObserved || !preparation.pinned ||
    !preparation.lastUninitReturned || !preparation.uninitCalls || preparation.thread!=l.thread ||
    preparation.thread!=GetCurrentThreadId() || preparation.resources!=l.resources || preparation.allocator!=l.allocator ||
    !preparation.token || preparation.token==0xffffffffu || preparation.token==preparation.previousToken || preparation.token<=preparation.serialBefore ||
    !common_audio_v2::guard::Retained(preparation.last) || !common_audio_v2::guard::SameOwner(preparation.original,preparation.last) ||
    !preparation.receipt.retired || !preparation.receipt.found || preparation.receipt.observerFault ||
    preparation.receipt.state!=3 || preparation.receipt.wait || !preparation.receipt.jobIdentity || !AudioPreparationSignatures())return false;
 for(const auto* token:{&l.audio,&l.track,&l.audioPatch,&l.trackPatch})if(token->submitted && token->value==preparation.token)return false;
 const auto receipt=initial_load_v2::Sample(l.resources,preparation.token);
 if(!receipt.found || !receipt.retired || receipt.observerFault || receipt.state!=3 || receipt.wait ||
    receipt.jobIdentity!=preparation.receipt.jobIdentity ||
    (strcmp(receipt.path,"audio/audio_loader.xml") && strcmp(receipt.path,"audio\\audio_loader.xml")))return false;
 loaded_audio_v1::Snapshot current{};
 const auto gate=loaded_audio_v1::Inspect(LoadedAdapterRead,base,l.game,preparation.original,physical,current);
 if(gate!=loaded_audio_v1::Gate::Ready){char line[100]{};sprintf_s(line,"LOADED_ACK audio_proof gate=%u\r\n",unsigned(gate));Log(line);return false;}
 return resource_observer_adapter_v1::Healthy(l.resources);
}
bool ResourcesAndVehicles() {
 using initial_load_v2::Flag;const auto& l=initial_load_v2::ledger;
 if(l.resourceObservationOnly || l.phase!=initial_load_v2::Phase::FinalizationBlocked || !l.mutated ||
    !l.audioIssued || !l.audioPatchIssued || !l.trackPatchIssued || !l.vehiclesComplete || !l.metadataApplied ||
    !resource_observer_adapter_v1::Healthy(l.resources) || !initial_load_v2::MetadataMatches() ||
    !Flag(l.setup+8,1) || !Flag(l.setup+0x78,1) || !Flag(l.setup+0x79,1) || !Flag(l.setup+0x7B,0) ||
    !initial_load_v2::Ptr(Read(l.setup+0xC)) || Read(l.setup+0x74)!=3 || Read(l.setup+0x60)!=l.track.value ||
    Read(l.setup+0x6C)!=l.audioPatch.value || !Flag(l.setup+0x68,l.trackPatchAbsent?0:1))return false;
 const initial_load_v2::Token* tokens[]{&l.audio,&l.track,&l.audioPatch,&l.trackPatch};
 const unsigned count=l.trackPatchAbsent?3u:4u;
 for(unsigned i=0;i<count;++i) {
  if(!ResourceProof(*tokens[i]))return false;
  for(unsigned j=0;j<i;++j)if(tokens[i]->value==tokens[j]->value)return false;
 }
 if(l.trackPatchAbsent) {
  if(l.trackPatch.submitted || l.trackPatch.state!=initial_load_v2::JobState::Unsubmitted)return false;
 } else if(Read(l.setup+0x64)!=l.trackPatch.value)return false;
 if(Read(l.vehicles+0x1C)>8 || !loaded_completion_v1::VehicleRangeComplete(Read(l.vehicles+0x38),Read(l.vehicles+0x3C),l.vehicleSerial))return false;
 native_vehicle_physics_v1::Snapshot physical{};
 if(native_vehicle_physics_v1::Inspect(LoadedAdapterRead,base,l.game,l.copies[2],l.entries,l.liveries,physical)!=native_vehicle_physics_v1::Gate::Ready)return false;
 return AudioProof(physical) && resource_observer_adapter_v1::Healthy(l.resources);
}
bool FinalizerRetired() {
 using finalization_v2::Load;const auto& l=initial_load_v2::ledger;auto& f=finalization_v2::state;
 if(f.phase!=finalization_v2::Phase::RegisteredBlocked || f.thread!=l.thread || f.thread!=GetCurrentThreadId() ||
    !f.pinnedModule || !f.hookInstalled || !f.queueAccepted || !f.pulseConsumed || !f.signalSeen || !f.registrationSubmitted ||
    f.ownedJob!=&f.job || f.expectedTable!=At(0x1323FBC) || f.expectedRender!=l.render || f.expectedGame!=l.game ||
    !finalization_v2::JobUnchanged(f) || !finalization_v2::HookIdentity() || !finalization_v2::WorkerBinding(false) || !finalization_v2::EventIdentity() ||
    !loaded_completion_v1::Retired(static_cast<unsigned>(Load(f.armed)),static_cast<unsigned>(Load(f.entered)),static_cast<unsigned>(Load(f.returned)),
        static_cast<unsigned>(Load(f.runFault)),static_cast<unsigned>(Load(f.unrelated)),f.thread,static_cast<DWORD>(Load(f.worker))) ||
    Read(f.pluginOwner+0x14C04)!=1 || !initial_load_v2::Flag(l.game+0x2DB5,1) ||
    !initial_load_v2::Flag(l.game+0x2E80,1) || !initial_load_v2::Flag(l.game+0x2E81,1))return false;
 for(unsigned i=0;i<3;++i) {
  if(!initial_load_v2::Ptr(f.mailbox[i],0x1A4) || Read(f.mailbox[i])!=f.mailboxTable[i] || !initial_load_v2::Flag(f.mailbox[i]+0x38,1))return false;
  for(unsigned j=0;j<i;++j)if(f.mailbox[i]==f.mailbox[j])return false;
 }
 return true;
}
bool Signatures() {
 const U32 observer=reinterpret_cast<U32>(&native_resource_observer_v1::ResourceRetireCallsiteObserver);
 return InitialLoadSignaturesV2() && InitialConfigSignatures() && FinalizationSignatures(true) && TaskOwnershipSignatures() &&
        loaded_adapter_guards_v1::Signatures(LoadedAdapterEqual,base) && loaded_lan_mode_v1::Signatures(LoadedAdapterEqual,base) &&
        native_resource_observer_v1::resource_completion::Signatures(native_resource_observer_v1::Equal,base,observer);
}
bool Completed(const native_loaded_ack::Context& c,bool afterAck=false) {
 auto& l=initial_load_v2::ledger;
 if(!c.transaction || !SameContext(c,Context()) || c.ownerThread!=GetCurrentThreadId() ||
    l.resourceObservationOnly || l.phase!=initial_load_v2::Phase::FinalizationBlocked ||
    finalization_v2::state.phase!=finalization_v2::Phase::RegisteredBlocked || !finalization_v2::HeldEngine(l.game) ||
    !SelectedCareerLoaded() || !NativeDriverSettingsReady(l.game) || !Signatures())return false;
 U32 frame{};if(!initial_load_v2::NativeFrame(l.allocator,frame))return false;
 l.tickFrame=frame; // Our ledger only; existing finalizer stopped refreshing it.
 native_registration_prerequisites_v2::Snapshot registration{};
 if(native_registration_prerequisites_v2::Inspect(LoadedAdapterRead,base,l.game,l.copies[2],l.entries,l.liveries,registration)!=native_registration_prerequisites_v2::Gate::Ready)return false;
 if(!finalization_v2::BoundOwners(true,afterAck) || !NativeLoadedConfiguration(afterAck) ||
    !FinalizerRetired() || !ResourcesAndVehicles())return false;
 return finalization_v2::BoundLiveSession(true) && SameContext(c,Context()) && FinalizerRetired();
}
void ObserveProgress() {
 if(!state.attempt.acknowledged || !Completed(state.context,true)){Reject("post-ack transaction/completion changed");return;}
 loaded_roster_v3::Progress progress{};
 const auto livery=[](U32 owner,int& value){return ReadStoredLivery(owner,value);};
 if(loaded_roster_v3::Inspect(LoadedAdapterRead,livery,LoadedRosterBinding(),true,progress)!=loaded_roster_v3::Gate::Ready) {
  Reject("post-ack roster/state changed");return;
 }
 if(progress.playerState[0]!=state.progress.playerState[0] || progress.playerState[1]!=state.progress.playerState[1] || progress.sessionState!=state.progress.sessionState) {
  char line[240]{};sprintf_s(line,"LOADED_ACK natural player0=%08x player1=%08x session=%08x acknowledged=1 activation=blocked driving=unproven\r\n",
       progress.playerState[0],progress.playerState[1],progress.sessionState);Log(line);state.progress=progress;
 }
 if(progress.sessionState==0x1490BD0 &&
    (progress.playerState[0]==0x1490BE4 || progress.playerState[0]==0x1490BA0) &&
    (progress.playerState[1]==0x1490BE4 || progress.playerState[1]==0x1490BA0))state.phase=Phase::ProgressObserved;
}
void TickBody() {
 if(state.phase==Phase::Rejected || state.phase==Phase::ProgressObserved)return;
 if(trace::Environment(L"DIRT2VR_ENGINE_LOADED_ACK_CANDIDATE")!=L"1") {
  if(state.attempt.attempted)Reject("diagnostic switch removed");return;
 }
 if(state.attempt.attempted) {ObserveProgress();return;}
 const auto& l=initial_load_v2::ledger;const auto& f=finalization_v2::state;
 if(l.resourceObservationOnly || l.phase==initial_load_v2::Phase::ResourceCheckpoint) {Reject("resource-only checkpoint has no audio/finalization proof");return;}
 if(l.phase==initial_load_v2::Phase::Failed || f.phase==finalization_v2::Phase::Failed || f.phase==finalization_v2::Phase::PlacementBlocked) {Reject("loading/finalization rejected");return;}
 if(l.phase!=initial_load_v2::Phase::FinalizationBlocked || f.phase!=finalization_v2::Phase::RegisteredBlocked)return;
 const auto c=Context();
 const auto complete=[](const native_loaded_ack::Context& value){return Completed(value);};
 const auto construct=[](native_loaded_ack::Event& event,U32 code) {
  reinterpret_cast<void*(__thiscall*)(native_loaded_ack::Event*,U32)>(At(0x80FA10))(&event,code);
 };
 const auto publish=[](U32 publisher,const native_loaded_ack::Event& event) {
  reinterpret_cast<void(__thiscall*)(void*,const native_loaded_ack::Event*)>(At(0x831F50))(reinterpret_cast<void*>(publisher),&event);
 };
 state.last=native_loaded_ack::PublishOnce(LoadedAdapterRead,LoadedAdapterEqual,complete,construct,publish,c,GetCurrentThreadId(),state.attempt);
 if(state.last==native_loaded_ack::Status::Published) {
  state.context=c;state.phase=Phase::Published;Log("LOADED_ACK event=7 native_latch=1 once=1 activation=blocked\r\n");return;
 }
 if(state.last!=native_loaded_ack::Status::WaitForBB4 && state.last!=native_loaded_ack::Status::WaitForTransport)
  Reject("completion/dispatch/readback rejected; no retry");
}
}
void NativeLoadedAckTickV2() {
 static volatile LONG busy{};if(InterlockedCompareExchange(&busy,1,0))return;
 __try {loaded_ack_adapter_v2::TickBody();}
 __except(EXCEPTION_EXECUTE_HANDLER){loaded_ack_adapter_v2::Reject("native exception; retain all owners until process exit");}
 InterlockedExchange(&busy,0);
}
