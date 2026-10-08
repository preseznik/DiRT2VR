#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
// Angles are radians. The game adapter supplies simulation time and validated
// vehicle speed; headset motion never enters this state machine.
struct ChaseOrbit {
    float yaw{}, pitch{}, idle{};
    bool returning{}, rearView{};
    static constexpr float pi=3.14159265358979323846f;
    void Reset() { *this={}; }
    bool Step(float dt,float speedKmh,float horizontal,float vertical,bool inputHeld,bool rearHeld=false) {
        if(!std::isfinite(dt) || dt<=0 || dt>.25f || !std::isfinite(speedKmh) ||
           !std::isfinite(horizontal) || !std::isfinite(vertical)) return false;
        // Look back takes priority over orbit input. Release always returns to
        // the ordinary chase angle, including while stationary.
        if(rearHeld) { yaw=pi; pitch=0; idle=0; rearView=true; return true; }
        if(rearView) { Reset(); return true; }
        const float speed=std::abs(speedKmh);
        if(speed>=3) returning=true;
        else if(speed<=1) returning=false;
        if(inputHeld || horizontal!=0 || vertical!=0) {
            idle=0;
            yaw=std::remainder(yaw+horizontal,2*pi);
            pitch=std::clamp(pitch+vertical,-pi/6,pi/3);
        } else {
            const float previous=idle;
            idle=std::min(idle+dt,1.f);
            if(returning && idle>.75f) {
                const float active=previous>=.75f ? dt : idle-.75f;
                const float decay=std::exp(-6.f*active);
                yaw*=decay; pitch*=decay;
                if(std::abs(yaw)<.0001f)yaw=0;
                if(std::abs(pitch)<.0001f)pitch=0;
            }
        }
        return true;
    }
};
}
