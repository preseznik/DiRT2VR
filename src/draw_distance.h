#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
bool EnableDrawDistance();
namespace lod {
// A bounded detail floor, not an unlimited render distance. Authored longer
// distances (including Aspen's accepted ranges) are never reduced.
struct Scenery {
    float terrainCull, worldCull, terrainDetail, objectSize;
};
inline bool Extend(Scenery& value) {
    for(float f:{value.terrainCull,value.worldCull,value.terrainDetail,value.objectSize})
        if(!std::isfinite(f) || f<0) return false;
    if(value.terrainCull<1 || value.worldCull<1 || value.terrainDetail<1) return false;
    value.terrainCull=std::max(value.terrainCull,1500.f);
    value.worldCull=std::max(value.worldCull,1000.f);
    value.terrainDetail=std::max(value.terrainDetail,1000.f);
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
