#pragma once
#include <algorithm>
#include <cmath>
namespace vr {
// Set before scene preparation, and retain through shadow-map generation and
// both eye renders. Restoring per eye makes sampling disagree with coverage.
// The caller revalidates object lifetime before supplying the current pointer.
class ShadowCoverage {
    float* radius_{};
    bool owned_{};
    float applied_=14.f;
public:
    void Update(float* radius,bool enabled,float distance=14.f) {
        if(radius_!=radius){radius_=radius;owned_=false;}
        if(!radius)return;
        if(owned_ && *radius!=applied_)owned_=false;
        if(enabled && (*radius==3.f || owned_)) {
            applied_=std::isfinite(distance)?std::clamp(distance,14.f,60.f):14.f;
            *radius=applied_;owned_=true;
        }
        if(!enabled && owned_){*radius=3.f;owned_=false;}
    }
    bool Active()const{return owned_;}
};
}
