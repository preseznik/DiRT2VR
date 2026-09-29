#pragma once
#include <cmath>

namespace vr {
// Select existing simulation snapshots; never fabricate extra snapshots after a hitch.
class ReplaySampleClock {
    bool initialized{};
    double previous{}, next{};
public:
    void Reset() { initialized=false; }
    bool Record(double time) {
        constexpr double step=1.0/60.0, epsilon=1e-7;
        if(!std::isfinite(time) || time<0) { Reset(); return true; }
        if(!initialized || time<previous) {
            initialized=true; previous=time; next=time+step; return true;
        }
        previous=time;
        if(time+epsilon<next) return false;
        next+=(std::floor((time+epsilon-next)/step)+1.0)*step;
        return true;
    }
};
}
