#define DIRECTINPUT_VERSION 0x0800
#include "photo_mode.h"
#include "photo_camera.h"
#include "scene_camera.h"
#include "game_pause.h"
#include "chase_cursor.h"
#include "seat_adjustment.h"
#include "direct_menus.h"
#include "common.h"
#include <dinput.h>
#include <xinput.h>
#include <atomic>
#include <cstring>
#include <mutex>

namespace vr {
namespace {
std::mutex mutex;
std::atomic<void*> selected{};
std::atomic<bool> active{},blocking{},focusLost{};
void* menuState{};void* manager{};
unsigned begin{},end{},previous{},external{};
bool finished{},armed{};
ULONGLONG tick{};
PhotoCamera camera;
IDirectInput8W* input{};
IDirectInputDevice8W* mouse{};
HWND mouseWindow{};
using XboxFn=DWORD(WINAPI*)(DWORD,XINPUT_STATE*);
XboxFn xbox{};
unsigned slot=4;
alignas(16) std::array<float,28> exported{};
ULONGLONG exportTick{};
double frozenTime{};
ULONGLONG sampleTick{};
template<class T> bool Read(const void* object,unsigned offset,T& value) {
    SIZE_T bytes{};
    return object && ReadProcessMemory(GetCurrentProcess(),static_cast<const char*>(object)+offset,&value,sizeof(value),&bytes) && bytes==sizeof(value);
}
unsigned char* Base(){return reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));}
HWND Focus() {
    const auto window=GetForegroundWindow();DWORD pid{};GetWindowThreadProcessId(window,&pid);
    return pid==GetCurrentProcessId() && !IsIconic(window)?window:nullptr;
}
bool NativePaused(void* state) {
    void** registry{};void* wrapper{};void* core{};unsigned index{};int value{};
    return Read(state,0x28,registry) && Read(Base(),0x107dd30,index) && index<4096 &&
        Read(registry,index*sizeof(void*),wrapper) && Read(wrapper,4,core) && Read(core,0x9a58,value) && value==9;
}
bool RaceClock(void* state,double& time) {
    void** registry{};unsigned index{};void* game{};void* root{};
    return Read(state,0x28,registry) && Read(Base(),0x107f3d8,index) && index<4096 &&
        Read(registry,index*sizeof(void*),game) && Read(game,0x229c,root) &&
        Read(root,0x1c8,time) && std::isfinite(time) && time>=0 && time<1e8;
}
bool SameManager() {
    unsigned first{},last{};
    return manager && selected.load()==manager && Read(manager,4,first) && Read(manager,8,last) &&
        first==begin && last==end;
}
bool SelectedSeed(void* owner,std::array<float,28>& record) {
    unsigned current{};void* object{};void* table{};char name[48]{};
    std::array<float,14> pose{};
    const auto entry=reinterpret_cast<const unsigned char*>(begin)+external*0x270;
    unsigned char cached{},overrideView{},lookback{},allViews{},blended{};
    const bool readable=SameManager() && Read(manager,0x128,current) && current==external &&
        Read(entry,0,object) && Read(object,0,table) && table==Base()+0xf51bb8 &&
        Read(object,0x80,name) && std::memchr(name,0,sizeof(name)) && !std::strcmp(name,"chase_close") &&
        Read(entry,0x10,pose) && Read(owner,0x11,cached) && Read(owner,0x12,overrideView) &&
        Read(manager,0x121,lookback) && Read(manager,0x122,allViews) && Read(manager,0x51c,blended);
    Log("photo seed before: valid=%d owner=%p entry=%p camera=%p index=%u bridge_cached=%u bridge_override=%u lookback=%u all_views=%u blended=%u q=%.6f,%.6f,%.6f,%.6f p=%.6f,%.6f,%.6f fov=%.6f near=%.6f export_p=%.6f,%.6f,%.6f export_fov=%.6f export_near=%.6f",
        readable,owner,entry,object,current,cached,overrideView,lookback,allViews,blended,
        pose[0],pose[1],pose[2],pose[3],pose[4],pose[5],pose[6],pose[12],pose[13],record[16],record[17],record[18],record[20],record[21]);
    double time{};
    if(!readable || !NativePaused(menuState) || !RaceClock(menuState,time) || std::abs(time-frozenTime)>.000001 ||
       !SeedPhotoCamera(record,pose))return false;
    Log("photo seed after: source=selected-entry entry=%p scheduler=9 race_time=%.9f p=%.6f,%.6f,%.6f fov=%.6f near=%.6f bridge_cached=%u bridge_override=%u blended=%u",
        entry,time,record[16],record[17],record[18],record[20],record[21],cached,overrideView,blended);
    return true;
}
bool Key(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
float Stick(SHORT value) {
    const float v=std::clamp(float(value)/32767.f,-1.f,1.f);
    return std::abs(v)<=.2f?0.f:std::copysign((std::abs(v)-.2f)/.8f,v);
}
PhotoInput Poll(float dt,bool movement) {
    PhotoInput result;
    result.exit=Key(VK_ESCAPE);
    if(movement) {
        result.right=float(Key('D'))-float(Key('A'));
        result.forward=float(Key('W'))-float(Key('S'));
        result.up=float(Key('E'))-float(Key('Q'));
        result.fast=Key(VK_SHIFT);
    }
    const auto window=Focus();
    if(movement && mouseWindow!=window) {
        if(mouse){mouse->Unacquire();mouse->Release();mouse=nullptr;}
        mouseWindow=window;
        if(!input)DirectInput8Create(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8W,reinterpret_cast<void**>(&input),nullptr);
        if(input && window && SUCCEEDED(input->CreateDevice(GUID_SysMouse,&mouse,nullptr))) {
            if(FAILED(mouse->SetDataFormat(&c_dfDIMouse2)) ||
               FAILED(mouse->SetCooperativeLevel(window,DISCL_FOREGROUND|DISCL_NONEXCLUSIVE))) {
                mouse->Release();mouse=nullptr;
            }
        }
    }
    if(movement && mouse) {
        DIMOUSESTATE2 state{};
        if(SUCCEEDED(mouse->GetDeviceState(sizeof(state),&state))) {
            result.yaw=float(state.lX)*.003f;result.pitch=-float(state.lY)*.003f;
        } else mouse->Acquire(); // Discard reacquisition deltas.
    }
    if(!xbox) {
        auto module=LoadLibraryExW(L"xinput1_4.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
        if(module)xbox=reinterpret_cast<XboxFn>(GetProcAddress(module,"XInputGetState"));
    }
    if(xbox)for(unsigned i=0;i<4;++i) {
        XINPUT_STATE value{};
        if(xbox(i,&value)!=ERROR_SUCCESS){if(slot==i)slot=4;continue;}
        const auto& pad=value.Gamepad;
        const bool used=pad.wButtons || Stick(pad.sThumbLX) || Stick(pad.sThumbLY) ||
            Stick(pad.sThumbRX) || Stick(pad.sThumbRY) || pad.bLeftTrigger>30 || pad.bRightTrigger>30;
        if(slot==4 && used)slot=i;
        if(slot!=i)continue;
        result.exit|=(pad.wButtons&(XINPUT_GAMEPAD_B|XINPUT_GAMEPAD_START))!=0;
        if(movement) {
            result.right+=Stick(pad.sThumbLX);result.forward+=Stick(pad.sThumbLY);
            result.yaw+=Stick(pad.sThumbRX)*2.5f*dt;result.pitch+=Stick(pad.sThumbRY)*1.5f*dt;
            result.up+=(std::max(0,int(pad.bRightTrigger)-30)-std::max(0,int(pad.bLeftTrigger)-30))/225.f;
            result.fast|=(pad.wButtons&XINPUT_GAMEPAD_RIGHT_SHOULDER)!=0;
        }
    }
    return result;
}
void End(const char* reason) {
    if(active.exchange(false)) {
        PhotoWorldVisible(false);
        unsigned current{};
        const bool restore=SameManager() && Read(manager,0x128,current) && current==external;
        if(restore) {
            using Set=void(__thiscall*)(void*,int);
            reinterpret_cast<Set>(Base()+0x72f840)(manager,int(previous));
        }
        Log("photo mode: exit reason=%s native_pause=%d camera_restored=%d",reason,GamePaused(),restore);
    }
    camera.Reset();exportTick=0;finished=true;armed=false;
    UpdatePhotoCursor(Focus(),false);
}
bool Begin(void* state) {
    manager=selected.load();unsigned current{};
    if(!GamePaused() || !NativePaused(state) || !RaceClock(state,frozenTime) || !Read(manager,4,begin) || !Read(manager,8,end) ||
       !Read(manager,0x128,current) || !begin || end<begin || (end-begin)%0x270 ||
       (end-begin)/0x270>64 || current>=(end-begin)/0x270)return false;
    using Find=int(__thiscall*)(void*,const char*);
    const auto find=reinterpret_cast<Find>(Base()+0x724f90);
    const int index=find(manager,"chase_close");
    if(index<0 || unsigned(index)>=(end-begin)/0x270)return false;
    previous=current;external=unsigned(index);
    using Set=void(__thiscall*)(void*,int);
    reinterpret_cast<Set>(Base()+0x72f840)(manager,index);
    if(!PhotoWorldVisible(true)) {
        reinterpret_cast<Set>(Base()+0x72f840)(manager,int(previous));return false;
    }
    camera.Reset();exportTick=0;sampleTick=0;tick=GetTickCount64();armed=false;slot=4;
    focusLost=false;active=true;blocking=true;
    Log("photo mode: enter state=%p camera=%u->%u scheduler=9 race_time=%.9f",state,previous,external,frozenTime);
    return true;
}
}
bool PhotoModeRequested() {
    static const bool requested=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_PHOTO_MODE",value,8)==1 && value[0]==L'1';}();
    return requested;
}
bool PhotoModeActive(){return active.load();}
bool PhotoModeBlocksInput(){return blocking.load();}
void PhotoModeFocusLost(){if(active.load())focusLost=true;}
void PhotoSelectCamera(void* value){selected=value;}
bool PhotoModeMenu(void* state,void* result) {
    if(!PhotoModeRequested() || std::memcmp(static_cast<const char*>(state)+4,"d2vr_photo_mode",sizeof("d2vr_photo_mode")))return false;
    std::lock_guard lock(mutex);
    if(menuState!=state) {
        if(active)End("state-replaced");
        menuState=state;finished=false;
        if(!Begin(state)){finished=true;Log("photo mode: entry refused; native pause/camera unavailable");}
    }
    if(active && focusLost.exchange(false))End("focus-lost");
    if(active && !NativePaused(state))End("pause-lost");
    using Assign=void(__thiscall*)(void*,const char*);
    reinterpret_cast<Assign>(Base()+0x6d4830)(result,finished?"disabled":"");
    if(finished)menuState=nullptr;
    return true;
}
void PhotoModePauseChanged(bool paused) {
    if(paused || !active.load())return;
    std::lock_guard lock(mutex);End("native-unpause");
}
void PhotoModeFrame() {
    if(!PhotoModeRequested())return;
    std::lock_guard lock(mutex);
    if(!active) {
        if(blocking && !Poll(0,false).exit)blocking=false;
        return;
    }
    const auto now=GetTickCount64();const auto window=Focus();
    if(focusLost.exchange(false) || !window) {End("focus-lost");return;}
    if(!SameManager() || !NativePaused(menuState)) {End("scene-changed");return;}
    double time{};
    if(!RaceClock(menuState,time) || std::abs(time-frozenTime)>.000001) {End("race-time-changed");return;}
    const float dt=float(now-tick)*.001f;tick=now;
    auto movement=Poll(dt,true);
    if(!armed) {if(!movement.exit)armed=true;movement={};}
    if(movement.exit){End("back");return;}
    camera.Move(movement,dt);
    UpdatePhotoCursor(window,true);
}
float* PhotoModeExport(void* owner,float* native) {
    if(!active.load())return nullptr;
    std::lock_guard lock(mutex);
    void* ownerManager{};
    if(!active || !Read(owner,0x24,ownerManager) || ownerManager!=manager || !SameManager())return nullptr;
    std::array<float,28> source{};
    if(!Read(native,0,source))return nullptr;
    if(!camera.Ready()) {
        if(!SelectedSeed(owner,source)){End("selected-camera-unavailable");return nullptr;}
        Log("photo native basis: up=%.6f,%.6f,%.6f left=%.6f,%.6f,%.6f forward=%.6f,%.6f,%.6f fov=%.6f near=%.6f far=%.6f",
            source[4],source[5],source[6],source[8],source[9],source[10],source[12],source[13],source[14],source[20],source[21],source[22]);
        if(!camera.Begin(source.data())){End("invalid-camera");return nullptr;}
    }
    exported=camera.Record();exportTick=GetTickCount64();
    if(LoggingEnabled() && exportTick-sampleTick>=1000) {
        Log("photo sample: wall_ms=%llu scheduler=9 race_time=%.9f native_p=%.6f,%.6f,%.6f camera_p=%.6f,%.6f,%.6f",
            exportTick,frozenTime,source[16],source[17],source[18],exported[16],exported[17],exported[18]);
        sampleTick=exportTick;
    }
    alignas(16) thread_local std::array<float,28> output;
    output=exported;
    return output.data();
}
bool MatchingPhotoCamera(const float* a,const float* b) {
    if(!active.load())return false;
    std::lock_guard lock(mutex);
    return exportTick && GetTickCount64()-exportTick<500 &&
        MatchingSceneCamera(a,exported.data()) && MatchingSceneCamera(b,exported.data());
}
bool EnablePhotoMode() {
    if(!PhotoModeRequested())return true;
    if(!SupportedHost())return false;
    const unsigned char set[]={0x56,0x57,0x8b,0x7c,0x24,0x0c,0x8b,0xf1,0x3b,0xbe,0x28,0x01,0,0};
    const unsigned char assign[]={0x8b,0xc1,0x8b,0x4c,0x24,0x04,0x85,0xc9};
    if(std::memcmp(Base()+0x72f840,set,sizeof(set)) || std::memcmp(Base()+0x6d4830,assign,sizeof(assign)))return false;
    const bool ready=EnablePauseObserver() && EnableSeatInput() && EnableMenuChecks();
    Log("photo mode: native hooks ready=%d",ready);return ready;
}
}
