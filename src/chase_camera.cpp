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
#include <MinHook.h>
#include <dinput.h>
#include <xinput.h>
#include <atomic>
#include <array>
#include <cstring>
#include <cmath>
#include <intrin.h>
#include <cstdlib>

namespace vr {
namespace {
using UpdateFn=void (__thiscall*)(void*,const float*,void*);
using TargetFn=float* (__thiscall*)(void*,float*,void*,bool);
using LookFn=void (__thiscall*)(void*,const void*,void*);
UpdateFn update{}, obstruction{};
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
}
void __fastcall Update(void* camera,void*,const float* timing,void* record) {
 const bool eligible=Eligible(camera);
 if(eligible) {
  float dt{},speed{};
  const auto now=GetTickCount64();Read(timing,0,dt);
  const bool ready=mode==2 && !GamePaused() && LiveDrivingCameraState() && Focus() && dt>0 && dt<=.25f && Movement(camera,speed);
  UpdateChaseCursor(Focus(),ready);
  if(orbitCamera!=camera || now-inputTick>250){orbit.Reset();zoom.Reset();armed=false;orbitCamera=camera;}
  inputTick=now;
  usingOrbit=false;
  if(ready) {
   const auto inputs=Poll(dt);
   if(!armed){if(inputs.neutral)armed=true;}
   else if(orbit.Step(dt,speed,inputs.x,inputs.y,inputs.held,inputs.rearHeld) && zoom.Step(dt,inputs.wheel))usingOrbit=true;
  } else {armed=false;orbit.Reset();}
  // Feed the game's native look direction. It computes yaw and distance using
  // its ordinary chase solver, then runs the original obstruction pass.
  if(usingOrbit) {
   const float x=std::sin(orbit.yaw)*.5f;
   const float y=std::cos(orbit.yaw)*.5f-.1f;
   std::memcpy(static_cast<char*>(record)+0xa4,&x,4);std::memcpy(static_cast<char*>(record)+0xa8,&y,4);
  }
 }
 update(camera,timing,record);
 if(!eligible || !ChaseCameraDiagnostic())return;
 const auto now=GetTickCount64();if(now-last<1000)return;
 last=now;sampled=camera;Snapshot("before-obstruction",camera,timing,record);
}
float* __fastcall Target(void* camera,void*,float* output,void* record,bool flag) {
 auto result=target(camera,output,record,flag);
 if(camera==applyingCamera && _ReturnAddress()==base+0x71f553 && result==output && usingOrbit && (orbit.pitch!=0 || zoom.scale!=1)) {
  std::array<float,8> pose{};Read(record,0,pose);
  if(OrbitChasePose(pose,{output[12],output[13],output[14]},0,orbit.pitch,zoom.scale)) {
   std::memcpy(record,pose.data(),sizeof(pose));
   std::memcpy(static_cast<char*>(record)+0xf0,pose.data()+4,16);
   std::memcpy(static_cast<char*>(record)+0x100,pose.data(),16);
  }
 }
 return result;
}
void __fastcall Obstruction(void* camera,void*,const float* timing,void* record) {
 const auto old=applyingCamera;applyingCamera=Eligible(camera)?camera:nullptr;
 obstruction(camera,timing,record);applyingCamera=old;
 if(sampled==camera){Snapshot("after-obstruction",camera,timing,record);sampled=nullptr;}
}
void __fastcall Look(void* camera,void*,const void* event,void* record) {
 unsigned type{};Read(event,4,type);
 if(mode==2 && usingOrbit && type==1 && Eligible(camera) && !GamePaused() && LiveDrivingCameraState() && Focus())return;
 look(camera,event,record);
}
}
void ChaseSelectCamera(void* manager) {selected=manager;}
bool ChaseCameraDiagnostic() {
 char value[8]{};return LoggingEnabled() && GetEnvironmentVariableA("DIRT2VR_CHASE_PROBE",value,sizeof(value))==1 && (value[0]=='1'||value[0]=='2');
}
bool ChaseCameraRequested() {
 return ChaseCameraDiagnostic() || (EnvFlag("DIRT2VR_CHASE_FREE_LOOK") && (!EnvFlag("DIRT2VR_HEADSET") || ExtendedViewsEnabled()));
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
 if(memcmp(base+0x72db40,updateGuard,sizeof(updateGuard))||memcmp(base+0x71f530,collisionGuard,sizeof(collisionGuard))||
    memcmp(base+0x70e770,targetGuard,sizeof(targetGuard))||memcmp(base+0x6f70b0,lookGuard,sizeof(lookGuard)))return false;
 auto status=MH_Initialize();if(status!=MH_OK&&status!=MH_ERROR_ALREADY_INITIALIZED)return false;
 std::array<unsigned,4> created{};unsigned count{};
 for(auto pair:{std::pair{0x72db40u,reinterpret_cast<void*>(Update)},std::pair{0x71f530u,reinterpret_cast<void*>(Obstruction)},
                std::pair{0x70e770u,reinterpret_cast<void*>(Target)},std::pair{0x6f70b0u,reinterpret_cast<void*>(Look)}}) {
  void** destination=pair.first==0x72db40?reinterpret_cast<void**>(&update):pair.first==0x71f530?reinterpret_cast<void**>(&obstruction):pair.first==0x70e770?reinterpret_cast<void**>(&target):reinterpret_cast<void**>(&look);
  status=MH_CreateHook(base+pair.first,pair.second,destination);if(status!=MH_OK)break;
  created[count++]=pair.first;
 }
 if(status==MH_OK)for(unsigned rva:{0x72db40,0x71f530,0x70e770,0x6f70b0}){status=EnableRecordedHook(base+rva);if(status!=MH_OK)break;}
 if(status!=MH_OK) {
  for(unsigned i=0;i<count;++i){MH_DisableHook(base+created[i]);MH_RemoveHook(base+created[i]);}
  update=nullptr;obstruction=nullptr;target=nullptr;look=nullptr;
 }
 Log("chase camera: hooks=%s mode=%u mouse=%.2f stick=%.2f invert=%d",MH_StatusToString(status),mode,mouseSensitivity,stickSensitivity,invert);
 return status==MH_OK;
}
}
