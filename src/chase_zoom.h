#pragma once
#include <algorithm>
#include <cmath>
#include <cstdlib>

namespace vr {
inline float ParseChaseMaxZoomOut(const char* text) {
    if(!text)return 1.8f;
    char* end{};
    const long percent=std::strtol(text,&end,10);
    return end!=text && *end==0 && percent>=180 && percent<=500 && percent%10==0?percent/100.f:1.8f;
}
struct ChaseZoom {
    float scale{1}, target{1};
    void Reset() { *this={}; }
    bool Step(float dt,float wheel,float maximum=1.8f) {
        if(!std::isfinite(dt) || dt<=0 || dt>.25f || !std::isfinite(wheel))return false;
        if(!std::isfinite(maximum) || maximum<1.8f || maximum>5.f)return false;
        // DirectInput reports 120 units per notch, including partial notches.
        // Limit a single burst, then bound the distance relative to the native
        // camera. This changes distance, never FOV or headset world scale.
        target=std::clamp(target*std::pow(1.1f,-std::clamp(wheel/120.f,-16.f,16.f)),.6f,maximum);
        scale=target+(scale-target)*std::exp(-12.f*dt);
        if(std::abs(scale-target)<.00001f)scale=target;
        return true;
    }
};
}
