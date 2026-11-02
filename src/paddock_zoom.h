#pragma once
#include <algorithm>
#include <cmath>

namespace vr {
// Mouse-wheel zoom for the paddock outside the RV in VR 3D menus (Advanced ->
// Paddock zoom). The headset view moves forward along the game's own camera,
// from 0 up to Maximum metres. Only DiRT2VR's eye offset changes: the game's
// camera, menus and the widened RV visibility are untouched.
struct PaddockZoom {
    static constexpr float Notch=.25f,Maximum=3.f; // metres per wheel notch, furthest zoom
    float distance{},target{};
    float position[3]{},forward[3]{};
    bool tracking{};
    void Reset() { *this={}; }
    // Follow the game's camera record (forward row 12-14, position row 16-18).
    // A cut to another shot returns to the game's view.
    void Observe(const float* camera) {
        const float* p=camera+16;const float* f=camera+12;
        for(unsigned i=0;i<3;++i)if(!std::isfinite(p[i]) || !std::isfinite(f[i])) {Reset();return;}
        if(tracking) {
            const float dx=p[0]-position[0],dy=p[1]-position[1],dz=p[2]-position[2];
            const float dot=f[0]*forward[0]+f[1]*forward[1]+f[2]*forward[2];
            if(dx*dx+dy*dy+dz*dz>1.f || dot<.99f)distance=target=0;
        }
        std::copy_n(p,3,position);std::copy_n(f,3,forward);tracking=true;
    }
    // DirectInput reports 120 units per wheel notch; forward zooms in.
    bool Step(float dt,float wheel) {
        if(!std::isfinite(dt) || dt<=0 || dt>.25f || !std::isfinite(wheel))return false;
        target=std::clamp(target+std::clamp(wheel/120.f,-16.f,16.f)*Notch,0.f,Maximum);
        distance=target+(distance-target)*std::exp(-12.f*dt);
        if(std::abs(distance-target)<.0001f)distance=target;
        return true;
    }
};
// Xbox right stick, which the paddock menus leave unused: up moves closer.
// Full deflection matches eight wheel notches a second, like replay zoom;
// a resting stick inside the dead zone does nothing.
inline float PaddockStickWheel(short thumbY,float dt) {
    if(!std::isfinite(dt) || dt<=0 || dt>.25f)return 0;
    constexpr float deadZone=.25f;
    const float y=std::clamp(thumbY/32767.f,-1.f,1.f),amount=std::abs(y);
    return amount<=deadZone?0.f:std::copysign((amount-deadZone)/(1-deadZone),y)*960.f*dt;
}
bool PaddockZoomEnabled();
// Once per headset frame: returns the forward offset in metres, or 0 when
// the paddock is not shown (which also resets the zoom).
float PaddockZoomDistance(bool paddock,const float* camera);
}
