#include "car_follow.h"
#include "common.h"
#include "photo_mode.h"
#include "photo_overlay.h"
#include "replay_camera.h"
#include "scene_camera.h"
#include <atomic>
#include <cmath>
#include <cstdio>
#include <mutex>
#include <string>
#include <unordered_map>

namespace vr {
namespace {
std::mutex mutex;
int followed{};                 // Tracked car id; 0 = the replay's own cameras.
bool held5{},held6{};
ULONGLONG lostSince{};
// Which way each car faces along its matrix's third row (+1 or -1), learned from its motion.
std::unordered_map<int,float> facing;
// Smoothed chase view, so small body movements and track noise do not shake the camera.
bool smoothed{};float position[3]{},direction[3]{};ULONGLONG smoothTick{};

bool Focused() {
    DWORD process{};GetWindowThreadProcessId(GetForegroundWindow(),&process);
    return process==GetCurrentProcessId();
}
bool Down(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
void Normalize(float* v){const float l=std::sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);if(l>1e-6f)for(int i=0;i<3;++i)v[i]/=l;}
// The car's forward: the matrix row along its length, signed by the way it moves.
void Forward(const TrackedCar& car,float* out) {
    float sign=1;
    {
        const float* row=car.matrix+8;
        const float speed=std::sqrt(car.velocity[0]*car.velocity[0]+car.velocity[1]*car.velocity[1]+car.velocity[2]*car.velocity[2]);
        auto& known=facing[car.id];
        if(speed>3)known=(row[0]*car.velocity[0]+row[1]*car.velocity[1]+row[2]*car.velocity[2])>=0?1.f:-1.f;
        sign=known!=0?known:1.f;
    }
    for(int i=0;i<3;++i)out[i]=car.matrix[8+i]*sign;
    Normalize(out);
}
void Message(int index,int count) {
    wchar_t text[96]{};
    if(index)swprintf_s(text,L"Following car %d of %d   \u00b7   F5 / F6 to change",index,count);
    else swprintf_s(text,L"Replay cameras   \u00b7   F5 / F6 to follow a car");
    PhotoShowMessage(text);
}
}

bool CarChaseRecord(const TrackedCar& car,const float* native,std::array<float,28>& out,float behind,float above) {
    if(!native)return false;
    for(int i=0;i<28;++i)out[i]=native[i];
    float forward[3];{std::lock_guard lock(mutex);Forward(car,forward);}
    const float target[3]{car.matrix[12],car.matrix[13]+1.f,car.matrix[14]};
    float eye[3];for(int i=0;i<3;++i)eye[i]=car.matrix[12+i]-forward[i]*behind;
    eye[1]+=above;
    float look[3]{target[0]-eye[0],target[1]-eye[1],target[2]-eye[2]};Normalize(look);
    // Keep the engine record's handedness: up = h * (forward x right), left = -right.
    const float cross[3]{native[5]*native[14]-native[6]*native[13],native[6]*native[12]-native[4]*native[14],native[4]*native[13]-native[5]*native[12]};
    const float h=-(cross[0]*native[8]+cross[1]*native[9]+cross[2]*native[10])>=0?1.f:-1.f;
    float right[3]{h*look[2],0,-h*look[0]};Normalize(right); // h * (world up x look)
    const float up[3]{h*(look[1]*right[2]-look[2]*right[1]),h*(look[2]*right[0]-look[0]*right[2]),h*(look[0]*right[1]-look[1]*right[0])};
    for(int i=0;i<3;++i){out[4+i]=up[i];out[8+i]=-right[i];out[12+i]=look[i];out[16+i]=eye[i];}
    for(int i:{4,5,6,8,9,10,12,13,14,16,17,18})if(!std::isfinite(out[i]))return false;
    return up[1]>0;
}

void CarFollowPresent(IDXGISwapChain* swapchain) {
    if(!PhotoModeRequested())return;
    const bool replay=ObservedReplayPlayback().Available(GetTickCount64()) && !PhotoModeActive();
    const bool focused=Focused();
    const bool f5=replay && focused && Down(VK_F5),f6=replay && focused && Down(VK_F6);
    bool show=false;int index=0,count=0;
    {
        std::lock_guard lock(mutex);
        if(!replay){followed=0;smoothed=false;held5=f5;held6=f6;}
        else if((f5 && !held5) || (f6 && !held6)) {
            const auto cars=TrackedCars();
            if(f6) { // Next car; after the last one, the replay's own cameras.
                int next=0;for(const auto& car:cars)if(car.id>followed){next=car.id;break;}
                followed=next;
            } else { // Previous car; from the replay's cameras, the last car.
                int previous=0;for(const auto& car:cars)if(followed==0 || car.id<followed)previous=car.id;
                followed=previous;
            }
            smoothed=false;lostSince=0;count=int(cars.size());
            for(int i=0;i<count;++i)if(cars[i].id==followed)index=i+1;
            show=true;
            Log("car follow: followed=%d (%d of %d)",followed,index,count);
        }
        held5=f5;held6=f6;
    }
    if(show)Message(index,count);
    if(replay)PhotoPanelDraw(swapchain,PhotoPanelState{}); // Only the message, while it lasts.
}

float* CarFollowExport(void* owner,const float* native) {
    if(!native || PhotoModeActive())return nullptr;
    int id{};{std::lock_guard lock(mutex);id=followed;}
    if(!id)return nullptr;
    void* manager{};SIZE_T read{};
    if(!ReadProcessMemory(GetCurrentProcess(),static_cast<char*>(owner)+0x24,&manager,sizeof manager,&read) || !manager || manager!=ObservedDisplayManager())return nullptr;
    const auto cars=TrackedCars();
    const TrackedCar* car{};for(const auto& c:cars)if(c.id==id)car=&c;
    const auto now=GetTickCount64();
    if(!car) {
        // Lost from view (the camera cut away or the car left the picture): back to the replay's cameras.
        std::lock_guard lock(mutex);
        if(!lostSince)lostSince=now;
        if(now-lostSince>800){followed=0;lostSince=0;Log("car follow: car %d lost; replay cameras",id);}
        return nullptr;
    }
    std::array<float,28> chase;
    if(!CarChaseRecord(*car,native,chase))return nullptr;
    alignas(16) thread_local std::array<float,28> output;
    std::lock_guard lock(mutex);
    lostSince=0;
    const float dt=smoothTick?std::min(.1f,float(now-smoothTick)*.001f):0;smoothTick=now;
    if(!smoothed){for(int i=0;i<3;++i){position[i]=chase[16+i];direction[i]=chase[12+i];}smoothed=true;}
    else {
        const float keep=std::exp(-dt/.12f);
        for(int i=0;i<3;++i){position[i]=chase[16+i]+(position[i]-chase[16+i])*keep;direction[i]=chase[12+i]+(direction[i]-chase[12+i])*keep;}
        Normalize(direction);
    }
    // Rebuild the basis around the smoothed direction (same handedness as the chase record).
    output=chase;
    float right[3]{-chase[8],-chase[9],-chase[10]};
    const float dot=right[0]*direction[0]+right[1]*direction[1]+right[2]*direction[2];
    for(int i=0;i<3;++i)right[i]-=direction[i]*dot;
    Normalize(right);
    const float sign=(chase[4]*(direction[1]*right[2]-direction[2]*right[1])+chase[5]*(direction[2]*right[0]-direction[0]*right[2])+chase[6]*(direction[0]*right[1]-direction[1]*right[0]))>=0?1.f:-1.f;
    const float up[3]{sign*(direction[1]*right[2]-direction[2]*right[1]),sign*(direction[2]*right[0]-direction[0]*right[2]),sign*(direction[0]*right[1]-direction[1]*right[0])};
    for(int i=0;i<3;++i){output[4+i]=up[i];output[8+i]=-right[i];output[12+i]=direction[i];output[16+i]=position[i];}
    return output.data();
}
}
