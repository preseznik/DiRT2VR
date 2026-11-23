#include "lab_camera.h"
#include "common.h"
#include "scene_camera.h"
#include <windows.h>
#include <array>
#include <atomic>
#include <cmath>
#include <fstream>
#include <mutex>
#include <sstream>
#include <string>

namespace vr {
namespace {
std::mutex mutex;
// The requested pose; valid while camera.request exists.
bool requested{};float eye[3]{},look[3]{},fov{};
std::string lastText;
ULONGLONG pollTick{};
std::atomic<bool> active{};
// The views last handed to rendering, to recognise them there.
std::array<float,28> exported{};ULONGLONG exportTick{};

bool Enabled() {
    static const bool enabled=[] {wchar_t value[8]{};return GetEnvironmentVariableW(L"DIRT2VR_CAPTURE_REQUESTS",value,8)>0 && !wcscmp(value,L"1");}();
    return enabled && LoggingEnabled();
}
void Normalize(float* v) {
    const float length=std::sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);
    if(length>0)for(int i=0;i<3;++i)v[i]/=length;
}
// "x y z yaw pitch [fov]" or "x y z look tx ty tz [fov]".
bool Parse(const std::string& text,float* position,float* direction,float& field) {
    std::istringstream in(text);std::string word;float values[6]{};
    if(!(in>>values[0]>>values[1]>>values[2]>>word))return false;
    field=0;
    if(word=="look") {
        if(!(in>>values[3]>>values[4]>>values[5]))return false;
        for(int i=0;i<3;++i)direction[i]=values[3+i]-values[i];
    } else {
        float pitch{};
        try{values[3]=std::stof(word);}catch(...){return false;}
        if(!(in>>pitch))return false;
        const float yaw=values[3]*.017453293f;pitch*=.017453293f;
        direction[0]=std::sin(yaw)*std::cos(pitch);direction[1]=std::sin(pitch);direction[2]=std::cos(yaw)*std::cos(pitch);
    }
    float degrees{};if(in>>degrees)field=degrees*.017453293f;
    for(int i=0;i<3;++i){position[i]=values[i];if(!std::isfinite(position[i]) || !std::isfinite(direction[i]))return false;}
    Normalize(direction);
    // Straight up or down has no heading; keep it a little off vertical.
    if(std::abs(direction[1])>.999f)return false;
    return !(field<0 || field>=3.1f);
}
}

void LabCameraPoll() {
    if(!Enabled())return;
    const auto now=GetTickCount64();
    if(now-pollTick<250)return;
    pollTick=now;
    std::ifstream input(Output()/"camera.request");
    std::string text;
    if(input)std::getline(input,text);
    std::lock_guard lock(mutex);
    if(!input || text.empty()) {
        if(requested)Log("lab camera: released");
        requested=false;lastText.clear();active=false;return;
    }
    if(text==lastText)return;
    lastText=text;
    float position[3]{},direction[3]{},field{};
    if(!Parse(text,position,direction,field)) {
        Log("lab camera: unreadable request \"%s\"",text.c_str());
        requested=false;active=false;return;
    }
    for(int i=0;i<3;++i){eye[i]=position[i];look[i]=direction[i];}
    fov=field;requested=true;active=true;
    Log("lab camera: p=%.3f,%.3f,%.3f forward=%.4f,%.4f,%.4f fov=%.4f",eye[0],eye[1],eye[2],look[0],look[1],look[2],fov);
}

bool LabCameraActive(){return active.load();}

float* LabCameraExport(void* owner,const float* native) {
    if(!native || !active.load())return nullptr;
    // The displayed camera only (mirrors and other exports keep their own views).
    void* manager{};SIZE_T read{};
    if(!ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(owner)+0x24,&manager,sizeof manager,&read) || !manager)return nullptr;
    const auto displayed=ObservedDisplayManager();
    if(displayed && manager!=displayed)return nullptr;
    alignas(16) thread_local std::array<float,28> output;
    for(int i=0;i<28;++i)output[i]=native[i];
    std::lock_guard lock(mutex);
    if(!requested)return nullptr;
    // Keep the engine record's handedness: up = h * (forward x right), left = -right (as CarChaseRecord).
    const float cross[3]{native[5]*native[14]-native[6]*native[13],native[6]*native[12]-native[4]*native[14],native[4]*native[13]-native[5]*native[12]};
    const float h=-(cross[0]*native[8]+cross[1]*native[9]+cross[2]*native[10])>=0?1.f:-1.f;
    float right[3]{h*look[2],0,-h*look[0]};Normalize(right);
    const float up[3]{h*(look[1]*right[2]-look[2]*right[1]),h*(look[2]*right[0]-look[0]*right[2]),h*(look[0]*right[1]-look[1]*right[0])};
    for(int i=0;i<3;++i){output[4+i]=up[i];output[8+i]=-right[i];output[12+i]=look[i];output[16+i]=eye[i];}
    if(fov>0)output[20]=fov;
    for(int i:{4,5,6,8,9,10,12,13,14,16,17,18,20})if(!std::isfinite(output[i]))return nullptr;
    static ULONGLONG logged{};
    const auto now=GetTickCount64();
    if(now-logged>=2000){Log("lab camera: export owner=%p manager=%p displayed=%p",owner,manager,displayed);logged=now;}
    exported=output;exportTick=now;
    return output.data();
}

bool MatchingLabCamera(const float* a,const float* b) {
    if(!active.load())return false;
    std::lock_guard lock(mutex);
    return exportTick && GetTickCount64()-exportTick<500 && MatchingSceneCamera(a,exported.data()) && MatchingSceneCamera(b,exported.data());
}
}
