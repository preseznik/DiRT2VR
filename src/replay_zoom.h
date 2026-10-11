#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
struct ReplayZoomBinding {
    int kind{},input{},calibration{};
    float deadZone{},saturation{1};
    bool valid{};
    bool Validate() const {
        return (kind==0 ? input>0 && input<255 : kind==1 && input>=0 && input<20) &&
            calibration>=0 && calibration<4 && std::isfinite(deadZone) && std::isfinite(saturation) &&
            deadZone>=0 && deadZone<=.9f && saturation>=.1f && saturation<=1 && deadZone<saturation;
    }
    float Amount(float raw) const {
        if(!valid || !std::isfinite(raw))return 0;
        float value=calibration==1?1-raw:calibration==2?-raw:raw;
        return std::clamp((value-deadZone)/(saturation-deadZone),0.f,1.f);
    }
};
inline float ReplayZoomWheel(float in,float out,float dt) {
    if(!std::isfinite(in)||!std::isfinite(out)||!std::isfinite(dt)||dt<=0||dt>.25f)return 0;
    return (std::clamp(in,0.f,1.f)-std::clamp(out,0.f,1.f))*960.f*dt;
}
}
