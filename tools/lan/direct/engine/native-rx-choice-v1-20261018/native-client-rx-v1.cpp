// Compile-only RX-choice fork of the retained V50 combined no-op context. Never executed.
#include <atomic>
#include "rx-request-v1.h"
#include <cmath>
#include "../scheduler-timestep-provenance-20261018/native-frame-code-guards-v1.h"
#include "../scheduler-timestep-provenance-20261018/native-frame-provenance-v1.h"
// Isolated native host initiation and completion observation. No menu/race control.
#include "../trace/guard.h"
#include "../direct-bootstrap/prerequisite-guard.h"
#include <MinHook.h>
#include <intrin.h>
#include <cstdint>
#include <cstddef>
#include <cstring>
#include "../direct-bootstrap/bootstrap-hold-guard.h"
#include "native-bootstrap-rx-v1.h"
#include "../native-join-20261018/native-join-contract.h"
#include "../direct-bootstrap/native-join-guard-v4.h"
#include "../direct-bootstrap/native-components-v1.h"
#include "../event-bootstrap-20261018/native-event-contract.h"

namespace {
using U32 = prerequisite::U32;
using Update = void (__thiscall*)(void*);
Update original{};
U32 base{};
HANDLE logFile = INVALID_HANDLE_VALUE;
volatile LONG started{}, armed{}, attempted{}, callbacks{}, ownerThread{}, careerPhase{};
U32 loadOwner{}, loadSdu{}, loadProfile{}, loadHandler{}, loadControl{}, loadContext{};
ULONGLONG loadBegan{};
int lastGate = -1;
U32 lastControlState=0xffffffff,loadHelper{};
volatile LONG headlessFailed{};
std::string expectedProfileName;
DWORD setupThread{};
U32 heldFlow{},heldOwner{};
volatile LONG holdCalls{},holdFailed{},holdThread{};
ULONGLONG holdSince{},holdLogAt{};
#include "private-game.inc"
constexpr unsigned char UpdateBytes[]{0x83,0xec,0x0c,0x56,0x8b,0xf1,0x8b,0x8e,0x98,0x20,0x00,0x00};
constexpr unsigned char BindBytes[]{0x56,0x57,0x8b,0x7c,0x24,0x0c,0x83,0xff,0x01,0x8b,0xf1};
constexpr unsigned char UserBytes[]{0x51,0x53,0x57,0x8b,0xf9,0x33,0xdb,0x83,0x7f,0x54,0xff};
constexpr unsigned char ContextBytes[]{0x8b,0x41,0x04,0x83,0xec,0x30,0x83,0xf8,0xff,0x56};
U32 At(U32 va) { return base + va - 0x400000; }
U32 Read(U32 p) {
    if (p < 0x10000 || p > 0x7ffffffb) return 0;
    __try { return *reinterpret_cast<const U32*>(p); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
}
void Log(const char* text) {
    DWORD written{};char line[2048]{};
    const int size=_snprintf_s(line,sizeof(line),_TRUNCATE,"[tick=%llu thread=%lu] %s",GetTickCount64(),GetCurrentThreadId(),text);
    if(size<0)return;
    WriteFile(logFile,line,static_cast<DWORD>(size),&written,nullptr);
    FlushFileBuffers(logFile);
}
LONG CALLBACK RecordFault(EXCEPTION_POINTERS* fault) {
    if (fault && fault->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION) {
        const auto r=fault->ContextRecord;
        char line[300]{};
        const U32 slot=Read(At(0x147D1F8));
        const U32 context=slot==0xffffffff?0:reinterpret_cast<U32>(TlsGetValue(slot));
        sprintf_s(line,"FAULT thread=%lu eip=%08lx eip_preferred=%08lx esp=%08lx edi=%08lx tls=%08x tls_allocator=%08x phase=%ld\r\n",
            GetCurrentThreadId(),r->Eip,r->Eip-base+0x400000,r->Esp,r->Edi,context,Read(context+4),careerPhase);Log(line);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
using BindUser=void(__thiscall*)(void*,int);
BindUser originalBind{};
void __fastcall ObserveBinding(void* self,void*,int requested) {
    const U32 manager=reinterpret_cast<U32>(self),caller=reinterpret_cast<U32>(_ReturnAddress());
    const bool owned=Read(At(0x14925E4))==manager;
    const U32 before=owned?Read(manager+0x54):0xffffffff;
    originalBind(self,requested); // Observe only; never suppress or replace native behavior.
    if(owned) {
        const U32 pe=Read(base+0x3C),size=Read(base+pe+0x50);
        const U32 preferred=caller>=base && caller-base<size?caller-base+0x400000:0;
        char line[220]{};sprintf_s(line,"BIND observed caller=%08x preferred=%08x requested=%d before=%u after=%u thread=%lu\r\n",caller,preferred,requested,before,Read(manager+0x54),GetCurrentThreadId());Log(line);
    }
}

native_frame_provenance_v1::Epoch activationEpoch;std::uint64_t activationCapturedEpoch{};bool activationEpochFailed{};
double activationNativeStep{};const double* activationStepPointer{};U32 activationStepCaller{};
void InitialLoadJob(void* self);
void BeginCoreInitialization(U32 frontend);
bool CoreInitializationReady();
bool CoreSignatures();
// 5781A0 is RET 4: scheduler supplies one time-step pointer, even though
// the original body does not read that argument. Preserve its callee cleanup.
using FrontendUpdate=void(__thiscall*)(void*,const double*);
FrontendUpdate originalFrontendUpdate{};
constexpr unsigned char FrontendReturnBytes[]{0xC2,0x04,0x00};
constexpr unsigned char FrontendBytes[]{0x83,0xec,0x2c,0x56,0x8b,0xf1,0x8b,0x46,0x08,0x8b,0x0d};
void __fastcall HoldFrontendUpdate(void* self,void*,const double* step) {
    const U32 frontend=reinterpret_cast<U32>(self),flow=Read(frontend+0x44);
    bootstrap_hold::Objects objects{};
    const auto gate=bootstrap_hold::Inspect(Read,base,flow,objects);
    if(gate!=bootstrap_hold::Gate::Ready || objects.frontend!=frontend) {
        if(flow==heldFlow) {InterlockedExchange(&holdFailed,1);Log("BOOT REJECT owned frontend changed\r\n");return;}
        originalFrontendUpdate(self,step);return;
    }
    const LONG thread=static_cast<LONG>(GetCurrentThreadId());
    if((heldFlow && heldFlow!=flow) || (holdThread && holdThread!=thread)) {
        InterlockedExchange(&holdFailed,1);Log("BOOT REJECT frontend/thread changed\r\n");return;
    }
    if(!heldFlow) {heldFlow=flow;heldOwner=objects.owner;holdThread=thread;holdSince=GetTickCount64();BeginCoreInitialization(frontend);}
    const LONG calls=InterlockedIncrement(&holdCalls);const ULONGLONG now=GetTickCount64();
    if(calls==1 || now-holdLogAt>=10000) {
        char line[240]{};sprintf_s(line,"BOOT held native-frontend-job flow=%08x owner=%08x thread=%ld calls=%ld flags=%08x elapsed=%llu original_called=0\r\n",
            flow,objects.owner,thread,calls,Read(flow+0x18),now-holdSince);Log(line);holdLogAt=now;
    }
    activationCapturedEpoch=activationEpoch.current;activationStepPointer=step;activationStepCaller=reinterpret_cast<U32>(_ReturnAddress());
    __try{activationNativeStep=step?*step:0.0;}__except(EXCEPTION_EXECUTE_HANDLER){activationNativeStep=-1.0;}
    InitialLoadJob(reinterpret_cast<void*>(objects.owner));
    // Own the entire frontend update, not only its flow activation. The stock
    // job's later presentation updates require globals created by that flow.
    // Engine/network/save jobs remain scheduled; the independent bootstrap
    // pump runs on the normal engine pre-update callback.
}

#include "../direct-bootstrap/native-allocation-inventory-v1.inc"
void CareerTick(void* self);
void NativeEventTick(void* self);
void NativeLocalIdentityTick();
void NativeSearchTick(void* self);
void NativeJoinTick(void* self);
void NativeCarTick(void* self);
void NativeCarCommitTick(void* self);
void NativeReadyTick(void* self);
void NativeLobbyTick(void* self);
void RoundCarTick(void* self);
void InitialConfigTick(void* self);
bool SearchSignatures();
bool EventSignatures();
void __fastcall BeforeUpdate(void* self, void*) {
    original(self); // Preserve the complete native pre-execution callback.
    if(!heldFlow || holdFailed || reinterpret_cast<U32>(self)!=heldOwner) return;
    bootstrap_hold::Objects held{};
    if(bootstrap_hold::Inspect(Read,base,heldFlow,held)!=bootstrap_hold::Gate::Ready ||
       GetCurrentThreadId()!=static_cast<DWORD>(holdThread)) {
        InterlockedExchange(&holdFailed,1);Log("BOOT REJECT ownership at engine callback\r\n");return;
    }
    if(!activationEpoch.Advance(base,heldOwner,GetCurrentThreadId(),reinterpret_cast<U32>(_ReturnAddress()))) {
        if(!activationEpochFailed)Log("ACT epoch_reject original preupdate return/owner\r\n");activationEpochFailed=true;
    }
    if(!CoreInitializationReady()) return;
    CareerTick(self);
    NativeEventTick(self);
    NativeLocalIdentityTick();
    NativeSearchTick(self);
    NativeJoinTick(self);
    NativeCarTick(self);
    NativeCarCommitTick(self);
    NativeReadyTick(self);
    NativeLobbyTick(self);
    RoundCarTick(self);
    InitialConfigTick(self);
    NativeAllocationInventory();
    if (!InterlockedCompareExchange(&armed, 0, 0) || InterlockedCompareExchange(&attempted, 0, 0)) return;
    const LONG thread = static_cast<LONG>(GetCurrentThreadId());
    const LONG previous = InterlockedCompareExchange(&ownerThread, thread, 0);
    if (static_cast<DWORD>(thread) == setupThread || (previous && previous != thread)) {
        InterlockedExchange(&attempted, 1);
        Log("REJECT callback thread identity changed or equals setup thread\r\n");
        return;
    }
    const LONG count = InterlockedIncrement(&callbacks);
    if (count < 10) return; // Observe stable callbacks before one bounded attempt.
    prerequisite::Objects objects{};
    const auto gate = prerequisite::Inspect(Read, base, reinterpret_cast<U32>(self), objects);
    if (static_cast<int>(gate) != lastGate) {
        char line[180]{};
        sprintf_s(line, "OBSERVE thread=%lu owner=%08x callbacks=%ld gate=%d\r\n", GetCurrentThreadId(), reinterpret_cast<U32>(self), count, static_cast<int>(gate));
        Log(line); lastGate = static_cast<int>(gate);
    }
    if (gate != prerequisite::Gate::Ready || InterlockedCompareExchange(&attempted, 1, 0)) return;
    char line[240]{};
    sprintf_s(line, "CALL bind-local-user thread=%lu manager=%08x index=0\r\n", GetCurrentThreadId(), objects.manager);
    Log(line);
    // Exact original thiscall ABIs: C95FB0 ret 4; C91330 ret; EBBEA0 ret.
    // Exceptions from native code are not swallowed; the isolated run must fail.
    reinterpret_cast<void (__thiscall*)(void*, int)>(At(0xC95FB0))(reinterpret_cast<void*>(objects.manager), 0);
    const U32 user = reinterpret_cast<U32 (__thiscall*)(void*)>(At(0xC91330))(reinterpret_cast<void*>(objects.manager));
    if (user != objects.user || Read(objects.manager + 0x54) != 0) {
        sprintf_s(line, "REJECT binding readback user=%08x expected=%08x index=%u\r\n", user, objects.user, Read(objects.manager + 0x54));
        Log(line); return;
    }
    // Binding notifications can change native ownership; validate before step 2.
    const U32 sdu = Read(reinterpret_cast<U32>(self) + 0x2294);
    if (Read(sdu) != At(0x1328574) || Read(sdu + 0x54) != 0 || Read(sdu + 0x3C) + 0x150 != objects.context ||
        Read(objects.context) != At(0x1375B40) || Read(objects.context + 4) != 0xffffffff) {
        Log("REJECT context ownership changed during user binding\r\n"); return;
    }
    Log("CALL create-protected-context\r\n");
    const U32 status = reinterpret_cast<U32 (__thiscall*)(void*)>(At(0xEBBEA0))(reinterpret_cast<void*>(objects.context));
    sprintf_s(line, "RESULT thread=%lu native_user=%08x index=%u context_status=%u context_handle=%08x direct_start=0 current_race_join=0\r\n",
              GetCurrentThreadId(), user, Read(objects.manager + 0x54), status, Read(objects.context + 4));
    Log(line);
    if (status == 0 && Read(objects.context + 4) != 0xffffffff) InterlockedExchange(&careerPhase, 1);
}

#include "rx-request-live-v1.inc"
#include "../direct-bootstrap/career-load-v7.inc"
#include "../direct-bootstrap/career-headless-v7.inc"
#include "../direct-bootstrap/core-initialization-v1.inc"
#include "host-event-rx-v1.inc"
#include "../direct-bootstrap/native-search-join-v1.inc"
#include "../direct-bootstrap/native-peer-code-guards-v2.inc"
#include "../direct-bootstrap/native-local-identity-v1.inc"
#include "../direct-bootstrap/native-join-v3.inc"
#include "native-choice-rx-v1.inc"
#include "rx-catalog-live-v1.inc"
#include "../direct-bootstrap/distinct-cars-candidate-v1/native-car-client-v1.inc"
#include "../direct-bootstrap/distinct-cars-candidate-v1/native-car-commit-client-v1.inc"
#include "../direct-bootstrap/native-client-ready-v1.inc"
#include "../direct-bootstrap/native-lobby-code-guards-v1.inc"
#include "../direct-bootstrap/native-client-lobby-v1.inc"
#include "../direct-bootstrap/distinct-cars-candidate-v1/native-round-car-v1.inc"
void RoundCarTick(void* self) {NativeRoundCarTick(self,searchContainer,searchHolder,joinedSession,joinedTransport,false);}
#include "../direct-bootstrap/native-config-code-guards-v2.inc"
#include "native-initial-config-rx-v1.inc"
void InitialConfigTick(void* self){NativeInitialConfigTick(self,searchContainer,searchHolder,joinedSession,joinedTransport,false);}
#include "../direct-bootstrap/native-resource-observer-adapter-v1.inc"
bool NativeLoadedRosterPlayers(ConfigPlayer (&out)[2],bool afterAck);
bool NativeLoadedSessionState(U32 tree,U32 session,bool afterAck);
#include "native-initial-load-rx-v1.inc"
#include "native-loaded-roster-rx-v1.inc"
#include "../direct-bootstrap/native-finalization-live-v7.inc"
#include "../direct-bootstrap/native-driver-settings-v1.inc"
#include "../loaded-ack-integration-v2-20261018/native-loaded-ack-adapter-v2.inc"
#include "../direct-bootstrap/native-activation-live-v2.inc"
#include "../direct-bootstrap/native-activation-live-scope-v2.inc"
#include "../direct-bootstrap/native-distance-rule.inc"
#include "../combined-noop-scope-20261018/frame-combined-noop-v1.inc"
#include "../nd2-buffer-observer-20261018/nd2-buffer-observer-v1.inc"
void InitialLoadJob(void* self) {
    if(!armed || !initialConfigCommitted || holdFailed || !CoreInitializationReady())return;
    initial_load_v2::Permit permit{};
    permit.thread=GetCurrentThreadId();
    // Entry is reachable only after pinned-image bootstrap and the exact held-job checks.
    permit.exactImageValidated=true;
    permit.exclusiveInitialLoad=heldOwner==reinterpret_cast<U32>(self) &&
        heldOwner==loadOwner && careerPhase==3 && !headlessFailed && SelectedCareerLoaded();
    // Core preparation completed in each actual native queue; this callback is
    // executing on the still-running owner scheduler. Pending work has deadlines.
    permit.nativeQueuesAdvancing=coreReady!=0 && coreObserved==7 && holdCalls>1;
    NativeInitialLoadAndActivateScopedV2(self,searchContainer,searchHolder,joinedSession,joinedTransport,false,permit);
    query_retirement_live::AfterNativeStartup();
    frame_observer::AfterNativeStartup();
    vehicle_processor_live::AfterNativeStartup();
    cloth_observer_live::AfterNativeStartup();
    nd2_observer::Drain();
}
}

extern "C" DWORD WINAPI StartBootstrap(void*) {
    if (InterlockedCompareExchange(&started, 1, 0)) return 10;
    if (trace::Environment(L"DIRT2VR_ENGINE_EVENT_PROBE") != L"1") return 22;
    if (trace::Environment(L"DIRT2VR_ENGINE_CAREER") != L"1" || trace::Environment(L"DIRT2VR_ENGINE_BOOTSTRAP_HOLD") != L"1") return 11;
    if (!InPrivateGameCopy()) return 12;
    std::string hash;
    if (!trace::FileDigest(trace::ModulePath(), hash) || hash != trace::OriginalHash) return 13;
    const auto expected = trace::Environment(L"DIRT2VR_ENGINE_EXPECTED_NAME");
    if(expected.empty() || expected.size()>63) return 21;
    for(const wchar_t c:expected) {if(c<32 || c>126) return 21;expectedProfileName.push_back(static_cast<char>(c));}
    const auto path = trace::Environment(L"DIRT2VR_ENGINE_CAREER_LOG");
    if (!trace::AbsolutePath(path)) return 14;
    if(!ParseJoinTarget())return 23;
    if(!InitializeRxRequest() || !InitializeVehicleRequest())return 23;
    base = reinterpret_cast<U32>(GetModuleHandleW(nullptr));
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0x737CB0)), UpdateBytes, sizeof(UpdateBytes))) return 1500;
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0xC95FB0)), BindBytes, sizeof(BindBytes))) return 1501;
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0xC91330)), UserBytes, sizeof(UserBytes))) return 1502;
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0xEBBEA0)), ContextBytes, sizeof(ContextBytes))) return 1503;
    if (!CareerSignatures()) return 1504;
    if (!CoreSignatures()) return 1505;
    if (!EventSignatures()) return 1506;
    if (!SearchSignatures()) return 1507;
    if (!CarSignatures()) return 1508;
    if (!DistinctChoiceSignatures()) return 1509;
    if (!CarCommitSignatures()) return 1510;
    if (!ReadySignatures()) return 1511;
    if (!LobbySignatures()) return 1512;
    if (!PeerIdentitySignatures()) return 1513;
    if (!InitialConfigSignatures()) return 1514;
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0x5781A0)),FrontendReturnBytes,sizeof(FrontendReturnBytes))) return 1515;
    if (!trace::ReadableEqual(reinterpret_cast<void*>(At(0x577DE0)),FrontendBytes,sizeof(FrontendBytes))) return 1516;
    logFile = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (logFile == INVALID_HANDLE_VALUE) return 16;
    HMODULE pinned{};
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                           reinterpret_cast<LPCWSTR>(&StartBootstrap), &pinned)) return 17;
    AddVectoredExceptionHandler(0, RecordFault);
    setupThread = GetCurrentThreadId();
    char line[200]{};
    sprintf_s(line, "START pid=%lu setup_thread=%lu image=%08x menu_calls=0 race_calls=0\r\n", GetCurrentProcessId(), setupThread, base);
    Log(line);
    sprintf_s(line,"RX_REQUEST race=1 class=1 rounds=1 track=%d location=%d laps=3 collision=0 defaulted=%u\r\n",RxRequest().selected.model,RxRequest().selected.location,unsigned(RxRequest().defaulted));Log(line);
    sprintf_s(line,"CHOICE immutable request vehicle=%d defaulted=%u\r\n",vehicleRequest.vehicle,unsigned(vehicleRequest.defaulted));Log(line);
    if (MH_Initialize() != MH_OK || MH_CreateHook(reinterpret_cast<void*>(At(0x737CB0)), reinterpret_cast<void*>(BeforeUpdate), reinterpret_cast<void**>(&original)) != MH_OK) return 18;
    // Never unload the library or trampoline while native callbacks can run.
    if (!direct_distance_rule::Install()) return 19;
    if (MH_CreateHook(reinterpret_cast<void*>(At(0x577DE0)),reinterpret_cast<void*>(HoldFrontendUpdate),reinterpret_cast<void**>(&originalFrontendUpdate))!=MH_OK || !InstallSaveHooks() || !nd2_observer::Install() || MH_CreateHook(reinterpret_cast<void*>(At(0xC95FB0)),reinterpret_cast<void*>(ObserveBinding),reinterpret_cast<void**>(&originalBind))!=MH_OK || MH_EnableHook(MH_ALL_HOOKS) != MH_OK) return 19;
    InterlockedExchange(&armed, 1);
    return 0;
}
