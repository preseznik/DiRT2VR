#include "car_tracker.h"
#include "common.h"
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>
#include <string>
#include <cstdio>
#include <unordered_map>

namespace vr {
namespace {
using Microsoft::WRL::ComPtr;
std::atomic<bool> active{};
std::mutex mutex;
// The first 64 bytes the game last wrote into each small constant buffer.
std::unordered_map<ID3D11Resource*,std::array<float,16>> models;
struct Candidate { std::array<float,16> m; UINT indices; };
std::vector<Candidate> frame;
struct Track {
    int id; std::array<float,16> m; float p[3]; float v[3]; float travelled; ULONGLONG seen,born; UINT size;
};
std::vector<Track> tracks;
// A car: it has driven a little, and its largest draw is body-sized.
bool IsCar(const Track& t){return t.travelled>20 && t.size>=1500;}
int nextId=1;
ULONGLONG lastFrame{};
// Time spent here (draw lookups and frame matching), for the 2-second report.
std::atomic<long long> spent{};unsigned spentFrames{};
long long Now(){LARGE_INTEGER t{};QueryPerformanceCounter(&t);return t.QuadPart;}

bool Rigid(const float* m) {
    for(int r=0;r<3;++r) {
        const float l=m[r*4]*m[r*4]+m[r*4+1]*m[r*4+1]+m[r*4+2]*m[r*4+2];
        if(!std::isfinite(l) || l<.8f || l>1.25f)return false;
    }
    const float x=m[12],y=m[13],z=m[14];
    return std::isfinite(x+y+z) && x*x+z*z>25.f && std::abs(m[15]-1)<.01f;
}
float Distance(const float* a,const float* b) {
    const float dx=a[0]-b[0],dy=a[1]-b[1],dz=a[2]-b[2];return std::sqrt(dx*dx+dy*dy+dz*dz);
}
}

bool CarTrackerActive(){return active.load(std::memory_order_relaxed);}

void CarTrackerConstant(ID3D11Resource* resource,const void* data,UINT size) {
    if(!active.load(std::memory_order_relaxed) || !resource || !data || size<64 || size>512)return;
    std::array<float,16> m;std::memcpy(m.data(),data,sizeof m);
    std::lock_guard lock(mutex);models[resource]=m;
}

void CarTrackerDraw(ID3D11DeviceContext* context,UINT indices) {
    // Small draws (particles, decals, wheels' details) are not needed to find a car.
    if(!active.load(std::memory_order_relaxed) || indices<300 || context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return;
    const long long start=Now();
    struct Spent{long long s;~Spent(){spent+=Now()-s;}} timer{start};
    ComPtr<ID3D11Buffer> globals;context->VSGetConstantBuffers(0,1,&globals);
    if(!globals)return;
    std::lock_guard lock(mutex);
    const auto found=models.find(globals.Get());
    if(found==models.end() || !Rigid(found->second.data()) || frame.size()>=4096)return;
    frame.push_back({found->second,indices});
}

void CarTrackerFrame(bool on,bool playing) {
    const auto now=GetTickCount64();
    std::lock_guard lock(mutex);
    if(!on) {
        if(active.exchange(false)){models.clear();frame.clear();tracks.clear();}
        return;
    }
    active=true;
    const long long frameStart=Now();
    const float dt=lastFrame?float(now-lastFrame)*.001f:0;lastFrame=now;
    // A car's parts (body, wheels, details) lie within 3 m of each other; keep the largest draw per place.
    std::vector<Candidate> places;
    for(const auto& c:frame) {
        auto same=std::find_if(places.begin(),places.end(),[&](const Candidate& p){return Distance(p.m.data()+12,c.m.data()+12)<3.f;});
        if(same==places.end())places.push_back(c);
        else if(c.indices>same->indices)*same=c;
    }
    frame.clear();
    // Nearest track within 3 m; cars move well under that between frames.
    std::vector<bool> used(tracks.size());
    for(const auto& place:places) {
        int best=-1;float bestDistance=3.f;
        for(size_t i=0;i<tracks.size();++i)
            if(!used[i]){const float d=Distance(tracks[i].p,place.m.data()+12);if(d<bestDistance){bestDistance=d;best=int(i);}}
        if(best<0) {
            Track t{nextId++,place.m,{place.m[12],place.m[13],place.m[14]},{},0,now,now,place.indices};
            tracks.push_back(t);used.push_back(true);continue;
        }
        auto& t=tracks[best];used[best]=true;
        if(playing && dt>0) {
            for(int k=0;k<3;++k){const float speed=(place.m[12+k]-t.p[k])/dt;t.v[k]=t.v[k]*.7f+speed*.3f;}
            t.travelled+=bestDistance;
        }
        t.m=place.m;t.p[0]=place.m[12];t.p[1]=place.m[13];t.p[2]=place.m[14];t.seen=now;t.size=std::max(t.size,place.indices);
    }
    // Forget places not drawn for a while (cars off-screen keep their last pose briefly). While time
    // stands still (a paused race or replay, Photo Mode) the cars stay where they were.
    if(playing)std::erase_if(tracks,[&](const Track& t){return now-t.seen>(t.travelled>20?5000ull:1000ull);});
    if(tracks.size()>256)tracks.resize(256);
    spent+=Now()-frameStart;++spentFrames;
    static ULONGLONG reported{};
    if(LoggingEnabled() && now-reported>=2000) {
        reported=now;int cars{};
        std::string list;for(const auto& t:tracks)if(t.travelled>20){++cars;char x[48]{};std::snprintf(x,sizeof x," %d:%u/%.0fm",t.id,t.size,t.travelled);list+=x;}
        LARGE_INTEGER f{};QueryPerformanceFrequency(&f);
        const double us=spentFrames?double(spent.exchange(0))*1e6/double(f.QuadPart)/spentFrames:0;spentFrames=0;
        Log("car tracker: places=%zu tracks=%zu moving=%d playing=%d cpu_us_per_frame=%.1f moving_tracks(id:indices/travelled):%s",places.size(),tracks.size(),cars,playing,us,list.c_str());
    }
}

std::vector<TrackedCar> TrackedCars() {
    std::lock_guard lock(mutex);
    std::vector<TrackedCar> cars;
    for(const auto& t:tracks) {
        if(!IsCar(t))continue;
        TrackedCar c;c.id=t.id;std::memcpy(c.matrix,t.m.data(),sizeof c.matrix);std::memcpy(c.velocity,t.v,sizeof c.velocity);c.travelled=t.travelled;
        cars.push_back(c);
    }
    return cars;
}
}
