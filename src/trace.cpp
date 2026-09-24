#include "trace.h"
#include "common.h"
#include "eye_pair.h"
#include "camera_math.h"
#include "game_xr.h"
#include "hud_capture.h"
#include "hud_elements.h"
#include "water_trace.h"
#include "gpu_timer.h"
#include "vr_hotkeys.h"
#include "light_replay.h"
#include "ground_cover.h"
#include "direct_menus.h"
#include "gfwl_compat.h"
#include "driving_controls.h"
#include "cockpit_start.h"
#include <MinHook.h>
#include <d3dcompiler.h>
#include <d3d11shader.h>
#include <wrl/client.h>
#include <psapi.h>
#include <atomic>
#include <fstream>
#include <mutex>
#include <unordered_set>
#include <unordered_map>
#include <array>
#include <vector>
#include <intrin.h>
#include <cmath>
#include <algorithm>
#include <stdexcept>
#include <cwchar>

using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
std::atomic<unsigned long long> frame{}, draws{}, dispatches{};
std::mutex attachMutex, shaderMutex;
std::unordered_set<void*> targets;
std::unordered_set<uint64_t> shaders;
std::unordered_map<void*,uint64_t> shaderNames;
using LapDescriptorCopyFn = void* (__thiscall*)(void*,const unsigned*);
LapDescriptorCopyFn realLapDescriptorCopy{};
unsigned directLaps=1;
void* directLapCaller{};
void* __fastcall LapDescriptorCopy(void* target,void*,const unsigned* source) {
    // Only the demo route constructor; all other descriptor copies are untouched.
    if(_ReturnAddress()!=directLapCaller) return realLapDescriptorCopy(target,source);
    unsigned descriptor[14];
    memcpy(descriptor,source,sizeof(descriptor));
    if(descriptor[0]!=1 || descriptor[5]!=1) {
        Log("direct start: unexpected demo route descriptor; stopping launch");
        ExitProcess(ERROR_BAD_EXE_FORMAT);
    }
    descriptor[5]=directLaps;
    auto result=realLapDescriptorCopy(target,descriptor);
    Log("direct start: demo route laps=%u copied=%u",directLaps,static_cast<unsigned*>(target)[5]);
    return result;
}
bool EnableDirectLaps(unsigned char* base,unsigned laps) {
    if(laps==1) return true;
    const unsigned char copyPrologue[]={0x8b,0xc1,0x8b,0x4c,0x24,0x04,0x8b,0x11};
    const unsigned char callSite[]={0x89,0x94,0x24,0xa4,0x00,0x00,0x00,0xe8,0xfb,0x56,0x02,0x00};
    if(memcmp(base+0x36a3d0,copyPrologue,sizeof(copyPrologue))!=0 ||
       memcmp(base+0x344cc9,callSite,sizeof(callSite))!=0) return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    directLaps=laps;
    directLapCaller=base+0x344cd5;
    status=MH_CreateHook(base+0x36a3d0,reinterpret_cast<void*>(LapDescriptorCopy),reinterpret_cast<void**>(&realLapDescriptorCopy));
    if(status==MH_OK) status=EnableRecordedHook(base+0x36a3d0);
    Log("direct start: demo lap override=%u hook=%s",laps,MH_StatusToString(status));
    return status==MH_OK;
}
// The demo start path otherwise forces the local vehicle back to AI every update.
// Only change the controller's override, preserving the frontend's loading flow.
void EnableDirectPractice() {
    static bool applied{};
    wchar_t enabled[8]{};
    if(applied || GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE",enabled,8)!=1 || enabled[0]!=L'1') return;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const unsigned char forceAi[]={0x74,0x06,0xc6,0x43,0x3a,0x01,0xeb,0x04,0xc6,0x43,0x3a,0x00};
    const unsigned char update[]={0x83,0xec,0x6c,0x53,0x55,0x8b,0xd9,0x80,0x7b,0x20,0x01};
    DWORD previous{};
    if(SupportedHost() && memcmp(base+0x72a5a1,forceAi,sizeof(forceAi))==0 &&
       memcmp(base+0x72a580,update,sizeof(update))==0 &&
       VirtualProtect(base+0x72a5a1,1,PAGE_EXECUTE_READWRITE,&previous)) {
        base[0x72a5a1]=0xeb;
        RecordCodeByte(base+0x72a5a1,0x74,0xeb);
        DWORD ignored{};
        const bool protectedAgain=VirtualProtect(base+0x72a5a1,1,previous,&ignored)!=0;
        const bool flushed=FlushInstructionCache(GetCurrentProcess(),base+0x72a5a1,1)!=0;
        if(protectedAgain && flushed) {
            if(!EnableDirectReturn()) {
                Log("direct session: return-to-menus hook guard failed");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            wchar_t text[16]{};
            const auto length=GetEnvironmentVariableW(L"DIRT2VR_LAPS",text,16);
            if(length) {
                if(length>=16) ExitProcess(ERROR_INVALID_PARAMETER);
                wchar_t* end{};
                const auto laps=wcstoul(text,&end,10);
                if(end==text || *end || laps<1 || laps>20 || !EnableDirectLaps(base,static_cast<unsigned>(laps))) {
                    Log("direct start: lap override validation failed");
                    ExitProcess(ERROR_BAD_EXE_FORMAT);
                }
                Log("direct start: requested laps=%lu (memory only)",laps);
            }
            applied=true;
            Log("direct practice: local human controller enabled (memory only)");
            return;
        }
    }
    Log("direct practice: instruction/protection guard failed; stopping launch");
    MessageBoxW(nullptr,L"Direct practice is incompatible with this game process. Use Game menus in the launcher.",L"DiRT2VR",MB_OK|MB_ICONERROR);
    ExitProcess(ERROR_BAD_EXE_FORMAT); // The session manager restores the prepared assets.
}
thread_local unsigned scenePass{};
using PresentFn = HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using DrawIndexedFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,INT);
using DrawFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT);
using DrawInstancedFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT,UINT);
using DrawIndexedInstancedFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT,INT,UINT);
using DispatchFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT);
using TargetsFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,ID3D11RenderTargetView* const*,ID3D11DepthStencilView*);
using VertexShaderFn = HRESULT (STDMETHODCALLTYPE*)(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11VertexShader**);
using PixelShaderFn = HRESULT (STDMETHODCALLTYPE*)(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11PixelShader**);
PresentFn realPresent{};
DrawIndexedFn realDrawIndexed{};
DrawFn realDraw{};
DrawInstancedFn realDrawInstanced{};
DrawIndexedInstancedFn realDrawIndexedInstanced{};
DispatchFn realDispatch{};
TargetsFn realTargets{};
VertexShaderFn realVS{};
PixelShaderFn realPS{};
using MapFn = HRESULT (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11Resource*,UINT,D3D11_MAP,UINT,D3D11_MAPPED_SUBRESOURCE*);
using UnmapFn = void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11Resource*,UINT);
MapFn realMap{};
UnmapFn realUnmap{};
thread_local std::unordered_map<ID3D11Resource*,const void*> cameraMaps;
using SceneFn = void (__thiscall*)(void*,void*,void*,void*,void*,void*);
SceneFn realScene{};
using InnerFn = void (__thiscall*)(void*,void*,void*,void*,void*,void*,void*);
InnerFn realInner{};
using CameraSetupFn = void (__thiscall*)(void*,void*,void*,float,bool);
CameraSetupFn realCameraSetup{};
using CameraUploadFn = void (__thiscall*)(void*,void*);
CameraUploadFn realCameraUpload{};
thread_local void* eyeRenderer{};
thread_local bool eyeCameraSetup{};
thread_local void* waterEyeRenderer{};
void* waterRenderer{};
uint64_t waterRendererFrame=~uint64_t{};
bool WaterReflectionsEnabled() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_WATER_REFLECTIONS",value,8)==1 && value[0]==L'1'; }();
    return enabled;
}
std::array<std::vector<std::string>,2> waterProbeDraws;
bool waterProbeRecording{};
// 0x33ad10 builds this scalar-pointer list header on its stack. The linked
// batches belong to the reflection renderer and remain intact until its next
// preparation. Copy the header only; never rerun queue construction mid-frame.
std::array<unsigned char,0xf4> waterLists;
uint64_t waterListsFrame=~uint64_t{};
void *waterCameraA{},*waterCameraB{},*waterContext{},*waterScene{},*waterFlags{};
bool waterCameraOnly{};
DWORD waterCameraThread{};
bool __stdcall WaterCameraOnlyFor(void* renderer) {
    return waterCameraOnly && renderer==waterEyeRenderer && GetCurrentThreadId()==waterCameraThread;
}
void *waterPrepareTail{},*waterPrepareExit{};
__declspec(naked) void WaterPrepareTail() {
    __asm {
        pushfd
        cmp byte ptr [waterCameraOnly],0
        je normal
        pushad
        push esi
        call WaterCameraOnlyFor
        test eax,eax
        jz otherThread
        popad
        popfd
        // Balance the FPU load preceding the intercepted LEA, as the original
        // tail does, then use the game's own saved-register epilogue.
        fstp dword ptr [ebx+0xf0]
        jmp dword ptr [waterPrepareExit]
    otherThread:
        popad
    normal:
        popfd
        jmp dword ptr [waterPrepareTail]
    }
}

thread_local float projectionShift{};
thread_local const XrFovf* eyeFov{};
thread_local unsigned eyeProjectionUploads{};
thread_local unsigned lightingEye{}; // 0 = original scene, 1/2 = headset eyes.
thread_local GroundCoverPair groundCoverPair;
bool LightingTrace() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_TRACE_LIGHTS",value,8)>0 && wcscmp(value,L"1")==0; }();
    return LoggingEnabled() && enabled;
}
void Stack(const char* event);
using LightSetupFn = void (__thiscall*)(void*,void*,void*,void*);
using PointSetupFn = void (__thiscall*)(void*,void*,void*);
LightSetupFn realSpotSetup{},realProjectedSetup{};
PointSetupFn realPointSetup{};
PreparedLights preparedLights;
bool lightingHooksReady{};
thread_local std::vector<LightCall> eyeLights;
thread_local bool lightsRefreshed{};
void RememberLight(unsigned kind,void* self,void* light,void* context,void* material) {
    preparedLights.Remember(frame.load(),{kind,self,light,context,material});
}
void RefreshLights(void* context) {
    if(lightsRefreshed) return;
    lightsRefreshed=true;
    for(const auto& call:eyeLights) {
        if(call.kind==0) realPointSetup(call.self,call.light,context);
        else if(call.kind==1) realSpotSetup(call.self,call.light,context,call.material);
        else realProjectedSetup(call.self,call.light,context,call.material);
    }
    if(LightingTrace() && frame.load()%120==0)
        Log("lighting refreshed frame=%llu eye=%u context=%p calls=%zu",frame.load(),lightingEye,context,eyeLights.size());
}
void TraceLight(unsigned kind,void* light,void* context) {
    static std::atomic<unsigned> seen{};
    const unsigned bit=1u<<(kind*3+lightingEye);
    if(!(seen.fetch_or(bit)&bit)) { Stack("LightSetup"); Log("light setup kind=%u eye=%u",kind,lightingEye); }
    if(frame.load()%120) return;
    static std::mutex outputMutex;
    std::lock_guard lock(outputMutex);
    static auto out=TraceFile(Output()/"lights.csv");
    static bool header=false;
    if(!header) {
        out << "frame,kind,eye,light,context";
        for(unsigned i=0;i<16;++i) out << ",view" << i;
        for(unsigned i=0;i<16;++i) out << ",lightWorld" << i;
        out << '\n'; header=true;
    }
    const auto view=reinterpret_cast<const float*>(static_cast<const unsigned char*>(context)+0x160);
    const auto object=*reinterpret_cast<unsigned char**>(static_cast<unsigned char*>(light)+0x1c);
    const auto world=reinterpret_cast<const float*>(object+0x90);
    out << frame.load() << ',' << kind << ',' << lightingEye << ',' << light << ',' << context;
    for(unsigned i=0;i<16;++i) out << ',' << view[i];
    for(unsigned i=0;i<16;++i) out << ',' << world[i];
    out << '\n'; out.flush();
}
void __fastcall PointSetup(void* self,void*,void* light,void* context) {
    RememberLight(0,self,light,context,nullptr);
    if(LightingTrace()) TraceLight(0,light,context);
    realPointSetup(self,light,context);
}
void __fastcall SpotSetup(void* self,void*,void* light,void* context,void* material) {
    RememberLight(1,self,light,context,material);
    if(LightingTrace()) TraceLight(1,light,context);
    realSpotSetup(self,light,context,material);
}
void __fastcall ProjectedSetup(void* self,void*,void* light,void* context,void* material) {
    RememberLight(2,self,light,context,material);
    if(LightingTrace()) TraceLight(2,light,context);
    realProjectedSetup(self,light,context,material);
}
bool cameraHooksReady{};
bool cameraSetupHookReady{};
using PauseWorldFn = int (__thiscall*)(void*,const void*);
PauseWorldFn realPauseWorld{};
bool pauseHookReady{};
std::atomic<bool> pauseWorld{};
int __fastcall PauseWorld(void* self,void*,const void* message) {
    // RenderPauseWorld's handler copies message+4 to frontend+0x96.
    // Observe the engine event, including gamepad pause; never change it.
    const auto bytes=static_cast<const unsigned char*>(message);
    const bool paused=bytes[4]!=0;
    const int result=realPauseWorld(self,message);
    if(pauseWorld.exchange(paused)!=paused)
        Log("OpenXR pause world=%d frame=%llu auxiliary=%u",paused,frame.load(),bytes[5]);
    return result;
}
using FrustumCopyFn = void* (__thiscall*)(void*,const void*);
FrustumCopyFn realFrustumCopy{},buildFrustum{};
bool WideVisibility() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_WIDE_VISIBILITY",value,8)>0 && wcscmp(value,L"1")==0; }();
    return enabled;
}
void* __fastcall FrustumCopy(void* self,void*,const void* source) {
    const auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-base;
    // This call copies the main renderer's visibility volume before scenery
    // jobs start. Do not alter reflection/shadow frustums or eye projections.
    if(caller==0x292b99 && buildFrustum) {
        const auto renderer=static_cast<unsigned char*>(self)-0x340;
        const auto a=reinterpret_cast<const float*>(renderer+0x5e0);
        const auto b=reinterpret_cast<const float*>(renderer+0x650);
        alignas(16) std::array<float,16> matrix;
        // The engine writes XYZ corners but leaves their fourth lane untouched.
        alignas(16) std::array<float,56> volume{};
        if(CockpitCameraCandidate(a,b) && VisibilityBox(a,b,matrix.data())) {
            buildFrustum(volume.data(),matrix.data());
            bool finite=true; for(float v:volume) finite &= std::isfinite(v);
            if(finite) {
                static std::atomic<bool> captured{};
                if(!captured.exchange(true)) {
                    auto before=TraceFile(Output()/"visibility-original.bin",std::ios::binary);
                    before.write(static_cast<const char*>(source),224);
                    auto after=TraceFile(Output()/"visibility-expanded.bin",std::ios::binary);
                    after.write(reinterpret_cast<const char*>(volume.data()),224);
                    Log("visibility expanded to all directions frame=%llu far=%.1f/%.1f",frame.load(),a[22],b[22]);
                }
                if(frame.load()%120==0) Log("visibility all-directions frame=%llu far=%.1f/%.1f",frame.load(),a[22],b[22]);
                return realFrustumCopy(self,volume.data());
            }
            Log("visibility expansion rejected invalid frustum frame=%llu",frame.load());
        }
    }
    return realFrustumCopy(self,source);
}

void __fastcall CameraSetup(void* self,void*,void* context,void* camera,float nearPlane,bool upload) {
    const bool previous=eyeCameraSetup;
    auto bytes=static_cast<unsigned char*>(self);
    eyeCameraSetup=(self==eyeRenderer && (camera==bytes+0x5e0 || camera==bytes+0x650)) ||
        (self==waterEyeRenderer && camera==*reinterpret_cast<void**>(bytes+0x940));
    realCameraSetup(self,context,camera,nearPlane,upload);
    if(eyeCameraSetup && !waterEyeRenderer && lightingEye && upload) RefreshLights(context);
    eyeCameraSetup=previous;
}
void __fastcall CameraUpload(void* self,void*,void* context) {
    if(eyeCameraSetup && (projectionShift!=0 || eyeFov)) {
        auto bytes=static_cast<unsigned char*>(context);
        auto projection=reinterpret_cast<float*>(bytes+0x120);
        const auto view=reinterpret_cast<const float*>(bytes+0x160);
        auto combined=reinterpret_cast<float*>(bytes+0x1a0);
        // This engine uses a row-vector projection with its own depth mapping.
        // Preserve depth; update the off-centre X term and its derived matrix.
        if(projection[11]==-1.f && projection[15]==0.f) {
            if(eyeFov) { ApplyFov(projection,*eyeFov); ++eyeProjectionUploads; }
            else projection[8]=projectionShift;
            MultiplyMatrices(view,projection,combined);
        }
    }
    realCameraUpload(self,context);
}
thread_local bool sampleInner{};
thread_local bool continuousMain{};
IDXGISwapChain* gameSwapchain{}; // Diagnostic run owns one swapchain until process exit.
void Screenshot(IDXGISwapChain*,unsigned long long);
void ScreenshotTexture(ID3D11Texture2D*,unsigned long long,bool alpha=false);
bool ContinuousReplayEnabled() {
    static const bool enabled=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_CONTINUOUS_REPLAY",value,16)>0 && wcscmp(value,L"1")==0; }();
    return enabled;
}
bool HeadsetEnabled() {
    static const bool enabled=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_HEADSET",value,16)>0 && wcscmp(value,L"1")==0; }();
    return enabled;
}
bool InteractiveEnabled() {
    static const bool enabled=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_INTERACTIVE",value,16)>0 && wcscmp(value,L"1")==0; }();
    return enabled;
}
bool DetailedTrace() {
    static const bool enabled=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_CAPTURE_DIAGNOSTICS",value,16)>0 ? wcscmp(value,L"1")==0 : !InteractiveEnabled(); }();
    return LoggingEnabled() && enabled;
}
bool HudProbe() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_HUD_PROBE",value,8)==1 && value[0]==L'1'; }();
    return DetailedTrace() && enabled;
}
unsigned HiddenHudElements() {
    static const unsigned mask=[] { char text[16]{}; auto n=GetEnvironmentVariableA("DIRT2VR_HUD_HIDE",text,sizeof(text)); return n>0 && n<sizeof(text) ? ParseHudHidden(text) : 1; }();
    return mask;
}
bool RequestedCapturesEnabled() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_CAPTURE_REQUESTS",value,8)>0 && wcscmp(value,L"1")==0; }();
    return LoggingEnabled() && enabled;
}
uint64_t requestedCaptureFrame=~uint64_t{};
unsigned requestedCaptureCount{};
bool TakeCaptureRequest(uint64_t f) {
    if(!RequestedCapturesEnabled() || requestedCaptureCount>=4 || f%60) return false;
    std::error_code error;
    if(!std::filesystem::remove(Output()/"capture.request",error)) return false;
    requestedCaptureFrame=f; ++requestedCaptureCount;
    Log("requested stereo capture=%u frame=%llu; capture stalls excluded from performance acceptance",requestedCaptureCount,f);
    return true;
}
// Process-owned diagnostic session. Never invoke the runtime under DLL detach's
// loader lock; explicit runtime stop/error is handled on the rendering thread.
GameXr* gameXr{};
HudCapture hudCapture;
uint64_t hudSceneFrame=~uint64_t{};
thread_local bool hudProbeDrawing{};
thread_local unsigned hudEye{};
bool HudShader(ID3D11DeviceContext* context) {
    ComPtr<ID3D11VertexShader> shader; context->VSGetShader(&shader,nullptr,nullptr);
    std::lock_guard lock(shaderMutex);
    auto found=shaderNames.find(shader.Get());
    if(found==shaderNames.end()) return false;
    // EGO UI image, ramp, distance-field font and lit dial shader variants.
    switch(found->second) {
    case 0xbef999561247f2d5ull: case 0x9d4dd795ddf7c656ull:
    case 0x47b5203eae84ab0full: case 0xedfda3c33aa2b9fcull:
    case 0xce4f6cb0ec7d5697ull: return true;
    default: return false;
    }
}
bool CaptureHudDraw(ID3D11DeviceContext* context,const std::function<void()>& draw) {
    if((!hudProbeDrawing && !hudEye) || !HudShader(context)) return false;
    const bool captured=hudCapture.Draw(context,draw,hudEye!=2);
    return hudEye && captured; // route HUD to the quad instead of either world image
}
uint64_t xrTickFrame=~uint64_t{};
XrPosef headsetReference{};
bool recenterRequested=true;
bool AutoCockpitEnabled() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_AUTO_COCKPIT",value,8)==1 && value[0]==L'1'; }();
    return enabled;
}
bool& ScreenMode() { static bool screen=InteractiveEnabled() && !AutoCockpitEnabled(); return screen; }
float GraphicsScale(const wchar_t* name,float fallback,float minimum,float maximum) {
    wchar_t value[32]{}; const auto length=GetEnvironmentVariableW(name,value,32);
    if(!length || length>=32) return fallback;
    wchar_t* end{}; const float scale=wcstof(value,&end);
    return end!=value && !*end && std::isfinite(scale) && scale>=minimum && scale<=maximum ? scale : fallback;
}
bool EnsureGameXr() {
    static bool attempted=false;
    if(!attempted && gameSwapchain) {
        attempted=true; gameXr=new GameXr;
        ComPtr<ID3D11Device> device; gameSwapchain->GetDevice(IID_PPV_ARGS(&device));
        const auto scale=GraphicsScale(L"DIRT2VR_HEADSET_SCALE",.5f,.25f,1.f);
        const auto fov=GraphicsScale(L"DIRT2VR_FOV_SCALE",1.f,.7f,1.f);
        Log("VR graphics headset_scale=%.2f fov_scale=%.2f",scale,fov);
        if(!gameXr->Initialize(device.Get(),scale,fov)) Log("OpenXR game initialization failed; desktop fallback");
    }
    return gameXr && gameXr->Active();
}
void PollHeadsetKeys() {
    static uint64_t polled=~uint64_t{};
    if(polled==frame.load()) return;
    polled=frame.load();
    const auto keys=ConsumeHotkeys();
    if(keys&ToggleScreen) {
        ScreenMode()=!ScreenMode(); recenterRequested=true;
        Log("OpenXR requested mode=%s frame=%llu",ScreenMode()?"screen":"cockpit",frame.load());
    }
    if(keys&Recenter) recenterRequested=true;
}
void PrepareHeadsetViews(const std::array<XrView,2>& views) {
    if(recenterRequested) {
        headsetReference=CenterPose(views); recenterRequested=false;
        Log("OpenXR recentered frame=%llu",frame.load());
    }
}
void HeadsetScreen() {
    const auto f=frame.load();
    ComPtr<ID3D11Texture2D> back;
    if(FAILED(gameSwapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) return;
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    XrFrames::Screen screen; screen.size.height=screen.size.width*desc.Height/desc.Width;
    static EyePair image;
    xrTickFrame=f;
    const bool submitted=gameXr->Tick([&](unsigned,const XrView&,ID3D11RenderTargetView* target,uint32_t w,uint32_t h) {
        if(FAILED(image.Capture(back.Get(),0,f)) || !gameXr->CopyEye(0,image.Texture(0),target,w,h))
            throw std::runtime_error("virtual screen copy");
    },[&](const std::array<XrView,2>& views) {
        PrepareHeadsetViews(views); screen.pose=ScreenPose(headsetReference,2.f);
    },&screen);
    static auto csv=TraceFile(Output()/"screen-frames.csv");
    static bool header=false;
    if(!header) { csv << "frame,submitted,visible\n"; header=true; }
    csv << f << ',' << submitted << ',' << gameXr->Visible() << '\n';
    if(f%120==0) { csv.flush(); Log("OpenXR screen frame=%llu submitted=%d visible=%d",f,submitted,gameXr->Visible()); }
    static uint64_t visible=0;
    if(DetailedTrace() && submitted && gameXr->Visible() && ++visible==120) ScreenshotTexture(image.Texture(0),910001);
}
bool InnerReplayEnabled() {
    static const bool enabled=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_INNER_REPLAY",value,16)>0 && wcscmp(value,L"1")==0; }();
    return enabled || ContinuousReplayEnabled();
}

// The cockpit colour pass reads self+0x650 directly. Copies supplied only as
// arguments move its depth pass but leave its colour camera behind. Temporarily
// move the two verified inline positions; do not restore opaque render state.
class ScopedCameraTranslation {
    std::array<float*,2> cameras_;
    std::array<std::array<float,3>,2> positions_;
public:
    ScopedCameraTranslation(void* a,void* b,float offset)
        : cameras_{static_cast<float*>(a),static_cast<float*>(b)} {
        for(unsigned i=0;i<cameras_.size();++i) {
            auto camera=cameras_[i];
            memcpy(positions_[i].data(),camera+16,12);
            // Captured main-view matrices identify -basis@0x20 as camera X.
            // basis@0x30 is the longitudinal axis, not eye separation.
            for(unsigned axis=0;axis<3;++axis) camera[16+axis]-=offset*camera[8+axis];
        }
    }
    ~ScopedCameraTranslation() {
        for(unsigned i=0;i<cameras_.size();++i) memcpy(cameras_[i]+16,positions_[i].data(),12);
    }
    ScopedCameraTranslation(const ScopedCameraTranslation&)=delete;
    ScopedCameraTranslation& operator=(const ScopedCameraTranslation&)=delete;
};

class ScopedEyePose {
    std::array<float*,2> cameras_;
    std::array<std::array<float,28>,2> original_;
public:
    ScopedEyePose(void* a,void* b,const XrPosef& pose,float scale):cameras_{static_cast<float*>(a),static_cast<float*>(b)} {
        for(unsigned i=0;i<2;++i) memcpy(original_[i].data(),cameras_[i],112);
        // Validate/compute both records before modifying either engine record.
        auto changed=original_;
        for(auto& camera:changed) ApplyEyePose(camera.data(),pose,scale);
        for(unsigned i=0;i<2;++i) for(unsigned offset:{4u,8u,12u,16u}) memcpy(cameras_[i]+offset,changed[i].data()+offset,12);
    }
    ~ScopedEyePose() {
        for(unsigned i=0;i<2;++i) for(unsigned offset:{4u,8u,12u,16u}) memcpy(cameras_[i]+offset,original_[i].data()+offset,12);
        eyeRenderer=nullptr; eyeFov=nullptr; lightingEye=0;
    }
};

void RenderEyeReflection(void* mainCamera) {
    if(!WaterReflectionsEnabled() || !waterPrepareTail || !waterCameraA || !waterRenderer || waterRendererFrame!=frame.load() || waterListsFrame!=frame.load()) return;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    auto renderer=static_cast<unsigned char*>(waterRenderer);
    const unsigned char prepare[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xb4,0x01,0,0};
    const unsigned char render[]={0x53,0x56,0x8b,0xf1,0x33,0xdb};
    if(*reinterpret_cast<void**>(renderer)!=base+0xf262f8 ||
       *reinterpret_cast<void**>(renderer+0x940)!=mainCamera ||
       *reinterpret_cast<void**>(renderer+0x95c) ||
       memcmp(base+0x2e22d0,prepare,sizeof(prepare)) || memcmp(base+0x2d1d40,render,sizeof(render))) return;
    const auto pass=scenePass;
    const bool main=continuousMain;
    const auto before=draws.load();
    const auto reflectionSerial=*reinterpret_cast<unsigned*>(renderer+0xc8);
    waterEyeRenderer=renderer;
    using Method=void (__thiscall*)(void*);
    waterCameraThread=GetCurrentThreadId();
    waterCameraOnly=true;
    reinterpret_cast<Method>(base+0x2e22d0)(renderer);
    waterCameraOnly=false;
    realInner(renderer,waterLists.data(),waterCameraA,waterCameraB,waterContext,waterScene,waterFlags);
    *reinterpret_cast<unsigned*>(renderer+0xc8)=reflectionSerial;
    waterEyeRenderer=nullptr;
    continuousMain=main;
    scenePass=pass;
    if(frame.load()%120==0) Log("water per-eye reflection frame=%llu draws=%llu pending=%p",frame.load(),draws.load()-before,*reinterpret_cast<void**>(renderer+0x95c));
}

bool HeadsetScene(void* self,void* lists,void* cameraA,void* cameraB,void* context,void* scene,void* flags) {
    const auto f=frame.load();
    PollHeadsetKeys();
    if(ScreenMode() || !continuousMain || f<300 || !gameSwapchain || !cameraHooksReady || xrTickFrame==f) return false;
    auto renderer=static_cast<unsigned char*>(self);
    if(cameraA!=renderer+0x5e0 || cameraB!=renderer+0x650) return false;
    const bool cockpit=CockpitCameraCandidate(static_cast<const float*>(cameraA),static_cast<const float*>(cameraB));
    static int previousCandidate=-1;
    if(previousCandidate!=static_cast<int>(cockpit)) {
        Log("OpenXR camera candidate=%s frame=%llu near=%f/%f",cockpit?"cockpit":"screen",f,
            static_cast<const float*>(cameraA)[21],static_cast<const float*>(cameraB)[21]);
        previousCandidate=cockpit;
    }
    // A paused cockpit retains its near plane. Render the original complete
    // frame (including modal dialogs) on the screen instead of replaying it.
    // Preserve the requested mode so resuming returns to cockpit VR.
    if(!cockpit || !lightingHooksReady || !pauseHookReady || pauseWorld.load()) return false;
    if(!preparedLights.Snapshot(f,context,eyeLights)) {
        Log("lighting replay capacity exceeded; using virtual screen frame=%llu",f);
        return false;
    }
    if(!EnsureGameXr()) return false;
    const bool requestedCapture=TakeCaptureRequest(f);
    xrTickFrame=f;
    static EyePair eyes;
    static const float scale=[] {
        wchar_t text[32]{}; GetEnvironmentVariableW(L"DIRT2VR_WORLD_SCALE",text,32);
        const float value=wcstof(text,nullptr); return std::isfinite(value) && value>=.25f && value<=4 ? value : 1.f;
    }();
    ComPtr<ID3D11Texture2D> back;
    if(FAILED(gameSwapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) return false;
    static GpuTimer gpu;
    static bool gpuAttempted=false;
    if(LoggingEnabled() && !gpuAttempted) {
        gpuAttempted=true; ComPtr<ID3D11Device> device; back->GetDevice(&device);
        Log("OpenXR GPU timer initialized=%d",gpu.Initialize(device.Get()));
    }
    static auto gpuCsv=TraceFile(Output()/"gpu-frames.csv");
    static bool gpuHeader=false;
    if(!gpuHeader) { gpuCsv << "frame,eye_pair_gpu_ms,valid\n"; gpuHeader=true; }
    for(const auto& timing:gpu.Poll()) gpuCsv << timing.frame << ',' << timing.milliseconds << ',' << timing.valid << '\n';
    if(f%120==0) gpuCsv.flush();
    bool rendered=false;
    bool restored=true;
    std::array<uint64_t,2> counts{};
    std::array<unsigned,2> projectionUploads{};
    LARGE_INTEGER start{},end{},frequency{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    std::array<float,28> originalA,originalB;
    memcpy(originalA.data(),cameraA,112); memcpy(originalB.data(),cameraB,112);
    // These three guarded parameter builders only read view (+0x160) and
    // inverse view (+0x90) from the context. Preserve their original inputs so
    // shared light materials can be restored after both eyes, including failure.
    alignas(16) std::array<unsigned char,0x1a0> originalLightContext;
    memcpy(originalLightContext.data(),context,originalLightContext.size());
    groundCoverPair.Begin();
    XrFrames::Overlay hud;
    const bool captureHud=hudCapture.Begin(back.Get(),f,HiddenHudElements());
    static const float hudDistance=GraphicsScale(L"DIRT2VR_HUD_DISTANCE",1.f,1.f,20.f);
    // Preserve the angular size of the existing 4 m wide, 4 m distant panel.
    hud.size={hudDistance,hudDistance/hudCapture.Aspect()};
    hud.draw=[&](unsigned,const XrView&,ID3D11RenderTargetView* target,uint32_t w,uint32_t h) {
        hudCapture.End(true);
        if(auto image=hudCapture.Current(f)) {
            if(!gameXr->CopyEye(2,image,target,w,h,true)) throw std::runtime_error("HUD presentation");
        } else {
            ComPtr<ID3D11Device> device; back->GetDevice(&device);
            ComPtr<ID3D11DeviceContext> immediate; device->GetImmediateContext(&immediate);
            const float clear[4]{}; immediate->ClearRenderTargetView(target,clear);
        }
    };
    const bool submitted=gameXr->Tick([&](unsigned eye,const XrView& view,ID3D11RenderTargetView* target,uint32_t w,uint32_t h) {
        const auto before=draws.load(); scenePass=(requestedCapture || (DetailedTrace() && f==3000)) ? eye+1 : 0;
        eyeProjectionUploads=0;
        if(eye==0) gpu.Begin(f);
        {
            ScopedEyePose pose(cameraA,cameraB,RelativePose(headsetReference,view.pose),scale);
            eyeRenderer=self; eyeFov=&view.fov; lightingEye=eye+1; lightsRefreshed=false;
            RenderEyeReflection(cameraA);
            hudEye=captureHud ? eye+1 : 0;
            realInner(self,lists,cameraA,cameraB,context,scene,flags);
            hudEye=0;
            rendered=true;
        }
        counts[eye]=draws.load()-before; scenePass=0;
        projectionUploads[eye]=eyeProjectionUploads;
        restored &= memcmp(originalA.data(),cameraA,112)==0 && memcmp(originalB.data(),cameraB,112)==0;
        if(!restored) throw std::runtime_error("eye camera restoration");
        if(!projectionUploads[eye]) throw std::runtime_error("eye projection was not uploaded");
        if(FAILED(eyes.Capture(back.Get(),eye,f))) throw std::runtime_error("eye capture");
        if(!gameXr->CopyEye(eye,eyes.Texture(eye),target,w,h)) throw std::runtime_error("eye presentation");
        if(eye==1) gpu.End();
    },[&](const std::array<XrView,2>& views) {
        for(const auto& view:views) {
            std::array<float,16> projection{}; projection[11]=-1;
            ApplyFov(projection.data(),view.fov);
        }
        PrepareHeadsetViews(views);
        static const bool follow=GraphicsScale(L"DIRT2VR_HUD_FOLLOW",0.f,0.f,1.f)==1.f;
        hud.pose=ScreenPose(follow ? CenterPose(views) : headsetReference,hudDistance);
    },nullptr,captureHud ? &hud : nullptr);
    hudEye=0;
    if(!submitted) hudCapture.End(false);
    if(submitted) hudSceneFrame=f;
    groundCoverPair.End();
    if(!eyeLights.empty()) {
        lightsRefreshed=false;
        RefreshLights(originalLightContext.data());
        eyeLights.clear();
    }
    gpu.End(false); // Close a partial pair after an acquire/draw failure.
    scenePass=0;
    if(requestedCapture && submitted && eyes.Ready(f)) {
        const unsigned id=920000+requestedCaptureCount*2;
        ScreenshotTexture(eyes.Texture(0),id); ScreenshotTexture(eyes.Texture(1),id+1);
        Log("requested stereo capture complete frame=%llu images=%u/%u",f,id,id+1);
    }
    requestedCaptureFrame=~uint64_t{};
    QueryPerformanceCounter(&end);
    static auto csv=TraceFile(Output()/"headset-frames.csv");
    static bool header=false;
    if(!header) {
        csv << "frame,submitted,visible,pair_ready,cameras_restored,left_draws,right_draws,left_projection_uploads,right_projection_uploads,tick_ms\n";
        header=true;
    }
    csv << f << ',' << submitted << ',' << gameXr->Visible() << ',' << eyes.Ready(f) << ',' << restored << ','
        << counts[0] << ',' << counts[1] << ',' << projectionUploads[0] << ',' << projectionUploads[1] << ','
        << 1000.0*(end.QuadPart-start.QuadPart)/frequency.QuadPart << '\n';
    if(f%120==0 || gameXr->Exiting()) csv.flush();
    if(f%120==0) Log("OpenXR frame=%llu submitted=%llu visible=%d draws=%llu/%llu pair=%d",f,gameXr->Submitted(),gameXr->Visible(),counts[0],counts[1],eyes.Ready(f));
    // Loading can consume thousands of desktop frames. Sample relative to the
    // first successful scene pair. Later samples cover driving after the intro.
    static uint64_t pairs=0;
    if(DetailedTrace() && submitted && (++pairs==120 || pairs==600 || pairs==1800 || pairs==3600)) {
        const auto id=pairs==120 ? 900001u : pairs==600 ? 900003u : pairs==1800 ? 900005u : 900007u;
        ScreenshotTexture(eyes.Texture(0),id);
        ScreenshotTexture(eyes.Texture(1),id+1);
        Log("OpenXR captured pair=%llu frame=%llu images=%u/%u",pairs,f,id,id+1);
    }
    if(gameXr->Exiting()) { Log("OpenXR stopping on render thread"); gameXr->Shutdown(); }
    return rendered;
}

bool ContinuousScene(void* self,void* lists,void* cameraA,void* cameraB,void* context,void* scene,void* flags) {
    static bool disabled=false;
    static EyePair eyes;
    static uint64_t pairs=0;
    const auto f=frame.load();
    if(!continuousMain || disabled || f<300 || !gameSwapchain) return false;
    auto renderer=static_cast<unsigned char*>(self);
    if(cameraA!=renderer+0x5e0 || cameraB!=renderer+0x650) {
        Log("CONTINUOUS disabled: unexpected inline camera addresses"); disabled=true; return false;
    }
    static const float separation=[] {
        wchar_t text[32]{}; GetEnvironmentVariableW(L"DIRT2VR_CAMERA_OFFSET",text,32);
        const auto value=wcstof(text,nullptr);
        return std::isfinite(value) && std::abs(value)<=0.25f ? value : 0.0f;
    }();
    static const float shift=[] {
        wchar_t text[32]{}; GetEnvironmentVariableW(L"DIRT2VR_PROJECTION_SHIFT",text,32);
        const auto value=wcstof(text,nullptr);
        return std::isfinite(value) && std::abs(value)<=0.25f ? value : 0.f;
    }();
    std::array<float,28> originalA,originalB;
    memcpy(originalA.data(),cameraA,112); memcpy(originalB.data(),cameraB,112);
    ComPtr<ID3D11Texture2D> back;
    if(FAILED(gameSwapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) {
        Log("CONTINUOUS disabled: no backbuffer"); disabled=true; return false;
    }
    const bool sampled=f==3000;
    std::array<uint64_t,2> eyeDraws{};
    LARGE_INTEGER start{},end{},frequency{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&start);
    bool restored=true;
    HRESULT capture=S_OK;
    if(WaterReflectionsEnabled()) groundCoverPair.Begin();
    if(WaterReflectionsEnabled()) { waterProbeDraws={}; waterProbeRecording=true; }
    for(unsigned eye=0;eye<2;++eye) {
        scenePass=(sampled || WaterReflectionsEnabled()) ? eye+1 : 0;
        const auto before=draws.load();
        {
            ScopedCameraTranslation translation(cameraA,cameraB,(eye==0?-0.5f:0.5f)*separation);
            eyeRenderer=self; projectionShift=(eye==0?-1.f:1.f)*shift;
            if(WaterReflectionsEnabled()) lightingEye=eye+1;
            RenderEyeReflection(cameraA);
            realInner(self,lists,cameraA,cameraB,context,scene,flags);
            eyeRenderer=nullptr; projectionShift=0; lightingEye=0;
        }
        eyeDraws[eye]=draws.load()-before;
        restored &= memcmp(originalA.data(),cameraA,112)==0 && memcmp(originalB.data(),cameraB,112)==0;
        capture=eyes.Capture(back.Get(),eye,f);
        if(FAILED(capture) || !restored) break;
    }
    if(WaterReflectionsEnabled()) groundCoverPair.End();
    waterProbeRecording=false;
    scenePass=sampled ? 1 : 0;
    QueryPerformanceCounter(&end);
    const bool ready=SUCCEEDED(capture) && eyes.Ready(f) && restored;
    const bool matched=eyeDraws[0]==eyeDraws[1];
    if(ready) ++pairs;
    static auto csv=TraceFile(Output()/"stereo-frames.csv");
    static bool header=false;
    if(!header) { csv << "frame,left_draws,right_draws,cameras_restored,pair_ready,cpu_ms\n"; header=true; }
    csv << f << ',' << eyeDraws[0] << ',' << eyeDraws[1] << ',' << restored << ',' << ready << ','
        << 1000.0*(end.QuadPart-start.QuadPart)/frequency.QuadPart << '\n';
    if(f%120==0 || !ready || !matched) {
        csv.flush(); Log("CONTINUOUS pairs=%llu frame=%llu draws=%llu/%llu restored=%d ready=%d capture=0x%08x",
            pairs,f,eyeDraws[0],eyeDraws[1],restored,ready,static_cast<unsigned>(capture));
    }
    if(ready && (f==3000 || f==4500 || f==6000)) {
        ScreenshotTexture(eyes.Texture(0),900001+2*(f-3000));
        ScreenshotTexture(eyes.Texture(1),900002+2*(f-3000));
    }
    if(!ready || !matched) {
        if(WaterReflectionsEnabled()) for(unsigned eye=0;eye<2;++eye) {
            auto diagnostic=TraceFile(Output()/("water-probe-failed-eye-"+std::to_string(eye+1)+".txt"));
            for(const auto& draw:waterProbeDraws[eye]) diagnostic << draw << '\n';
        }
        Log("CONTINUOUS disabled after failed pair; inspect stereo-frames.csv"); disabled=true;
    }
    if(WaterReflectionsEnabled() && (disabled || f>=6000)) { DXGI_SWAP_CHAIN_DESC desc{}; if(SUCCEEDED(gameSwapchain->GetDesc(&desc))) PostMessageW(desc.OutputWindow,WM_CLOSE,0,0); }
    return true;
}

void __fastcall Inner(void* self,void*,void* lists,void* cameraA,void* cameraB,void* context,void* scene,void* flags) {
    if(WaterReflectionsEnabled() && self==waterRenderer && !waterEyeRenderer) {
        memcpy(waterLists.data(),lists,waterLists.size());
        waterListsFrame=frame.load();
        waterCameraA=cameraA; waterCameraB=cameraB; waterContext=context; waterScene=scene; waterFlags=flags;
    }
    if(HeadsetEnabled()) {
        if(!HeadsetScene(self,lists,cameraA,cameraB,context,scene,flags)) realInner(self,lists,cameraA,cameraB,context,scene,flags);
        return;
    }
    if(HudProbe()) {
        const auto f=frame.load();
        if(continuousMain && (f==300 || f==1200 || f==3000) && gameSwapchain) {
            ComPtr<ID3D11Texture2D> back;
            if(SUCCEEDED(gameSwapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) hudProbeDrawing=hudCapture.Begin(back.Get(),f,HiddenHudElements());
        }
        realInner(self,lists,cameraA,cameraB,context,scene,flags);
        hudProbeDrawing=false;
        return;
    }
    if(ContinuousScene(self,lists,cameraA,cameraB,context,scene,flags)) return;
    static bool tested=false;
    const bool test=sampleInner && !tested && gameSwapchain;
    if(test) tested=true;
    realInner(self,lists,cameraA,cameraB,context,scene,flags);
    if(!test) return;
    Screenshot(gameSwapchain,900001);
    const auto before=draws.load();
    Log("EXPERIMENT prepared inner scene replay begins; NOT accepted as safe stereo");
    scenePass=2;
    wchar_t offsetText[32]{}; GetEnvironmentVariableW(L"DIRT2VR_CAMERA_OFFSET",offsetText,32);
    float offset=wcstof(offsetText,nullptr);
    if(std::isfinite(offset) && offset!=0 && std::abs(offset)<=0.25f) {
        auto renderer=static_cast<unsigned char*>(self);
        if(cameraA!=renderer+0x5e0 || cameraB!=renderer+0x650) {
            Log("EXPERIMENT rejected translation: unexpected inline camera addresses");
            realInner(self,lists,cameraA,cameraB,context,scene,flags);
        } else {
            std::array<float,28> originalA,originalB;
            memcpy(originalA.data(),cameraA,112); memcpy(originalB.data(),cameraB,112);
            {
                ScopedCameraTranslation translation(cameraA,cameraB,offset);
                Log("EXPERIMENT translated inline cameras local_right=%f game_units",offset);
                realInner(self,lists,cameraA,cameraB,context,scene,flags);
            }
            Log("EXPERIMENT inline camera records restored=%d",
                memcmp(originalA.data(),cameraA,112)==0 && memcmp(originalB.data(),cameraB,112)==0);
        }
    } else realInner(self,lists,cameraA,cameraB,context,scene,flags);
    scenePass=1;
    Log("EXPERIMENT prepared inner scene replay returned draws=%llu",draws.load()-before);
    Screenshot(gameSwapchain,900002);
}

void __fastcall Scene(void* self,void*,void* a,void* b,void* c,void* d,void* e) {
    const auto f=frame.load();
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const bool mainView=caller==0x2889c0;
    if(WaterReflectionsEnabled() && caller==0x2d1daf && !waterEyeRenderer) { waterRenderer=self; waterRendererFrame=f; }
    const bool sample=DetailedTrace() && (f==300 || f==1200 || f==3000);
    if(sample) {
        Log("scene_enter frame=%llu caller=0x%zx main=%d self=%p args=%p,%p,%p,%p,%p",f,caller,mainView,self,a,b,c,d,e);
        // The call sites pass two inline 0x70-byte camera records. Read only.
        unsigned index=0;
        for(auto address:{a,b}) {
            MEMORY_BASIC_INFORMATION region{};
            if(VirtualQuery(address,&region,sizeof(region)) && region.State==MEM_COMMIT &&
               !(region.Protect&(PAGE_NOACCESS|PAGE_GUARD)) &&
               reinterpret_cast<uintptr_t>(address)+112 <= reinterpret_cast<uintptr_t>(region.BaseAddress)+region.RegionSize) {
                auto out=TraceFile(Output()/("scene-camera-"+std::to_string(f)+"-"+std::to_string(index)+".bin"),std::ios::binary);
                out.write(static_cast<const char*>(address),112);
            }
            ++index;
        }
    }
    const auto start=draws.load();
    if(DetailedTrace() && mainView && f==3000) scenePass=1;
    sampleInner=mainView && f==3000 && InnerReplayEnabled() && !ContinuousReplayEnabled();
    continuousMain=mainView && (ContinuousReplayEnabled() || HudProbe());
    realScene(self,a,b,c,d,e);
    sampleInner=false;
    continuousMain=false;
    scenePass=0;
    if(sample) Log("scene_exit frame=%llu draws=%llu",f,draws.load()-start);
    static const bool replay=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_REPLAY_PROBE",value,16)>0 && wcscmp(value,L"1")==0; }();
    static bool replayed=false;
    if(replay && !InnerReplayEnabled() && !replayed && f==3000 && mainView && gameSwapchain) {
        replayed=true;
        Screenshot(gameSwapchain,900001);
        const auto before=draws.load();
        Log("EXPERIMENT same-state scene replay begins; NOT accepted as safe stereo");
        scenePass=2;
        wchar_t offsetText[32]{};
        GetEnvironmentVariableW(L"DIRT2VR_CAMERA_OFFSET",offsetText,32);
        float offset=wcstof(offsetText,nullptr);
        if(std::isfinite(offset) && offset!=0 && std::abs(offset)<=0.25f) {
            // Diagnostic translation only. Game units are not yet calibrated to
            // metres. Copy the two records; never alter engine-owned cameras.
            alignas(16) std::array<float,28> cameraA, cameraB;
            memcpy(cameraA.data(),a,112); memcpy(cameraB.data(),b,112);
            for(auto camera:{cameraA.data(),cameraB.data()})
                for(unsigned axis=0;axis<3;++axis) camera[16+axis]-=offset*camera[8+axis];
            Log("EXPERIMENT translated replay camera local_right=%f game_units",offset);
            realScene(self,cameraA.data(),cameraB.data(),c,d,e);
        } else realScene(self,a,b,c,d,e);
        scenePass=0;
        Log("EXPERIMENT same-state scene replay returned draws=%llu",draws.load()-before);
        Screenshot(gameSwapchain,900002);
    }
}

bool Sample() { auto f=frame.load(); return DetailedTrace() && (f==300 || f==1200 || f==3000); }
void Stack(const char* event) {
    if(!LoggingEnabled()) return;
    void* addresses[20]{};
    const auto n=CaptureStackBackTrace(1,20,addresses,nullptr);
    auto out=TraceFile(Output()/"stacks.txt",std::ios::app);
    auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    out << event << " frame=" << frame << " tid=" << GetCurrentThreadId();
    for (USHORT i=0;i<n;++i) {
        auto p=reinterpret_cast<uintptr_t>(addresses[i]);
        if(p>=base && p<base+0x1839000) out << " game+0x" << std::hex << p-base << std::dec;
    }
    // FPO in the 2010 executable defeats normal stack unwinding. These are
    // explicitly candidates, filtered to direct CALL return addresses.
    auto cursor=reinterpret_cast<uintptr_t*>(_AddressOfReturnAddress());
    MEMORY_BASIC_INFORMATION region{};
    if(VirtualQuery(cursor,&region,sizeof(region))) {
        auto end=reinterpret_cast<uintptr_t>(region.BaseAddress)+region.RegionSize;
        for(unsigned i=0;i<1024 && reinterpret_cast<uintptr_t>(cursor+i)<end;++i) {
            uintptr_t p=cursor[i];
            if(p>=base+0x1005 && p<base+0xe8b000 && *reinterpret_cast<unsigned char*>(p-5)==0xe8)
                out << " candidate+0x" << std::hex << p-base << std::dec;
        }
    }
    out << '\n';
}

HRESULT STDMETHODCALLTYPE Map(ID3D11DeviceContext* c,ID3D11Resource* resource,UINT sub,D3D11_MAP mode,UINT flags,D3D11_MAPPED_SUBRESOURCE* mapped) {
    HRESULT hr=realMap(c,resource,sub,mode,flags,mapped);
    if(SUCCEEDED(hr) && Sample() && mode!=D3D11_MAP_READ && mode!=D3D11_MAP_READ_WRITE) {
        ComPtr<ID3D11Buffer> buffer;
        if(SUCCEEDED(resource->QueryInterface(IID_PPV_ARGS(&buffer)))) {
            D3D11_BUFFER_DESC desc{}; buffer->GetDesc(&desc);
            if(desc.ByteWidth==400 && (desc.BindFlags&D3D11_BIND_CONSTANT_BUFFER))
                cameraMaps[resource]=mapped->pData;
        }
    }
    return hr;
}
void STDMETHODCALLTYPE Unmap(ID3D11DeviceContext* c,ID3D11Resource* resource,UINT sub) {
    auto it=cameraMaps.find(resource);
    if(it!=cameraMaps.end()) {
        static std::atomic<unsigned> index{};
        unsigned id=index++;
        auto out=TraceFile(Output()/("camera-"+std::to_string(frame.load())+"-"+std::to_string(id)+".bin"),std::ios::binary);
        out.write(static_cast<const char*>(it->second),400);
        Stack("CameraUnmap");
        cameraMaps.erase(it);
    }
    realUnmap(c,resource,sub);
}

uint64_t Shader(const void* bytes, SIZE_T length, const char* stage) {
    uint64_t hash=14695981039346656037ull;
    auto data=static_cast<const unsigned char*>(bytes);
    for(SIZE_T i=0;i<length;++i) hash=(hash^data[i])*1099511628211ull;
    if(!DetailedTrace() && !RequestedCapturesEnabled()) return hash; // Hashes are still needed for the water filter.
    std::lock_guard lock(shaderMutex);
    if(!shaders.insert(hash).second) return hash;
    char name[80]{};
    sprintf_s(name,"%s-%016llx",stage,hash);
    auto directory=Output()/"shaders";
    std::error_code ec;
    std::filesystem::create_directories(directory,ec);
    auto binary=TraceFile(directory/(std::string(name)+".dxbc"),std::ios::binary);
    binary.write(static_cast<const char*>(bytes),length);
    ComPtr<ID3D11ShaderReflection> reflection;
    if(FAILED(D3DReflect(bytes,length,IID_ID3D11ShaderReflection,&reflection))) return hash;
    D3D11_SHADER_DESC desc{};
    reflection->GetDesc(&desc);
    auto text=TraceFile(directory/(std::string(name)+".txt"));
    text << "instruction_count=" << desc.InstructionCount << '\n';
    for(UINT b=0;b<desc.BoundResources;++b) {
        D3D11_SHADER_INPUT_BIND_DESC binding{};
        reflection->GetResourceBindingDesc(b,&binding);
        text << "binding " << binding.Name << " type=" << binding.Type << " slot=" << binding.BindPoint << '\n';
    }
    for(UINT b=0;b<desc.ConstantBuffers;++b) {
        auto cb=reflection->GetConstantBufferByIndex(b);
        D3D11_SHADER_BUFFER_DESC bd{}; cb->GetDesc(&bd);
        text << "buffer " << bd.Name << " bytes=" << bd.Size << '\n';
        for(UINT v=0;v<bd.Variables;++v) {
            D3D11_SHADER_VARIABLE_DESC vd{}; cb->GetVariableByIndex(v)->GetDesc(&vd);
            text << "  " << vd.Name << " offset=" << vd.StartOffset << " bytes=" << vd.Size << " flags=" << vd.uFlags << '\n';
        }
    }
    return hash;
}

bool RecordDraw(ID3D11DeviceContext* context,const char* kind,UINT count,UINT instances=1,UINT start=0,INT base=0,UINT firstInstance=0) {
    static const bool skipWater=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_SKIP_WATER",value,16)>0 && wcscmp(value,L"1")==0; }();
    if(!scenePass && !skipWater) return true;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11VertexShader> vs;
    context->PSGetShader(&ps,nullptr,nullptr); context->VSGetShader(&vs,nullptr,nullptr);
    uint64_t ph{},vh{};
    { std::lock_guard lock(shaderMutex); ph=shaderNames[ps.Get()]; vh=shaderNames[vs.Get()]; }
    if(scenePass==1) {
        static bool depthTraced=false, colourTraced=false;
        if(ph==0x4822905e184bebd9ull && !depthTraced) { depthTraced=true; Stack("CockpitDepth"); }
        if(ph==0x644e47e779da6256ull && !colourTraced) { colourTraced=true; Stack("CockpitColour"); }
    }
    // Only these two water shaders were identified by reflection in the supported
    // build. Never suppress general depth rendering or unrecognized shaders.
    if(skipWater && (ph==0x08192877abdf068aull || ph==0x122478b22e69d9faull)) {
        if(scenePass) {
            auto skipped=TraceFile(Output()/"skipped-water.csv",std::ios::app);
            skipped << scenePass << ',' << kind << ',' << count << ',' << instances << ',' << std::hex << vh << ',' << ph << '\n';
        }
        return false;
    }
    if(!scenePass) return true;
    if(waterProbeRecording && scenePass<=2 && waterProbeDraws[scenePass-1].size()<10000) {
        char line[160]{}; snprintf(line,sizeof(line),"%s count=%u instances=%u vs=%llx ps=%llx reflection=%d",kind,count,instances,vh,ph,waterEyeRenderer!=nullptr);
        waterProbeDraws[scenePass-1].emplace_back(line);
    }
    if(WaterReflectionsEnabled() && Sample() && frame.load()==3000) TraceWaterInputs(context,ph,frame.load(),scenePass);
    if(requestedCaptureFrame==frame.load()) {
        TraceWaterInputs(context,ph,frame.load(),scenePass);
        ComPtr<ID3D11Buffer> vertex,index; UINT stride{},offset{},indexOffset{}; DXGI_FORMAT format{};
        D3D11_PRIMITIVE_TOPOLOGY topology{};
        context->IAGetVertexBuffers(0,1,vertex.GetAddressOf(),&stride,&offset);
        context->IAGetIndexBuffer(&index,&format,&indexOffset); context->IAGetPrimitiveTopology(&topology);
        auto capture=TraceFile(Output()/("capture-"+std::to_string(frame.load())+"-eye-"+std::to_string(scenePass)+".csv"),std::ios::app);
        capture << kind << ',' << count << ',' << instances << ',' << std::hex << vh << ',' << ph << std::dec
            << ',' << start << ',' << base << ',' << firstInstance << ',' << topology << ',' << vertex.Get()
            << ',' << stride << ',' << offset << ',' << index.Get() << ',' << format << ',' << indexOffset << '\n';
    }
    if(waterProbeRecording && !Sample()) return true;
    auto out=TraceFile(Output()/("draws-pass-"+std::to_string(scenePass)+".csv"),std::ios::app);
    out << kind << ',' << count << ',' << instances << ',' << std::hex << vh << ',' << ph << '\n';
    return true;
}

void Screenshot(IDXGISwapChain* swapchain, unsigned long long number) {
    ComPtr<ID3D11Texture2D> back;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) return;
    ScreenshotTexture(back.Get(),number);
}
void ScreenshotTexture(ID3D11Texture2D* back,unsigned long long number,bool alpha) {
    if(!LoggingEnabled()) return;
    ComPtr<ID3D11Texture2D> source,staging;
    ComPtr<ID3D11Device> device; back->GetDevice(&device);
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    if(desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB &&
       desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM) return;
    source=back;
    if(desc.SampleDesc.Count>1) {
        desc.SampleDesc={1,0}; desc.BindFlags=0; desc.MiscFlags=0;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&source))) return;
        context->ResolveSubresource(source.Get(),0,back,0,desc.Format);
    }
    desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    desc.BindFlags=0; desc.MiscFlags=0;
    if(FAILED(device->CreateTexture2D(&desc,nullptr,&staging))) return;
    context->CopyResource(staging.Get(),source.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped))) return;
    // PPM is intentionally simple and keeps screenshot support out of the render hook's dependencies.
    auto out=TraceFile(Output()/("frame-"+std::to_string(number)+".ppm"),std::ios::binary);
    out << "P6\n" << desc.Width << " " << desc.Height << "\n255\n";
    auto alphaOut=alpha ? TraceFile(Output()/("frame-"+std::to_string(number)+"-alpha.pgm"),std::ios::binary) : std::ofstream{};
    if(alpha) alphaOut << "P5\n" << desc.Width << " " << desc.Height << "\n255\n";
    std::vector<char> row(desc.Width*3);
    for(UINT y=0;y<desc.Height;++y) {
        auto p=static_cast<const unsigned char*>(mapped.pData)+y*mapped.RowPitch;
        for(UINT x=0;x<desc.Width;++x) {
            bool bgra=desc.Format==DXGI_FORMAT_B8G8R8A8_UNORM;
            row[x*3]=p[x*4+(bgra?2:0)]; row[x*3+1]=p[x*4+1]; row[x*3+2]=p[x*4+(bgra?0:2)];
            if(alpha) alphaOut.put(static_cast<char>(p[x*4+3]));
        }
        out.write(row.data(),row.size());
    }
    context->Unmap(staging.Get(),0);
    Log("screenshot frame=%llu %ux%u; capture stalls excluded from performance acceptance",number,desc.Width,desc.Height);
}

HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* swapchain,UINT interval,UINT flags) {
    if(flags & DXGI_PRESENT_TEST) return realPresent(swapchain,interval,flags);
    if(HudProbe()) hudCapture.End(true);
    if(HudProbe() && Sample()) {
        Log("HUD probe frame=%llu captured draws=%u",frame.load(),hudCapture.Draws());
        if(auto hud=hudCapture.Current(frame.load())) ScreenshotTexture(hud,920000+frame.load(),true);
    }
    if(hudSceneFrame==frame.load() && frame.load()%120==0) Log("HUD captured draws=%u frame=%llu",hudCapture.Draws(),frame.load());
    if(HeadsetEnabled() && EnsureGameXr()) {
        PollHeadsetKeys();
        if(xrTickFrame!=frame.load()) HeadsetScreen();
        if(gameXr->Exiting()) { Log("OpenXR stopping on render thread"); gameXr->Shutdown(); }
        interval=0;
    }
    LARGE_INTEGER now{},frequency{}; QueryPerformanceCounter(&now); QueryPerformanceFrequency(&frequency);
    static long long previous{};
    const auto f=frame.fetch_add(1);
    const double ms=previous ? 1000.0*(now.QuadPart-previous)/frequency.QuadPart : 0;
    previous=now.QuadPart;
    auto d=draws.exchange(0), c=dispatches.exchange(0);
    if(LoggingEnabled()) {
        PROCESS_MEMORY_COUNTERS_EX memory{}; memory.cb=sizeof(memory);
        GetProcessMemoryInfo(GetCurrentProcess(),reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory));
        static auto csv=TraceFile(Output()/"frames.csv");
        if(f==0) csv << "frame,interval_ms,draws,dispatches,private_bytes,working_set_bytes,vsync\n";
        csv << f << ',' << ms << ',' << d << ',' << c << ',' << memory.PrivateUsage << ',' << memory.WorkingSetSize << ',' << interval << '\n';
        if(f%120==0) {
            csv.flush(); Log("frame=%llu interval_ms=%.3f draws=%llu dispatches=%llu private_MB=%zu",f,ms,d,c,memory.PrivateUsage/1048576);
            SYSTEM_INFO info{}; GetSystemInfo(&info);
            const uint64_t limit=reinterpret_cast<uintptr_t>(info.lpMaximumApplicationAddress)+1ull;
            uint64_t committed=0,reserved=0,free=0,largestFree=0;
            bool complete=true;
            for(uint64_t address=0;address<limit;) {
                MEMORY_BASIC_INFORMATION region{};
                if(!VirtualQuery(reinterpret_cast<const void*>(static_cast<uintptr_t>(address)),&region,sizeof(region))) { complete=false; break; }
                const uint64_t end=std::min(limit,reinterpret_cast<uintptr_t>(region.BaseAddress)+static_cast<uint64_t>(region.RegionSize));
                if(end<=address) { complete=false; break; }
                const auto bytes=end-address;
                if(region.State==MEM_COMMIT) committed+=bytes;
                else if(region.State==MEM_RESERVE) reserved+=bytes;
                else if(region.State==MEM_FREE) { free+=bytes; largestFree=std::max(largestFree,bytes); }
                address=end;
            }
            static auto addresses=TraceFile(Output()/"address-space.csv");
            if(f==0) addresses << "frame,limit_bytes,committed_bytes,reserved_bytes,free_bytes,largest_free_bytes,complete\n";
            addresses << f << ',' << limit << ',' << committed << ',' << reserved << ',' << free << ',' << largestFree << ',' << complete << '\n';
            addresses.flush();
        }
    }
    if(DetailedTrace() && (f==300 || f==1200 || f==3000)) { Stack("Present"); Screenshot(swapchain,f); }
    return realPresent(swapchain,interval,flags);
}
void STDMETHODCALLTYPE DrawIndexed(ID3D11DeviceContext* c,UINT n,UINT start,INT base) {
    if(Sample() && draws.load()<3) Stack("DrawIndexed"); if(!RecordDraw(c,"indexed",n,1,start,base)) return;
    if(CaptureHudDraw(c,[&] { realDrawIndexed(c,n,start,base); })) return; ++draws; realDrawIndexed(c,n,start,base);
}
void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* c,UINT n,UINT start) { if(!RecordDraw(c,"draw",n,1,start) || CaptureHudDraw(c,[&] { realDraw(c,n,start); })) return; ++draws; realDraw(c,n,start); }
void STDMETHODCALLTYPE DrawInstanced(ID3D11DeviceContext* c,UINT a,UINT b,UINT d,UINT e) { if(!RecordDraw(c,"instanced",a,b,d,0,e) || CaptureHudDraw(c,[&] { realDrawInstanced(c,a,b,d,e); })) return; ++draws; realDrawInstanced(c,a,b,d,e); }
void STDMETHODCALLTYPE DrawIndexedInstanced(ID3D11DeviceContext* c,UINT a,UINT b,UINT d,INT e,UINT f) {
    if(lightingEye) {
        ComPtr<ID3D11VertexShader> vs; ComPtr<ID3D11PixelShader> ps;
        c->VSGetShader(&vs,nullptr,nullptr); c->PSGetShader(&ps,nullptr,nullptr);
        uint64_t vh{},ph{};
        { std::lock_guard lock(shaderMutex); vh=shaderNames[vs.Get()]; ph=shaderNames[ps.Get()]; }
        // Novigrad capture: these three ground-cover batches have counts
        // 18/13/16 in the first eye, then 0/0/0 with the same mesh bindings.
        // Restrict correction to this verified shader pair and exact IA state.
        if(vh==0x5277336be52ad8fdull && ph==0x1292783744ff6ba8ull) {
            GroundCoverKey key; key.indices=a; key.start=d; key.base=e;
            std::array<ID3D11Buffer*,32> buffers{}; std::array<UINT,32> strides{},offsets{};
            c->IAGetVertexBuffers(0,32,buffers.data(),strides.data(),offsets.data());
            for(unsigned i=0;i<32;++i) {
                key.streams[i]={reinterpret_cast<uintptr_t>(buffers[i]),strides[i],offsets[i]};
                if(buffers[i]) buffers[i]->Release();
            }
            ComPtr<ID3D11Buffer> index; ComPtr<ID3D11InputLayout> layout;
            DXGI_FORMAT format{}; D3D11_PRIMITIVE_TOPOLOGY topology{};
            c->IAGetIndexBuffer(&index,&format,&key.indexOffset); c->IAGetInputLayout(&layout); c->IAGetPrimitiveTopology(&topology);
            key.index=reinterpret_cast<uintptr_t>(index.Get()); key.layout=reinterpret_cast<uintptr_t>(layout.Get());
            key.format=format; key.topology=topology;
            const bool repaired=groundCoverPair.Apply(lightingEye,key,b,f);
            if(requestedCaptureFrame==frame.load()) {
                Log("ground cover eye=%u repaired=%d instances=%u first=%u indices=%u",lightingEye,repaired,b,f,a);
                Stack("GroundCoverDraw");
            }
        }
    }
    if(!RecordDraw(c,"indexed_instanced",a,b,d,e,f) || CaptureHudDraw(c,[&] { realDrawIndexedInstanced(c,a,b,d,e,f); })) return; ++draws; realDrawIndexedInstanced(c,a,b,d,e,f);
}
void STDMETHODCALLTYPE Dispatch(ID3D11DeviceContext* c,UINT x,UINT y,UINT z) { ++dispatches; realDispatch(c,x,y,z); }
void STDMETHODCALLTYPE Targets(ID3D11DeviceContext* c,UINT n,ID3D11RenderTargetView* const* views,ID3D11DepthStencilView* depth) {
    if(Sample()) {
        Stack("OMSetRenderTargets");
        for(UINT i=0;i<n;++i) if(views && views[i]) {
            ComPtr<ID3D11Resource> resource; views[i]->GetResource(&resource);
            ComPtr<ID3D11Texture2D> texture;
            if(SUCCEEDED(resource.As(&texture))) {
                D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
                Log("target frame=%llu slot=%u %ux%u format=%u msaa=%u resource=%p depth=%p",frame.load(),i,desc.Width,desc.Height,desc.Format,desc.SampleDesc.Count,resource.Get(),depth);
            }
        }
    }
    realTargets(c,n,views,depth);
}
HRESULT STDMETHODCALLTYPE CreateVS(ID3D11Device* d,const void* b,SIZE_T n,ID3D11ClassLinkage* l,ID3D11VertexShader** s) {
    auto hash=Shader(b,n,"vs"); auto hr=realVS(d,b,n,l,s);
    if(SUCCEEDED(hr) && s && *s) { std::lock_guard lock(shaderMutex); shaderNames[*s]=hash; }
    return hr;
}
HRESULT STDMETHODCALLTYPE CreatePS(ID3D11Device* d,const void* b,SIZE_T n,ID3D11ClassLinkage* l,ID3D11PixelShader** s) {
    auto hash=Shader(b,n,"ps"); auto hr=realPS(d,b,n,l,s);
    if(SUCCEEDED(hr) && s && *s) { std::lock_guard lock(shaderMutex); shaderNames[*s]=hash; }
    return hr;
}
template<class F> void Hook(void* object,unsigned index,void* replacement,F& original) {
    auto target=(*static_cast<void***>(object))[index];
    if(targets.contains(target)) return;
    auto status=MH_CreateHook(target,replacement,reinterpret_cast<void**>(&original));
    if(status==MH_OK) status=EnableRecordedHook(target);
    Log("hook slot=%u status=%s",index,MH_StatusToString(status));
    if(status==MH_OK) targets.insert(target);
}
}
void AttachTrace(ID3D11Device* device,ID3D11DeviceContext* context,IDXGISwapChain* swapchain) {
    std::lock_guard lock(attachMutex);
    if(!EnableDrivingControls()) { Log("driving controls: incompatible process"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    if(AutoCockpitEnabled() && !EnableCockpitStart()) { Log("VR starting camera: incompatible process"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    wchar_t desktop[8]{};
    const bool desktopControls=GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_CONTROLS",desktop,8)==1 && desktop[0]==L'1';
    if(desktopControls || (GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_PRACTICE",desktop,8)==1 && desktop[0]==L'1')) {
        EnableDirectPractice();
        Log("desktop practice: no rendering hooks, VR hotkeys or OpenXR initialization");
        return;
    }
    static bool initialized=[] { auto status=MH_Initialize(); return status==MH_OK||status==MH_ERROR_ALREADY_INITIALIZED; }();
    if(!initialized) { Log("MinHook initialization failed"); return; }
    if(HeadsetEnabled() && !EnableGfwlCompatibility()) { Log("GFWL compatibility: initialization failed; stopping VR launch"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    EnableDirectPractice();
    Log("DX11 device=%p feature_level=0x%x context=%p swapchain=%p",device,device->GetFeatureLevel(),context,swapchain);
    Hook(device,12,reinterpret_cast<void*>(CreateVS),realVS);
    Hook(device,15,reinterpret_cast<void*>(CreatePS),realPS);
    ComPtr<ID3D11DeviceContext> owned;
    if(!context) { device->GetImmediateContext(&owned); context=owned.Get(); }
    Hook(context,12,reinterpret_cast<void*>(DrawIndexed),realDrawIndexed);
    Hook(context,13,reinterpret_cast<void*>(Draw),realDraw);
    Hook(context,14,reinterpret_cast<void*>(Map),realMap);
    Hook(context,15,reinterpret_cast<void*>(Unmap),realUnmap);
    Hook(context,20,reinterpret_cast<void*>(DrawIndexedInstanced),realDrawIndexedInstanced);
    Hook(context,21,reinterpret_cast<void*>(DrawInstanced),realDrawInstanced);
    Hook(context,41,reinterpret_cast<void*>(Dispatch),realDispatch);
    Hook(context,33,reinterpret_cast<void*>(Targets),realTargets);
    if(swapchain) {
        gameSwapchain=swapchain;
        if(HeadsetEnabled()) {
            DXGI_SWAP_CHAIN_DESC desc{};
            const bool attached=SUCCEEDED(swapchain->GetDesc(&desc)) && AttachHotkeys(desc.OutputWindow);
            Log("OpenXR window hotkeys attached=%d",attached);
        }
        Hook(swapchain,8,reinterpret_cast<void*>(Present),realPresent);
        // Runtime traces from this exact SHA identify the common scene entry.
        // A prologue guard prevents detouring unexpected/incompatible code.
        auto entry=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr))+0x33ad10;
        const unsigned char prologue[]={0x81,0xec,0xf8,0,0,0,0x56,0x8b,0xf1};
        if(!realScene && memcmp(entry,prologue,sizeof(prologue))==0) {
            auto status=MH_CreateHook(entry,reinterpret_cast<void*>(Scene),reinterpret_cast<void**>(&realScene));
            if(status==MH_OK) status=EnableRecordedHook(entry);
            Log("scene trace RVA=0x33ad10 status=%s",MH_StatusToString(status));
        }
        auto inner=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr))+0x336be0;
        const unsigned char innerPrologue[]={0x53,0x55,0x8b,0x6c,0x24,0x14,0x56,0x57,0x8b,0x7c,0x24,0x20};
        if(!realInner && (InnerReplayEnabled() || HudProbe()) && memcmp(inner,innerPrologue,sizeof(innerPrologue))==0) {
            auto status=MH_CreateHook(inner,reinterpret_cast<void*>(Inner),reinterpret_cast<void**>(&realInner));
            if(status==MH_OK) status=EnableRecordedHook(inner);
            Log("inner scene trace RVA=0x336be0 status=%s",MH_StatusToString(status));
        }
        if(ContinuousReplayEnabled()) {
            auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
            if(HeadsetEnabled() && !realPauseWorld) {
                const unsigned char pause[]={0x8b,0x44,0x24,0x04,0x8a,0x50,0x04,0x56,0x8b,0xf1,
                    0x8a,0x8e,0x96,0,0,0,0x88,0x96,0x96,0,0,0};
                if(memcmp(base+0x16ec90,pause,sizeof(pause))==0) {
                    auto status=MH_CreateHook(base+0x16ec90,reinterpret_cast<void*>(PauseWorld),reinterpret_cast<void**>(&realPauseWorld));
                    if(status==MH_OK) status=EnableRecordedHook(base+0x16ec90);
                    pauseHookReady=status==MH_OK;
                    Log("pause world hook RVA=0x16ec90 status=%s",MH_StatusToString(status));
                } else Log("pause world hook rejected instruction guard; using virtual screen");
            }
            if(HeadsetEnabled() && WideVisibility() && !realFrustumCopy) {
                const unsigned char copy[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x8b,0xc1,0x8b,0x4d,0x08};
                const unsigned char build[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x04,0x01,0,0};
                if(memcmp(base+0x2b7db0,copy,sizeof(copy))==0 && memcmp(base+0xd26c40,build,sizeof(build))==0) {
                    buildFrustum=reinterpret_cast<FrustumCopyFn>(base+0xd26c40);
                    auto status=MH_CreateHook(base+0x2b7db0,reinterpret_cast<void*>(FrustumCopy),reinterpret_cast<void**>(&realFrustumCopy));
                    if(status==MH_OK) status=EnableRecordedHook(base+0x2b7db0);
                    Log("visibility frustum hook status=%s",MH_StatusToString(status));
                } else Log("visibility frustum hook rejected instruction guard");
            }
            if(HeadsetEnabled()) {
                const unsigned char point[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x10};
                const unsigned char spot[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x8b,0x45,0x10,0x83,0xec,0x14};
                const unsigned char projected[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x8b,0x45,0x10,0x81,0xec,0x14,0x01,0,0};
                auto hook=[&](unsigned rva,const void* signature,size_t size,void* replacement,void** original) {
                    if(*original) return true;
                    if(memcmp(base+rva,signature,size)!=0) return false;
                    auto status=MH_CreateHook(base+rva,replacement,original);
                    if(status==MH_OK) status=EnableRecordedHook(base+rva);
                    Log("lighting parameter hook RVA=0x%x status=%s",rva,MH_StatusToString(status));
                    if(status!=MH_OK) *original=nullptr;
                    return status==MH_OK;
                };
                const bool p=hook(0x7c0d30,point,sizeof(point),reinterpret_cast<void*>(PointSetup),reinterpret_cast<void**>(&realPointSetup));
                const bool s=hook(0x7c7d70,spot,sizeof(spot),reinterpret_cast<void*>(SpotSetup),reinterpret_cast<void**>(&realSpotSetup));
                const bool m=hook(0x7c7fa0,projected,sizeof(projected),reinterpret_cast<void*>(ProjectedSetup),reinterpret_cast<void**>(&realProjectedSetup));
                lightingHooksReady=p && s && m;
                Log("lighting parameter hooks ready=%d",lightingHooksReady);
            }
            if(WaterReflectionsEnabled() && !waterPrepareTail) {
                const unsigned char tail[]={0x8d,0x8e,0x60,0x09,0,0};
                const unsigned char exit[]={0x5f,0x5e,0x5b,0x8b,0xe5,0x5d,0xc3};
                if(!memcmp(base+0x2e2975,tail,sizeof(tail)) && !memcmp(base+0x2e2a5d,exit,sizeof(exit))) {
                    waterPrepareExit=base+0x2e2a5d;
                    auto status=MH_CreateHook(base+0x2e2975,reinterpret_cast<void*>(WaterPrepareTail),&waterPrepareTail);
                    if(status==MH_OK) status=EnableRecordedHook(base+0x2e2975);
                    Log("water camera-only hook status=%s",MH_StatusToString(status));
                    if(status!=MH_OK) waterPrepareTail=nullptr;
                }
            }
            const unsigned char setupPrologue[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xe4,0,0,0};
            const unsigned char uploadPrologue[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x54};
            if(!realCameraSetup && memcmp(base+0x330b70,setupPrologue,sizeof(setupPrologue))==0) {
                auto status=MH_CreateHook(base+0x330b70,reinterpret_cast<void*>(CameraSetup),reinterpret_cast<void**>(&realCameraSetup));
                if(status==MH_OK) status=EnableRecordedHook(base+0x330b70);
                Log("camera setup hook status=%s",MH_StatusToString(status));
                cameraSetupHookReady=status==MH_OK;
            }
            if(!realCameraUpload && memcmp(base+0xba12d0,uploadPrologue,sizeof(uploadPrologue))==0) {
                auto status=MH_CreateHook(base+0xba12d0,reinterpret_cast<void*>(CameraUpload),reinterpret_cast<void**>(&realCameraUpload));
                if(status==MH_OK) status=EnableRecordedHook(base+0xba12d0);
                Log("camera upload hook status=%s",MH_StatusToString(status));
                cameraHooksReady=status==MH_OK && cameraSetupHookReady;
            }
        }
    }
}
}
