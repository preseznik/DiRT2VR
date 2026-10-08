#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
struct ChaseZoom {
    float scale{1}, target{1};
    void Reset() { *this={}; }
    bool Step(float dt,float wheel) {
        if(!std::isfinite(dt) || dt<=0 || dt>.25f || !std::isfinite(wheel))return false;
        // DirectInput reports 120 units per notch, including partial notches.
        // Limit a single burst, then bound the distance relative to the native
        // camera. This changes distance, never FOV or headset world scale.
        target=std::clamp(target*std::pow(1.1f,-std::clamp(wheel/120.f,-16.f,16.f)),.6f,1.8f);
        scale=target+(scale-target)*std::exp(-12.f*dt);
        if(std::abs(scale-target)<.00001f)scale=target;
        return true;
    }
};
}
