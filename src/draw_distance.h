#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
bool EnableDrawDistance();
// Ground-cover budgets. DIRT2VR_EXTENDED_GRASS=1 raises them for desktop and VR;
// otherwise VR keeps its 0.18.0 budget and desktop is untouched. VR always keeps
// its 250 m range floor.
bool EnableGrassBudget(bool headset);
namespace lod {
// A bounded detail floor, not an unlimited render distance. Authored longer
// distances (including Aspen's accepted ranges) are never reduced.
struct Scenery {
    float terrainCull, worldCull, terrainDetail, objectSize;
};
inline bool Extend(Scenery& value,bool performancePikes=false) {
    for(float f:{value.terrainCull,value.worldCull,value.terrainDetail,value.objectSize})
        if(!std::isfinite(f) || f<0) return false;
    if(value.terrainCull<1 || value.worldCull<1 || value.terrainDetail<1) return false;
    value.terrainCull=std::max(value.terrainCull,1500.f);
    value.worldCull=std::max(value.worldCull,1000.f);
    value.terrainDetail=std::max(value.terrainDetail,performancePikes?200.f:1000.f);
    value.objectSize=std::min(value.objectSize,.001f);
    return true;
}
// Keep disabled/sentinel settings and longer authored distances intact. The
// range floor is applied once at load; it is not multiplied on every restart.
inline bool TreeRanges(float* ranges) {
    if(!std::isfinite(ranges[0]) || !std::isfinite(ranges[1]) || !std::isfinite(ranges[2]) ||
       ranges[0]<=0 || ranges[1]<ranges[0] || ranges[2]<ranges[1]) return false;
    ranges[0]=std::max(ranges[0],80.f);
    ranges[1]=std::max(ranges[1],300.f);
    ranges[2]=std::max(ranges[2],1000.f);
    return true;
}
// Trackside props use a separate three-distance table. Zero skips an authored
// detail tier; keep that sentinel and all longer distances intact.
inline bool OrnamentRanges(float* ranges) {
    if(!std::isfinite(ranges[0]) || !std::isfinite(ranges[1]) || !std::isfinite(ranges[2]) ||
       ranges[0]<0 || ranges[1]<ranges[0] || ranges[2]<ranges[1] || ranges[2]<=0) return false;
    if(ranges[0]>0) ranges[0]=std::max(ranges[0],80.f);
    if(ranges[1]>0) ranges[1]=std::max(ranges[1],300.f);
    ranges[2]=std::max(ranges[2],1000.f);
    return true;
}
// Trees and trackside props draw shadows through the near (shadow_1) and
// outer (shadow_2) map passes, each capped per style for the game's 14 m and
// 80 m ranges (Tanamera Falls: trees 150/600, props 200/200). A longer range
// covers more of them; once a dense scene overflows the cap, which ones cast a
// shadow changes as the view moves. Scale the cap with the covered area.
// A zero budget (no shadows from that pass) stays zero. Budgets cost memory in
// a VR process limited to 2 GB, so the caller bounds each style.
inline int ShadowBudget(int budget,float range,float native,int cap) {
    if(budget<=0 || !std::isfinite(range) || !(native>0) || range<=native) return budget;
    const float scaled=std::min(float(budget)*(range/native)*(range/native),float(cap));
    return std::max(budget,int(scaled));
}
// About 70 bytes a tree billboard in lab runs; 12,000 per style still covers
// the ~13,000 trees across Tanamera's styles in a 640 m outer map.
constexpr int TreeShadowCap=12000;
// Trackside props (banners, tents, gantries) take 36 bytes an instance. A 640 m
// outer map of Tanamera's start area holds about 560 against a budget of 200.
constexpr int PropShadowCap=6000;
inline bool Nearby(const float* eye,const float* car,bool previous) {
    float squared=0;
    for(unsigned i=0;i<3;++i) {
        if(!std::isfinite(eye[i]) || !std::isfinite(car[i])) return false;
        const float d=eye[i]-car[i]; squared+=d*d;
    }
    const float range=previous ? 40.f : 30.f;
    return squared<=range*range;
}
}
}
