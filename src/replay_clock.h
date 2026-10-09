#pragma once
#include <cmath>
#include <cstdint>
namespace vr {
struct ReplayPlayback {
    int state{-1};
    uint64_t frame{},tick{};
    double time{},step{};
    bool Available(uint64_t now) const {
        return tick && now>=tick && now-tick<250 && frame &&
            (state==1 || state==2 || state==11) && std::isfinite(time) && std::isfinite(step);
    }
    bool Forward() const { return state==1 && step>0; }
};
// Playback time can stop or run backwards. Input time cannot. Consume input at
// most once per recorder update, even if the camera is evaluated several times.
struct ReplayInputClock {
    uint64_t frame{};
    double previous{};
    void Reset() { *this={}; }
    float Step(uint64_t nextFrame,double now) {
        if(!nextFrame || !std::isfinite(now)){Reset();return 0;}
        if(frame==nextFrame)return 0;
        const double elapsed=now-previous;
        const bool ready=frame && nextFrame>frame && elapsed>0 && elapsed<=.25;
        frame=nextFrame;previous=now;
        return ready?static_cast<float>(elapsed):0;
    }
};
}
