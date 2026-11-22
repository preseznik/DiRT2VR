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
    int number{}; // Car number, given once it moves like a car; kept when it comes back into view.
    ULONGLONG stillSince{}; // Not moving while time runs (since then); 0 while it moves.
    double seenRun{};       // Replay time when last seen (time that only runs while the replay plays).
    struct Point { double time; float p[3]; };
    std::vector<Point> path; // Where it went, every 0.1 s of replay time, for the last minute.
};
// Replay time: only runs while the replay (or the race) plays.
double runTime{};
std::vector<Track> tracks;
int nextId=1,nextNumber=1;
// Moves like a car: it has driven a little, and its largest draw is body-sized.
bool Moving(const Track& t){return t.travelled>20 && t.size>=1500;}
// A car: moving, with a body at least half the size of the largest car's (not a smaller moving object),
// and not standing still for long while time runs (a parked look-alike, or a car that stopped for good).
ULONGLONG trackerClock{};
bool IsCar(const Track& t,UINT largest){return Moving(t) && t.size*2>=largest && !(t.stillSince && trackerClock-t.stillSince>4000);}
UINT LargestBody(){UINT largest{};for(const auto& t:tracks)if(Moving(t))largest=std::max(largest,t.size);return largest;}
bool SameBody(UINT a,UINT b){return (a>b?a-b:b-a)*100<=std::max(a,b)*15;}
// How long cars that left the picture are remembered while time runs (to switch back to them).
constexpr ULONGLONG KeepCarMs=30000;
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
// Where a car out of the picture is probably now. Cars drive the same road: if another car passed the
// place where this one was last seen some seconds earlier, this one is now about where that car was that
// many seconds ago. Otherwise straight on from its last speed (for a few seconds at most).
// forward: its direction of travel there. True when another car's trail gave the answer.
bool Guess(const Track& car,float* out,float* forward) {
    const double away=runTime-car.seenRun;
    std::memcpy(out,car.p,sizeof car.p);
    if(forward)std::memcpy(forward,car.m.data()+8,3*sizeof(float));
    if(away<.3)return false;
    float bestDistance=10.f;bool found=false;
    for(const auto& other:tracks) {
        if(&other==&car || !other.number || other.path.size()<2)continue;
        // Where the other car's trail passes closest to the last sighting.
        size_t nearest=0;float distance=1e9f;
        for(size_t i=0;i<other.path.size();++i){const float d=Distance(other.path[i].p,car.p);if(d<distance){distance=d;nearest=i;}}
        const double lag=car.seenRun-other.path[nearest].time; // How far behind the other car it was.
        if(distance>=bestDistance || lag<=0)continue;
        const double target=runTime-lag;
        if(target<other.path.front().time || target>other.path.back().time)continue;
        size_t at=nearest;while(at+1<other.path.size() && other.path[at+1].time<=target)++at;
        const auto& a=other.path[at];const auto& b=other.path[std::min(at+1,other.path.size()-1)];
        const float f=b.time>a.time?float((target-a.time)/(b.time-a.time)):0.f;
        for(int k=0;k<3;++k)out[k]=a.p[k]+(b.p[k]-a.p[k])*std::clamp(f,0.f,1.f);
        if(forward && Distance(a.p,b.p)>.01f){const float l=Distance(a.p,b.p);for(int k=0;k<3;++k)forward[k]=(b.p[k]-a.p[k])/l;}
        bestDistance=distance;found=true;
    }
    if(!found) {
        const float ahead=float(std::min(away,8.0));
        for(int k=0;k<3;++k)out[k]=car.p[k]+car.v[k]*ahead;
    }
    return found;
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
    const long long frameStart=Now();trackerClock=now;
    const float dt=lastFrame?float(now-lastFrame)*.001f:0;lastFrame=now;
    if(playing)runTime+=std::min(dt,.25f);
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
            t.seenRun=runTime;
            tracks.push_back(t);used.push_back(true);continue;
        }
        auto& t=tracks[best];used[best]=true;
        if(playing && dt>0) {
            for(int k=0;k<3;++k){const float speed=(place.m[12+k]-t.p[k])/dt;t.v[k]=t.v[k]*.7f+speed*.3f;}
            t.travelled+=bestDistance;
            if(bestDistance<.01f){if(!t.stillSince)t.stillSince=now;}else t.stillSince=0;
        }
        t.m=place.m;t.p[0]=place.m[12];t.p[1]=place.m[13];t.p[2]=place.m[14];t.seen=now;t.seenRun=runTime;t.size=std::max(t.size,place.indices);
        if(playing && t.number && (t.path.empty() || runTime-t.path.back().time>=.1)) {
            t.path.push_back({runTime,{t.p[0],t.p[1],t.p[2]}});
            if(t.path.size()>600)t.path.erase(t.path.begin(),t.path.begin()+100);
        }
    }
    // A track that has just started moving like a car may be a car seen before (it came back somewhere the
    // quick match above did not expect): it takes over the nearest such car that is out of sight, keeping
    // that car's number. Otherwise it is a new car.
    std::vector<bool> merged(tracks.size());
    for(size_t i=0;i<tracks.size();++i) {
        auto& t=tracks[i];
        if(t.number || !Moving(t))continue;
        int best=-1;float bestDistance=1e9f;
        for(size_t j=0;j<tracks.size();++j) {
            const auto& old=tracks[j];
            if(j==i || merged[j] || !old.number || now-old.seen<250 || !SameBody(old.size,t.size) || (old.stillSince && now-old.stillSince>4000))continue;
            const float away=float(std::min(30.0,runTime-old.seenRun));
            float predicted[3];const bool trail=Guess(old,predicted,nullptr);
            const float speed=std::sqrt(old.v[0]*old.v[0]+old.v[1]*old.v[1]+old.v[2]*old.v[2]);
            const float d=Distance(predicted,t.p);
            if(d<(trail?60.f:40.f+speed*away) && d<bestDistance){bestDistance=d;best=int(j);}
        }
        if(best<0){t.number=nextNumber++;continue;}
        auto& old=tracks[best];
        Log("car tracker: car %d found again after %.1f s (%.0f m from where expected)",old.number,float(now-old.seen)*.001f,bestDistance);
        old.m=t.m;std::memcpy(old.p,t.p,sizeof old.p);std::memcpy(old.v,t.v,sizeof old.v);old.seen=t.seen;
        old.size=std::max(old.size,t.size);old.travelled+=t.travelled;old.stillSince=t.stillSince;old.seenRun=t.seenRun;
        merged[i]=true;
    }
    for(size_t i=tracks.size();i-->0;)if(merged[i])tracks.erase(tracks.begin()+std::ptrdiff_t(i));
    // Forget places not drawn for a while; cars are remembered longer, to switch back to them. While time
    // stands still (a paused race or replay, Photo Mode) everything stays where it was.
    if(playing)std::erase_if(tracks,[&](const Track& t){return now-t.seen>(Moving(t)?KeepCarMs:1000ull);});
    if(tracks.size()>256) {
        std::stable_partition(tracks.begin(),tracks.end(),[](const Track& t){return Moving(t);});
        tracks.resize(256);
    }
    spent+=Now()-frameStart;++spentFrames;
    static ULONGLONG reported{};
    if(LoggingEnabled() && now-reported>=2000) {
        reported=now;int cars{};const UINT largest=LargestBody();
        std::string list;
        for(const auto& t:tracks)if(Moving(t)) {
            ++cars;char x[64]{};
            std::snprintf(x,sizeof x," %d:%u/%.0fm/%llus%s",t.number,t.size,t.travelled,(now-t.seen)/1000,IsCar(t,largest)?"":"(not a car)");list+=x;
        }
        LARGE_INTEGER f{};QueryPerformanceFrequency(&f);
        const double us=spentFrames?double(spent.exchange(0))*1e6/double(f.QuadPart)/spentFrames:0;spentFrames=0;
        Log("car tracker: places=%zu tracks=%zu moving=%d playing=%d cpu_us_per_frame=%.1f cars(number:indices/travelled/unseen):%s",places.size(),tracks.size(),cars,playing,us,list.c_str());
    }
}

std::vector<TrackedCar> TrackedCars() {
    std::lock_guard lock(mutex);
    const auto now=GetTickCount64();const UINT largest=LargestBody();
    std::vector<TrackedCar> cars;
    for(const auto& t:tracks) {
        if(!IsCar(t,largest))continue;
        TrackedCar c;c.id=t.id;c.number=t.number;c.unseenMs=unsigned(std::min<ULONGLONG>(now-t.seen,~0u));
        std::memcpy(c.matrix,t.m.data(),sizeof c.matrix);std::memcpy(c.velocity,t.v,sizeof c.velocity);c.travelled=t.travelled;
        c.guessFromTrail=Guess(t,c.guess,c.guessForward);
        cars.push_back(c);
    }
    std::sort(cars.begin(),cars.end(),[](const TrackedCar& a,const TrackedCar& b){return a.number<b.number;});
    return cars;
}
}
