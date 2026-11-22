#include "car_follow.h"
#include "common.h"
#include "photo_mode.h"
#include "photo_overlay.h"
#include "replay_camera.h"
#include "scene_camera.h"
#include <algorithm>
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
ULONGLONG lostSince{},followStart{};
// Which way each car faces along its matrix's third row (+1 or -1), learned from its motion.
std::unordered_map<int,float> facing;
// Smoothed chase view, so small body movements and track noise do not shake the camera.
bool smoothed{};float position[3]{},direction[3]{};ULONGLONG smoothTick{};
// The views last handed to rendering, to recognise them there (the export can run more than once a frame).
std::array<std::array<float,28>,4> exported{};unsigned exportNext{};ULONGLONG exportTick{};

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
// Cars that can be followed: seen in the last few seconds, so their position is still known.
std::vector<TrackedCar> Followable() {
    auto cars=TrackedCars();
    // Out of sight for long only when another car's trail tells where it is (cars behind it on the road).
    std::erase_if(cars,[](const TrackedCar& c){return c.unseenMs>8000 && !c.guessFromTrail;});
    return cars;
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
        if(!replay){followed=0;smoothed=false;exportTick=0;held5=f5;held6=f6;}
        else if((f5 && !held5) || (f6 && !held6)) {
            // In car-number order; past either end, the replay's own cameras.
            const auto cars=Followable();count=int(cars.size());
            int at=-1;for(int i=0;i<count;++i)if(cars[i].id==followed)at=i;
            if(f6)at=at<0?0:at+1;
            else at=at<0?count-1:at-1;
            followed=at>=0 && at<count?cars[at].id:0;
            smoothed=false;lostSince=0;followStart=GetTickCount64();index=followed?at+1:0;
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
    const TrackedCar* found{};for(const auto& c:cars)if(c.id==id)found=&c;
    const auto now=GetTickCount64();
    ULONGLONG started{};{std::lock_guard lock(mutex);started=followStart;}
    // Out of sight for a while (the replay cut away, or the car is hidden): back to the replay's cameras.
    // A car chosen while out of the picture gets a few seconds to come into view.
    if(!found || (found->unseenMs>1500 && now-started>3000)) {
        std::lock_guard lock(mutex);
        if(followed==id) {
            followed=0;lostSince=0;exportTick=0;
            Log("car follow: car %d out of sight; replay cameras",found?found->number:0);
            PhotoShowMessage(L"The car is out of sight   \u00b7   replay cameras   \u00b7   F5 / F6 to follow a car");
        }
        return nullptr;
    }
    // While it is out of the picture: where it probably is by now, heading along the road.
    TrackedCar car=*found;
    if(car.unseenMs>100) {
        const float speed=std::sqrt(car.velocity[0]*car.velocity[0]+car.velocity[1]*car.velocity[1]+car.velocity[2]*car.velocity[2]);
        for(int i=0;i<3;++i){car.matrix[12+i]=car.guess[i];car.matrix[8+i]=car.guessForward[i];car.velocity[i]=car.guessForward[i]*speed;}
    }
    std::array<float,28> chase;
    if(!CarChaseRecord(car,native,chase))return nullptr;
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
    exported[exportNext++%exported.size()]=output;exportTick=now;
    return output.data();
}

bool MatchingFollowCamera(const float* a,const float* b) {
    std::lock_guard lock(mutex);
    const auto now=GetTickCount64();
    if(!followed || !exportTick || now-exportTick>=250)return false;
    for(const auto& view:exported)if(MatchingSceneCamera(a,view.data()) && MatchingSceneCamera(b,view.data()))return true;
    static ULONGLONG reported{};
    if(now-reported>=1000) {
        reported=now;float nearest=1e9f;
        for(const auto& view:exported){const float dx=a[16]-view[16],dy=a[17]-view[17],dz=a[18]-view[18];nearest=std::min(nearest,std::sqrt(dx*dx+dy*dy+dz*dz));}
        Log("car follow: render camera does not match the follow view (nearest %.3f m, export %llu ms ago)",nearest,now-exportTick);
    }
    return false;
}
}
