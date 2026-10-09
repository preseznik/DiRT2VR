#define DIRECTINPUT_VERSION 0x0800
#include "chase_camera.h"
#include "chase_transform.h"
#include "chase_mouse.h"
#include "chase_zoom.h"
#include "chase_cursor.h"
#include "common.h"
#include "gfwl_compat.h"
#include "scene_camera.h"
#include "game_pause.h"
#include "replay_camera.h"
#include "replay_pose_overlay.h"
#include "replay_display_pose.h"
#include <MinHook.h>
#include <dinput.h>
#include <xinput.h>
#include <atomic>
#include <array>
#include <cstring>
#include <cmath>
#include <intrin.h>
#include <cstdlib>
#include <chrono>
#include <mutex>

namespace vr {
namespace {
using UpdateFn=void (__thiscall*)(void*,const float*,void*);
using TargetFn=float* (__thiscall*)(void*,float*,void*,bool);
using LookFn=void (__thiscall*)(void*,const void*,void*);
using ReplayUpdateFn=void (__thiscall*)(void*);
using PoseWriteFn=void (__thiscall*)(void*,void*);
using CameraExportFn=float* (__thiscall*)(void*);
using ReplaySceneFn=void (__thiscall*)(void*,void*,void*,void*,void*,void*);
UpdateFn update{}, obstruction{};
ReplayUpdateFn replayUpdate{};
PoseWriteFn finalizePose{},storePose{};
CameraExportFn exportCamera{};
ReplaySceneFn replayScene{};
struct DisplayPose {
 void* manager{};void* camera{};ULONGLONG tick{};uint64_t frame{};double time{};
 std::array<float,16> pose{};
};
std::mutex displayPoseMutex;
DisplayPose displayPose;
struct ExportedPose {
 void* manager{};void* camera{};ULONGLONG tick{};
 std::array<float,28> record{};
};
std::mutex exportedPoseMutex;
ExportedPose exportedPose;
TargetFn target{};LookFn look{};
unsigned char* base{};
std::atomic<void*> selected{};
thread_local void* sampled{};
thread_local ULONGLONG last{};
thread_local ChaseOrbit orbit;
thread_local ChaseZoom zoom;
thread_local void* orbitCamera{};
thread_local void* applyingCamera{};
thread_local bool armed{},usingOrbit{};
thread_local ULONGLONG inputTick{};
thread_local ReplayInputClock replayClock;
thread_local bool replayOrbit{};
thread_local bool replayApplying{};
thread_local bool liveApplying{};
thread_local void* replayRecordCamera{};
thread_local void* replayRecordManager{};
thread_local void* replayCache{};
thread_local unsigned replayApplied{},replayRestored{},replayEvaluations{},replayRejected{},replayBlocked{};
thread_local unsigned replayCacheRestored{},replayCameraRestored{},replayRestoreConflict{};
// Only own the pose, not FOV or other independently updated camera properties.
thread_local ReplayPoseOverlay<0x20> replayCacheOverlay;
thread_local ReplayPoseOverlay<0x40> replayCameraOverlay;
IDirectInput8W* input{};IDirectInputDevice8W* mouse{};HWND mouseWindow{};
using XboxFn=DWORD (WINAPI*)(DWORD,XINPUT_STATE*);
XboxFn xbox{};
unsigned mode{};
float mouseSensitivity=1,stickSensitivity=1;bool invert{};
ChaseMouseMode mouseMode=ChaseMouseMode::Right;
unsigned selectedSlot=4;
bool EnvFlag(const char* name) {
 char value[8]{};return GetEnvironmentVariableA(name,value,sizeof(value))==1 && value[0]=='1';
}
float Sensitivity(const char* name) {
 char value[16]{};const auto length=GetEnvironmentVariableA(name,value,sizeof(value));
 if(!length || length>=sizeof(value))return 1;
 char* end{};const long percent=std::strtol(value,&end,10);
 return end!=value && *end==0 && percent>=25 && percent<=300?percent/100.f:1.f;
}

template<class T> bool Read(const void* object,unsigned offset,T& value) {
 SIZE_T bytes{};return object && ReadProcessMemory(GetCurrentProcess(),static_cast<const char*>(object)+offset,&value,sizeof(value),&bytes) && bytes==sizeof(value);
}
bool Eligible(void* camera) {
 unsigned table{};char name[48]{};
 if(!Read(camera,0,table)||table!=reinterpret_cast<unsigned>(base+0xf51bb8)||!Read(camera,0x80,name)||!memchr(name,0,sizeof(name))||
    (strcmp(name,"chase_close")&&strcmp(name,"chase_far")))return false;
 auto manager=selected.load();return manager && ObservedCameraObject(manager)==camera;
}
void* ReplayCache(void* manager,void* camera) {
 unsigned begin{},end{},table{};void* object{};
 if(!Read(camera,0,table) || table!=reinterpret_cast<unsigned>(base+0xf51bb8) ||
    !Read(manager,4,begin) || !Read(manager,8,end) ||
    !begin || end<begin || (end-begin)%0x270 || (end-begin)/0x270>64)return nullptr;
 // A camera switch can precede the next playback tick. Restore the old entry
 // too, provided it still belongs to the same manager and has our exact output.
 for(unsigned index=0;index<(end-begin)/0x270;++index) {
  auto entry=reinterpret_cast<unsigned char*>(begin)+index*0x270;
  if(Read(entry,0,object) && object==camera)return entry+0x10;
 }
 return nullptr;
}
HWND Focus() {
 const auto window=GetForegroundWindow();DWORD pid{};GetWindowThreadProcessId(window,&pid);
 return pid==GetCurrentProcessId()?window:nullptr;
}
bool Movement(void* camera,float& speed) {
 void* provider{};void* car{};void* physics{};void* manager{};unsigned table{};
 if(!Read(camera,0x74,provider)||!Read(provider,0,table)||table!=reinterpret_cast<unsigned>(base+0xf511b4)||
    !Read(provider,12,car)||!Read(car,0x8384,manager)||manager!=selected.load()||!Read(provider,8,physics)||
    !Read(physics,0x2218,speed)||!std::isfinite(speed)||std::abs(speed)>200)return false;
 speed*=3.6f;return true;
}
struct Inputs {float x{},y{},wheel{};bool held{},rearHeld{};bool neutral{true};};
Inputs Poll(float dt) {
 Inputs result;const auto window=Focus();if(!window)return result;
 if(mouseWindow!=window) {
  if(mouse){mouse->Unacquire();mouse->Release();mouse=nullptr;}
  mouseWindow=window;
  if(!input)DirectInput8Create(GetModuleHandleW(nullptr),DIRECTINPUT_VERSION,IID_IDirectInput8W,reinterpret_cast<void**>(&input),nullptr);
  if(input && SUCCEEDED(input->CreateDevice(GUID_SysMouse,&mouse,nullptr))) {
   if(FAILED(mouse->SetDataFormat(&c_dfDIMouse2))||FAILED(mouse->SetCooperativeLevel(window,DISCL_FOREGROUND|DISCL_NONEXCLUSIVE))) {mouse->Release();mouse=nullptr;}
  }
  armed=false;
 }
 DIMOUSESTATE2 state{};
 bool available=false;
 if(mouse) {
  available=SUCCEEDED(mouse->GetDeviceState(sizeof(state),&state));
  if(!available){if(SUCCEEDED(mouse->Acquire()))mouse->GetDeviceState(sizeof(state),&state);state={};}
 }
 const auto activation=ChaseMouseActivation(mouseMode,(GetAsyncKeyState(VK_LBUTTON)&0x8000)!=0,
     (GetAsyncKeyState(VK_RBUTTON)&0x8000)!=0,state.lX!=0 || state.lY!=0,available);
 result.held=activation.held;result.neutral=activation.neutral;
 // Scrolling works in every activation mode without holding a mouse button.
 result.wheel=available?float(state.lZ):0;
 if(result.wheel!=0)result.neutral=false;
 if(activation.active){result.x=-float(state.lX)*.003f*mouseSensitivity;result.y=-float(state.lY)*.003f*mouseSensitivity;}
 if(!xbox) {
  auto module=LoadLibraryExW(L"xinput1_4.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
  if(module)xbox=reinterpret_cast<XboxFn>(GetProcAddress(module,"XInputGetState"));
 }
 if(xbox)for(unsigned slot=0;slot<4;++slot) {
  XINPUT_STATE value{};
  if(xbox(slot,&value)!=ERROR_SUCCESS){if(slot==selectedSlot){selectedSlot=4;armed=false;}continue;}
  float x=value.Gamepad.sThumbRX/32767.f,y=value.Gamepad.sThumbRY/32767.f;
  const float length=std::sqrt(x*x+y*y);
  const bool rear=(value.Gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_THUMB)!=0;
  if(length<=.2f && !rear)continue;
  result.neutral=false;
  if(selectedSlot==4)selectedSlot=slot;
  if(slot!=selectedSlot)continue;
  result.rearHeld=rear;
  if(length<=.2f)continue;
  const float amount=std::min((length-.2f)/.8f,1.f)/length;
  result.x-=x*amount*2.5f*dt*stickSensitivity;
  result.y+=y*amount*1.5f*dt*stickSensitivity;result.held=true;
 }
 if(invert)result.y=-result.y;
 return result;
}
void Snapshot(const char* stage,void* camera,const float* timing,void* record) {
 std::array<float,8> pose{};float dt{},speed{};Read(timing,0,dt);Read(record,0,pose);
 const bool valid=Movement(camera,speed);
 Log("chase probe: stage=%s camera=%p live=%d movement=%d speed_kmh=%.4f dt=%.6f orbit=%.4f,%.4f active=%d q=%.6f,%.6f,%.6f,%.6f p=%.6f,%.6f,%.6f",
  stage,camera,LiveDrivingCameraState(),valid,speed,dt,orbit.yaw,orbit.pitch,usingOrbit,pose[0],pose[1],pose[2],pose[3],pose[4],pose[5],pose[6]);
 if(ReplayOrbitProbe()) {
  const auto playback=ObservedReplayPlayback();
  Log("replay orbit: state=%d frame=%llu time=%.6f step=%.6f display=%d paused=%d input=%d armed=%d zoom=%.4f applied=%u restored=%u",
   playback.state,playback.frame,playback.time,playback.step,ObservedDisplayCamera()==camera,GamePaused(),replayOrbit,armed,zoom.scale,replayApplied,replayRestored);
  Log("replay pose ownership: evaluations=%u rejected=%u blocked=%u cache_restored=%u camera_restored=%u restore_conflicts=%u",
      replayEvaluations,replayRejected,replayBlocked,replayCacheRestored,replayCameraRestored,replayRestoreConflict);
 }
}
void PrepareOrbit(void* camera,const float* timing,void* record,bool playbackPath) {
  float dt{},speed{};
  const auto now=GetTickCount64();Read(timing,0,dt);
  const auto playback=ObservedReplayPlayback();
  const bool replay=playbackPath && ReplayOrbitEnabled() && playback.Available(now) && ObservedDisplayCamera()==camera;
  const bool inputEnabled=playbackPath?ReplayOrbitEnabled():
      (ChaseCameraDiagnostic() || ReplayOrbitProbe() || (EnvFlag("DIRT2VR_CHASE_FREE_LOOK") && (!EnvFlag("DIRT2VR_HEADSET") || ExtendedViewsEnabled())));
  const bool ownerReady=inputEnabled && mode==2 && !GamePaused() && Focus() && Movement(camera,speed);
  if(orbitCamera!=camera || now-inputTick>250 || replayOrbit!=replay) {
   orbit.Reset();zoom.Reset();replayClock.Reset();armed=false;usingOrbit=false;orbitCamera=camera;
  }
  replayOrbit=replay;
  const bool repeat=replay && ownerReady && replayClock.frame==playback.frame;
  if(replay) {
   const double wall=std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
   dt=replayClock.Step(playback.frame,wall);
   if(!playback.Forward())speed=0; // Paused/backward playback must not return behind the car.
  } else replayClock.Reset();
  const bool ready=ownerReady && (replay || (!playbackPath && LiveDrivingCameraState())) && dt>0 && dt<=.25f;
  UpdateChaseCursor(Focus(),ready || repeat);
  inputTick=now;
  // A repeated evaluation of this recorder frame reuses the previous input.
  if(!repeat)usingOrbit=false;
  if(ready) {
   const auto inputs=Poll(dt);
   if(!armed){if(inputs.neutral)armed=true;}
   else if(orbit.Step(dt,speed,inputs.x,inputs.y,inputs.held,inputs.rearHeld) && zoom.Step(dt,inputs.wheel))usingOrbit=true;
  } else if(!repeat) {armed=false;orbit.Reset();}
  // Feed the game's native look direction. It computes yaw and distance using
  // its ordinary chase solver, then runs the original obstruction pass.
  if(usingOrbit && !playbackPath) {
   const float x=std::sin(orbit.yaw)*.5f;
   const float y=std::cos(orbit.yaw)*.5f-.1f;
   std::memcpy(static_cast<char*>(record)+0xa4,&x,4);std::memcpy(static_cast<char*>(record)+0xa8,&y,4);
  }
}
void __fastcall Update(void* camera,void*,const float* timing,void* record) {
 const bool eligible=Eligible(camera);
 if(eligible)PrepareOrbit(camera,timing,record,false);
 update(camera,timing,record);
 if(!eligible || (!ChaseCameraDiagnostic() && !ReplayOrbitProbe()))return;
 const auto now=GetTickCount64();if(now-last<1000)return;
 last=now;sampled=camera;Snapshot("before-obstruction",camera,timing,record);
}
float* __fastcall Target(void* camera,void*,float* output,void* record,bool flag) {
 auto result=target(camera,output,record,flag);
 const float yaw=replayApplying?orbit.yaw:0;
 if(camera==applyingCamera && (liveApplying || replayApplying) && _ReturnAddress()==base+0x71f553 && result==output && usingOrbit && (yaw!=0 || orbit.pitch!=0 || zoom.scale!=1)) {
  std::array<float,8> pose{};Read(record,0,pose);
  const auto before=pose;
  if(OrbitChasePose(pose,{output[12],output[13],output[14]},yaw,orbit.pitch,zoom.scale)) {
   std::memcpy(record,pose.data(),sizeof(pose));
   std::memcpy(static_cast<char*>(record)+0xf0,pose.data()+4,16);
   std::memcpy(static_cast<char*>(record)+0x100,pose.data(),16);
   if(replayApplying)++replayApplied;
   if(replayApplying && GetTickCount64()-last>=1000)Log("replay pose transform: yaw=%.5f pitch=%.5f scale=%.5f before_q=%.6f,%.6f,%.6f,%.6f after_q=%.6f,%.6f,%.6f,%.6f before_p=%.6f,%.6f,%.6f after_p=%.6f,%.6f,%.6f pivot=%.6f,%.6f,%.6f",
    yaw,orbit.pitch,zoom.scale,before[0],before[1],before[2],before[3],pose[0],pose[1],pose[2],pose[3],before[4],before[5],before[6],pose[4],pose[5],pose[6],output[12],output[13],output[14]);
  } else if(replayApplying) {
   ++replayRejected;
   if(GetTickCount64()-last>=1000)Log("replay pose rejected: q=%.6f,%.6f,%.6f,%.6f p=%.6f,%.6f,%.6f pivot=%.6f,%.6f,%.6f",
    before[0],before[1],before[2],before[3],before[4],before[5],before[6],output[12],output[13],output[14]);
  }
 }
 return result;
}
void __fastcall Obstruction(void* camera,void*,const float* timing,void* record) {
 const auto old=applyingCamera;applyingCamera=Eligible(camera)?camera:nullptr;
 const auto caller=_ReturnAddress();
 const bool replayCall=ReplayOrbitEnabled() && (caller==base+0x92cf2e || caller==base+0x92ce60);
 const auto oldReplay=replayApplying,oldLive=liveApplying;
 replayApplying=false;
 liveApplying=applyingCamera && !replayCall && LiveDrivingCameraState() &&
     !ObservedReplayPlayback().Available(GetTickCount64());
 if(replayCall && applyingCamera) {
  PrepareOrbit(camera,timing,record,true);
  // Rendering reads both the camera object and its manager's cached pose.
  // Keep them consistent until the next playback update instead of restoring
  // only the temporary record before the manager consumes it.
  if(replayOrbit && usingOrbit && !replayRecordCamera && (orbit.yaw!=0 || orbit.pitch!=0 || zoom.scale!=1)) {
   auto manager=selected.load();auto cache=ReplayCache(manager,camera);
   std::array<unsigned char,0x20> cacheValue{};std::array<unsigned char,0x40> cameraValue{};
   if(cache && Read(cache,0,cacheValue) && Read(camera,0x10,cameraValue)) {
    replayCacheOverlay.Capture(cacheValue);replayCameraOverlay.Capture(cameraValue);
    replayCache=cache;replayRecordCamera=camera;replayRecordManager=manager;replayApplying=true;
   }
  }
 }
 if(replayCall && applyingCamera && usingOrbit && !replayApplying)++replayBlocked;
 obstruction(camera,timing,record);replayApplying=oldReplay;liveApplying=oldLive;applyingCamera=old;
 if(replayCall && ReplayCameraProbe() && Eligible(camera)) {
  const auto now=GetTickCount64();
  if(now-last>=1000){last=now;Snapshot("replay-after-obstruction",camera,timing,record);}
 }
 if(sampled==camera){Snapshot("after-obstruction",camera,timing,record);sampled=nullptr;}
}
void __fastcall ReplayUpdate(void* controller,void*) {
 // The native replay-camera evaluator can run more than once per recorder tick.
 // Undo the previous display pose before it reads its cache, and commit only
 // this evaluation's output. Input remains deduplicated by the recorder serial.
 BeginReplayCameraFrame();
 ++replayEvaluations;
 replayUpdate(controller);
 EndReplayCameraFrame();
}
void TracePoseWrite(const char* kind,void* camera,void* record,const void* caller) {
 if(!ReplayOrbitProbe() || !ObservedReplayPlayback().Available(GetTickCount64()) || !Eligible(camera))return;
 // Bounded by writer callsite and thread: identify the later writer instead of
 // mistaking a successful intermediate calculation for the displayed camera.
 struct Writer {const void* caller{};const char* kind{};ULONGLONG tick{};unsigned count{};};
 thread_local std::array<Writer,24> writers{};
 Writer* slot=nullptr;
 for(auto& writer:writers)if(writer.caller==caller && writer.kind==kind){slot=&writer;break;}
 if(!slot)for(auto& writer:writers)if(!writer.caller){slot=&writer;writer.caller=caller;writer.kind=kind;break;}
 if(!slot)return;
 ++slot->count;const auto now=GetTickCount64();if(now-slot->tick<1000)return;
 std::array<float,8> pose{};std::array<float,4> position{};Read(record,0,pose);Read(camera,0x40,position);
 Log("replay pose writer: kind=%s caller=%x camera=%p record=%p count=%u q=%.6f,%.6f,%.6f,%.6f p=%.6f,%.6f,%.6f object_p=%.6f,%.6f,%.6f",
  kind,reinterpret_cast<unsigned>(caller)-reinterpret_cast<unsigned>(base),camera,record,slot->count,
  pose[0],pose[1],pose[2],pose[3],pose[4],pose[5],pose[6],position[0],position[1],position[2]);
 slot->tick=now;slot->count=0;
}
void __fastcall FinalizePose(void* camera,void*,void* record) {
 finalizePose(camera,record);
 TracePoseWrite("finalize",camera,record,_ReturnAddress());
}
void __fastcall StorePose(void* manager,void*,void* record) {
 storePose(manager,record);
 if(manager==selected.load())TracePoseWrite("cache",ObservedCameraObject(manager),record,_ReturnAddress());
}
bool CurrentDisplayPose(DisplayPose& result) {
 {std::lock_guard lock(displayPoseMutex);result=displayPose;}
 const auto now=GetTickCount64();const auto playback=ObservedReplayPlayback();
 return result.camera && now>=result.tick && now-result.tick<250 && playback.Available(now) &&
     playback.frame>=result.frame && playback.frame-result.frame<=2 &&
     std::abs(playback.time-result.time)<.1 && result.manager==selected.load() &&
     ObservedDisplayCamera()==result.camera && Eligible(result.camera);
}
float* __fastcall ExportCamera(void* owner,void*) {
 auto result=exportCamera(owner);
 DisplayPose latest;void* manager{};
 if(!Read(owner,0x24,manager) || manager!=selected.load())return result;
 const auto camera=ObservedDisplayCamera();
 if(!ObservedReplayPlayback().Available(GetTickCount64()) || !camera || !Eligible(camera))return result;
 // Replay's exported view can come from owner+0x30/+0xa0 rather than the
 // mutable camera object. Return an isolated display copy; never feed the
 // adjusted result into the recorder/cache used as next frame's baseline.
 const auto bytes=static_cast<char*>(owner);
 if(result!=reinterpret_cast<float*>(bytes+0x30) && result!=reinterpret_cast<float*>(bytes+0xa0) &&
    result!=reinterpret_cast<float*>(bytes+0x110))return result;
 alignas(16) thread_local std::array<float,28> output;
 std::array<float,28> native{};
 if(!Read(result,0,native))return result;
 const bool adjusted=CurrentDisplayPose(latest) && manager==latest.manager;
 output=native;
 if(adjusted && !CopyReplayDisplayPose(output,latest.pose))return result;
 // Classify the camera actually exported to rendering, including neutral orbit.
 // The mutable native camera object is not always the rendered replay pose.
 {std::lock_guard lock(exportedPoseMutex);exportedPose={manager,camera,GetTickCount64(),output};}
 thread_local ULONGLONG tick{};const auto now=GetTickCount64();
 if(ReplayCameraProbe() && adjusted && now-tick>=1000) {
  Log("replay camera export: owner=%p manager=%p camera=%p source=%x frame=%llu native_p=%.6f,%.6f,%.6f output_p=%.6f,%.6f,%.6f",
   owner,manager,latest.camera,reinterpret_cast<unsigned>(result)-reinterpret_cast<unsigned>(owner),latest.frame,
   native[16],native[17],native[18],output[16],output[17],output[18]);tick=now;
 }
 return adjusted?output.data():result;
}
void __fastcall ReplayScene(void* self,void*,void* a,void* b,void* c,void* d,void* e) {
 if(_ReturnAddress()==base+0x2889c0)ObserveReplayRenderCamera(a,b);
 replayScene(self,a,b,c,d,e);
}
void __fastcall Look(void* camera,void*,const void* event,void* record) {
 unsigned type{};Read(event,4,type);
 const auto playback=ObservedReplayPlayback();
 const bool replay=ReplayOrbitEnabled() && playback.Available(GetTickCount64()) && ObservedDisplayCamera()==camera;
 if(mode==2 && usingOrbit && type==1 && Eligible(camera) && !GamePaused() && (LiveDrivingCameraState() || replay) && Focus())return;
 look(camera,event,record);
}
}
bool MatchingReplayChaseCamera(const float* a,const float* b) {
 ExportedPose latest;{std::lock_guard lock(exportedPoseMutex);latest=exportedPose;}
 const auto now=GetTickCount64();
 return latest.camera && now>=latest.tick && now-latest.tick<250 &&
     ObservedReplayPlayback().Available(now) && latest.manager==selected.load() &&
     latest.camera==ObservedDisplayCamera() && Eligible(latest.camera) &&
     MatchingSceneCamera(a,latest.record.data()) && MatchingSceneCamera(b,latest.record.data());
}
void ObserveReplayRenderCamera(void* a,void* b) {
 if(ReplayOrbitProbe()) {
  DisplayPose latest;thread_local ULONGLONG tick{};const auto now=GetTickCount64();
  if(now-tick>=1000 && CurrentDisplayPose(latest)) {
   std::array<float,28> first{},second{};
   if(Read(a,0,first) && Read(b,0,second)) {
    float errorA=0,errorB=0;
    for(unsigned row=0;row<4;++row)for(unsigned axis=0;axis<3;++axis) {
     errorA=std::max(errorA,std::abs(first[4+row*4+axis]-latest.pose[row*4+axis]));
     errorB=std::max(errorB,std::abs(second[4+row*4+axis]-latest.pose[row*4+axis]));
    }
    Log("replay rendered camera: frame=%llu camera=%p pose_error=%.6f,%.6f a_p=%.6f,%.6f,%.6f b_p=%.6f,%.6f,%.6f",
     latest.frame,latest.camera,errorA,errorB,first[16],first[17],first[18],second[16],second[17],second[18]);
   }
   tick=now;
  }
 }
}
void BeginReplayCameraFrame() {
 if(replayRecordCamera && replayCache==ReplayCache(replayRecordManager,replayRecordCamera)) {
  std::array<unsigned char,0x20> cacheValue{};std::array<unsigned char,0x40> cameraValue{};
  bool restored=false;
  if(Read(replayCache,0,cacheValue) && replayCacheOverlay.Restore(cacheValue)) {
   std::memcpy(replayCache,cacheValue.data(),cacheValue.size());restored=true;++replayCacheRestored;
  } else {
   ++replayRestoreConflict;
  }
  if(Read(replayRecordCamera,0x10,cameraValue) && replayCameraOverlay.Restore(cameraValue)) {
   std::memcpy(static_cast<char*>(replayRecordCamera)+0x10,cameraValue.data(),cameraValue.size());restored=true;++replayCameraRestored;
  } else {
   ++replayRestoreConflict;
  }
  if(restored)++replayRestored;
 }
 replayRecordCamera=nullptr;replayRecordManager=nullptr;replayCache=nullptr;
}
void EndReplayCameraFrame() {
 if(!replayRecordCamera || replayCache!=ReplayCache(replayRecordManager,replayRecordCamera)) {
  std::lock_guard lock(displayPoseMutex);displayPose={};return;
 }
 std::array<unsigned char,0x20> cacheValue{};std::array<unsigned char,0x40> cameraValue{};
 if(Read(replayCache,0,cacheValue))replayCacheOverlay.Commit(cacheValue);
 if(Read(replayRecordCamera,0x10,cameraValue)) {
  replayCameraOverlay.Commit(cameraValue);
  const auto playback=ObservedReplayPlayback();
  DisplayPose next{replayRecordManager,replayRecordCamera,GetTickCount64(),playback.frame,playback.time};
  std::memcpy(next.pose.data(),cameraValue.data(),cameraValue.size());
  std::lock_guard lock(displayPoseMutex);displayPose=next;
 }
}
void ChaseSelectCamera(void* manager) {selected=manager;}
bool ChaseCameraDiagnostic() {
 char value[8]{};return LoggingEnabled() && GetEnvironmentVariableA("DIRT2VR_CHASE_PROBE",value,sizeof(value))==1 && (value[0]=='1'||value[0]=='2');
}
bool ChaseCameraRequested() {
 return ChaseCameraDiagnostic() || ReplayCamerasEnabled() || (EnvFlag("DIRT2VR_CHASE_FREE_LOOK") && (!EnvFlag("DIRT2VR_HEADSET") || ExtendedViewsEnabled()));
}
bool EnableChaseCamera() {
 if(!ChaseCameraRequested())return true;if(update)return true;if(!SupportedHost())return false;
 if(!EnablePauseObserver())return false;
 char value[8]{};GetEnvironmentVariableA("DIRT2VR_CHASE_PROBE",value,sizeof(value));mode=ChaseCameraDiagnostic() && value[0]=='1'?1:2;
 mouseSensitivity=Sensitivity("DIRT2VR_CHASE_MOUSE_SENSITIVITY");stickSensitivity=Sensitivity("DIRT2VR_CHASE_STICK_SENSITIVITY");
 invert=EnvFlag("DIRT2VR_CHASE_INVERT_VERTICAL");
 char mouseValue[16]{};const auto mouseLength=GetEnvironmentVariableA("DIRT2VR_CHASE_MOUSE_MODE",mouseValue,sizeof(mouseValue));
 mouseMode=ParseChaseMouseMode(mouseLength<sizeof(mouseValue)?mouseValue:"");
 base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 const unsigned char updateGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x14,0x01,0,0};
 const unsigned char collisionGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x48,0x56};
 const unsigned char targetGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x6c,0x56};
 const unsigned char lookGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x28,0x56};
 const unsigned char replayGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xc4,0x02,0,0};
 const unsigned char finalizeGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x28,0x56,0x57};
 const unsigned char storeGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x08,0x56,0x57};
 const unsigned char exportGuard[]={0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x2c,0x56};
 const unsigned char sceneGuard[]={0x81,0xec,0xf8,0,0,0,0x56,0x8b,0xf1};
 if(memcmp(base+0x72db40,updateGuard,sizeof(updateGuard))||memcmp(base+0x71f530,collisionGuard,sizeof(collisionGuard))||
    memcmp(base+0x70e770,targetGuard,sizeof(targetGuard))||memcmp(base+0x6f70b0,lookGuard,sizeof(lookGuard))||
    (ReplayCamerasEnabled() && (memcmp(base+0x92cb10,replayGuard,sizeof(replayGuard)) ||
     memcmp(base+0x6eafd0,finalizeGuard,sizeof(finalizeGuard)) || memcmp(base+0x7258e0,storeGuard,sizeof(storeGuard)) ||
     memcmp(base+0x7293c0,exportGuard,sizeof(exportGuard)) || memcmp(base+0x33ad10,sceneGuard,sizeof(sceneGuard)))))return false;
 auto status=MH_Initialize();if(status!=MH_OK&&status!=MH_ERROR_ALREADY_INITIALIZED)return false;
 std::array<unsigned,9> created{};unsigned count{};
 for(auto pair:{std::pair{0x72db40u,reinterpret_cast<void*>(Update)},std::pair{0x71f530u,reinterpret_cast<void*>(Obstruction)},
                std::pair{0x70e770u,reinterpret_cast<void*>(Target)},std::pair{0x6f70b0u,reinterpret_cast<void*>(Look)}}) {
  void** destination=pair.first==0x72db40?reinterpret_cast<void**>(&update):pair.first==0x71f530?reinterpret_cast<void**>(&obstruction):pair.first==0x70e770?reinterpret_cast<void**>(&target):reinterpret_cast<void**>(&look);
  status=MH_CreateHook(base+pair.first,pair.second,destination);if(status!=MH_OK)break;
  created[count++]=pair.first;
 }
 if(status==MH_OK && ReplayCamerasEnabled()) {
  status=MH_CreateHook(base+0x92cb10,reinterpret_cast<void*>(ReplayUpdate),reinterpret_cast<void**>(&replayUpdate));
  if(status==MH_OK)created[count++]=0x92cb10;
  if(status==MH_OK && ReplayCameraProbe()) {
   status=MH_CreateHook(base+0x6eafd0,reinterpret_cast<void*>(FinalizePose),reinterpret_cast<void**>(&finalizePose));
   if(status==MH_OK)created[count++]=0x6eafd0;
  }
  if(status==MH_OK && ReplayCameraProbe()) {
   status=MH_CreateHook(base+0x7258e0,reinterpret_cast<void*>(StorePose),reinterpret_cast<void**>(&storePose));
   if(status==MH_OK)created[count++]=0x7258e0;
  }
  if(status==MH_OK) {
   status=MH_CreateHook(base+0x7293c0,reinterpret_cast<void*>(ExportCamera),reinterpret_cast<void**>(&exportCamera));
   if(status==MH_OK)created[count++]=0x7293c0;
  }
  if(status==MH_OK && ReplayCameraProbe() && !EnvFlag("DIRT2VR_INNER_REPLAY") && !EnvFlag("DIRT2VR_HEADSET")) {
   status=MH_CreateHook(base+0x33ad10,reinterpret_cast<void*>(ReplayScene),reinterpret_cast<void**>(&replayScene));
   if(status==MH_OK)created[count++]=0x33ad10;
  }
 }
 if(status==MH_OK)for(unsigned i=0;i<count;++i){status=EnableRecordedHook(base+created[i]);if(status!=MH_OK)break;}
 if(status!=MH_OK) {
  for(unsigned i=0;i<count;++i){MH_DisableHook(base+created[i]);MH_RemoveHook(base+created[i]);}
  update=nullptr;obstruction=nullptr;target=nullptr;look=nullptr;replayUpdate=nullptr;finalizePose=nullptr;storePose=nullptr;exportCamera=nullptr;replayScene=nullptr;
 }
 Log("chase camera: hooks=%s mode=%u mouse=%.2f stick=%.2f invert=%d",MH_StatusToString(status),mode,mouseSensitivity,stickSensitivity,invert);
 return status==MH_OK;
}
}
