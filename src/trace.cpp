#include "graphics_diagnostics.h"
#include "anisotropic_filter.h"
#include "resolution_report.h"
#include "trace.h"
#include "f04_camera.h"
#include "common.h"
#include "terrain_registry.h"
#include "pikes_scope.h"
#include "eye_pair.h"
#include "camera_math.h"
#include "scene_camera.h"
#include "replay_camera.h"
#include "game_pause.h"
#include "photo_mode.h"
#include "game_xr.h"
#include "hud_capture.h"
#include "hud_elements.h"
#include "water_trace.h"
#include "water_ripples.h"
#include "gpu_timer.h"
#include "vr_hotkeys.h"
#include "seat_adjustment.h"
#include "light_replay.h"
#include "ground_cover.h"
#include "shadow_trace.h"
#include "pipeline_trace.h"
#include "test_message.h"
#include "shadow_mask.h"
#include "scene_depth.h"
#include "shadow_coverage.h"
#include "shadow_distance.h"
#include "exposure_window.h"
#include "trailer_background.h"
#include "sky_sun.h"
#include "direct_menus.h"
#include "gfwl_compat.h"
#include "driving_controls.h"
#include "lap_results.h"
#include "cockpit_start.h"
#include "startup_hooks.h"
#include "steering_animation.h"
#include "direct_startup_trace.h"
#include "race_difficulty.h"
#include "car_setup.h"
#include "car_grade.h"
#include "race_drivers.h"
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
unsigned directStaggeredDiscipline{};
void* directLapCaller{};
const void* StaggeredDiscipline(unsigned database) {
    using GetTable=void* (__thiscall*)(void*,const char*);
    using GetRow=const void* (__thiscall*)(void*,unsigned);
    using GetInteger=int (__thiscall*)(void*,int*,const void*,const char*);
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    auto table=reinterpret_cast<GetTable>(base+0x72fca0)(reinterpret_cast<void*>(database),"discipline");
    if(!table) return nullptr;
    const auto count=*reinterpret_cast<const unsigned*>(static_cast<const unsigned char*>(table)+0x30);
    if(count>64) return nullptr;
    for(unsigned index=0;index<count;++index) {
        const auto row=reinterpret_cast<GetRow>(base+0x6ecc40)(table,index);
        if(!row) return nullptr;
        int id=-1;
        if(reinterpret_cast<GetInteger>(base+0x719260)(table,&id,row,"id")==0 &&
           id==static_cast<int>(directStaggeredDiscipline)) return row;
    }
    return nullptr;
}
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
    if(directStaggeredDiscipline) {
        const auto discipline=StaggeredDiscipline(descriptor[1]);
        if(descriptor[2]!=0 || directLaps!=1 || !discipline) {
            Log("direct start: staggered route validation failed; stopping launch");
            ExitProcess(ERROR_BAD_EXE_FORMAT);
        }
        // Native race-type enum 2 is staggered (database race_type ID 4).
        // The demo otherwise uses standard racing and the first discipline (Raid).
        descriptor[2]=2;
        descriptor[4]=reinterpret_cast<unsigned>(discipline);
    }
    auto result=realLapDescriptorCopy(target,descriptor);
    Log("direct start: demo route laps=%u copied=%u",directLaps,static_cast<unsigned*>(target)[5]);
    if(directStaggeredDiscipline)
        Log("direct start: staggered race type=%u discipline=%u",static_cast<unsigned*>(target)[2],directStaggeredDiscipline);
    return result;
}
bool EnableDirectLaps(unsigned char* base,unsigned laps) {
    wchar_t mode[16]{};
    const auto modeLength=GetEnvironmentVariableW(L"DIRT2VR_STAGGERED_DISCIPLINE",mode,16);
    unsigned discipline=0;
    if(modeLength) {
        if(modeLength>=16) return false;
        if(!wcscmp(mode,L"rally")) discipline=30;
        else if(!wcscmp(mode,L"trailblazer")) discipline=28;
        else return false;
        if(laps!=1) return false;
        constexpr unsigned char tableStart[]{0x51,0x56,0x57,0x8b,0xf1};
        constexpr unsigned char integerStart[]{0x51,0x8b,0x44,0x24,0x10,0x8b,0x54,0x24,0x0c};
        constexpr unsigned char rowStart[]{0x8b,0x54,0x24,0x04,0x33,0xc0,0x3b,0x51,0x30};
        if(memcmp(base+0x72fca0,tableStart,sizeof(tableStart)) ||
           memcmp(base+0x719260,integerStart,sizeof(integerStart)) ||
           memcmp(base+0x6ecc40,rowStart,sizeof(rowStart))) return false;
    }
    if(laps==1 && !discipline) return true;
    const unsigned char copyPrologue[]={0x8b,0xc1,0x8b,0x4c,0x24,0x04,0x8b,0x11};
    const unsigned char callSite[]={0x89,0x94,0x24,0xa4,0x00,0x00,0x00,0xe8,0xfb,0x56,0x02,0x00};
    if(memcmp(base+0x36a3d0,copyPrologue,sizeof(copyPrologue))!=0 ||
       memcmp(base+0x344cc9,callSite,sizeof(callSite))!=0) return false;
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED) return false;
    directLaps=laps;
    directStaggeredDiscipline=discipline;
    directLapCaller=base+0x344cd5;
    status=MH_CreateHook(base+0x36a3d0,reinterpret_cast<void*>(LapDescriptorCopy),reinterpret_cast<void**>(&realLapDescriptorCopy));
    if(status==MH_OK) status=EnableRecordedHook(base+0x36a3d0);
    Log("direct start: demo lap override=%u hook=%s",laps,MH_StatusToString(status));
    return status==MH_OK;
}
// Circuit races normally divide this bar by laps. Single-lap Nordschleife retains its
// eight timing checkpoints instead, using the native route-split renderer.
bool EnableNordschleifeCheckpoints() {
    wchar_t enabled[8]{};
    const auto length=GetEnvironmentVariableW(L"DIRT2VR_NORDSCHLEIFE_CHECKPOINTS",enabled,8);
    if(!length) return true;
    if(length!=1 || enabled[0]!=L'1' || !SupportedHost()) return false;
    static bool applied{};
    if(applied) return true;
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    constexpr unsigned char compare[]={0x80,0x79,0x54,0x01};
    constexpr unsigned char checkpoints[]={0x80,0x79,0x54,0x02};
    constexpr unsigned char finishBranch[]={0x74,0x2c};
    constexpr unsigned char showFinish[]={0xeb,0x2c};
    struct Edit { unsigned rva,size; const unsigned char *before,*after; };
    const Edit edits[]={{0x3c445f,sizeof(compare),compare,checkpoints},
                        {0x3c487a,sizeof(finishBranch),finishBranch,showFinish}};
    for(const auto& edit:edits)
        if(memcmp(base+edit.rva,edit.before,edit.size)) return false;
    // Select native checkpoint positions rather than lap divisions. Then append
    // the native finish tick at 100 percent: nine splits plus finish use ten slots.
    // The shared circuit/lap descriptor and timing state remain untouched.
    for(const auto& edit:edits) {
        auto target=base+edit.rva;
        DWORD previous{},ignored{};
        if(!VirtualProtect(target,edit.size,PAGE_EXECUTE_READWRITE,&previous)) return false;
        memcpy(target,edit.after,edit.size);
        for(unsigned i=0;i<edit.size;++i)
            if(edit.before[i]!=edit.after[i]) RecordCodeByte(target+i,edit.before[i],edit.after[i]);
        if(!VirtualProtect(target,edit.size,previous,&ignored) || !FlushInstructionCache(GetCurrentProcess(),target,edit.size)) return false;
    }
    applied=true;
    Log("Nordschleife: native circuit progress with timing checkpoint markers (memory only)");
    return true;
}
// The demo start path otherwise forces the local vehicle back to AI every update.
// Only change the controller's override, preserving the frontend's loading flow.
void EnableDirectPractice() {
    EnableLapResults();
    if(!EnableNordschleifeCheckpoints()) {
        Log("Nordschleife: checkpoint display guard failed; stopping launch");
        ExitProcess(ERROR_BAD_EXE_FORMAT);
    }
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
            unsigned laps=1;
            if(length) {
                if(length>=16) ExitProcess(ERROR_INVALID_PARAMETER);
                wchar_t* end{};
                laps=wcstoul(text,&end,10);
                if(end==text || *end || laps<1 || laps>20) {
                    Log("direct start: lap override validation failed");
                    ExitProcess(ERROR_BAD_EXE_FORMAT);
                }
            }
            if(!EnableDirectLaps(base,laps)) {
                Log("direct start: route override validation failed");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            Log("direct start: requested laps=%u (memory only)",laps);
            if(!EnableRaceDifficulty()) {
                Log("direct race: difficulty validation failed; stopping launch");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            if(!EnableCarGrade()) {
                Log("direct car grade: validation failed; stopping launch");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            if(!EnableCarSetup()) {
                Log("direct car setup: validation failed; stopping launch");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            if(!EnableRaceDrivers()) {
                Log("direct race: opponent identity validation failed; stopping launch");
                ExitProcess(ERROR_BAD_EXE_FORMAT);
            }
            EnableDirectStartupTrace();
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
using BindPixelShaderFn=void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11PixelShader*,ID3D11ClassInstance* const*,UINT);
BindPixelShaderFn realBindPixelShader{};
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
thread_local ShadowRays shadowRays;
bool ShadowsEnabled() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_SHADOWS",value,8)==1 && value[0]==L'1'; }();
    return enabled;
}
using ShadowInteriorDefaultsFn=void (__thiscall*)(void*);
ShadowInteriorDefaultsFn realShadowInteriorDefaults{};
void* shadowSystem{};
void __declspec(naked) ShadowInteriorDefaults() {
    // Native callers reuse ECX across adjacent leaf initializers. Preserve
    // every register and flag, including nominally caller-saved registers.
    // Observe the live object only; native defaults stay unchanged.
    __asm {
        call dword ptr [realShadowInteriorDefaults]
        mov dword ptr [shadowSystem],ecx
        ret
    }
}
float* ShadowWorldRange() {
    // The renderer owns this object. Recheck its concrete type and native
    // self-reference each time, including after track unload/reload.
    auto* object=static_cast<unsigned char*>(shadowSystem);
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    __try {
        if(!object || *reinterpret_cast<void**>(object)!=base+0xf20540 ||
           *reinterpret_cast<void**>(object+0x6d4)!=object)return nullptr;
        return reinterpret_cast<float*>(object+0xd64);
    } __except(EXCEPTION_EXECUTE_HANDLER) {return nullptr;}
}
ShadowCoverage shadowCoverage;
float ShadowDetailDistance() {
    static const float distance=[] {
        wchar_t value[16]{};const auto size=GetEnvironmentVariableW(L"DIRT2VR_SHADOW_DETAIL_DISTANCE",value,16);
        if(!size || size>=16)return 14.f;
        wchar_t* end{};const auto parsed=wcstoul(value,&end,10);
        return end!=value && !*end && parsed>=14 && parsed<=60 ? float(parsed):14.f;
    }();return distance;
}
bool CockpitShadowMode();
void* shadowCasterFovContinue{};
bool shadowCasterFovReady{},shadowCasterFovActive{};
// The native selection job uses FOV for object-size rejection. Keep its
// normal chase threshold: the widened VR camera must not discard road casters.
// This field is separate from the light matrices and visible camera.
float shadowCasterFovValue=0.907571211f; // Native chase 52 degrees.
void __declspec(naked) ShadowCasterFov() {
    __asm {
        fld dword ptr [eax+50h]
        fstp dword ptr [esi+0f0h]
        pushfd
        cmp byte ptr [shadowCasterFovActive],0
        je unchanged
        cmp dword ptr [ebx+2ch],1
        jne unchanged
        push eax
        mov eax,dword ptr [shadowCasterFovValue]
        mov dword ptr [esi+0f0h],eax
        pop eax
    unchanged:
        popfd
        jmp dword ptr [shadowCasterFovContinue]
    }
}
// Read-only, explicitly opt-in shadow-caster diagnostics. Never change the
// native visibility decision, camera, bounds or returned fade value.
using ShadowCastersFn=void (__thiscall*)(void*,int,void*,void*);
using ShadowFadeFn=float (__cdecl*)(const float*,const float*,float,float,float,float,float,float);
using ShadowItemFn=void (__thiscall*)(void*,const float*,float,unsigned,unsigned,unsigned);
ShadowCastersFn realShadowCasters{};
ShadowFadeFn realShadowFade{};
ShadowItemFn realShadowItem{};
thread_local int shadowCasterCascade{};
thread_local float shadowCasterFov{};
void __fastcall ShadowCasters(void* self,void*,int cascade,void* context,void* list) {
    const auto previous=shadowCasterCascade;const auto previousFov=shadowCasterFov;
    if(self==shadowSystem && context && ShadowWorldRange() && ShadowSequenceFrame(frame.load())) {
        shadowCasterCascade=cascade;
        shadowCasterFov=*reinterpret_cast<float*>(static_cast<unsigned char*>(context)+0xf4);
        if(list && cascade>0 && cascade<=2) {
            static std::mutex mutex;std::lock_guard lock(mutex);
            static auto out=TraceFile(Output()/"shadow-caster-context.csv");
            const auto* object=static_cast<unsigned char*>(self);
            const auto* native=*reinterpret_cast<unsigned char* const*>(object+0x94+cascade*4);
            out<<frame.load()<<','<<*reinterpret_cast<const unsigned*>(object+0x2c)<<','<<cascade;
            for(const auto* p:{static_cast<unsigned char*>(context),static_cast<unsigned char*>(list)})
                for(auto offset:{0xc0,0xc4,0xc8,0xf4,0xf8,0xfc,0x100})out<<','<<*reinterpret_cast<const float*>(p+offset);
            if(native) {
                for(auto offset:{0x28,0xd0,0x34,0xf4})out<<','<<*reinterpret_cast<const unsigned*>(native+offset);
                out<<','<<*reinterpret_cast<const float*>(native+0x2c0)<<','<<*reinterpret_cast<const float*>(native+0x2c8);
            }
            out<<std::endl;
        }
    }
    realShadowCasters(self,cascade,context,list);
    shadowCasterCascade=previous;shadowCasterFov=previousFov;
}
float __cdecl ShadowFade(const float* bounds,const float* position,float low,float high,float start,float end,float fovScale,float bias) {
    const float result=realShadowFade(bounds,position,low,high,start,end,fovScale,bias);
    auto* base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    if(shadowCasterCascade && _ReturnAddress()==base+0x97bec9) {
        static std::mutex mutex;std::lock_guard lock(mutex);
        static unsigned rows{};
        if(rows>=12000)return result;
        static auto out=TraceFile(Output()/"shadow-caster-fade.csv");
        if(!rows)out<<"frame,preset,cascade,fov,fovScale,low,high,start,end,bias,result,minX,minY,minZ,maxX,maxY,maxZ,viewX,viewY,viewZ\n";
        ++rows;
        out<<frame.load()<<','<<*reinterpret_cast<unsigned*>(static_cast<unsigned char*>(shadowSystem)+0x2c)<<','<<shadowCasterCascade<<','<<shadowCasterFov<<','<<fovScale<<','<<low<<','<<high<<','<<start<<','<<end<<','<<bias<<','<<result;
        for(auto i:{0,1,2,4,5,6})out<<','<<bounds[i];
        for(int i=0;i<3;++i)out<<','<<position[i];
        out<<std::endl;
    }
    return result;
}
void __fastcall ShadowItem(void* self,void*,const float* position,float scale,unsigned index,unsigned flags,unsigned selector) {
    if(shadowCasterCascade && flags==0x401 && selector<12) {
        const auto* object=*reinterpret_cast<unsigned char**>(static_cast<unsigned char*>(self)+0x3c0)+index*128;
        const auto* model=*reinterpret_cast<unsigned char* const*>(object+0x64);
        unsigned before[3]{},after[3]{};
        for(unsigned i=0;i<3;++i)before[i]=*reinterpret_cast<const unsigned*>(model+0x98+(selector+i*12)*4);
        realShadowItem(self,position,scale,index,flags,selector);
        for(unsigned i=0;i<3;++i)after[i]=*reinterpret_cast<const unsigned*>(model+0x98+(selector+i*12)*4);
        static std::mutex mutex;std::lock_guard lock(mutex);static unsigned rows{};
        if(rows++>=12000)return;
        static auto out=TraceFile(Output()/"shadow-caster-items.csv");
        if(rows==1)out<<"frame,preset,cascade,selector,index,flag6d,flag6f,model35,minX,minY,minZ,maxX,maxY,maxZ,lod0delta,lod1delta,lod2delta\n";
        out<<frame.load()<<','<<*reinterpret_cast<unsigned*>(static_cast<unsigned char*>(shadowSystem)+0x2c)<<','<<shadowCasterCascade<<','<<selector<<','<<index<<','<<unsigned(object[0x6d])<<','<<unsigned(object[0x6f])<<','<<unsigned(model[0x35]);
        const auto* bounds=reinterpret_cast<const float*>(object+0x30);
        for(auto i:{0,1,2,4,5,6})out<<','<<bounds[i];
        for(unsigned i=0;i<3;++i)out<<','<<int(after[i]-before[i]);
        out<<std::endl;return;
    }
    realShadowItem(self,position,scale,index,flags,selector);
}
void TraceShadowPresets(uint64_t f) {
    if(!ShadowSequenceFrame(f) || !ShadowWorldRange())return;
    auto* object=static_cast<unsigned char*>(shadowSystem);
    auto out=TraceFile(Output()/"shadow-presets.csv",std::ios::app);
    const auto active=*reinterpret_cast<const unsigned*>(object+0x2c);
    const auto quality=*reinterpret_cast<const unsigned*>(object+0x720);
    for(unsigned preset=0;preset<4;++preset)for(unsigned offset=0;offset<0x94;offset+=4) {
        unsigned bits{};float value{};
        memcpy(&bits,object+0xc78+preset*0x94+offset,4);memcpy(&value,&bits,4);
        out<<f<<','<<active<<','<<quality<<','<<preset<<','<<offset<<','<<bits<<','<<value<<'\n';
    }
}
thread_local void* waterEyeRenderer{};
void* waterRenderer{};
uint64_t waterRendererFrame=~uint64_t{};
unsigned waterNativePasses{};
bool waterMultipleRenderers{};
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
bool HeadsetEnabled();
bool& ScreenMode();
float TrailerTrackingScale(SceneCamera);
float TrailerSourceScale();
extern uint64_t requestedCaptureFrame;
bool TrailerBackgroundEnabled() {
    // Keep an internal opt-out for paired diagnostics. The player-facing gate
    // is the existing experimental trailer option, which remains off by default.
    static const bool enabled=[] {wchar_t value[8]{};return !(GetEnvironmentVariableW(L"DIRT2VR_TRAILER_BACKGROUND_REPROJECT",value,8)==1 && value[0]==L'0');}();
    return TrailerMenusEnabled() && enabled;
}
// The source pass precedes xrWaitFrame. Use the last tracked centre with a
// generous angular margin; the displayed eyes still use this frame's poses.
thread_local XrPosef trailerTrackedPose{{0,0,0,1},{0,0,0}};
thread_local uint64_t trailerTrackedFrame=~uint64_t{};
struct TrailerSource {
    void* renderer{};
    uint64_t frame=~uint64_t{};
    XrPosef pose{{0,0,0,1},{0,0,0}};
    alignas(16) std::array<unsigned char,0x1a0> camera{};
    bool ready{};
};
thread_local TrailerSource trailerSource;
thread_local bool trailerSourceSetup{},trailerDisplaySetup{};
const XrFovf trailerSourceFov{-1.309f,1.309f,1.309f,-1.309f}; // 150 degrees, both axes.
thread_local const unsigned char* trailerNativeCamera{};
thread_local TrailerBackgroundRays trailerBackgroundRays;
struct ScopedTrailerBackground {
    const unsigned char* previous=trailerNativeCamera;
    ScopedTrailerBackground(bool trailer,void* renderer) {
        trailerNativeCamera=trailer && TrailerBackgroundEnabled() && trailerSource.ready &&
            trailerSource.renderer==renderer && trailerSource.frame==frame.load()?trailerSource.camera.data():nullptr;
        trailerBackgroundRays={};
        if(trailerNativeCamera && (frame.load()==3000 || requestedCaptureFrame==frame.load())) {
            auto out=TraceFile(Output()/("trailer-background-source-"+std::to_string(frame.load())+".bin"),std::ios::binary);
            out.write(reinterpret_cast<const char*>(trailerNativeCamera),trailerSource.camera.size());
        }
    }
    ~ScopedTrailerBackground(){trailerNativeCamera=previous;trailerBackgroundRays={};}
};
thread_local GroundCoverPair groundCoverPair;
bool LightingTrace() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_TRACE_LIGHTS",value,8)>0 && wcscmp(value,L"1")==0; }();
    return LoggingEnabled() && enabled;
}
bool PipelineOption(const wchar_t* name) {
    wchar_t value[8]{};
    return PipelineTraceEnabled() && GetEnvironmentVariableW(name,value,8)==1 && value[0]==L'1';
}
thread_local MeterWindow meterWindow;
bool HeadsetEnabled();
bool ExposureWindowEnabled() {
    static const bool enabled=[] { wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_EXPOSURE_WINDOW_PROBE",value,8)==1 && value[0]==L'1'; }();
    return HeadsetEnabled() || ShadowsEnabled() || (LoggingEnabled() && enabled);
}
bool PipelineLights() {
    static const bool enabled=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_PIPELINE_LIGHTS",value,8)==1 && value[0]==L'1'; }();
    return PipelineTraceEnabled() && enabled;
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
bool pauseHookReady{};
// The game updates its inline render camera during scene preparation. Classify
// the original, validated main view once; never retain that identity next frame.
thread_local void* classifiedRenderer{};
thread_local SceneCamera classifiedCamera=SceneCamera::Unknown;
thread_local void* preparedCameraRenderer{};
thread_local uint64_t preparedCameraFrame=~uint64_t{};
thread_local SceneCamera preparedCameraKind=SceneCamera::Unknown;
SceneCamera RenderSceneCamera(void* renderer,const float* a,const float* b,uint64_t f) {
    if((ExtendedViewsEnabled() || ReplayStereoEnabled() || TrailerMenusEnabled() || PhotoModeRequested()) && renderer==classifiedRenderer)return classifiedCamera;
    return IdentifySceneCamera(a,b,f);
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
        // This is the last unmodified camera before the engine prepares the scene.
        // Carry its verified identity only to this renderer's scene in this frame.
        const auto kind=IdentifySceneCamera(a,b,frame.load());
        if(ShadowsEnabled()) {
            const bool before=shadowCoverage.Active();
            auto* range=ShadowWorldRange();
            const bool cockpit=kind==SceneCamera::Cockpit && CockpitShadowMode();
            shadowCoverage.Update(range,cockpit,ShadowDetailDistance());
            shadowCasterFovActive=shadowCasterFovReady && range && cockpit;
            TraceShadowPresets(frame.load());
            if(before!=shadowCoverage.Active())Log("cockpit shadow preparation frame=%llu extended=%d",frame.load(),shadowCoverage.Active());
        }
        if(ExtendedViewsEnabled() || ReplayStereoEnabled() || TrailerMenusEnabled() || PhotoModeRequested()) {
            preparedCameraRenderer=renderer;preparedCameraFrame=frame.load();preparedCameraKind=kind;
        }
        if(WideVisibility() && StereoCameraAllowed(kind,ExtendedViewsEnabled(),GamePaused(),true,ReplayStereoEnabled(),TrailerMenusEnabled()) && VisibilityBox(a,b,matrix.data(),static_cast<const float*>(source))) {
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
    const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress())-reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const bool source=TrailerBackgroundEnabled() && !GamePaused() && (!HeadsetEnabled() || !ScreenMode()) &&
        caller==0x288c23 && *reinterpret_cast<const unsigned*>(bytes+0x960)==2 &&
        IdentifySceneCamera(reinterpret_cast<const float*>(bytes+0x5e0),reinterpret_cast<const float*>(bytes+0x650),frame.load())==SceneCamera::Frontend;
    std::array<float,28> original{};
    const auto previousFov=eyeFov;
    const bool previousSource=trailerSourceSetup,previousDisplay=trailerDisplaySetup;
    if(source) {
        trailerSource={};trailerSource.renderer=self;trailerSource.frame=frame.load();
        if(HeadsetEnabled()) {
            if(trailerTrackedFrame<=frame.load() && frame.load()-trailerTrackedFrame<=2)trailerSource.pose=trailerTrackedPose;
        } else if(PipelineOption(L"DIRT2VR_PIPELINE_POSE"))
            trailerSource.pose={{-.08418598f,.25783416f,.02255757f,.96225019f},{.10f,.05f,-.10f}};
        memcpy(original.data(),camera,112);
        ApplyEyePose(static_cast<float*>(camera),trailerSource.pose,TrailerSourceScale());
        eyeFov=&trailerSourceFov;trailerSourceSetup=true;
    }
    trailerDisplaySetup=trailerNativeCamera && (camera==bytes+0x5e0 || camera==bytes+0x650);
    eyeCameraSetup=(self==eyeRenderer && (camera==bytes+0x5e0 || camera==bytes+0x650)) ||
        (self==waterEyeRenderer && camera==*reinterpret_cast<void**>(bytes+0x940)) || source;
    if(PipelineTraceEnabled() && frame.load()==3000 && eyeCameraSetup && !waterEyeRenderer)
        Log("PIPELINE CameraSetup eye=%u near=%f upload=%d camera=%p",scenePass,nearPlane,upload,camera);
    realCameraSetup(self,context,camera,nearPlane,upload);
    if(source) {
        memcpy(camera,original.data(),112);
        eyeFov=previousFov;
    }
    // The frontend prepares its scene through a different path from racing.
    // Widen only the RV's visibility volume before its scene jobs are queued;
    // keep the original projection and all loading/transition cameras intact.
    if(TrailerMenusEnabled() && WideVisibility() && caller==0x288c23 && buildFrustum && realFrustumCopy && !eyeRenderer) {
        const auto a=reinterpret_cast<const float*>(bytes+0x5e0);
        const auto b=reinterpret_cast<const float*>(bytes+0x650);
        if(IdentifySceneCamera(a,b,frame.load())==SceneCamera::Frontend) {
            alignas(16) std::array<float,16> matrix{};
            alignas(16) std::array<float,56> volume{};
            if(VisibilityBox(a,b,matrix.data())) {
                buildFrustum(volume.data(),matrix.data());
                if(std::all_of(volume.begin(),volume.end(),[](float v){return std::isfinite(v);})) {
                    realFrustumCopy(bytes+0x340,volume.data());
                    if(frame.load()%120==0)Log("trailer visibility all-directions frame=%llu",frame.load());
                }
            }
        }
    }
    if(eyeCameraSetup && !waterEyeRenderer && lightingEye && upload) RefreshLights(context);
    eyeCameraSetup=previous;
    trailerSourceSetup=previousSource;trailerDisplaySetup=previousDisplay;
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
            if(eyeFov) {
                const float nativeX=projection[0],nativeY=projection[5];
                ApplyFov(projection,*eyeFov); ++eyeProjectionUploads;
                if(!waterEyeRenderer && ExposureWindowEnabled()) meterWindow=NativeMeterWindow(nativeX,nativeY,projection);
            }
            else projection[8]=projectionShift;
            MultiplyMatrices(view,projection,combined);
        }
    }
    if(eyeCameraSetup && !waterEyeRenderer && ShadowsEnabled())
        shadowRays=ShadowViewRays(reinterpret_cast<const float*>(static_cast<unsigned char*>(context)+0x120));
    if(trailerSourceSetup && eyeCameraSetup && !waterEyeRenderer) {
        memcpy(trailerSource.camera.data(),context,trailerSource.camera.size());trailerSource.ready=true;
    }
    if((eyeCameraSetup || trailerDisplaySetup) && !waterEyeRenderer && trailerNativeCamera) {
        trailerBackgroundRays=TrailerBackgroundTransform(reinterpret_cast<const float*>(trailerNativeCamera+0x160),
            reinterpret_cast<const float*>(trailerNativeCamera+0x120),
            reinterpret_cast<const float*>(static_cast<unsigned char*>(context)+0x160),
            reinterpret_cast<const float*>(static_cast<unsigned char*>(context)+0x120));
        if(frame.load()%120==0)Log("Trailer background coverage frame=%llu eye=%u full=%d source_frame=%llu",frame.load(),lightingEye,TrailerBackgroundCoversView(trailerBackgroundRays),trailerSource.frame);
    }
    if(!waterEyeRenderer && ShadowSequenceFrame(frame.load()))
        TraceShadowCamera(frame.load(),lightingEye,reinterpret_cast<const float*>(static_cast<unsigned char*>(context)+0x120),reinterpret_cast<const float*>(static_cast<unsigned char*>(context)+0x160));
    realCameraUpload(self,context);
}
thread_local bool sampleInner{};
thread_local bool continuousMain{};
IDXGISwapChain* gameSwapchain{}; // Diagnostic run owns one swapchain until process exit.
void Screenshot(IDXGISwapChain*,unsigned long long);
bool ScreenshotTexture(ID3D11Texture2D*,unsigned long long,bool alpha=false);
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
bool TakeCaptureRequest(uint64_t f,bool cockpit) {
    if(!RequestedCapturesEnabled() || requestedCaptureCount>=4 || f%60) return false;
    if(TestMessageTyping())return false;
    std::error_code error;
    const auto request=Output()/"capture.request";
    if(!std::filesystem::exists(request,error))return false;
    // A mode-specific request survives replying in another window and the
    // game's automatic pause. Empty requests retain the original behavior.
    std::ifstream input(request);std::string mode;input>>mode;input.close();
    if((mode=="cockpit" && (!cockpit || GamePaused())) ||
       (mode=="screen" && (cockpit || GamePaused())))return false;
    static uint64_t warning{};
    if(TestMessagesEnabled()) {
        if(!TestMessageCurrent(warning))
            warning=TestMessage(L"Capture starting",L"Hold still and face forward. A short stutter is normal. Wait for the Capture saved message.",30);
        if(!TestMessageVisibleFor(warning,2000))return false;
    }
    if(!std::filesystem::remove(request,error)) return false;
    warning=0;
    requestedCaptureFrame=f; ++requestedCaptureCount;
    Log("requested render capture=%u frame=%llu; capture stalls excluded from performance acceptance",requestedCaptureCount,f);
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
    // Scaled UI image variant used by trailer popup backing panels. Keep this
    // extension scoped to menus; racing HUD classification stays unchanged.
    case 0x2444a05c829f7ffbull: return classifiedCamera==SceneCamera::Frontend;
    default: return false;
    }
}
bool CaptureHudDraw(ID3D11DeviceContext* context,const std::function<void()>& draw) {
    if((!hudProbeDrawing && !hudEye) || !HudShader(context)) return false;
    const bool captured=hudCapture.Draw(context,draw,hudEye!=2);
    return hudEye && captured; // route HUD to the quad instead of either world image
}
uint64_t xrTickFrame=~uint64_t{};
XrPosef headsetReference{{0,0,0,1},{0,0,0}};
XrPosef photoReference{{0,0,0,1},{0,0,0}};
bool photoReferenceReady{};
bool recenterRequested=true;
bool headsetCentered=false;
bool initialCockpitCentered=false;
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
float TrailerTrackingScale(SceneCamera camera) {
    // Internal comparison; the original 1:1 scale has headset acceptance.
    // Scale BOTH tracked movement and eye spacing; never change projection or
    // the player's cockpit world-scale setting. Ordinary launches retain 1:1.
    static const float scale=GraphicsScale(L"DIRT2VR_TRAILER_TRACKING_SCALE",1.f,.25f,1.f);
    return camera==SceneCamera::Frontend ? scale : 1.f;
}
float TrailerSourceScale() {
    static const float worldScale=GraphicsScale(L"DIRT2VR_WORLD_SCALE",1.f,.25f,4.f);
    return TrailerTrackingScale(SceneCamera::Frontend)*(HeadsetEnabled()?worldScale:1.f);
}
bool EnsureGameXr() {
    static bool attempted=false;
    if(!attempted && gameSwapchain) {
        attempted=true; gameXr=new GameXr;
        ComPtr<ID3D11Device> device; gameSwapchain->GetDevice(IID_PPV_ARGS(&device));
        const auto scale=GraphicsScale(L"DIRT2VR_HEADSET_SCALE",1.f,.25f,1.f);
        const auto fov=GraphicsScale(L"DIRT2VR_FOV_SCALE",1.f,.7f,1.f);
        Log("VR graphics headset_scale=%.2f fov_scale=%.2f",scale,fov);
        ComPtr<ID3D11Texture2D> back;
        D3D11_TEXTURE2D_DESC source{};
        if(SUCCEEDED(gameSwapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) back->GetDesc(&source);
        if(!gameXr->Initialize(device.Get(),scale,fov,source.Width ? source.Width : 1600,source.Height ? source.Height : 1200)) Log("OpenXR game initialization failed; desktop fallback");
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
bool CockpitShadowMode() {
    if(!HeadsetEnabled())return ContinuousReplayEnabled(); // isolated stereo diagnostics
    PollHeadsetKeys();
    return gameXr && gameXr->Active() && gameXr->Visible() && !ScreenMode() && !GamePaused();
}
void PrepareHeadsetViews(const std::array<XrView,2>& views,bool cockpit=false) {
    if(!PhotoModeActive())photoReferenceReady=false;
    if(cockpit && !initialCockpitCentered && gameXr->CanRecenter()) {
        // The loading screen can appear while the player is still putting on
        // the headset. Establish the driving origin once cockpit play begins.
        initialCockpitCentered=true; recenterRequested=true;
        Log("OpenXR initial cockpit recenter frame=%llu",frame.load());
    }
    if(!headsetCentered && !gameXr->CanRecenter()) {
        // Keep startup/loading imagery near the provisional pose, but leave the
        // initial recenter pending until the runtime is focused and tracking.
        headsetReference=RecenterPose(CenterPose(views),headsetReference);
        SetTestMessageReference(headsetReference);
        return;
    }
    if(recenterRequested && gameXr->CanRecenter()) {
        headsetReference=RecenterPose(CenterPose(views),headsetReference); recenterRequested=false; headsetCentered=true;
        SetTestMessageReference(headsetReference);
        const auto& p=headsetReference.position;
        Log("OpenXR recentered position+yaw frame=%llu position=(%.4f,%.4f,%.4f) tracked=1 focused=1",frame.load(),p.x,p.y,p.z);
    }
}
void HeadsetScreen() {
    SeatInactive();
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
    if(requestedCaptureFrame==f) {
        const bool saved=ScreenshotTexture(back.Get(),930000+requestedCaptureCount);
        TestMessage(saved?L"Capture saved":L"Capture failed",saved?L"Flat-screen capture finished. Stay here for the next instruction.":L"The image was not saved. Wait here while I check the log.",30);
        Log("requested flat capture complete frame=%llu image=%u",f,930000+requestedCaptureCount);
        requestedCaptureFrame=~uint64_t{};
    }
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

// Record transitions without GPU readbacks or per-draw inspection. A normal
// 120-frame sample cannot explain a single skipped reflection or plane change.
// Never dereference a renderer retained from an earlier frame: it may be gone.
void TraceWaterReflection(const char* status,const unsigned char* renderer,uint64_t count=0) {
    if(!LoggingEnabled() || lightingEye<1 || lightingEye>2) return;
    struct State {
        const char* status{};
        const void* renderer{};
        unsigned nativePasses{};
        bool multipleRenderers{};
        bool planeValid{};
        std::array<float,4> plane{};
    };
    static std::array<State,2> previous{};
    static unsigned transitions{};
    if(transitions>=1024) return;
    const bool current=waterRendererFrame==frame.load();
    State state{status,renderer,current?waterNativePasses:0,current && waterMultipleRenderers,false,{}};
    std::array<float,8> p{}; // Native point (+0xd20) and normal (+0xd30).
    if(renderer) {
        memcpy(p.data(),renderer+0xd20,sizeof(p));
        const float length=std::sqrt(p[4]*p[4]+p[5]*p[5]+p[6]*p[6]);
        if(std::isfinite(length) && length>0) {
            for(unsigned i=0;i<3;++i) state.plane[i]=p[i+4]/length;
            state.plane[3]=-(p[0]*state.plane[0]+p[1]*state.plane[1]+p[2]*state.plane[2]);
            state.planeValid=std::all_of(state.plane.begin(),state.plane.end(),[](float v){return std::isfinite(v);});
        }
    }
    // A different point on the same plane is not a reflection transition.
    // Ignore sub-millimetre numerical drift so normal driving does not spam logs.
    auto& last=previous[lightingEye-1];
    if(last.status && !strcmp(last.status,state.status) && last.renderer==state.renderer &&
       last.nativePasses==state.nativePasses && last.multipleRenderers==state.multipleRenderers &&
       last.planeValid==state.planeValid && (!state.planeValid ||
       std::equal(last.plane.begin(),last.plane.end(),state.plane.begin(),[](float a,float b){return std::abs(a-b)<0.001f;}))) return;
    last=state;
    Log("water reflection transition frame=%llu eye=%u status=%s renderer=%p native_passes=%u multiple_renderers=%d renderer_frame=%llu lists_frame=%llu draws=%llu plane_valid=%d point=%.6f,%.6f,%.6f,%.6f normal=%.6f,%.6f,%.6f,%.6f",
        frame.load(),lightingEye,status,renderer,state.nativePasses,state.multipleRenderers,
        waterRendererFrame,waterListsFrame,count,state.planeValid,p[0],p[1],p[2],p[3],p[4],p[5],p[6],p[7]);
    if(++transitions==1024) Log("water reflection transition limit reached; start a fresh session for further investigation");
}

void RenderEyeReflection(void* mainCamera) {
    if(!WaterReflectionsEnabled()) return;
    if(!waterPrepareTail) { TraceWaterReflection("hook-unavailable",nullptr); return; }
    if(!waterRenderer || waterRendererFrame!=frame.load()) { TraceWaterReflection("no-current-renderer",nullptr); return; }
    if(!waterCameraA || waterListsFrame!=frame.load()) { TraceWaterReflection("no-current-lists",nullptr); return; }
    auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    auto renderer=static_cast<unsigned char*>(waterRenderer);
    const unsigned char prepare[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xb4,0x01,0,0};
    const unsigned char render[]={0x53,0x56,0x8b,0xf1,0x33,0xdb};
    if(*reinterpret_cast<void**>(renderer)!=base+0xf262f8 ||
       memcmp(base+0x2e22d0,prepare,sizeof(prepare)) || memcmp(base+0x2d1d40,render,sizeof(render))) {
        TraceWaterReflection("guard-rejected",nullptr); return;
    }
    if(*reinterpret_cast<void**>(renderer+0x940)!=mainCamera) { TraceWaterReflection("source-camera-mismatch",renderer); return; }
    if(*reinterpret_cast<void**>(renderer+0x95c)) { TraceWaterReflection("preparation-pending",renderer); return; }
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
    TraceWaterReflection("rendered",renderer,draws.load()-before);
    if(frame.load()%120==0) Log("water per-eye reflection frame=%llu draws=%llu pending=%p",frame.load(),draws.load()-before,*reinterpret_cast<void**>(renderer+0x95c));
}

bool HeadsetScene(void* self,void* lists,void* cameraA,void* cameraB,void* context,void* scene,void* flags) {
    const auto f=frame.load();
    PollHeadsetKeys();
    // Reflection/repeated subpasses do not own the published view. Animation
    // workers must retain the previous cockpit state during frame preparation.
    if(!continuousMain || xrTickFrame==f) return false;
    SteeringCockpitFrame steeringFrame;
    if((ScreenMode() && !PhotoModeActive()) || f<300 || !gameSwapchain || !cameraHooksReady) return false;
    auto renderer=static_cast<unsigned char*>(self);
    if(cameraA!=renderer+0x5e0 || cameraB!=renderer+0x650) return false;
    const auto camera=RenderSceneCamera(self,static_cast<const float*>(cameraA),static_cast<const float*>(cameraB),f);
    const bool cockpit=camera==SceneCamera::Cockpit;
    static int previousCandidate=-1;
    if(previousCandidate!=static_cast<int>(camera)) {
        Log("OpenXR camera candidate=%s frame=%llu near=%f/%f trailer_tracking_scale=%.3f",cockpit?"cockpit":camera==SceneCamera::External?"external":camera==SceneCamera::ReplayChase?"replay-chase":camera==SceneCamera::Frontend?"trailer":camera==SceneCamera::Photo?"photo":"screen",f,
            static_cast<const float*>(cameraA)[21],static_cast<const float*>(cameraB)[21],TrailerTrackingScale(camera));
        previousCandidate=static_cast<int>(camera);
    }
    // A paused cockpit retains its near plane. Render the original complete
    // frame (including modal dialogs) on the screen instead of replaying it.
    // Preserve the requested mode so resuming returns to cockpit VR.
    if(!StereoCameraAllowed(camera,ExtendedViewsEnabled(),GamePaused(),lightingHooksReady && pauseHookReady,ReplayStereoEnabled(),TrailerMenusEnabled())) return false;
    if(!cockpit) SeatInactive();
    if(!preparedLights.Snapshot(f,context,eyeLights)) {
        Log("lighting replay capacity exceeded; using virtual screen frame=%llu",f);
        return false;
    }
    if(!EnsureGameXr()) return false;
    const bool requestedCapture=requestedCaptureFrame==f;
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
    ScopedTrailerBackground background(camera==SceneCamera::Frontend,self);
    groundCoverPair.Begin();
    XrFrames::Overlay hud;
    const bool trailer=camera==SceneCamera::Frontend;
    const bool photo=camera==SceneCamera::Photo;
    const bool captureHud=!photo && hudCapture.Begin(back.Get(),f,trailer?0:HiddenHudElements(),trailer);
    static const float hudDistance=GraphicsScale(L"DIRT2VR_HUD_DISTANCE",1.f,1.f,20.f);
    const float panelDistance=trailer?2.f:hudDistance;
    // Preserve the angular size of the existing 4 m wide, 4 m distant panel.
    hud.size={panelDistance,panelDistance/hudCapture.Aspect()};
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
            auto relative=RelativePose(photo?photoReference:headsetReference,view.pose);
            if(cockpit) relative=SeatEyePose(relative);
            ScopedEyePose pose(cameraA,cameraB,relative,scale*TrailerTrackingScale(camera));
            ScopedSceneDepth depthRange(static_cast<unsigned char*>(self)+0x40);
            shadowRays={}; meterWindow={};
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
        if(photo) {
            if(!photoReferenceReady || (recenterRequested && gameXr->CanRecenter())) {
                photoReference=RecenterPose(CenterPose(views),photoReference);
                if(gameXr->CanRecenter()) {
                    photoReferenceReady=true;recenterRequested=false;
                    Log("OpenXR photo recentered frame=%llu tracked=1 focused=1",f);
                }
            }
        } else PrepareHeadsetViews(views,cockpit);
        if(trailer) {
            trailerTrackedPose=RelativePose(headsetReference,CenterPose(views));
            trailerTrackedFrame=f;
        }
        if(cockpit) SeatPrepare(views);
        SteeringCockpitView(cockpit && gameXr->Visible());
        static const bool follow=GraphicsScale(L"DIRT2VR_HUD_FOLLOW",0.f,0.f,1.f)==1.f;
        hud.pose=ScreenPose(follow && !trailer ? CenterPose(views) : headsetReference,panelDistance);
        if(!follow && cockpit) hud.pose=SeatHudPose(hud.pose,headsetReference);
    },nullptr,captureHud ? &hud : nullptr,cockpit ? SeatOverlay() : nullptr);
    steeringFrame.Complete(cockpit && submitted && gameXr->Visible());
    if(!submitted) SeatInactive();
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
    FlushWaterSurfaceTrace(f);
    scenePass=0;
    if(requestedCapture && submitted && eyes.Ready(f)) {
        const unsigned id=920000+requestedCaptureCount*2;
        const bool leftSaved=ScreenshotTexture(eyes.Texture(0),id);
        const bool rightSaved=ScreenshotTexture(eyes.Texture(1),id+1);
        TestMessage(leftSaved && rightSaved?L"Capture saved":L"Capture failed",leftSaved && rightSaved?L"Both eye images are saved. Stay here for the next instruction.":L"An eye image was not saved. Wait here while I check the log.",30);
        Log("requested stereo capture complete frame=%llu images=%u/%u",f,id,id+1);
    } else if(requestedCapture) {
        TestMessage(L"Capture interrupted",L"The headset frame was not ready. Wait here while I check it.",30);
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
    static const bool shadowProbe=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_SHADOW_PROBE",value,8)==1 && value[0]==L'1'; }();
    // Keep the first paired shadow comparison on the existing detailed-capture
    // frame. This desktop diagnostic never changes the headset render path.
    if((ExtendedViewsEnabled() || ReplayStereoEnabled() || TrailerMenusEnabled()) && (f<1200 || !StereoCameraAllowed(
        RenderSceneCamera(self,static_cast<float*>(cameraA),static_cast<float*>(cameraB),f),true,GamePaused(),true,ReplayStereoEnabled(),TrailerMenusEnabled())))return false;
    if(!continuousMain || disabled || f<(shadowProbe && !PipelineOption(L"DIRT2VR_PIPELINE_SETTLE") ? 3000u : 300u) || !gameSwapchain) return false;
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
    alignas(16) std::array<unsigned char,0x1a0> originalLightContext{};
    if(PipelineLights()) {
        if(!lightingHooksReady || !preparedLights.Snapshot(f,context,eyeLights)) {
            Log("PIPELINE light snapshot rejected frame=%llu",f); disabled=true; return false;
        }
        memcpy(originalLightContext.data(),context,originalLightContext.size());
        if(f==3000) Log("PIPELINE light snapshot frame=%llu calls=%zu",f,eyeLights.size());
    }
    const bool requestedTrailerCapture=TrailerMenusEnabled() && RequestedCapturesEnabled() && TakeCaptureRequest(f,true);
    if(classifiedCamera==SceneCamera::Frontend)memcpy(originalLightContext.data(),context,originalLightContext.size());
    ScopedTrailerBackground background(classifiedCamera==SceneCamera::Frontend,self);
    const bool sampled=f==3000;
    const bool pipelineHud=PipelineOption(L"DIRT2VR_PIPELINE_HUD") && hudCapture.Begin(back.Get(),f,0,
        RenderSceneCamera(self,static_cast<float*>(cameraA),static_cast<float*>(cameraB),f)==SceneCamera::Frontend);
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
            XrPosef pipelinePose{{0,0,0,1},{0,0,0}};
            if(PipelineOption(L"DIRT2VR_PIPELINE_POSE")) {
                // Fixed 30 degree yaw, -10 degree pitch and a seated lean.
                pipelinePose={{-.08418598f,.25783416f,.02255757f,.96225019f},{.10f,.05f,-.10f}};
            }
            if(PipelineOption(L"DIRT2VR_PIPELINE_STEREO"))pipelinePose=OffsetPose(pipelinePose,{eye==0?-.032f:.032f,0,0});
            ScopedEyePose pipelineEye(cameraA,cameraB,pipelinePose,TrailerTrackingScale(classifiedCamera));
            auto clip=reinterpret_cast<float*>(static_cast<unsigned char*>(self)+0x40);
            ScopedSceneDepth depthRange(clip);
            if(ShadowSequenceFrame(f))Log("shadow sequence clip before frame=%llu eye=%u near=%.8f far=%.8f",f,eye+1,clip[0],clip[1]);
            shadowRays={}; meterWindow={};
            eyeRenderer=self; projectionShift=(eye==0?-1.f:1.f)*shift;
            XrFovf pipelineFov{-.947f,.698f,.768f,-.960f};
            if(eye==1 && PipelineOption(L"DIRT2VR_PIPELINE_STEREO"))pipelineFov={-.698f,.947f,.768f,-.960f};
            if(PipelineTraceEnabled() && !PipelineOption(L"DIRT2VR_PIPELINE_FLAT"))eyeFov=&pipelineFov;
            if(WaterReflectionsEnabled() || PipelineLights()) lightingEye=eye+1;
            if(PipelineLights()) lightsRefreshed=false;
            RenderEyeReflection(cameraA);
            hudEye=pipelineHud ? eye+1 : 0;
            realInner(self,lists,cameraA,cameraB,context,scene,flags);
            hudEye=0;
            eyeRenderer=nullptr; eyeFov=nullptr; projectionShift=0; lightingEye=0;
            if(ShadowSequenceFrame(f))Log("shadow sequence clip after frame=%llu eye=%u near=%.8f far=%.8f",f,eye+1,clip[0],clip[1]);
        }
        eyeDraws[eye]=draws.load()-before;
        restored &= memcmp(originalA.data(),cameraA,112)==0 && memcmp(originalB.data(),cameraB,112)==0;
        capture=eyes.Capture(back.Get(),eye,f);
        if(FAILED(capture) || !restored) break;
    }
    if(pipelineHud) {
        hudCapture.End(true);
        if(sampled) Log("PIPELINE HUD extracted draws=%u",hudCapture.Draws());
    }
    if(PipelineLights()) {
        lightsRefreshed=false;
        RefreshLights(originalLightContext.data());
        eyeLights.clear();
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
    if(requestedTrailerCapture) {
        const unsigned id=920000+requestedCaptureCount*2;
        if(ready) {ScreenshotTexture(eyes.Texture(0),id);ScreenshotTexture(eyes.Texture(1),id+1);}
        if(pipelineHud)if(auto image=hudCapture.Current(f))ScreenshotTexture(image,930000+requestedCaptureCount,true);
        Log("requested desktop trailer capture frame=%llu ready=%d images=%u/%u ui_draws=%u",f,ready,id,id+1,pipelineHud?hudCapture.Draws():0);
        requestedCaptureFrame=~uint64_t{};
    }
    if(!ready || !matched) {
        if(WaterReflectionsEnabled()) for(unsigned eye=0;eye<2;++eye) {
            auto diagnostic=TraceFile(Output()/("water-probe-failed-eye-"+std::to_string(eye+1)+".txt"));
            for(const auto& draw:waterProbeDraws[eye]) diagnostic << draw << '\n';
        }
        Log("CONTINUOUS disabled after failed pair; inspect stereo-frames.csv"); disabled=true;
    }
    if(WaterReflectionsEnabled() && (disabled || f>=(PipelineTraceEnabled()?3120u:6000u))) { DXGI_SWAP_CHAIN_DESC desc{}; if(SUCCEEDED(gameSwapchain->GetDesc(&desc))) PostMessageW(desc.OutputWindow,WM_CLOSE,0,0); }
    // Diagnostic eyes intentionally omit captured UI. Restore a complete
    // native desktop image after saving them so manual navigation stays legible.
    if(pipelineHud)realInner(self,lists,cameraA,cameraB,context,scene,flags);
    return true;
}

void __fastcall Inner(void* self,void*,void* lists,void* cameraA,void* cameraB,void* context,void* scene,void* flags) {
    if(TrailerBackgroundEnabled() && trailerSource.renderer==self && trailerSource.frame==frame.load() &&
       *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(self)+0x960)==2 &&
       cameraA==static_cast<unsigned char*>(self)+0x5e0 && cameraB==static_cast<unsigned char*>(self)+0x650) {
        ScopedEyePose pose(cameraA,cameraB,trailerSource.pose,TrailerSourceScale());
        const auto previousRenderer=eyeRenderer;const auto previousFov=eyeFov;const bool previousSource=trailerSourceSetup;
        eyeRenderer=self;eyeFov=&trailerSourceFov;trailerSourceSetup=true;
        realInner(self,lists,cameraA,cameraB,context,scene,flags);
        eyeRenderer=previousRenderer;eyeFov=previousFov;trailerSourceSetup=previousSource;
        return;
    }
    if(WaterReflectionsEnabled() && self==waterRenderer && !waterEyeRenderer) {
        memcpy(waterLists.data(),lists,waterLists.size());
        waterListsFrame=frame.load();
        waterCameraA=cameraA; waterCameraB=cameraB; waterContext=context; waterScene=scene; waterFlags=flags;
    }
    if(HeadsetEnabled()) {
        if(RequestedCapturesEnabled() && continuousMain && requestedCaptureFrame!=frame.load()) {
            PollHeadsetKeys();
            const auto renderer=static_cast<unsigned char*>(self);
            if(cameraA==renderer+0x5e0 && cameraB==renderer+0x650) {
                const bool cockpit=!ScreenMode() && StereoCameraAllowed(
                    RenderSceneCamera(self,static_cast<const float*>(cameraA),static_cast<const float*>(cameraB),frame.load()),
                    ExtendedViewsEnabled(),GamePaused(),lightingHooksReady && pauseHookReady,ReplayStereoEnabled(),TrailerMenusEnabled());
                TakeCaptureRequest(frame.load(),cockpit);
            }
        }
        if(!HeadsetScene(self,lists,cameraA,cameraB,context,scene,flags)) {
            // F9 or an unavailable XR frame can select the flat view after the
            // wide source was already rendered. Map it back for that view too.
            ScopedTrailerBackground background(classifiedCamera==SceneCamera::Frontend,self);
            realInner(self,lists,cameraA,cameraB,context,scene,flags);
        }
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
    static const bool trailerProbe=[] { wchar_t value[8]{}; return GetEnvironmentVariableW(L"DIRT2VR_TRAILER_PROBE",value,8)==1 && value[0]==L'1'; }();
    if(mainView && trailerProbe && f%120==0) {
        const auto ca=static_cast<const float*>(a),cb=static_cast<const float*>(b);
        Log("trailer camera frame=%llu renderer=%p args=%p/%p/%p/%p/%p near=%.6f/%.6f fov=%.6f/%.6f p=%.4f,%.4f,%.4f paused=%d",f,self,a,b,c,d,e,ca[21],cb[21],ca[20],cb[20],ca[16],ca[17],ca[18],GamePaused());
        unsigned mode{},owner{},root{}; unsigned char state[0x30]{}; SIZE_T read{};
        const auto bytes=static_cast<const unsigned char*>(self);
        ReadProcessMemory(GetCurrentProcess(),bytes+0x960,&mode,4,&read);
        ReadProcessMemory(GetCurrentProcess(),bytes+0x5d4,&root,4,&read);
        ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(root+0x98),&owner,4,&read);
        ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(owner+0x80),state,sizeof(state),&read);
        Log("trailer state mode=%u owner=%x flags90=%02x,%02x,%02x,%02x,%02x,%02x,%02x,%02x a0=%02x,%02x,%02x,%02x",mode,owner,state[16],state[17],state[18],state[19],state[20],state[21],state[22],state[23],state[32],state[33],state[34],state[35]);
        if(f==1200 || f==3000) {
            std::array<char,0x200> snapshot{};
            if(ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(owner),snapshot.data(),snapshot.size(),&read)) {
                auto out=TraceFile(Output()/("trailer-owner-"+std::to_string(f)+".bin"),std::ios::binary);out.write(snapshot.data(),snapshot.size());
            }
        }
        static bool sampled=false;
        if(!sampled && f>300) { sampled=true; Stack("TrailerScene"); }
    }
    if(WaterReflectionsEnabled() && caller==0x2d1daf && !waterEyeRenderer) {
        if(waterRendererFrame!=f) { waterNativePasses=0; waterMultipleRenderers=false; }
        else if(waterRenderer!=self) waterMultipleRenderers=true;
        ++waterNativePasses;
        waterRenderer=self; waterRendererFrame=f;
    }
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
    if(mainView && !HeadsetEnabled() && PikesScope() && TakeCaptureRequest(f,false))scenePass=1;
    sampleInner=mainView && f==3000 && InnerReplayEnabled() && !ContinuousReplayEnabled();
    continuousMain=mainView && (ContinuousReplayEnabled() || HudProbe());
    const auto previousRenderer=classifiedRenderer;
    const auto previousCamera=classifiedCamera;
    classifiedRenderer=mainView ? self : nullptr;
    classifiedCamera=SceneCamera::Unknown;
    if(mainView)ObserveReplayRenderCamera(a,b);
    if(mainView && (ExtendedViewsEnabled() || ReplayStereoEnabled() || TrailerMenusEnabled() || PhotoModeRequested())) {
        classifiedCamera=(preparedCameraRenderer==self && preparedCameraFrame==f) ? preparedCameraKind :
            IdentifySceneCamera(static_cast<float*>(a),static_cast<float*>(b),f);
        if(classifiedCamera==SceneCamera::Frontend) {
            unsigned mode=~0u;SIZE_T read{};
            const bool display=ReadProcessMemory(GetCurrentProcess(),static_cast<unsigned char*>(self)+0x960,&mode,sizeof(mode),&read) &&
                read==sizeof(mode) && TrailerDisplayPass(mode);
            // The intermediate source is rendered once with wider coverage.
            // It must not consume the XR frame before the displayed composition.
            if(!display) {continuousMain=false;sampleInner=false;}
            if(trailerProbe && f%120==0)Log("trailer display pass frame=%llu mode=%u display=%d",f,mode,display);
        }
        if(f%120==0)Log("extended main frame=%llu kind=%u prepared=%d",f,unsigned(classifiedCamera),preparedCameraRenderer==self && preparedCameraFrame==f);
    }
    // The opt-in desktop tracer measures the native main scene separately from
    // VR's stereo pair. Timestamp queries use the same nonblocking ring.
    static GpuTimer desktopGpu;
    const bool desktopTiming=mainView && !HeadsetEnabled() && LoggingEnabled() && PikesScope() && gameSwapchain;
    bool desktopSample=false;
    if(desktopTiming) {
        static const bool initialized=[] {
            ComPtr<ID3D11Device> device;gameSwapchain->GetDevice(IID_PPV_ARGS(&device));
            const bool ready=desktopGpu.Initialize(device.Get());Log("Pikes desktop GPU timer initialized=%d",ready);return ready;
        }();
        if(initialized) {
            static auto csv=TraceFile(Output()/"desktop-gpu-frames.csv");
            static bool header=false;if(!header){csv << "frame,main_scene_gpu_ms,valid\n";header=true;}
            for(const auto& timing:desktopGpu.Poll())csv << timing.frame << ',' << timing.milliseconds << ',' << timing.valid << '\n';
            if(f%120==0)csv.flush();
            desktopSample=desktopGpu.Begin(f);
        }
    }
    realScene(self,a,b,c,d,e);
    if(desktopSample)desktopGpu.End();
    classifiedRenderer=previousRenderer;
    classifiedCamera=previousCamera;
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
    static const bool exposureProbe=[] {wchar_t v[8]{};return GetEnvironmentVariableW(L"DIRT2VR_EXPOSURE_PROBE",v,8)==1 && v[0]==L'1';}();
    if(exposureProbe && LoggingEnabled() && mode==D3D11_MAP_READ && sub==0) {
        const auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
        const auto caller=reinterpret_cast<uintptr_t>(_ReturnAddress());
        // Exclude our own staging reads; only observe the game reading back
        // its 1x1 luminance/adaptation result. Never change data or readiness.
        if(caller>=base+0x1000 && caller<base+0xe8b000) {
            ComPtr<ID3D11Texture2D> texture;
            if(SUCCEEDED(resource->QueryInterface(IID_PPV_ARGS(&texture)))) {
                D3D11_TEXTURE2D_DESC d{};texture->GetDesc(&d);
                if(d.Width==1 && d.Height==1 && d.Format==DXGI_FORMAT_R16G16B16A16_FLOAT) {
                    static std::unordered_set<uintptr_t> sites;
                    if(sites.insert(caller).second)Stack("ExposureReadback");
                    if(frame.load()%120==0 || requestedCaptureFrame==frame.load()) {
                        const auto data=SUCCEEDED(hr)?static_cast<const unsigned short*>(mapped->pData):nullptr;
                        Log("exposure readback frame=%llu eye=%u caller=0x%llx resource=%p hr=0x%x flags=%u half=%04x/%04x/%04x/%04x",
                            frame.load(),lightingEye,uint64_t(caller-base),resource,unsigned(hr),flags,
                            data?data[0]:0,data?data[1]:0,data?data[2]:0,data?data[3]:0);
                    }
                }
            }
        }
    }
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
    // Normal headset frames have scenePass==0. Observe known water bindings
    // before the detailed-capture gate, excluding the reflection subpass itself.
    if(lightingEye && !waterEyeRenderer)
        TraceWaterSurfaceDraw(context,frame.load(),lightingEye,kind,count,instances,start,base,firstInstance);
    if(Sample()) {
        static unsigned long long sampledFrame{};
        static unsigned samples{};
        if(sampledFrame!=frame.load()) { sampledFrame=frame.load(); samples=0; }
        if(samples<8) {
            ComPtr<ID3D11RenderTargetView> colour;
            ComPtr<ID3D11DepthStencilView> depth;
            context->OMGetRenderTargets(1,&colour,&depth);
            if(colour && depth) {
                ComPtr<ID3D11Resource> resource;
                ComPtr<ID3D11Texture2D> texture;
                depth->GetResource(&resource);
                if(SUCCEEDED(resource.As(&texture))) {
                    D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
                    D3D11_VIEWPORT viewport{}; UINT n=1; context->RSGetViewports(&n,&viewport);
                    Log("scene raster frame=%llu draw=%s depth=%ux%u msaa=%u viewport=%.0fx%.0f",sampledFrame,kind,desc.Width,desc.Height,desc.SampleDesc.Count,viewport.Width,viewport.Height);
                    ++samples;
                }
            }
        }
    }
    static const bool skipWater=[] { wchar_t value[16]{}; return GetEnvironmentVariableW(L"DIRT2VR_SKIP_WATER",value,16)>0 && wcscmp(value,L"1")==0; }();
    if(!scenePass && !skipWater && !ShadowSequenceFrame(frame.load()) && requestedCaptureFrame!=frame.load() && !(PipelineTraceEnabled() && frame.load()==3000)) return true;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11VertexShader> vs;
    context->PSGetShader(&ps,nullptr,nullptr); context->VSGetShader(&vs,nullptr,nullptr);
    uint64_t ph{},vh{};
    { std::lock_guard lock(shaderMutex); ph=shaderNames[ps.Get()]; vh=shaderNames[vs.Get()]; }
    TracePipelineDraw(context,frame.load(),lightingEye,waterEyeRenderer!=nullptr,kind,count,instances,vh,ph,false,requestedCaptureFrame==frame.load());
    if(ShadowSequenceFrame(frame.load()) && !waterEyeRenderer)
        TraceShadowInputs(context,ph,frame.load(),lightingEye,true);
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
    if((requestedCaptureFrame==frame.load() || (PipelineTraceEnabled() && frame.load()==3000)) && !waterEyeRenderer) {
        TraceShadowInputs(context,ph,frame.load(),lightingEye,true);
        TraceLightingStage(context,ph,frame.load(),lightingEye);
    }
    if(!scenePass) return true;
    if(waterProbeRecording && scenePass<=2 && waterProbeDraws[scenePass-1].size()<10000) {
        char line[160]{}; snprintf(line,sizeof(line),"%s count=%u instances=%u vs=%llx ps=%llx reflection=%d",kind,count,instances,vh,ph,waterEyeRenderer!=nullptr);
        waterProbeDraws[scenePass-1].emplace_back(line);
    }
    if(WaterReflectionsEnabled() && Sample() && frame.load()==3000) TraceWaterInputs(context,ph,frame.load(),scenePass);
    if(Sample()) TraceShadowInputs(context,ph,frame.load(),scenePass);
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
bool ScreenshotTexture(ID3D11Texture2D* back,unsigned long long number,bool alpha) {
    if(!LoggingEnabled()) return false;
    ComPtr<ID3D11Texture2D> source,staging;
    ComPtr<ID3D11Device> device; back->GetDevice(&device);
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
    if(desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB &&
       desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM) return false;
    source=back;
    if(desc.SampleDesc.Count>1) {
        desc.SampleDesc={1,0}; desc.BindFlags=0; desc.MiscFlags=0;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&source))) return false;
        context->ResolveSubresource(source.Get(),0,back,0,desc.Format);
    }
    desc.Usage=D3D11_USAGE_STAGING; desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    desc.BindFlags=0; desc.MiscFlags=0;
    if(FAILED(device->CreateTexture2D(&desc,nullptr,&staging))) return false;
    context->CopyResource(staging.Get(),source.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped))) return false;
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
    out.flush();
    const bool saved=bool(out);
    Log("screenshot frame=%llu %ux%u; capture stalls excluded from performance acceptance",number,desc.Width,desc.Height);
    return saved;
}

HRESULT STDMETHODCALLTYPE DesktopCapturePresent(IDXGISwapChain* swapchain,UINT interval,UINT flags) {
    if(flags & DXGI_PRESENT_TEST)return realPresent(swapchain,interval,flags);
    graphics::SampleVideoMemory(swapchain);
    PhotoModeFrame();
    const auto f=frame.fetch_add(1);
    if(TakeCaptureRequest(f,false)) {
        Screenshot(swapchain,f);
        requestedCaptureFrame=~uint64_t{};
    }
    return realPresent(swapchain,interval,flags);
}
HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* swapchain,UINT interval,UINT flags) {
    if(flags & DXGI_PRESENT_TEST) return realPresent(swapchain,interval,flags);
    graphics::SampleVideoMemory(swapchain);
    PhotoModeFrame();
    if(!HeadsetEnabled() && (requestedCaptureFrame==frame.load() || TakeCaptureRequest(frame.load(),false))) {
        Screenshot(swapchain,frame.load());requestedCaptureFrame=~uint64_t{};
    }
    if(HeadsetEnabled()) {
        static ResolutionReport resolution([] {
            static wchar_t name[128]{};
            if(GetEnvironmentVariableW(L"DIRT2VR_RESOLUTION_CHANNEL",name,128)>=128) name[0]=0;
            return name;
        }());
        if(resolution.Active()) {
            ComPtr<ID3D11Texture2D> back;
            if(SUCCEEDED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back)))) {
                D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
                const auto eye=gameXr ? gameXr->EyeDimensions() : std::array<uint32_t,4>{};
                resolution.Observe({desc.Width,desc.Height,eye[0],eye[1],eye[2],eye[3]},
                    LoggingEnabled() ? +[](const char* message) { Log("%s",message); } : nullptr);
            }
        }
    }
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
    const auto result=realPresent(swapchain,interval,flags);
    if(FAILED(result) && graphics::logger.load()) {
        ComPtr<ID3D11Device> device; swapchain->GetDevice(IID_PPV_ARGS(&device));
        graphics::Failed(result,device.Get(),"Present");
    }
    return result;
}
void STDMETHODCALLTYPE DrawIndexed(ID3D11DeviceContext* c,UINT n,UINT start,INT base) {
    if(Sample() && draws.load()<3) Stack("DrawIndexed"); if(!RecordDraw(c,"indexed",n,1,start,base)) return;
    if(CaptureHudDraw(c,[&] { realDrawIndexed(c,n,start,base); })) return;
    uint64_t captureShader{};
    if((requestedCaptureFrame==frame.load() || (PipelineTraceEnabled() && frame.load()==3000)) && !waterEyeRenderer) {
        ComPtr<ID3D11PixelShader> ps;c->PSGetShader(&ps,nullptr,nullptr);
        std::lock_guard lock(shaderMutex);const auto found=shaderNames.find(ps.Get());
        if(found!=shaderNames.end())captureShader=found->second;
    }
    ++draws;
    // Only the main headset eyes own this correction. Native desktop/screen
    // rendering and per-eye water reflections keep their original sky pass.
    if(lightingEye && !waterEyeRenderer) {
        ComPtr<ID3D11PixelShader> ps;c->PSGetShader(&ps,nullptr,nullptr);
        uint64_t vertex{},pixel{};
        {
            std::lock_guard lock(shaderMutex);
            const auto p=shaderNames.find(ps.Get());
            if(p!=shaderNames.end())pixel=p->second;
        }
        if(pixel==0xa0291237968fd075ull) {
            ComPtr<ID3D11VertexShader> vs;c->VSGetShader(&vs,nullptr,nullptr);
            {
                std::lock_guard lock(shaderMutex);const auto v=shaderNames.find(vs.Get());
                if(v!=shaderNames.end())vertex=v->second;
            }
            static SkySunPass sun;
            if(sun.Draw(c,vertex,pixel,[&] {
                TracePipelineDraw(c,frame.load(),lightingEye,false,"indexed",n,1,vertex,pixel,true,requestedCaptureFrame==frame.load());
                realDrawIndexed(c,n,start,base);
            }))return;
        }
    }
    realDrawIndexed(c,n,start,base);
    if(captureShader)TraceLightingStage(c,captureShader,frame.load(),lightingEye,true);
}
void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* c,UINT n,UINT start) {
    if(!RecordDraw(c,"draw",n,1,start) || CaptureHudDraw(c,[&] { realDraw(c,n,start); })) return;
    ++draws;
    uint64_t captureShader{};
    if((requestedCaptureFrame==frame.load() || (PipelineTraceEnabled() && frame.load()==3000)) && !waterEyeRenderer) {
        ComPtr<ID3D11PixelShader> ps;c->PSGetShader(&ps,nullptr,nullptr);
        std::lock_guard lock(shaderMutex);
        const auto found=shaderNames.find(ps.Get());
        if(found!=shaderNames.end())captureShader=found->second;
    }
    if(n==4 && (lightingEye || trailerNativeCamera) && !waterEyeRenderer && (ShadowsEnabled() || ExposureWindowEnabled() || trailerNativeCamera)) {
        ComPtr<ID3D11VertexShader> vs;ComPtr<ID3D11PixelShader> ps;
        c->VSGetShader(&vs,nullptr,nullptr);c->PSGetShader(&ps,nullptr,nullptr);
        uint64_t vertex{},pixel{};
        {std::lock_guard lock(shaderMutex);vertex=shaderNames[vs.Get()];pixel=shaderNames[ps.Get()];}
        static TrailerBackground trailerBackground;
        if(trailerNativeCamera && trailerBackground.Draw(c,vertex,pixel,trailerBackgroundRays,[&] {realDraw(c,n,start);})) {
            if(frame.load()%120==0)Log("Trailer background reprojected frame=%llu eye=%u",frame.load(),lightingEye);
            return;
        }
        static ExposureWindow exposureWindow;
        if(lightingEye && ExposureWindowEnabled() && exposureWindow.Draw(c,vertex,pixel,meterWindow,[&] {realDraw(c,n,start);})) {
            if(frame.load()%120==0)Log("Exposure window applied frame=%llu eye=%u region=%.4f,%.4f,%.4f,%.4f",frame.load(),lightingEye,meterWindow.bounds[0],meterWindow.bounds[1],meterWindow.bounds[2],meterWindow.bounds[3]);
            return;
        }
        static ShadowMaskPass shadowMask;
        if(lightingEye && ShadowsEnabled() && shadowMask.Draw(c,vertex,pixel,shadowRays,[&] {
            TraceShadowRays(frame.load(),lightingEye,pixel,shadowRays.values.data(),requestedCaptureFrame==frame.load());
            if(PipelineTraceEnabled() && frame.load()==3000) {
                ComPtr<ID3D11VertexShader> applied;c->VSGetShader(&applied,nullptr,nullptr);uint64_t actual{};
                {std::lock_guard lock(shaderMutex);actual=shaderNames[applied.Get()];}
                TracePipelineDraw(c,frame.load(),lightingEye,false,"draw",n,1,actual,pixel,true,requestedCaptureFrame==frame.load());
            }
            realDraw(c,n,start);
            if(captureShader)TraceLightingStage(c,captureShader,frame.load(),lightingEye,true);
        })) return;
    }
    realDraw(c,n,start);
    if(captureShader)
        TraceLightingStage(c,captureShader,frame.load(),lightingEye,true);
}
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
    auto hash=Shader(b,n,"ps");
    const bool water=TraceWaterShaderCreationEnabled(hash);
    LARGE_INTEGER begin{},end{},frequency{};
    if(water) { QueryPerformanceFrequency(&frequency);QueryPerformanceCounter(&begin); }
    auto hr=realPS(d,b,n,l,s);
    if(water) {
        QueryPerformanceCounter(&end);
        if(SUCCEEDED(hr) && s && *s) ObserveWaterShaderCreation(*s,hash,frame.load(),1000.0*(end.QuadPart-begin.QuadPart)/frequency.QuadPart);
    }
    if(SUCCEEDED(hr) && s && *s) { std::lock_guard lock(shaderMutex); shaderNames[*s]=hash; }
    return hr;
}
void STDMETHODCALLTYPE BindPixelShader(ID3D11DeviceContext* context,ID3D11PixelShader* shader,ID3D11ClassInstance* const* classes,UINT count) {
    realBindPixelShader(context,shader,classes,count);
    ObserveWaterShaderBinding(context,shader);
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
    if(!EnablePikesTerrain()) { Log("Pikes terrain initialization failed; stopping launch"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    if(LoggingEnabled()) graphics::logger.store(+[](const char* message) { Log("%s",message); });
    wchar_t direct[8]{};
    const bool directSession=GetEnvironmentVariableW(L"DIRT2VR_DIRECT_PRACTICE",direct,8)==1 && direct[0]==L'1';
    if(!EnableStartupHooks(HeadsetEnabled(),AutoCockpitEnabled(),directSession)) ExitProcess(ERROR_BAD_EXE_FORMAT);
    if(!EnableAnisotropicFiltering(device)) { Log("anisotropic filtering: initialization failed"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
    EnableDesktopShadowDistance();
    wchar_t desktop[8]{};
    const bool desktopControls=GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_CONTROLS",desktop,8)==1 && desktop[0]==L'1';
    if(desktopControls || (GetEnvironmentVariableW(L"DIRT2VR_DESKTOP_PRACTICE",desktop,8)==1 && desktop[0]==L'1')) {
        EnableDirectPractice();
        EnablePikesCockpit();
        if(!EnableF04Camera()) { Log("f04 desktop camera initialization failed"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
        Log("desktop session: optional camera/texture filtering; no VR hotkeys or OpenXR initialization");
        if(swapchain && (LoggingEnabled() || PhotoModeRequested() || (RequestedCapturesEnabled() && PikesScope())))
            Hook(swapchain,8,reinterpret_cast<void*>(DesktopCapturePresent),realPresent);
        return;
    }
    static bool initialized=[] { auto status=MH_Initialize(); return status==MH_OK||status==MH_ERROR_ALREADY_INITIALIZED; }();
    if(!initialized) { Log("MinHook initialization failed"); return; }
    EnableDirectPractice();
    Log("DX11 device=%p feature_level=0x%x context=%p swapchain=%p",device,device->GetFeatureLevel(),context,swapchain);
    Hook(device,12,reinterpret_cast<void*>(CreateVS),realVS);
    Hook(device,15,reinterpret_cast<void*>(CreatePS),realPS);
    ComPtr<ID3D11DeviceContext> owned;
    if(!context) { device->GetImmediateContext(&owned); context=owned.Get(); }
    if(InitializeWaterSurfaceTrace()) Hook(context,9,reinterpret_cast<void*>(BindPixelShader),realBindPixelShader);
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
            if(HeadsetEnabled())pauseHookReady=EnablePauseObserver();
            if((HeadsetEnabled() || PipelineOption(L"DIRT2VR_PIPELINE_WIDE")) && (WideVisibility() || ExtendedViewsEnabled() || ReplayStereoEnabled() || ShadowsEnabled()) && !realFrustumCopy) {
                const unsigned char copy[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x8b,0xc1,0x8b,0x4d,0x08};
                const unsigned char build[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x04,0x01,0,0};
                if(memcmp(base+0x2b7db0,copy,sizeof(copy))==0 && memcmp(base+0xd26c40,build,sizeof(build))==0) {
                    buildFrustum=reinterpret_cast<FrustumCopyFn>(base+0xd26c40);
                    auto status=MH_CreateHook(base+0x2b7db0,reinterpret_cast<void*>(FrustumCopy),reinterpret_cast<void**>(&realFrustumCopy));
                    if(status==MH_OK) status=EnableRecordedHook(base+0x2b7db0);
                    Log("visibility frustum hook status=%s",MH_StatusToString(status));
                } else Log("visibility frustum hook rejected instruction guard");
            }
            if(HeadsetEnabled() || PipelineLights()) {
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
            if(WaterReflectionsEnabled())EnableWaterRippleInitialization();
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
            if(ShadowsEnabled() && !realShadowInteriorDefaults) {
                const auto operand=reinterpret_cast<uintptr_t>(base+0xf1fe00);
                const unsigned char prefix[]={0xf3,0x0f,0x10,0x05};
                if(!memcmp(base+0x2865d0,prefix,sizeof(prefix)) &&
                   *reinterpret_cast<const uintptr_t*>(base+0x2865d4)==operand) {
                    auto status=MH_CreateHook(base+0x2865d0,reinterpret_cast<void*>(ShadowInteriorDefaults),reinterpret_cast<void**>(&realShadowInteriorDefaults));
                    if(status==MH_OK)status=EnableRecordedHook(base+0x2865d0);
                    Log("cockpit shadow coverage observer status=%s",MH_StatusToString(status));
                } else Log("cockpit shadow coverage observer rejected instruction guard");
            }
            if(ShadowsEnabled() && !shadowCasterFovReady) {
                const unsigned char assign[]={0xd9,0x40,0x50,0xd9,0x9e,0xf0,0,0,0};
                if(!memcmp(base+0x2abc3f,assign,sizeof(assign))) {
                    shadowCasterFovContinue=base+0x2abc48;
                    void* unused{};
                    auto status=MH_CreateHook(base+0x2abc3f,reinterpret_cast<void*>(ShadowCasterFov),&unused);
                    if(status==MH_OK)status=EnableRecordedHook(base+0x2abc3f);
                    shadowCasterFovReady=status==MH_OK;
                    Log("cockpit road-shadow selection hook=%s; detailed range=%.0fm",MH_StatusToString(status),ShadowDetailDistance());
                } else Log("cockpit road-shadow selection rejected instruction guard");
            }
            if(LoggingEnabled() && !realShadowCasters && !realShadowFade) {
                wchar_t probe[8]{};
                const unsigned char collect[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x34};
                const unsigned char fade[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x30};
                const unsigned char item[]={0x8b,0x44,0x24,0x0c,0x53,0x55};
                if(GetEnvironmentVariableW(L"DIRT2VR_SHADOW_CASTER_PROBE",probe,8)==1 && probe[0]==L'1' &&
                   !memcmp(base+0x297170,collect,sizeof(collect)) && !memcmp(base+0x970900,fade,sizeof(fade)) && !memcmp(base+0x97be00,item,sizeof(item))) {
                    auto status=MH_CreateHook(base+0x297170,reinterpret_cast<void*>(ShadowCasters),reinterpret_cast<void**>(&realShadowCasters));
                    if(status==MH_OK)status=MH_CreateHook(base+0x970900,reinterpret_cast<void*>(ShadowFade),reinterpret_cast<void**>(&realShadowFade));
                    if(status==MH_OK)status=MH_CreateHook(base+0x97be00,reinterpret_cast<void*>(ShadowItem),reinterpret_cast<void**>(&realShadowItem));
                    if(status==MH_OK)status=EnableRecordedHook(base+0x297170);
                    if(status==MH_OK)status=EnableRecordedHook(base+0x970900);
                    if(status==MH_OK)status=EnableRecordedHook(base+0x97be00);
                    Log("shadow caster observation hooks=%s",MH_StatusToString(status));
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
