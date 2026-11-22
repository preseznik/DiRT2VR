#define DIRECTINPUT_VERSION 0x0800
#include "photo_mode.h"
#include "photo_camera.h"
#include "photo_overlay.h"
#include "photo_dof.h"
#include "photo_filter.h"
#include "photo_neural.h"
#include "scene_camera.h"
#include "replay_camera.h"
#include "car_tracker.h"
#include "car_follow.h"
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
#include <string>
#include <span>

namespace vr {
namespace {
std::mutex mutex;
std::atomic<void*> selected{};
std::atomic<bool> active{},blocking{},focusLost{};
void* menuState{};void* manager{};
unsigned begin{},end{},previous{},external{};
bool finished{},armed{};
// Entered from the paused Direct replay menu rather than the race's pause menu.
bool replayMode{};int replayState{};
// The replay's own view when Photo Mode began: the free camera starts there.
alignas(16) std::array<float,28> replaySeed{};bool replaySeedValid{};
// The camera owner whose export showed that view (the replay's cinematic camera).
void* replayOwner{};std::array<void*,8> replaySeen{};
ULONGLONG tick{};
PhotoCamera camera;
// The last shot of this pause, resumed when Photo Mode is chosen again.
PhotoCamera saved;
bool savedValid{};
void* savedManager{};
double savedTime{};
// Desktop panel: shown by default, H/View toggles it for the rest of the session.
bool panelVisible=true,panelHeld{},photoHeld{},lastController{};
// Desktop depth of field; kept for the rest of the session.
bool dofEnabled{},dofKeyHeld{},padXHeld{};
int blurHeld{};
PhotoDofSettings dof;
ULONGLONG dofSince{},dofDrawn{};
// Desktop filters; kept for the rest of the session.
PhotoFilterSettings filter;
bool lookHeld{},letterboxHeld{},gridHeld{},padViewHeld{};
unsigned filterFrame{};
// DLSS 5 Neural Rendering (desktop, needs the launcher's runtime folder); kept for the session.
bool neuralEnabled{},neuralHeld{};
PhotoNeuralState neuralState{};
// Photo panel: selected row, base move speed and held-key auto-repeat.
int menuSelected{},speedLevel=2;
// The car the camera last jumped behind (1-based among the cars on screen; 0 = none yet).
int jumpCar{};
constexpr float SpeedScales[]{.25f,.5f,1.f,2.f,4.f};
struct Repeat {
    bool held{};ULONGLONG next{};
    bool Step(bool down,ULONGLONG now) {
        if(!down){held=false;return false;}
        if(!held){held=true;next=now+350;return true;}
        if(now>=next){next=now+70;return true;}
        return false;
    }
} menuUpKey,menuDownKey,menuLeftKey,menuRightKey;
enum class Setting { FieldOfView,Tilt,Speed,Car,DepthOfField,Autofocus,Distance,Blur,Look,Brightness,Letterbox,Grid,Neural,NeuralOption };
std::wstring KeyLabel(unsigned scan) {
    wchar_t name[32]{};
    return GetKeyNameTextW(LONG(scan<<16),name,32)>0?name:L"";
}
std::wstring Format(const wchar_t* format,double value) {
    wchar_t text[64]{};swprintf_s(text,format,value);return text;
}
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
// Paused replay: the replay clock (not the race clock) and the playback state.
bool ReplayClock(void* state,double& time,int& playback) {
    void** registry{};unsigned index{};void* game{};unsigned char* root{};
    return Read(state,0x28,registry) && Read(Base(),0x107f3d8,index) && index<4096 &&
        Read(registry,index*sizeof(void*),game) && Read(game,0x229c,root) && root &&
        Read(root+0x10,0x1b8,time) && Read(root,0xa60,playback) && std::isfinite(time) && time>=0 && time<1e8;
}
// The scene stays as it was when Photo Mode began.
bool StillPaused(void* state) {
    if(!replayMode)return NativePaused(state);
    double time{};int playback{};
    return ReplayClock(state,time,playback) && playback==replayState;
}
bool FrozenClock(void* state,double& time) {
    int playback{};
    return replayMode?ReplayClock(state,time,playback):RaceClock(state,time);
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
    if(!readable || !StillPaused(menuState) || !FrozenClock(menuState,time) || std::abs(time-frozenTime)>.000001 ||
       !SeedPhotoCamera(record,pose))return false;
    Log("photo seed after: source=selected-entry entry=%p scheduler=9 race_time=%.9f p=%.6f,%.6f,%.6f fov=%.6f near=%.6f bridge_cached=%u bridge_override=%u blended=%u",
        entry,time,record[16],record[17],record[18],record[20],record[21],cached,overrideView,blended);
    return true;
}
bool Key(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
// Zoom, roll and smooth acceleration are desktop-only: the headset sets the
// field of view, and a tilted horizon or eased motion is uncomfortable in VR.
bool Headset() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_HEADSET",value,8)==1 && value[0]==L'1';}();
    return enabled;
}
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
        result.slow=Key(VK_CONTROL);
        // Tilt uses the physical keys either side of X (Z/C on QWERTY, Y/C on QWERTZ).
        static const int tiltLeft=int(MapVirtualKeyW(0x2c,MAPVK_VSC_TO_VK)),tiltRight=int(MapVirtualKeyW(0x2e,MAPVK_VSC_TO_VK));
        result.roll=(float(tiltRight && Key(tiltRight))-float(tiltLeft && Key(tiltLeft)))*.5f*dt;
        result.zoom=(float(Key(VK_PRIOR))-float(Key(VK_NEXT)))*.8f*dt;
        result.restart=Key('R');
        // Buttons from the window's input state: DirectInput polling missed short clicks.
        result.level=Key(VK_MBUTTON);result.autofocus=Key(VK_RBUTTON);
        result.photo=Key('P');result.panel=Key('H');
        result.zoom+=float(TakePhotoWheel())/WHEEL_DELTA*.1f; // Wheel away from you zooms in.
        // Depth of field: F, and the top-row keys 1-4 by position (focus nearer/farther, less/more blur).
        static const auto row=[](unsigned scan){return int(MapVirtualKeyW(scan,MAPVK_VSC_TO_VK));};
        static const int nearer=row(0x02),farther=row(0x03),less=row(0x04),more=row(0x05);
        const auto held=[](int key){return key && Key(key);};
        result.dof=Key('F');
        result.focus=(float(held(farther))-float(held(nearer)))*dt;
        result.blur=int(held(more))-int(held(less));
        // Filters: G look (Shift+G back), top-row 5/6 brightness, L letterbox, T thirds grid.
        static const int darker=row(0x06),brighter=row(0x07);
        result.look=Key('G');result.letterbox=Key('L');result.grid=Key('T');
        result.neural=Key('N');
        result.menuUp=Key(VK_UP);result.menuDown=Key(VK_DOWN);result.menuLeft=Key(VK_LEFT);result.menuRight=Key(VK_RIGHT);
        result.exposure=(float(held(brighter))-float(held(darker)))*dt;
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
    result.keyboard=result.right!=0 || result.forward!=0 || result.up!=0 || result.yaw!=0 || result.pitch!=0 ||
        result.zoom!=0 || result.roll!=0 || result.fast || result.slow || result.restart || result.level || result.photo || result.panel ||
        result.dof || result.focus!=0 || result.blur!=0 || result.autofocus ||
        result.look || result.letterbox || result.grid || result.exposure!=0 || result.neural ||
        result.menuUp || result.menuDown || result.menuLeft || result.menuRight;
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
            result.slow|=(pad.wButtons&XINPUT_GAMEPAD_LEFT_SHOULDER)!=0;
            const auto pressed=[&](WORD button){return (pad.wButtons&button)?1.f:0.f;};
            const float vertical=pressed(XINPUT_GAMEPAD_DPAD_UP)-pressed(XINPUT_GAMEPAD_DPAD_DOWN);
            const float horizontal=pressed(XINPUT_GAMEPAD_DPAD_RIGHT)-pressed(XINPUT_GAMEPAD_DPAD_LEFT);
            // X toggles depth of field and View hides the panel (both taps).
            const bool x=(pad.wButtons&XINPUT_GAMEPAD_X)!=0,view=(pad.wButtons&XINPUT_GAMEPAD_BACK)!=0;
            result.padX|=x;result.padView|=view;
            if(panelVisible && !Headset()) {
                // The D-pad selects and changes settings while the Photo panel is shown.
                result.menuUp|=vertical>0;result.menuDown|=vertical<0;result.menuLeft|=horizontal<0;result.menuRight|=horizontal>0;
            } else {result.zoom+=vertical*.8f*dt;result.roll+=horizontal*.5f*dt;}
            result.autofocus|=(pad.wButtons&XINPUT_GAMEPAD_LEFT_THUMB)!=0;
            result.restart|=(pad.wButtons&XINPUT_GAMEPAD_Y)!=0;
            result.level|=(pad.wButtons&XINPUT_GAMEPAD_RIGHT_THUMB)!=0;
            result.photo|=(pad.wButtons&XINPUT_GAMEPAD_A)!=0;
            result.controller|=used;
        }
    }
    if(result.slow){result.zoom*=.25f;result.roll*=.25f;result.focus*=.25f;result.exposure*=.25f;}
    if(Headset()) {
        result.zoom=result.roll=result.focus=result.exposure=0;result.blur=result.lookStep=0;
        result.level=result.dof=result.padX=result.autofocus=result.look=result.letterbox=result.grid=result.padView=result.neural=false;
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
    if(camera.Ready()){saved=camera;savedValid=true;savedManager=manager;savedTime=frozenTime;}
    camera.Reset();exportTick=0;finished=true;armed=false;
    PhotoDofWanted(false);
    UpdatePhotoCursor(Focus(),false);
}
bool Begin(void* state,bool replay) {
    manager=selected.load();unsigned current{};
    replayMode=replay;
    if(replay) {
        // The replay menu follows set_replay_system_pause; the race pause states do not apply.
        double time{};int playback{};const bool clock=ReplayClock(state,time,playback);
        unsigned char* scheduler{};int value{};void** registry{};void* wrapper{};unsigned schedulerIndex{};
        const bool read=Read(state,0x28,registry) && Read(Base(),0x107dd30,schedulerIndex) && schedulerIndex<4096 &&
            Read(registry,schedulerIndex*sizeof(void*),wrapper) && Read(wrapper,4,scheduler) && Read(scheduler,0x9a58,value);
        Log("photo mode: replay entry clock=%d time=%.6f playback=%d world_paused=%d scheduler=%d(%d) manager=%p",
            clock,time,playback,GamePaused(),read?value:-1,read,manager);
        if(!clock)return false;
        frozenTime=time;replayState=playback;
    }
    if((!replay && (!GamePaused() || !NativePaused(state) || !RaceClock(state,frozenTime))) || !Read(manager,4,begin) || !Read(manager,8,end) ||
       !Read(manager,0x128,current) || !begin || end<begin || (end-begin)%0x270 ||
       (end-begin)/0x270>64 || current>=(end-begin)/0x270)return false;
    using Find=int(__thiscall*)(void*,const char*);
    const auto find=reinterpret_cast<Find>(Base()+0x724f90);
    const int index=find(manager,"chase_close");
    if(index<0 || unsigned(index)>=(end-begin)/0x270)return false;
    previous=current;external=unsigned(index);
    using Set=void(__thiscall*)(void*,int);
    reinterpret_cast<Set>(Base()+0x72f840)(manager,index);
    // A paused replay may not hold the world pause; then its menu simply is not shown.
    if(!PhotoWorldVisible(true) && !replay) {
        reinterpret_cast<Set>(Base()+0x72f840)(manager,int(previous));return false;
    }
    replaySeedValid=false;
    if(replay) {
        // Replays usually show their own (cinematic) cameras. Start from that view and
        // hand the renderer the car's chase camera, whose export Photo Mode replaces.
        void* shown=ObservedDisplayCamera();char shownName[48]{};
        replaySeedValid=shown && Read(shown,0x80,shownName) && std::memchr(shownName,0,sizeof(shownName)) &&
            ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(shown)+0x10,replaySeed.data()+4,96,nullptr) &&
            std::isfinite(replaySeed[16]) && std::isfinite(replaySeed[20]) && replaySeed[20]>.05f && replaySeed[20]<3.f;
        replayOwner=nullptr;replaySeen={};
        Log("photo mode: replay view shown=%p name=%s seed=%d p=%.3f,%.3f,%.3f fov=%.4f",
            shown,shownName,replaySeedValid,replaySeed[16],replaySeed[17],replaySeed[18],replaySeed[20]);
    }
    jumpCar=0;
        camera.Reset();camera.SetSmoothing(!Headset());TakePhotoWheel();exportTick=0;sampleTick=0;tick=GetTickCount64();armed=false;slot=4;
    focusLost=false;active=true;blocking=true;
    Log("photo mode: enter state=%p camera=%u->%u replay=%d time=%.9f",state,previous,external,replay,frozenTime);
    return true;
}
}
bool PhotoModeRequested() {
    static const bool requested=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_PHOTO_MODE",value,8)==1 && value[0]==L'1';}();
    return requested;
}
bool PhotoModeActive(){return active.load();}
bool PhotoModeBlocksInput(){return blocking.load();}
bool PhotoModeHidesHud(){return active.load() && replayMode;}
void PhotoModeFocusLost(){if(active.load())focusLost=true;}
void PhotoSelectCamera(void* value){selected=value;}
bool PhotoModeMenu(void* state,void* result) {
    const bool replay=!std::memcmp(static_cast<const char*>(state)+4,"d2vr_photo_replay",sizeof("d2vr_photo_replay"));
    if(!PhotoModeRequested() || (!replay && std::memcmp(static_cast<const char*>(state)+4,"d2vr_photo_mode",sizeof("d2vr_photo_mode"))))return false;
    std::lock_guard lock(mutex);
    if(menuState!=state) {
        if(active)End("state-replaced");
        menuState=state;finished=false;
        if(!Begin(state,replay)){finished=true;Log("photo mode: entry refused; native pause/camera unavailable replay=%d",replay);}
    }
    if(active && focusLost.exchange(false))End("focus-lost");
    if(active && !StillPaused(state))End("pause-lost");
    using Assign=void(__thiscall*)(void*,const char*);
    reinterpret_cast<Assign>(Base()+0x6d4830)(result,finished?"disabled":"");
    if(finished)menuState=nullptr;
    return true;
}
void PhotoModePauseChanged(bool paused) {
    if(paused)return;
    std::lock_guard lock(mutex);
    if(active)End("native-unpause");
    savedValid=false; // A kept shot belongs to the pause it was taken in.
}
namespace {
// One Photo panel row and what it changes.
struct MenuEntry { Setting id; NeuralOption option{}; PhotoMenuRow row; };
// The Photo panel's rows from the current settings (desktop).
std::vector<MenuEntry> MenuRows(ULONGLONG now) {
    std::vector<MenuEntry> rows;
    const auto add=[&](Setting id,const wchar_t* section,const wchar_t* label,std::wstring value,std::wstring key={},bool dim=false,bool warn=false) {
        rows.push_back({id,{},PhotoMenuRow{section,label,std::move(value),std::move(key),dim,warn}});
    };
    const auto record=camera.Record();
    add(Setting::FieldOfView,L"CAMERA",L"Field of view",Format(L"%.0f\u00b0",record[20]*57.2957795));
    add(Setting::Tilt,L"",L"Tilt",Format(L"%+.0f\u00b0",camera.Roll()*57.2957795),KeyLabel(0x2c)+L"/"+KeyLabel(0x2e));
    add(Setting::Speed,L"",L"Move speed",Format(L"%.0f m/s",8.0*SpeedScales[speedLevel]));
    const int cars=int(TrackedCars().size());
    add(Setting::Car,L"",L"Jump to car",!cars?std::wstring(L"None in view"):jumpCar && jumpCar<=cars?
        Format(L"Car %.0f",jumpCar)+Format(L" of %.0f",cars):Format(L"%.0f in view",cars),L"",!cars);
    const bool unavailable=dofEnabled && now-dofSince>1000 && now-dofDrawn>1000;
    add(Setting::DepthOfField,L"DEPTH OF FIELD",L"Depth of field",!dofEnabled?L"Off":unavailable?L"Unavailable":L"On",L"F",false,unavailable);
    add(Setting::Autofocus,L"",L"Autofocus",dof.autoFocus?L"On":L"Off",L"",!dofEnabled);
    const float distance=dof.autoFocus?PhotoDofCentre():dof.focus;
    add(Setting::Distance,L"",L"Focus distance",distance<=0?std::wstring(L"\u2014"):distance>=4000?std::wstring(L"Far"):
        Format(distance<10?L"%.1f m":L"%.0f m",distance),KeyLabel(0x02)+L"/"+KeyLabel(0x03),!dofEnabled);
    add(Setting::Blur,L"",L"Blur",Format(L"%.0f",dof.blur),KeyLabel(0x04)+L"/"+KeyLabel(0x05),!dofEnabled);
    add(Setting::Look,L"LOOK",L"Filter",PhotoLookName(filter.look),L"G");
    add(Setting::Brightness,L"",L"Brightness",Format(L"%+.1f",filter.exposure),KeyLabel(0x06)+L"/"+KeyLabel(0x07));
    add(Setting::Letterbox,L"",L"Letterbox",filter.letterbox==1?L"2.39:1":filter.letterbox==2?L"1.85:1":L"Off",L"L");
    add(Setting::Grid,L"",L"Thirds grid",filter.grid?L"On":L"Off",L"T");
    if(PhotoNeuralConfigured()) {
        const wchar_t* state=!neuralEnabled?L"Off":neuralState==PhotoNeuralState::Starting?L"Starting\u2026":
            neuralState==PhotoNeuralState::Failed?L"Unavailable":L"On";
        add(Setting::Neural,L"DLSS 5 NEURAL RENDERING",L"Neural rendering",state,L"N",false,neuralEnabled && neuralState==PhotoNeuralState::Failed);
        for(auto& [option,row]:NeuralOptionRows(neuralEnabled))rows.push_back({Setting::NeuralOption,option,std::move(row)});
    }
    return rows;
}
// Left/right on a Photo panel row.
void Adjust(const MenuEntry& entry,int direction,ULONGLONG now) {
    PhotoInput change;
    const int looks=PhotoLookCount();
    switch(entry.id) {
    case Setting::FieldOfView:change.zoom=-.08f*direction;camera.Move(change,0);break;
    case Setting::Tilt:change.roll=.0349066f*direction;camera.Move(change,0);break;
    case Setting::Speed:speedLevel=std::clamp(speedLevel+direction,0,4);break;
    case Setting::Car: {
        // Behind the next car seen in the picture (cars only count once they have moved).
        const auto cars=TrackedCars();
        if(cars.empty())break;
        const int count=int(cars.size());
        jumpCar=jumpCar<1 || jumpCar>count?(direction>0?1:count):(jumpCar-1+direction+count)%count+1;
        std::array<float,28> chase;const auto current=camera.Record();
        if(CarChaseRecord(cars[jumpCar-1],current.data(),chase) && camera.Begin(chase.data()))
            Log("photo camera: jumped to car %d of %d p=%.2f,%.2f,%.2f",jumpCar,count,chase[16],chase[17],chase[18]);
        break;
    }
    case Setting::DepthOfField:dofEnabled=!dofEnabled;dofSince=now;Log("photo depth of field: enabled=%d",dofEnabled);break;
    case Setting::Autofocus:if(dofEnabled)dof.autoFocus=!dof.autoFocus;break;
    case Setting::Distance:
        if(!dofEnabled)break;
        if(dof.autoFocus) {
            const float centre=PhotoDofCentre();
            if(centre>.3f && centre<5000)dof.focus=centre;
            dof.autoFocus=false;
        }
        dof.focus=std::clamp(dof.focus*std::exp(.1f*direction),.5f,2000.f);break;
    case Setting::Blur:if(dofEnabled)dof.blur=std::clamp(dof.blur+direction,1,10);break;
    case Setting::Look:filter.look=(filter.look+direction+looks)%looks;Log("photo filter: look=%d",filter.look);break;
    case Setting::Brightness:filter.exposure=std::clamp(std::round((filter.exposure+.1f*direction)*10)/10,-2.f,2.f);break;
    case Setting::Letterbox:filter.letterbox=(filter.letterbox+direction+int(PhotoLetterboxes.size()))%int(PhotoLetterboxes.size());break;
    case Setting::Grid:filter.grid=!filter.grid;break;
    case Setting::Neural:neuralEnabled=!neuralEnabled;Log("photo neural rendering: enabled=%d",neuralEnabled);break;
    case Setting::NeuralOption:if(neuralEnabled)NeuralOptionAdjust(entry.option,direction);break;
    }
}
}

void PhotoModeFrame(IDXGISwapChain* swapchain) {
    if(!PhotoModeRequested())return;
    PhotoPanelState overlay;
    bool show{},capture{},neuralWanted{};int dofState{};uint64_t sceneKey{};
    {
        std::lock_guard lock(mutex);
        if(!active) {
            if(blocking && !Poll(0,false).exit)blocking=false;
            return;
        }
        const auto now=GetTickCount64();const auto window=Focus();
        if(focusLost.exchange(false) || !window) {End("focus-lost");return;}
        if(!SameManager() || !StillPaused(menuState)) {End("scene-changed");return;}
        double time{};
        if(!FrozenClock(menuState,time) || std::abs(time-frozenTime)>.000001) {End("race-time-changed");return;}
        const float dt=float(now-tick)*.001f;tick=now;
        auto movement=Poll(dt,true);
        if(!armed) {if(!movement.exit)armed=true;movement={};}
        if(movement.exit){End("back");return;}
        movement.speedScale=SpeedScales[speedLevel];
        camera.Move(movement,dt);
        UpdatePhotoCursor(window,true);
        // The panel and photos use the desktop frame; VR shows the headset view instead.
        if(!Headset() && camera.Ready()) {
            if(movement.controller)lastController=true;
            else if(movement.keyboard)lastController=false;
            // H or the controller's View button shows or hides the panel.
            if((movement.panel && !panelHeld) || (movement.padView && !padViewHeld))panelVisible=!panelVisible;
            // Direct keys (also listed in the panel).
            const int looks=PhotoLookCount();
            if(movement.look && !lookHeld) {
                filter.look=(filter.look+(movement.fast?-1:1)+looks)%looks; // Shift+G goes back.
                Log("photo filter: look=%d",filter.look);
            }
            if(movement.exposure!=0)filter.exposure=std::clamp(filter.exposure+movement.exposure,-2.f,2.f);
            if(movement.letterbox && !letterboxHeld)filter.letterbox=(filter.letterbox+1)%int(PhotoLetterboxes.size());
            if(movement.grid && !gridHeld)filter.grid=!filter.grid;
            if(movement.neural && !neuralHeld && PhotoNeuralConfigured()) {
                neuralEnabled=!neuralEnabled;Log("photo neural rendering: enabled=%d",neuralEnabled);
            }
            capture=movement.photo && !photoHeld;
            if((movement.dof && !dofKeyHeld) || (movement.padX && !padXHeld)) {
                dofEnabled=!dofEnabled;dofSince=now;
                Log("photo depth of field: enabled=%d",dofEnabled);
            }
            if(dofEnabled) {
                if(movement.autofocus)dof.autoFocus=true;
                if(movement.focus!=0) {
                    if(dof.autoFocus) {
                        // Start manual focus where autofocus was.
                        const float centre=PhotoDofCentre();
                        if(centre>.3f && centre<5000)dof.focus=centre;
                        dof.autoFocus=false;
                    }
                    dof.focus=std::clamp(dof.focus*std::exp(movement.focus),.5f,2000.f);
                }
                if(movement.blur!=0 && movement.blur!=blurHeld)dof.blur=std::clamp(dof.blur+(movement.blur>0?1:-1),1,10);
            }
            // Photo panel: up/down select a setting, left/right change it.
            auto rows=MenuRows(now);
            const int count=int(rows.size());
            if(panelVisible && count) {
                menuSelected=std::clamp(menuSelected,0,count-1);
                if(menuUpKey.Step(movement.menuUp,now))menuSelected=(menuSelected+count-1)%count;
                if(menuDownKey.Step(movement.menuDown,now))menuSelected=(menuSelected+1)%count;
                const int direction=menuRightKey.Step(movement.menuRight,now)?1:menuLeftKey.Step(movement.menuLeft,now)?-1:0;
                if(direction){Adjust(rows[menuSelected],direction,now);rows=MenuRows(now);}
            }
            overlay.panel=panelVisible;overlay.controller=lastController;overlay.selected=menuSelected;
            for(auto& entry:rows)overlay.rows.push_back(std::move(entry.row));
            dofState=!dofEnabled?0:(now-dofSince>1000 && now-dofDrawn>1000)?2:1;
            neuralWanted=neuralEnabled;
            // The camera as it will be drawn: unchanged means the same picture.
            const auto view=camera.Record();sceneKey=1469598103934665603ull;
            for(const auto byte:std::span(reinterpret_cast<const unsigned char*>(view.data()),sizeof(view)))sceneKey=(sceneKey^byte)*1099511628211ull;
            show=true;
        }
        neuralHeld=movement.neural;lookHeld=movement.look;letterboxHeld=movement.letterbox;gridHeld=movement.grid;
        padViewHeld=movement.padView;blurHeld=movement.blur;dofKeyHeld=movement.dof;padXHeld=movement.padX;
        PhotoDofWanted(show && dofEnabled);
        panelHeld=movement.panel;photoHeld=movement.photo;
    }
    // Outside the lock: GPU copies must not hold up the camera export on another thread.
    if(!show)return;
    // Neural rendering first (on the game's own frame), then blur, filters and saving;
    // photos include all of these but never the panel.
    const auto state=PhotoNeuralApply(swapchain,neuralWanted,sceneKey);
    {std::lock_guard lock(mutex);neuralState=state;}
    if(dofState && PhotoDofApply(swapchain,dof)) {
        std::lock_guard lock(mutex);dofDrawn=GetTickCount64();
    }
    // Filters after depth of field (so blur works on the original colours); the grid is on screen only.
    PhotoFilterSettings look;
    {std::lock_guard lock(mutex);look=filter;}
    static bool filterReported{};
    if(!PhotoFilterApply(swapchain,look,filterFrame++) && !filterReported){filterReported=true;Log("photo filter: draw failed");}
    if(capture){Log("photo capture requested");PhotoCapture(swapchain);}
    PhotoGridDraw(swapchain,look);
    PhotoPanelDraw(swapchain,overlay);
}
float* PhotoModeExport(void* owner,float* native) {
    if(!active.load())return nullptr;
    std::lock_guard lock(mutex);
    void* ownerManager{};
    std::array<float,28> source{};
    if(!active || !Read(owner,0x24,ownerManager) || !Read(native,0,source))return nullptr;
    if(replayMode) {
        // The replay's own camera: the owner whose export is the view shown at entry.
        if(!replayOwner) {
            const bool fresh=std::find(replaySeen.begin(),replaySeen.end(),owner)==replaySeen.end();
            if(fresh)for(auto& slot:replaySeen)if(!slot){slot=owner;break;}
            const bool match=replaySeedValid && std::abs(source[16]-replaySeed[16])<.05f && std::abs(source[17]-replaySeed[17])<.05f &&
                std::abs(source[18]-replaySeed[18])<.05f && std::abs(source[20]-replaySeed[20])<.01f;
            if(fresh)Log("photo mode: replay export owner=%p manager=%p p=%.3f,%.3f,%.3f fov=%.4f match=%d",owner,ownerManager,source[16],source[17],source[18],source[20],match);
            if(!match)return nullptr;
            replayOwner=owner;
        }
        if(owner!=replayOwner)return nullptr;
    } else if(ownerManager!=manager || !SameManager())return nullptr;
    if(!camera.Ready()) {
        if(replayMode) {
            if(!replaySeedValid){End("replay-view-unavailable");return nullptr;}
            std::memcpy(source.data()+4,replaySeed.data()+4,96);
            Log("photo seed: source=replay-view p=%.6f,%.6f,%.6f fov=%.6f near=%.6f",source[16],source[17],source[18],source[20],source[21]);
        } else if(!SelectedSeed(owner,source)){End("selected-camera-unavailable");return nullptr;}
        Log("photo native basis: up=%.6f,%.6f,%.6f left=%.6f,%.6f,%.6f forward=%.6f,%.6f,%.6f fov=%.6f near=%.6f far=%.6f",
            source[4],source[5],source[6],source[8],source[9],source[10],source[12],source[13],source[14],source[20],source[21],source[22]);
        if(!camera.Begin(source.data())){End("invalid-camera");return nullptr;}
        // Resume the kept shot only for the same frozen scene and starting view.
        const auto& start=camera.Start();const auto& previousStart=saved.Start();
        if(savedValid && savedManager==manager && savedTime==frozenTime &&
           std::abs(start[16]-previousStart[16])<.01f && std::abs(start[17]-previousStart[17])<.01f &&
           std::abs(start[18]-previousStart[18])<.01f) {
            camera=saved;camera.SetSmoothing(!Headset());
            const auto resumed=camera.Record();
            Log("photo camera resumed: p=%.6f,%.6f,%.6f fov=%.6f roll=%.6f",resumed[16],resumed[17],resumed[18],resumed[20],camera.Roll());
        }
        savedValid=false;
    }
    exported=camera.Record();exportTick=GetTickCount64();
    if(LoggingEnabled() && exportTick-sampleTick>=1000) {
        Log("photo sample: wall_ms=%llu scheduler=9 race_time=%.9f native_p=%.6f,%.6f,%.6f camera_p=%.6f,%.6f,%.6f fov=%.6f roll=%.6f dof=%d auto=%d focus=%.3f blur=%d centre=%.3f look=%d exposure=%.3f letterbox=%d grid=%d",
            exportTick,frozenTime,source[16],source[17],source[18],exported[16],exported[17],exported[18],exported[20],camera.Roll(),
            dofEnabled,dof.autoFocus,dof.focus,dof.blur,PhotoDofCentre(),filter.look,filter.exposure,filter.letterbox,filter.grid);
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
