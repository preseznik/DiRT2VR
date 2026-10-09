#pragma once
namespace vr {
// Set before scene preparation, and retain through shadow-map generation and
// both eye renders. Restoring per eye makes sampling disagree with coverage.
// The caller revalidates object lifetime before supplying the current pointer.
class ShadowCoverage {
    float* radius_{};
    bool owned_{};
public:
    void Update(float* radius,bool enabled) {
        if(radius_!=radius){radius_=radius;owned_=false;}
        if(!radius)return;
        if(owned_ && *radius!=14.f)owned_=false;
        if(enabled && *radius==3.f){*radius=14.f;owned_=true;}
        if(!enabled && owned_){*radius=3.f;owned_=false;}
    }
    bool Active()const{return owned_;}
};
}
